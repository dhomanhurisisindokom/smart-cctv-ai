using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using MiniWeb.Models;

namespace MiniWeb.Data
{
    public static class Users
    {
        static User Map(IDataRecord r)
        {
            return new User
            {
                Id = r.Int("id"), Username = r.Str("username"), FullName = r.Str("full_name"), Email = r.Str("email"),
                Role = r.Str("role"), PasswordHash = r.Str("password_hash"), IsActive = r.Bool("is_active"),
                LastLoginAt = r.Date("last_login_at"),
            };
        }

        public static List<User> All()
        {
            return Db.Query("SELECT * FROM users ORDER BY CASE role WHEN 'admin' THEN 0 WHEN 'supervisor' THEN 1 ELSE 2 END, username", Map);
        }

        public static List<User> Active()
        {
            return Db.Query("SELECT * FROM users WHERE is_active = 1 ORDER BY full_name", Map);
        }

        public static User ByUsername(string username)
        {
            return Db.One("SELECT * FROM users WHERE username = @u", Map, "@u", username);
        }

        public static User ById(int id)
        {
            return Db.One("SELECT * FROM users WHERE id = @id", Map, "@id", id);
        }

        public static void TouchLogin(int id)
        {
            Db.Execute("UPDATE users SET last_login_at = SYSUTCDATETIME() WHERE id = @id", "@id", id);
        }

        public static void Create(string username, string fullName, string email, string role, string password)
        {
            Db.Execute("INSERT INTO users (username, full_name, email, role, password_hash, is_active) VALUES (@u, @n, @e, @r, @h, 1)",
                "@u", username, "@n", fullName, "@e", email, "@r", role, "@h", PasswordHasher.Hash(password));
        }

        public static void Update(int id, string fullName, string email, string role)
        {
            Db.Execute("UPDATE users SET full_name = @n, email = @e, role = @r WHERE id = @id",
                "@n", fullName, "@e", email, "@r", role, "@id", id);
        }

        public static void SetPassword(int id, string password)
        {
            Db.Execute("UPDATE users SET password_hash = @h WHERE id = @id", "@h", PasswordHasher.Hash(password), "@id", id);
        }

        public static void SetActive(int id, bool active)
        {
            Db.Execute("UPDATE users SET is_active = @a WHERE id = @id", "@a", active, "@id", id);
        }
    }

    public static class Cameras
    {
        static Camera Map(IDataRecord r)
        {
            return new Camera
            {
                Id = r.Int("id"), Name = r.Str("name"), Location = r.Str("location"), RtspUrl = r.Str("rtsp_url"),
                ApdEnabled = r.Bool("apd_enabled"), VehicleEnabled = r.Bool("vehicle_enabled"),
                ApdMask = r.Str("apd_mask"), ApdPenutup = r.Str("apd_penutup"), VehicleProfile = r.Str("vehicle_profile"),
            };
        }

        public static readonly int[] Slots = { 1, 2 };

        public static List<Camera> All() { return Db.Query("SELECT * FROM cameras ORDER BY id", Map); }

        public static Camera ById(int id) { return Db.One("SELECT * FROM cameras WHERE id = @id", Map, "@id", id); }

        public static void Save(Camera c, bool isNew)
        {
            var sql = isNew
                ? "INSERT INTO cameras (id, name, location, rtsp_url, apd_enabled, vehicle_enabled, apd_mask, apd_penutup, vehicle_profile) " +
                  "VALUES (@id, @name, @loc, @url, @apd, @veh, @mask, @penutup, @profile)"
                : "UPDATE cameras SET name = @name, location = @loc, rtsp_url = @url, apd_enabled = @apd, vehicle_enabled = @veh, " +
                  "apd_mask = @mask, apd_penutup = @penutup, vehicle_profile = @profile, updated_at = SYSUTCDATETIME() WHERE id = @id";
            Db.Execute(sql, "@id", c.Id, "@name", c.Name, "@loc", c.Location ?? "", "@url", c.RtspUrl, "@apd", c.ApdEnabled,
                "@veh", c.VehicleEnabled, "@mask", c.ApdMask, "@penutup", c.ApdPenutup, "@profile", c.VehicleProfile);
        }

        public static bool HasViolations(int id)
        {
            return Db.Count("SELECT COUNT(*) FROM violations WHERE camera_id = @id", "@id", id) > 0;
        }

        public static void Delete(int id) { Db.Execute("DELETE FROM cameras WHERE id = @id", "@id", id); }
    }

