using System;
using System.Security.Cryptography;
using System.Text;

namespace MiniWeb.Data
{
    public static class PasswordHasher
    {
        const int Iterations = 100000;

        // Format shared with sqlserver/migrate/seed.py: pbkdf2_sha256$<iter>$<salt b64>$<hash b64>.
        public static string Hash(string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            return string.Format("pbkdf2_sha256${0}${1}${2}", Iterations, Convert.ToBase64String(salt),
                Convert.ToBase64String(Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, 32)));
        }

        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
            var parts = stored.Split('$');
            int iterations;
            if (parts.Length != 4 || parts[0] != "pbkdf2_sha256" || !int.TryParse(parts[1], out iterations)) return false;
            try
            {
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(parts[2]), iterations, expected.Length);
                return FixedEquals(expected, actual);
            }
            catch (FormatException) { return false; }
        }

        // PBKDF2-HMAC-SHA256 written out: Mono's Rfc2898DeriveBytes only does SHA-1.
        static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int length)
        {
            using (var hmac = new HMACSHA256(password))
            {
                var output = new byte[length];
                int blocks = (length + 31) / 32;
                for (int block = 1; block <= blocks; block++)
                {
                    var input = new byte[salt.Length + 4];
                    Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
                    input[salt.Length] = (byte)(block >> 24);
                    input[salt.Length + 1] = (byte)(block >> 16);
                    input[salt.Length + 2] = (byte)(block >> 8);
                    input[salt.Length + 3] = (byte)block;
                    var u = hmac.ComputeHash(input);
                    var t = (byte[])u.Clone();
                    for (int i = 1; i < iterations; i++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int j = 0; j < t.Length; j++) t[j] ^= u[j];
                    }
                    Buffer.BlockCopy(t, 0, output, (block - 1) * 32, Math.Min(32, length - (block - 1) * 32));
                }
                return output;
            }
        }

        public static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static bool TokenEquals(string a, string b)
        {
            return a != null && b != null && FixedEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }
    }

    // Stream URLs carry "<expiry>.<signature>"; fastapi/mini/main.py checks the same HMAC.
    public static class StreamToken
    {
        public static string Create(TimeSpan lifetime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var expiry = ((long)(DateTime.UtcNow + lifetime - epoch).TotalSeconds).ToString();
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.StreamKey)))
            {
                var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(expiry)))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                return expiry + "." + sig;
            }
        }
    }
}
