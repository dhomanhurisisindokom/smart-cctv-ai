using System;
using System.Collections.Generic;
using MiniWeb.Data;

namespace MiniWeb.Models
{
    public class User
    {
        public int Id;
        public string Username;
        public string FullName;
        public string Email;
        public string Role; // admin | supervisor | operator
        public string PasswordHash;
        public bool IsActive;
        public DateTime? LastLoginAt;

        public bool IsAdmin { get { return Role == "admin"; } }
    }

    // What the session keeps about the signed-in user.
    [Serializable]
    public class SessionUser
    {
        public int Id;
        public string Username;
        public string FullName;
        public string Role;
        public bool IsAdmin { get { return Role == "admin"; } }

        public string Initials
        {
            get
            {
                var parts = (FullName ?? Username ?? "?").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var s = "";
                foreach (var p in parts) if (s.Length < 2) s += char.ToUpperInvariant(p[0]);
                return s.Length > 0 ? s : "?";
            }
        }

        public string RoleLabel
        {
            get { return Role == "admin" ? "Admin" : Role == "supervisor" ? "Supervisor" : "Operator"; }
        }
    }

    public class Camera
    {
        public int Id;
        public string Name;
        public string Location;
        public string RtspUrl;
        public bool ApdEnabled;
        public bool VehicleEnabled;
        public string ApdMask;
        public string ApdPenutup;
        public string VehicleProfile;

        public bool AiEnabled { get { return (ApdEnabled || VehicleEnabled) && !string.IsNullOrEmpty(RtspUrl); } }

        // Host only: credentials in the RTSP URL are never shown back in the UI.
        public string Host
        {
            get
            {
                Uri uri;
                return !string.IsNullOrEmpty(RtspUrl) && Uri.TryCreate(RtspUrl, UriKind.Absolute, out uri) ? uri.Host : null;
            }
        }
    }

    public class Violation
    {
        public long Id;
        public int CameraId;
        public string CameraName;
        public string CameraLocation;
        public string Category; // apd | vehicle
        public string Label;
        public string Severity;
        public double Confidence;
        public DateTime DetectedAt; // UTC
        public string CaseStatus;   // null = alert; baru | diproses | selesai
        public DateTime? CaseOpenedAt;
        public int? AssignedUserId;
        public string AssignedName;
        public bool HasEvidence;

        public string CaseCode { get { return "CASE-" + Id; } }
        public bool IsCase { get { return CaseStatus != null; } }
        public string CategoryLabel { get { return Category == "apd" ? "APD" : "Vehicle"; } }
        public string CategoryTitle { get { return Category == "apd" ? "APD Detection" : "Vehicle Violation"; } }
    }

    public class CaseNote
    {
        public string UserName;
        public string Note;
        public DateTime CreatedAt;
    }

    public static class Fmt
    {
        static readonly System.Globalization.CultureInfo Id = new System.Globalization.CultureInfo("id-ID");

        public static DateTime Wib(DateTime utc) { return utc + Config.Wib; }

        // "02 Okt 2026, 15.37.29"
        public static string Full(DateTime utc) { return Wib(utc).ToString("dd MMM yyyy, HH.mm.ss", Id); }
        // "02 Okt, 15.37"
        public static string Short(DateTime utc) { return Wib(utc).ToString("dd MMM, HH.mm", Id); }
        public static string Number(double value, int decimals = 0) { return value.ToString("N" + decimals, Id); }

        public static string StatusText(string caseStatus)
        {
            switch (caseStatus)
            {
                case "baru": return "Baru";
                case "diproses": return "Diproses";
                case "selesai": return "Selesai";
                default: return "Alert";
            }
        }

        public static string StatusBadge(string caseStatus)
        {
            switch (caseStatus)
            {
                case "selesai": return "b-success";
                case "baru":
                case "diproses": return "b-warning";
                default: return "b-muted";
            }
        }
    }

    public class ViolationFilter
    {
        public string Category;  // apd | vehicle | null
        public string Range;     // today | 24h | 7d | 30d | all | custom
        public DateTime? From;   // custom, WIB date
        public DateTime? To;     // custom, WIB date (inclusive)
        public int? CameraId;
        public string Label;
        public string Status;    // alert | baru | diproses | selesai | open | case
        public int Page = 1;
        public int PageSize = 25;
    }

    public class PagedViolations
    {
        public List<Violation> Rows = new List<Violation>();
        public int Total;
        public int Page;
        public int PageSize;
        public int Pages { get { return Math.Max(1, (Total + PageSize - 1) / PageSize); } }
    }
}