    public static class Violations
    {
        const string Select =
            "SELECT v.id, v.camera_id, v.camera_name, c.location, v.category, v.label, v.severity, v.confidence, v.detected_at, " +
            "v.case_status, v.case_opened_at, v.assigned_user_id, u.full_name AS assigned_name, " +
            "CASE WHEN e.violation_id IS NULL THEN 0 ELSE 1 END AS has_evidence " +
            "FROM violations v LEFT JOIN cameras c ON c.id = v.camera_id LEFT JOIN users u ON u.id = v.assigned_user_id " +
            "LEFT JOIN violation_evidence e ON e.violation_id = v.id ";

        static Violation Map(IDataRecord r)
        {
            var assigned = r["assigned_user_id"];
            return new Violation
            {
                Id = r.Long("id"), CameraId = r.Int("camera_id"), CameraName = r.Str("camera_name"), CameraLocation = r.Str("location"),
                Category = r.Str("category"), Label = r.Str("label"), Severity = r.Str("severity"), Confidence = r.Dbl("confidence"),
                DetectedAt = r.Date("detected_at").Value, CaseStatus = r.Str("case_status"), CaseOpenedAt = r.Date("case_opened_at"),
                AssignedUserId = assigned == DBNull.Value ? (int?)null : Convert.ToInt32(assigned), AssignedName = r.Str("assigned_name"),
                HasEvidence = r.Int("has_evidence") == 1,
            };
        }

        // UTC bounds for a range key; "custom" uses WIB calendar dates (To inclusive).
        public static void Bounds(string range, DateTime? from, DateTime? to, out DateTime? start, out DateTime? end)
        {
            var now = DateTime.UtcNow;
            var todayWib = (now + Config.Wib).Date;
            start = null; end = null;
            switch (range)
            {
                case "today": start = todayWib - Config.Wib; break;
                case "24h": start = now.AddHours(-24); break;
                case "7d": start = todayWib.AddDays(-6) - Config.Wib; break;
                case "30d": start = todayWib.AddDays(-29) - Config.Wib; break;
                case "custom":
                    if (from.HasValue) start = from.Value.Date - Config.Wib;
                    if (to.HasValue) end = to.Value.Date.AddDays(1) - Config.Wib;
                    break;
            }
        }

        static string Where(ViolationFilter f, List<object> args)
        {
            var sb = new StringBuilder("WHERE 1 = 1");
            if (!string.IsNullOrEmpty(f.Category)) { sb.Append(" AND v.category = @cat"); args.Add("@cat"); args.Add(f.Category); }
            DateTime? start, end;
            Bounds(f.Range, f.From, f.To, out start, out end);
            if (start.HasValue) { sb.Append(" AND v.detected_at >= @start"); args.Add("@start"); args.Add(start.Value); }
            if (end.HasValue) { sb.Append(" AND v.detected_at < @end"); args.Add("@end"); args.Add(end.Value); }
            if (f.CameraId.HasValue) { sb.Append(" AND v.camera_id = @cam"); args.Add("@cam"); args.Add(f.CameraId.Value); }
            if (!string.IsNullOrEmpty(f.Label)) { sb.Append(" AND v.label = @label"); args.Add("@label"); args.Add(f.Label); }
            switch (f.Status)
            {
                case "alert": sb.Append(" AND v.case_status IS NULL"); break;
                case "case": sb.Append(" AND v.case_status IS NOT NULL"); break;
                case "open": sb.Append(" AND v.case_status IN ('baru', 'diproses')"); break;
                case "baru":
                case "diproses":
                case "selesai": sb.Append(" AND v.case_status = @st"); args.Add("@st"); args.Add(f.Status); break;
            }
            return sb.ToString();
        }

        public static PagedViolations Page(ViolationFilter f)
        {
            var args = new List<object>();
            var where = Where(f, args);
            var page = new PagedViolations { PageSize = f.PageSize };
            page.Total = Db.Count("SELECT COUNT(*) FROM violations v " + where, args.ToArray());
            page.Page = Math.Max(1, Math.Min(f.Page, page.Pages));
            var paged = new List<object>(args) { "@skip", (page.Page - 1) * f.PageSize, "@take", f.PageSize };
            var order = f.Status == "case" || f.Status == "open" ? "v.case_opened_at DESC, v.id DESC" : "v.detected_at DESC, v.id DESC";
            page.Rows = Db.Query(Select + where + " ORDER BY " + order + " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", Map, paged.ToArray());
            return page;
        }

