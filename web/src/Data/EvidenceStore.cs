using System;
using System.IO;
using System.Linq;

namespace MiniWeb.Data
{
    // Evidence JPEGs as files under EVIDENCE_DIR: yyyy/MM/dd/<category>-cam<id>-<time>-<key>.jpg (WIB date).
    // Only the relative path goes into the database, so the folder can move to another mount.
    public static class EvidenceStore
    {
        static string Root { get { return Path.GetFullPath(Config.EvidenceDir); } }

        public static string Write(string eventKey, int cameraId, string category, DateTime detectedAtUtc, byte[] jpeg)
        {
            var wib = detectedAtUtc + Config.Wib;
            var key = new string(eventKey.Where(char.IsLetterOrDigit).Take(16).ToArray());
            var relative = string.Format("{0:yyyy}/{0:MM}/{0:dd}/{1}-cam{2}-{0:HHmmss}-{3}.jpg", wib, category, cameraId, key);
            var full = Resolve(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            // Temp file + rename: a reader never sees half a JPEG, and a retried delivery just
            // writes the same name again.
            var temp = full + ".tmp";
            File.WriteAllBytes(temp, jpeg);
            if (File.Exists(full)) File.Delete(full);
            File.Move(temp, full);
            return relative;
        }

        public static byte[] Read(string relative)
        {
            var full = Resolve(relative);
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }

        // Paths come from our own rows, but never let one point outside the evidence folder.
        static string Resolve(string relative)
        {
            var root = Root.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', '/')));
            if (!full.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Path bukti tidak valid");
            return full;
        }

        // For /api/health: can the web write to the evidence folder right now?
        public static string Check()
        {
            if (!Config.EvidenceToFile) return "db";
            try
            {
                Directory.CreateDirectory(Root);
                var probe = Path.Combine(Root, ".write-check-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return "file ok";
            }
            catch (Exception ex)
            {
                return "file error: " + ex.GetType().Name;
            }
        }
    }
}
