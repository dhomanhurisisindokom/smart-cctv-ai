using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using MiniWeb.Data;
using MiniWeb.Models;

namespace MiniWeb.Controllers
{
    public class ReportsModel
    {
        public string Range;
        public DateTime From;
        public DateTime To;
        public Summary Summary;
        public int Days;
        public List<DayCount> Trend;
        public List<AreaCount> Areas;
    }

    public class ReportsController : BaseController
    {
        ReportsModel Build(string range, string from, string to)
        {
            var today = (DateTime.UtcNow + Config.Wib).Date;
            var model = new ReportsModel { Range = range == "30d" || range == "all" || range == "custom" ? range : "7d" };
            DateTime? start, end;
            Violations.Bounds(model.Range, ParseDate(from), ParseDate(to), out start, out end);
            model.From = model.Range == "all" ? (Stats.FirstDetection() ?? today).Date
                : model.Range == "custom" ? (ParseDate(from) ?? today.AddDays(-6)) : (start.Value + Config.Wib).Date;
            model.To = model.Range == "custom" ? (ParseDate(to) ?? today) : today;
            if (model.To < model.From) model.To = model.From;
            if (model.Range == "custom") Violations.Bounds("custom", model.From, model.To, out start, out end);
            model.Days = (int)(model.To - model.From).TotalDays + 1;
            model.Summary = Stats.Summary(start, end);
            // A readable chart: at most the last 31 days of the selected range.
            model.Trend = Stats.PerDay(model.Days > 31 ? model.To.AddDays(-30) : model.From, model.To);
            model.Areas = Stats.PerCamera(start, end);
            return model;
        }

        public ActionResult Index(string range, string from, string to) { return View(Build(range, from, to)); }

        public ActionResult Summary(string range, string from, string to)
        {
            var m = Build(range, from, to);
            var s = m.Summary;
            var sb = new StringBuilder();
            sb.Append(CsvExport.Line("Ringkasan Eksekutif Smart CCTV AI", m.From.ToString("yyyy-MM-dd") + " s/d " + m.To.ToString("yyyy-MM-dd")));
            sb.Append(CsvExport.Line("Metrik", "Nilai"));
            sb.Append(CsvExport.Line("Total Pelanggaran", s.Total));
            sb.Append(CsvExport.Line("Rata-rata / Hari", Math.Round((double)s.Total / Math.Max(1, m.Days), 1)));
            sb.Append(CsvExport.Line("APD Detection", s.Apd));
            sb.Append(CsvExport.Line("Vehicle Violation", s.Vehicle));
            sb.Append(CsvExport.Line("Dijadikan Kasus", s.Cases));
            sb.Append(CsvExport.Line("Kasus Selesai", s.CasesDone));
            sb.Append(CsvExport.Line("Rata-rata Waktu Respons (menit)", s.AvgResponseMinutes.HasValue ? Math.Round(s.AvgResponseMinutes.Value, 1).ToString() : "-"));
            sb.Append("\r\n").Append(CsvExport.Line("Kamera", "Lokasi", "APD", "Vehicle", "Total"));
            foreach (var a in m.Areas) sb.Append(CsvExport.Line(a.Camera, a.Location, a.Apd, a.Vehicle, a.Total));
            return CsvExport.Text(this, "ringkasan-eksekutif", sb.ToString());
        }

        public ActionResult Detail(string range, string from, string to)
        {
            var m = Build(range, from, to);
            var filter = new ViolationFilter { Range = "custom", From = m.From, To = m.To };
            return CsvExport.File(this, "laporan-detail", Violations.Export(filter));
        }
    }

    public class SettingsController : BaseController
    {
        protected override bool AdminOnly { get { return true; } }

        public ActionResult Index()
        {
            ViewBag.FreeSlot = Cameras.Slots.Except(Cameras.All().Select(c => c.Id)).Cast<int?>().FirstOrDefault();
            return View(Cameras.All());
        }

