using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MiniWeb.Data
{
    public class DayCount
    {
        public DateTime Day; // WIB date
        public int Apd;
        public int Vehicle;
    }

    public class AreaCount
    {
        public int CameraId;
        public string Camera;
        public string Location;
        public int Apd;
        public int Vehicle;
        public int Total { get { return Apd + Vehicle; } }
    }

    public class Summary
    {
        public int Total;
        public int Apd;
        public int Vehicle;
        public int Cases;
        public int CasesDone;
        public int OpenCases;
        public double? AvgResponseMinutes;
    }

    public static class Stats
    {
        static void Range(List<object> args, StringBuilder where, DateTime? start, DateTime? end)
        {
            if (start.HasValue) { where.Append(" AND detected_at >= @start"); args.Add("@start"); args.Add(start.Value); }
            if (end.HasValue) { where.Append(" AND detected_at < @end"); args.Add("@end"); args.Add(end.Value); }
        }

        public static Summary Summary(DateTime? start, DateTime? end)
        {
            var args = new List<object>();
            var where = new StringBuilder("WHERE 1 = 1");
            Range(args, where, start, end);
            return Db.One(
                "SELECT COUNT(*) AS total, SUM(CASE WHEN category = 'apd' THEN 1 ELSE 0 END) AS apd, " +
                "SUM(CASE WHEN category = 'vehicle' THEN 1 ELSE 0 END) AS vehicle, " +
                "SUM(CASE WHEN case_status IS NOT NULL THEN 1 ELSE 0 END) AS cases, " +
                "SUM(CASE WHEN case_status = 'selesai' THEN 1 ELSE 0 END) AS done, " +
                "SUM(CASE WHEN case_status IN ('baru', 'diproses') THEN 1 ELSE 0 END) AS open_cases, " +
                "AVG(CASE WHEN case_opened_at IS NOT NULL THEN CAST(DATEDIFF(SECOND, detected_at, case_opened_at) AS FLOAT) / 60 END) AS response " +
                "FROM violations " + where,
                r => new Summary
                {
                    Total = r.Int("total"), Apd = r.Int("apd"), Vehicle = r.Int("vehicle"), Cases = r.Int("cases"),
                    CasesDone = r.Int("done"), OpenCases = r.Int("open_cases"),
                    AvgResponseMinutes = r["response"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["response"]),
                }, args.ToArray());
        }

        // One bar group per WIB day from firstDay to lastDay inclusive (days with no events are zero).
        public static List<DayCount> PerDay(DateTime firstDay, DateTime lastDay)
        {
            var start = firstDay.Date - Config.Wib;
            var end = lastDay.Date.AddDays(1) - Config.Wib;
            var rows = Db.Query(
                "SELECT CAST(DATEADD(HOUR, 7, detected_at) AS DATE) AS d, category, COUNT(*) AS n FROM violations " +
                "WHERE detected_at >= @start AND detected_at < @end GROUP BY CAST(DATEADD(HOUR, 7, detected_at) AS DATE), category",
                r => new { Day = r.Date("d").Value, Category = r.Str("category"), N = r.Int("n") }, "@start", start, "@end", end);
            var days = new List<DayCount>();
            for (var d = firstDay.Date; d <= lastDay.Date; d = d.AddDays(1))
            {
                days.Add(new DayCount
                {
                    Day = d,
                    Apd = rows.Where(x => x.Day == d && x.Category == "apd").Sum(x => x.N),
                    Vehicle = rows.Where(x => x.Day == d && x.Category == "vehicle").Sum(x => x.N),
                });
            }
            return days;
        }

        public static DateTime? FirstDetection()
        {
            var value = Db.Scalar("SELECT MIN(detected_at) FROM violations");
            return value == null ? (DateTime?)null : (DateTime)value + Config.Wib;
        }

        public static List<AreaCount> PerCamera(DateTime? start, DateTime? end)
        {
            var args = new List<object>();
            var where = new StringBuilder("WHERE 1 = 1");
            Range(args, where, start, end);
            return Db.Query(
                "SELECT v.camera_id, MAX(v.camera_name) AS camera_name, MAX(c.location) AS location, " +
                "SUM(CASE WHEN v.category = 'apd' THEN 1 ELSE 0 END) AS apd, SUM(CASE WHEN v.category = 'vehicle' THEN 1 ELSE 0 END) AS vehicle " +
                "FROM violations v LEFT JOIN cameras c ON c.id = v.camera_id " + where.ToString().Replace("detected_at", "v.detected_at") +
                " GROUP BY v.camera_id ORDER BY COUNT(*) DESC",
                r => new AreaCount
                {
                    CameraId = r.Int("camera_id"), Camera = r.Str("camera_name"), Location = r.Str("location"),
                    Apd = r.Int("apd"), Vehicle = r.Int("vehicle"),
                }, args.ToArray());
        }
    }

    // Grouped bar chart in the mockup's style (APD amber, Vehicle red), drawn server-side as SVG.
    public static class BarChart
    {
        public const string ApdColor = "#B57712";
        public const string VehicleColor = "#B8021F";
        static readonly CultureInfo Id = new CultureInfo("id-ID");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static int NiceMax(int max)
        {
            if (max <= 4) return 4;
            var step = Math.Pow(10, Math.Floor(Math.Log10(max / 4.0)));
            foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
                if (step * m * 4 >= max) return (int)Math.Ceiling(step * m * 4);
            return max;
        }

        public static string Bars(List<DayCount> days, int height, string ariaLabel)
        {
            const double left = 40, right = 880;
            double top = 10, bottom = height - 34;
            var max = NiceMax(days.Count == 0 ? 0 : days.Max(d => Math.Max(d.Apd, d.Vehicle)));
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg viewBox=\"0 0 900 {0}\" width=\"100%\" role=\"img\" aria-label=\"{1}\" style=\"display:block;overflow:visible\">",
                height, System.Web.HttpUtility.HtmlAttributeEncode(ariaLabel));
            sb.Append("<g font-family=\"Inter, sans-serif\" font-size=\"11\" fill=\"#61686F\">");
            for (int i = 0; i <= 4; i++)
            {
                var y = bottom - (bottom - top) * i / 4;
                sb.AppendFormat(Inv, "<line x1=\"{0}\" y1=\"{1:0.#}\" x2=\"{2}\" y2=\"{1:0.#}\" stroke=\"#DEE2E6\"/><text x=\"32\" y=\"{3:0.#}\" text-anchor=\"end\">{4}</text>",
                    left, y, right, y + 4, max * i / 4);
            }
            var slot = days.Count == 0 ? 0 : (right - left) / days.Count;
            var bar = Math.Max(2, Math.Min(22, slot / 2 - 4));
            var every = Math.Max(1, (int)Math.Ceiling(days.Count / 15.0));
            for (int i = 0; i < days.Count; i++)
            {
                var cx = left + slot * (i + 0.5);
                if (i % every == 0)
                {
                    var label = days.Count <= 7 ? days[i].Day.ToString("ddd", Id) : days[i].Day.ToString("dd/MM", Id);
                    sb.AppendFormat(Inv, "<text x=\"{0:0.#}\" y=\"{1:0.#}\" text-anchor=\"middle\">{2}</text>", cx, bottom + 18, label);
                }
            }
            sb.Append("</g>");
            for (int i = 0; i < days.Count; i++)
            {
                var cx = left + slot * (i + 0.5);
                Rect(sb, cx - bar - 2, bar, days[i].Apd, max, top, bottom, ApdColor, days[i].Day, "APD");
                Rect(sb, cx + 2, bar, days[i].Vehicle, max, top, bottom, VehicleColor, days[i].Day, "Vehicle");
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        static void Rect(StringBuilder sb, double x, double w, int value, int max, double top, double bottom, string color, DateTime day, string name)
        {
            if (value <= 0) return;
            var h = (bottom - top) * value / max;
            sb.AppendFormat(Inv, "<rect x=\"{0:0.#}\" y=\"{1:0.#}\" width=\"{2:0.#}\" height=\"{3:0.#}\" rx=\"3\" fill=\"{4}\"><title>{5} {6}: {7}</title></rect>",
                x, bottom - h, w, h, color, day.ToString("dd MMM", Id), name, value);
        }
    }
}
