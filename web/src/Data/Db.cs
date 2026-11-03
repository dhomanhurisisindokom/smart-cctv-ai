using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace MiniWeb.Data
{
    // Thin ADO.NET helper: every query is parameterised, connections come from the pool.
    // Arguments are name/value pairs: Db.Query(sql, map, "@id", 5, "@name", "x").
    public static class Db
    {
        static SqlCommand Command(SqlConnection conn, string sql, object[] args)
        {
            var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            for (int i = 0; i + 1 < args.Length; i += 2)
                cmd.Parameters.AddWithValue((string)args[i], args[i + 1] ?? DBNull.Value);
            return cmd;
        }

        public static List<T> Query<T>(string sql, Func<IDataRecord, T> map, params object[] args)
        {
            var list = new List<T>();
            using (var conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                using (var cmd = Command(conn, sql, args))
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) list.Add(map(reader));
            }
            return list;
        }

        public static T One<T>(string sql, Func<IDataRecord, T> map, params object[] args) where T : class
        {
            var rows = Query(sql, map, args);
            return rows.Count > 0 ? rows[0] : null;
        }

        public static object Scalar(string sql, params object[] args)
        {
            using (var conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                using (var cmd = Command(conn, sql, args))
                {
                    var value = cmd.ExecuteScalar();
                    return value == DBNull.Value ? null : value;
                }
            }
        }

        public static int Count(string sql, params object[] args)
        {
            var value = Scalar(sql, args);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        public static int Execute(string sql, params object[] args)
        {
            using (var conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                using (var cmd = Command(conn, sql, args)) return cmd.ExecuteNonQuery();
            }
        }

        public static string Str(this IDataRecord r, string name)
        {
            var v = r[name];
            return v == DBNull.Value ? null : Convert.ToString(v);
        }

        public static DateTime? Date(this IDataRecord r, string name)
        {
            var v = r[name];
            return v == DBNull.Value ? (DateTime?)null : (DateTime)v;
        }

        public static int Int(this IDataRecord r, string name)
        {
            var v = r[name];
            return v == DBNull.Value ? 0 : Convert.ToInt32(v);
        }

        public static long Long(this IDataRecord r, string name)
        {
            var v = r[name];
            return v == DBNull.Value ? 0 : Convert.ToInt64(v);
        }

        public static bool Bool(this IDataRecord r, string name)
        {
            var v = r[name];
            return v != DBNull.Value && Convert.ToBoolean(v);
        }

        public static double Dbl(this IDataRecord r, string name)
        {
            var v = r[name];
            return v == DBNull.Value ? 0 : Convert.ToDouble(v);
        }
    }
}