        // CSV export: same filter, capped so a huge range cannot exhaust the web process.
        public static List<Violation> Export(ViolationFilter f, int limit = 50000)
        {
            var args = new List<object>();
            var where = Where(f, args);
            args.Add("@limit"); args.Add(limit);
            return Db.Query("SELECT TOP (@limit) * FROM (" + Select + where + ") x ORDER BY detected_at DESC", Map, args.ToArray());
        }

        public static List<KeyValuePair<string, int>> LabelCounts(string category)
        {
            return Db.Query("SELECT label, COUNT(*) AS n FROM violations WHERE category = @c GROUP BY label ORDER BY COUNT(*) DESC, label",
                r => new KeyValuePair<string, int>(r.Str("label"), r.Int("n")), "@c", category);
        }

        public static Violation ById(long id) { return Db.One(Select + "WHERE v.id = @id", Map, "@id", id); }

        public static byte[] Evidence(long id)
        {
            return Db.Scalar("SELECT jpeg FROM violation_evidence WHERE violation_id = @id", "@id", id) as byte[];
        }

        public static List<Violation> Latest(int count)
        {
            return Db.Query(Select + "ORDER BY v.detected_at DESC, v.id DESC OFFSET 0 ROWS FETCH NEXT @n ROWS ONLY", Map, "@n", count);
        }

        public static void OpenCase(long id)
        {
            Db.Execute("UPDATE violations SET case_status = 'baru', case_opened_at = SYSUTCDATETIME() WHERE id = @id AND case_status IS NULL", "@id", id);
        }

        public static void SetStatus(long id, string status)
        {
            Db.Execute("UPDATE violations SET case_status = @s WHERE id = @id AND case_status IS NOT NULL", "@s", status, "@id", id);
        }

        // Back to a plain alert: assignment and notes belong to the case, so they go too.
        public static void CancelCase(long id)
        {
            Db.Execute("DELETE FROM case_notes WHERE violation_id = @id; " +
                       "UPDATE violations SET case_status = NULL, case_opened_at = NULL, assigned_user_id = NULL WHERE id = @id", "@id", id);
        }

        public static void Assign(long id, int? userId)
        {
            Db.Execute("UPDATE violations SET assigned_user_id = @u WHERE id = @id AND case_status IS NOT NULL", "@u", userId, "@id", id);
        }

        public static void AddNote(long id, int userId, string note)
        {
            Db.Execute("INSERT INTO case_notes (violation_id, user_id, note) VALUES (@id, @u, @n)", "@id", id, "@u", userId, "@n", note);
        }

        public static List<CaseNote> Notes(long id)
        {
            return Db.Query("SELECT n.note, n.created_at, u.full_name FROM case_notes n JOIN users u ON u.id = n.user_id " +
                            "WHERE n.violation_id = @id ORDER BY n.created_at",
                r => new CaseNote { Note = r.Str("note"), CreatedAt = r.Date("created_at").Value, UserName = r.Str("full_name") }, "@id", id);
        }

        // Idempotent insert for the AI outbox: an existing event_key returns the existing id.
        public static long Ingest(string eventKey, int cameraId, string cameraName, string category, string label,
                                  string severity, double confidence, DateTime detectedAtUtc, byte[] jpeg)
        {
            const string sql =
                "SET XACT_ABORT ON; BEGIN TRAN; " +
                "DECLARE @id BIGINT = (SELECT id FROM violations WITH (UPDLOCK, HOLDLOCK) WHERE event_key = @key); " +
                "IF @id IS NULL BEGIN " +
                "  INSERT INTO violations (event_key, camera_id, camera_name, category, label, severity, confidence, detected_at) " +
                "  VALUES (@key, @cam, @name, @cat, @label, @sev, @conf, @at); " +
                "  SET @id = SCOPE_IDENTITY(); " +
                "  IF @jpeg IS NOT NULL INSERT INTO violation_evidence (violation_id, jpeg) VALUES (@id, @jpeg); " +
                "END; COMMIT; SELECT @id;";
            var jpegArg = jpeg == null ? (object)DBNull.Value : jpeg;
            return Convert.ToInt64(Db.Scalar(sql, "@key", eventKey, "@cam", cameraId, "@name", cameraName, "@cat", category,
                "@label", label, "@sev", severity, "@conf", confidence, "@at", detectedAtUtc, "@jpeg", jpegArg));
        }
    }
}