        static string Clean(string value, int max)
        {
            value = (value ?? "").Trim();
            return value.Length == 0 ? null : value.Length > max ? value.Substring(0, max) : value;
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Save(int id, string name, string location, bool apd = false, bool vehicle = false,
                                 string rtsp = null, bool clearRtsp = false, string apdMask = null, string apdPenutup = null,
                                 string vehicleProfile = null)
        {
            if (!Cameras.Slots.Contains(id)) return new HttpStatusCodeResult(400);
            var existing = Cameras.ById(id);
            name = Clean(name, 64);
            if (name == null) { Flash("Nama kamera wajib diisi.", false); return RedirectToAction("Index"); }
            rtsp = Clean(rtsp, 500);
            Uri uri;
            if (rtsp != null && (!Uri.TryCreate(rtsp, UriKind.Absolute, out uri) || Regex.IsMatch(rtsp, @"\s")
                                 || !new[] { "rtsp", "rtsps", "http", "https" }.Contains(uri.Scheme)))
            {
                Flash("RTSP URL tidak valid (contoh: rtsp://user:pass@192.168.1.10:554/stream1).", false);
                return RedirectToAction("Index");
            }
            var profile = Clean(vehicleProfile, 64);
            if (profile != null && !Regex.IsMatch(profile, @"^[A-Za-z0-9_\-]+$"))
            {
                Flash("Profil vehicle hanya boleh huruf, angka, '-' dan '_'.", false);
                return RedirectToAction("Index");
            }
            var camera = new Camera
            {
                Id = id, Name = name, Location = Clean(location, 128), ApdEnabled = apd, VehicleEnabled = vehicle,
                // Blank on edit keeps the stored URL, so the password never has to be shown again.
                RtspUrl = clearRtsp ? null : rtsp ?? (existing != null ? existing.RtspUrl : null),
                ApdMask = Clean(apdMask, 200), ApdPenutup = Clean(apdPenutup, 200), VehicleProfile = profile,
            };
            Cameras.Save(camera, existing == null);
            Flash("Kamera \"" + name + "\" disimpan. Layanan AI memuat ulang pengaturan dalam ±15 detik, lalu model siap ±1–2 menit.");
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var camera = Cameras.ById(id);
            if (camera == null) return RedirectToAction("Index");
            if (Cameras.HasViolations(id))
                Flash("Kamera \"" + camera.Name + "\" punya riwayat pelanggaran, jadi tidak bisa dihapus. Matikan deteksinya saja.", false);
            else
            {
                Cameras.Delete(id);
                Flash("Kamera \"" + camera.Name + "\" dihapus.");
            }
            return RedirectToAction("Index");
        }
    }

    public class AdminController : BaseController
    {
        protected override bool AdminOnly { get { return true; } }
        static readonly string[] Roles = { "admin", "supervisor", "operator" };

        public ActionResult Index() { return View(Users.All()); }

        static string Clean(string value, int max)
        {
            value = (value ?? "").Trim();
            return value.Length == 0 ? null : value.Length > max ? value.Substring(0, max) : value;
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Create(string username, string fullName, string email, string role, string password)
        {
            username = Clean(username, 64);
            fullName = Clean(fullName, 128);
            if (username == null || !Regex.IsMatch(username, @"^[A-Za-z0-9._\-]+$")) Flash("Username hanya huruf, angka, '.', '-' dan '_'.", false);
            else if (fullName == null) Flash("Nama wajib diisi.", false);
            else if (!Roles.Contains(role)) Flash("Role tidak valid.", false);
            else if ((password ?? "").Length < 8) Flash("Kata sandi minimal 8 karakter.", false);
            else if (Users.ByUsername(username) != null) Flash("Username \"" + username + "\" sudah dipakai.", false);
            else
            {
                Users.Create(username, fullName, Clean(email, 128), role, password);
                Flash("User \"" + username + "\" dibuat.");
            }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Update(int id, string fullName, string email, string role, string password)
        {
            var user = Users.ById(id);
            fullName = Clean(fullName, 128);
            if (user == null) return HttpNotFound();
            if (fullName == null || !Roles.Contains(role)) { Flash("Nama dan role wajib diisi.", false); return RedirectToAction("Index"); }
            if (id == CurrentUser.Id && role != "admin") { Flash("Role akun Anda sendiri tidak bisa diturunkan.", false); return RedirectToAction("Index"); }
            if (!string.IsNullOrEmpty(password) && password.Length < 8) { Flash("Kata sandi minimal 8 karakter.", false); return RedirectToAction("Index"); }
            Users.Update(id, fullName, Clean(email, 128), role);
            if (!string.IsNullOrEmpty(password)) Users.SetPassword(id, password);
            Flash("User \"" + user.Username + "\" diperbarui.");
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var user = Users.ById(id);
            if (user == null) return HttpNotFound();
            if (id == CurrentUser.Id) Flash("Akun Anda sendiri tidak bisa dinonaktifkan.", false);
            else
            {
                Users.SetActive(id, !user.IsActive);
                Flash("User \"" + user.Username + "\" " + (user.IsActive ? "dinonaktifkan." : "diaktifkan."));
            }
            return RedirectToAction("Index");
        }
    }
}
