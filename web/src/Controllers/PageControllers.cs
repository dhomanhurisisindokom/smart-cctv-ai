using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using MiniWeb.Data;
using MiniWeb.Models;

namespace MiniWeb.Controllers
{
    public class DashboardModel
    {
        public int CameraCount;
        public int ApdCameras;
        public int VehicleCameras;
        public Summary AllTime;
        public int Last24h;
        public List<DayCount> Week;
        public List<AreaCount> Areas;
        public List<Violation> Feed;
    }

    public class HomeController : BaseController
    {
        public ActionResult Index()
        {
            var cameras = Cameras.All();
            var today = (DateTime.UtcNow + Config.Wib).Date;
            var model = new DashboardModel
            {
                CameraCount = cameras.Count,
                ApdCameras = cameras.Count(c => c.ApdEnabled && c.AiEnabled),
                VehicleCameras = cameras.Count(c => c.VehicleEnabled && c.AiEnabled),
                AllTime = Stats.Summary(null, null),
                Last24h = Stats.Summary(DateTime.UtcNow.AddHours(-24), null).Total,
                Week = Stats.PerDay(today.AddDays(-6), today),
                Areas = Stats.PerCamera(null, null),
                Feed = Violations.Latest(5),
            };
            return View(model);
        }
    }

    public class DetectionModel
    {
        public string Category;
        public string Title;
        public string Subtitle;
        public string Controller;
        public List<Camera> Cameras;
        public int Monitored;
        public int Last24h;
        public int OpenCases;
        public ViolationFilter Filter;
        public PagedViolations Page;
        public List<KeyValuePair<string, int>> Labels;
    }

    // APD and Vehicle pages are the same screen for a different category and stream mode.
    public abstract class DetectionController : BaseController
    {
        protected abstract string Category { get; }
        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }

        protected ViolationFilter Filter(string range, string from, string to, int? camera, string label, string status, int page)
        {
            return new ViolationFilter
            {
                Category = Category, Range = string.IsNullOrEmpty(range) ? "all" : range, From = ParseDate(from), To = ParseDate(to),
                CameraId = camera, Label = string.IsNullOrEmpty(label) ? null : label,
                Status = string.IsNullOrEmpty(status) ? null : status, Page = Math.Max(1, page),
            };
        }

        public ActionResult Index(string range, string from, string to, int? camera, string label, string status, int page = 1)
        {
            var filter = Filter(range, from, to, camera, label, status, page);
            var cameras = Cameras.All();
            SetAiAccess();
            var open = new ViolationFilter { Category = Category, Range = "all", Status = "open", PageSize = 1 };
            var model = new DetectionModel
            {
                Category = Category, Title = Title, Subtitle = Subtitle, Controller = Category == "apd" ? "Apd" : "Vehicle",
                Cameras = cameras,
                Monitored = cameras.Count(c => c.AiEnabled && (Category == "apd" ? c.ApdEnabled : c.VehicleEnabled)),
                Last24h = Violations.Page(new ViolationFilter { Category = Category, Range = "24h", PageSize = 1 }).Total,
                OpenCases = Violations.Page(open).Total,
                Filter = filter, Page = Violations.Page(filter), Labels = Violations.LabelCounts(Category),
            };
            return View("~/Views/Shared/Detection.cshtml", model);
        }

        public ActionResult Csv(string range, string from, string to, int? camera, string label, string status)
        {
            var rows = Violations.Export(Filter(range, from, to, camera, label, status, 1));
            return CsvExport.File(this, "pelanggaran-" + Category, rows);
        }
    }

    public class ApdController : DetectionController
    {
        protected override string Category { get { return "apd"; } }
        protected override string Title { get { return "APD Detection"; } }
        protected override string Subtitle { get { return "Deteksi kepatuhan alat pelindung diri (helm, kacamata, sarung tangan, wearpack, sepatu) di kamera terkait."; } }
    }

    public class VehicleController : DetectionController
    {
        protected override string Category { get { return "vehicle"; } }
        protected override string Title { get { return "Vehicle Violation"; } }
        protected override string Subtitle { get { return "Deteksi pelanggaran kendaraan (mis. penumpang di bak kendaraan) di kamera terkait."; } }
    }

    public class CasesController : BaseController
    {
        public ActionResult Index(string tab, int page = 1)
        {
            tab = tab == "apd" || tab == "vehicle" || tab == "open" ? tab : "all";
            var filter = new ViolationFilter
            {
                Range = "all", Status = tab == "open" ? "open" : "case",
                Category = tab == "apd" || tab == "vehicle" ? tab : null, Page = Math.Max(1, page),
            };
            ViewBag.Tab = tab;
            return View(Violations.Page(filter));
        }
    }

    // Case handling popup (Detail kasus). Loaded with fetch; every action re-renders the popup.
    public class ViolationsController : BaseController
    {
        public ActionResult Detail(long id)
        {
            var v = Violations.ById(id);
            if (v == null) return HttpNotFound();
            ViewBag.Notes = v.IsCase ? Violations.Notes(id) : new List<CaseNote>();
            ViewBag.Users = Users.Active();
            return PartialView("_Detail", v);
        }

        [OutputCache(Duration = 3600, VaryByParam = "id", Location = System.Web.UI.OutputCacheLocation.Client)]
        public ActionResult Evidence(long id)
        {
            var jpeg = Violations.Evidence(id);
            if (jpeg == null) return HttpNotFound();
            return File(jpeg, "image/jpeg");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult OpenCase(long id) { Violations.OpenCase(id); return Detail(id); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SetStatus(long id, string status)
        {
            if (status == "baru" || status == "diproses" || status == "selesai") Violations.SetStatus(id, status);
            return Detail(id);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Cancel(long id) { Violations.CancelCase(id); return Detail(id); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Assign(long id, int? userId) { Violations.Assign(id, userId); return Detail(id); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Note(long id, string note)
        {
            note = (note ?? "").Trim();
            var v = Violations.ById(id);
            if (v != null && v.IsCase && note.Length > 0)
                Violations.AddNote(id, CurrentUser.Id, note.Length > 1000 ? note.Substring(0, 1000) : note);
            return Detail(id);
        }
    }

    public static class CsvExport
    {
        static string Cell(object value)
        {
            var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            // Neutralise spreadsheet formulas in text cells.
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string Line(params object[] cells) { return string.Join(",", cells.Select(Cell)) + "\r\n"; }

        public static ActionResult File(Controller c, string name, List<Violation> rows)
        {
            var sb = new StringBuilder();
            sb.Append(Line("ID", "Waktu (WIB)", "Kamera", "Lokasi", "Kategori", "Jenis", "Confidence", "Status", "Ditugaskan"));
            foreach (var v in rows)
                sb.Append(Line(v.IsCase ? v.CaseCode : v.Id.ToString(), Fmt.Wib(v.DetectedAt).ToString("yyyy-MM-dd HH:mm:ss"),
                    v.CameraName, v.CameraLocation, v.CategoryLabel, v.Label, v.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    v.IsCase ? "Kasus - " + Fmt.StatusText(v.CaseStatus) : "Alert", v.AssignedName));
            return Text(c, name, sb.ToString());
        }

        public static ActionResult Text(Controller c, string name, string csv)
        {
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = name + "-" + (DateTime.UtcNow + Config.Wib).ToString("yyyyMMdd-HHmm") + ".csv" };
        }
    }
}
