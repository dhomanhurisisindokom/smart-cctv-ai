using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Compilation;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using MiniWeb.Data;

namespace MiniWeb.Controllers
{
    // Machine-to-machine endpoints for the AI service (fastapi/). Bearer token, no session.
    public class IngestController : Controller
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        bool Authorized()
        {
            var header = Request.Headers["Authorization"] ?? "";
            return header.StartsWith("Bearer ", StringComparison.Ordinal)
                && PasswordHasher.TokenEquals(header.Substring(7).Trim(), Config.IngestToken);
        }

        ActionResult JsonStatus(int status, object body)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            return Content(Json.Serialize(body), "application/json");
        }

        // GET /api/cameras: the AI's camera configuration (includes RTSP credentials, so token only).
        [HttpGet]
        public ActionResult Cameras()
        {
            if (!Authorized()) return JsonStatus(401, new { error = "unauthorized" });
            var cameras = Data.Cameras.All().Select(c => new Dictionary<string, object>
            {
                { "id", c.Id }, { "name", c.Name }, { "url", c.RtspUrl }, { "apd_enabled", c.ApdEnabled },
                { "vehicle_enabled", c.VehicleEnabled }, { "apd_mask", c.ApdMask ?? "" }, { "apd_penutup", c.ApdPenutup ?? "" },
                { "vehicle_profile", c.VehicleProfile ?? "" },
            }).ToList();
            return JsonStatus(200, new { cameras });
        }

        // POST /api/cameras/bootstrap: first install only. The AI offers the cameras from its .env;
        // they are stored only while the cameras table is still empty.
        [HttpPost]
        public ActionResult Bootstrap()
        {
            if (!Authorized()) return JsonStatus(401, new { error = "unauthorized" });
            if (Data.Cameras.All().Count > 0) return JsonStatus(200, new { created = 0 });
            Dictionary<string, object> body;
            try
            {
                using (var reader = new StreamReader(Request.InputStream))
                    body = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }
            catch (Exception) { return JsonStatus(400, new { error = "invalid json" }); }
            var created = 0;
            foreach (var item in (body != null && body.ContainsKey("cameras") ? body["cameras"] as System.Collections.ArrayList : null) ?? new System.Collections.ArrayList())
            {
                var c = item as Dictionary<string, object>;
                int id;
                if (c == null || !int.TryParse(Convert.ToString(c["id"]), out id) || !Data.Cameras.Slots.Contains(id)) continue;
                Func<string, string> s = k => c.ContainsKey(k) && c[k] != null && Convert.ToString(c[k]).Trim().Length > 0 ? Convert.ToString(c[k]).Trim() : null;
                Func<string, bool> b = k => c.ContainsKey(k) && c[k] is bool && (bool)c[k];
                Data.Cameras.Save(new Models.Camera
                {
                    Id = id, Name = s("name") ?? "CCTV " + id, Location = "", RtspUrl = s("url"),
                    ApdEnabled = b("apd_enabled"), VehicleEnabled = b("vehicle_enabled"),
                    ApdMask = s("apd_mask"), ApdPenutup = s("apd_penutup"), VehicleProfile = s("vehicle_profile"),
                }, true);
                created++;
            }
            return JsonStatus(201, new { created });
        }

        // POST /api/ingest: one confirmed violation from the AI outbox. Must answer
        // {"event_key", "id"} with 200/201; 4xx quarantines the event, 5xx makes the AI retry.
        [HttpPost]
        public ActionResult Ingest()
        {
            if (!Authorized()) return JsonStatus(401, new { error = "unauthorized" });
            Dictionary<string, object> body;
            try
            {
                using (var reader = new StreamReader(Request.InputStream))
                    body = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }
            catch (Exception) { return JsonStatus(400, new { error = "invalid json" }); }

            Func<string, string> str = key => body != null && body.ContainsKey(key) ? Convert.ToString(body[key], CultureInfo.InvariantCulture) : null;
            var key_ = str("event_key");
            var cameraKey = str("camera_key") ?? "";
            var parts = cameraKey.Split('-');
            int cameraId;
            DateTime capturedAt;
            double confidence;
            byte[] jpeg;
            if (string.IsNullOrEmpty(key_) || key_.Length > 64 || parts.Length != 3 || parts[0] != "live"
                || !int.TryParse(parts[1], out cameraId) || (parts[2] != "apd" && parts[2] != "vehicle") || parts[2] != str("category")
                || !DateTime.TryParse(str("captured_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out capturedAt)
                || !double.TryParse(str("confidence"), NumberStyles.Float, CultureInfo.InvariantCulture, out confidence)
                || string.IsNullOrEmpty(str("label")) || string.IsNullOrEmpty(str("severity")))
                return JsonStatus(422, new { error = "invalid event" });
            try { jpeg = Convert.FromBase64String(str("snapshot_jpeg") ?? ""); }
            catch (FormatException) { return JsonStatus(422, new { error = "invalid snapshot" }); }
            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return JsonStatus(422, new { error = "invalid snapshot" });

            var camera = Data.Cameras.ById(cameraId);
            if (camera == null) return JsonStatus(422, new { error = "unknown camera" });
            var category = parts[2];
            // Detection turned off for this category on this camera: acknowledge without storing.
            if (category == "apd" ? !camera.ApdEnabled : !camera.VehicleEnabled)
                return JsonStatus(200, new { event_key = key_, id = "skipped" });

            var label = str("label");
            var id = Violations.Ingest(key_, cameraId, camera.Name, category, label.Length > 128 ? label.Substring(0, 128) : label,
                str("severity").Length > 16 ? str("severity").Substring(0, 16) : str("severity"),
                Math.Max(0, Math.Min(1, confidence)), capturedAt, jpeg);
            return JsonStatus(201, new { event_key = key_, id });
        }

        public ActionResult Health()
        {
            try { Db.Scalar("SELECT 1"); return JsonStatus(200, new { status = "ok" }); }
            catch (Exception) { return JsonStatus(503, new { status = "database unavailable" }); }
        }
    }

    // Compiles every Razor view once at container start (start.sh), so the first visitor
    // does not wait for Mono's view compiler.
    public class WarmupController : Controller
    {
        public ActionResult Index()
        {
            if (!Request.IsLocal) return HttpNotFound();
            var root = HostingEnvironment.MapPath("~/Views");
            var done = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*.cshtml", SearchOption.AllDirectories))
            {
                var virtualPath = "~/Views/" + file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (Path.GetFileName(file).StartsWith("_View")) continue;
                BuildManager.GetCompiledType(virtualPath);
                done.Add(virtualPath);
            }
            return Content("Compiled " + done.Count + " views", "text/plain");
        }
    }
}
