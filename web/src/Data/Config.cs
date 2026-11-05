using System;

namespace MiniWeb.Data
{
    // All settings come from environment variables (web/.env via docker compose), never Web.config,
    // so secrets stay out of the image and out of git.
    public static class Config
    {
        static string Env(string key, string fallback = null)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        static string Required(string key)
        {
            var value = Env(key);
            if (value == null) throw new InvalidOperationException(key + " belum diisi di web/.env");
            return value;
        }

        public static string ConnectionString
        {
            get
            {
                // Mono's SqlClient cannot negotiate TLS with SQL Server; traffic stays on the
                // private Docker network (smartcctv_mini).
                return string.Format("Server={0},{1};Database={2};User Id={3};Password={4};Encrypt=false;Max Pool Size=50",
                    Env("DB_HOST", "sqlserver"), Env("DB_PORT", "1433"), Env("DB_NAME", "smartcctv_mini"),
                    Env("DB_USER", "sa"), Required("DB_PASSWORD"));
            }
        }

        // Shared with fastapi/.env: the AI sends events and reads cameras with it.
        public static string IngestToken { get { return Required("MINI_INGEST_TOKEN"); } }

        // Shared with fastapi/.env: signs short-lived stream URLs handed to logged-in browsers.
        public static string StreamKey { get { return Required("MINI_STREAM_KEY"); } }

        // Browser-facing AI address. Empty = same host as this page, port AI_PUBLIC_PORT.
        public static string AiPublicUrl { get { return Env("AI_PUBLIC_URL", ""); } }
        public static string AiPublicPort { get { return Env("AI_PUBLIC_PORT", "8090"); } }

        // Server-to-server AI address on the Docker network (camera status in Pengaturan).
        public static string AiInternalUrl { get { return Env("AI_INTERNAL_URL", "http://smartcctv_mini_api:8090"); } }

        // Where evidence JPEGs go: "db" (table violation_evidence) or "file" (EVIDENCE_DIR,
        // e.g. a NAS share mounted by docker-compose.nas.yml).
        public static bool EvidenceToFile
        {
            get { return string.Equals(Env("EVIDENCE_STORAGE", "db"), "file", StringComparison.OrdinalIgnoreCase); }
        }

        public static string EvidenceDir { get { return Env("EVIDENCE_DIR", "/data/evidence"); } }

        public static readonly TimeSpan Wib = TimeSpan.FromHours(7);
    }
}
