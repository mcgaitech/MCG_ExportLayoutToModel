using System;
using System.Data.SQLite;
using System.Diagnostics;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Data
{
    /// <summary>
    /// Seed profile catalog (EN10067 defaults) vào SQLite.
    /// Chạy 1 lần khi init DB — skip nếu đã có data.
    /// </summary>
    public static class ProfileCatalogSeeder
    {
        private const string LOG_PREFIX = "[ProfileCatalogSeeder]";

        /// <summary>Seed profiles nếu bảng trống</summary>
        public static void Seed()
        {
            Debug.WriteLine($"{LOG_PREFIX} Starting seed...");

            var connStr = $"Data Source={DetailDesignConstants.DB_PATH};Version=3;";
            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();

                // Kiểm tra đã có data chưa
                using (var countCmd = new SQLiteCommand("SELECT COUNT(*) FROM profiles", conn))
                {
                    var count = Convert.ToInt32(countCmd.ExecuteScalar());
                    if (count > 0)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Already seeded ({count} profiles). Skipping.");
                        return;
                    }
                }

                int seeded = 0;

                // HP profiles (Holland Profiles)
                seeded += InsertProfile(conn, "HP80x6",   "HP", 80,  6,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP100x6",  "HP", 100, 6,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP120x6",  "HP", 120, 6,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP120x7",  "HP", 120, 7,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP140x7",  "HP", 140, 7,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP160x8",  "HP", 160, 8,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP180x9",  "HP", 180, 9,   0, 0, "EN10067");
                seeded += InsertProfile(conn, "HP200x10", "HP", 200, 10,  0, 0, "EN10067");

                // FB profiles (Flat Bars)
                seeded += InsertProfile(conn, "FB60x6",   "FB", 60,  6,   0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB75x6",   "FB", 75,  6,   0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB75x8",   "FB", 75,  8,   0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB80x6",   "FB", 80,  6,   0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB100x8",  "FB", 100, 8,   0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB120x10", "FB", 120, 10,  0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB150x10", "FB", 150, 10,  0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB150x12", "FB", 150, 12,  0, 0, "EN10058");
                seeded += InsertProfile(conn, "FB200x12", "FB", 200, 12,  0, 0, "EN10058");

                // Angle profiles
                seeded += InsertProfile(conn, "L65x65x8",  "ANGLE", 65,  8, 65, 8, "EN10056");
                seeded += InsertProfile(conn, "L75x75x8",  "ANGLE", 75,  8, 75, 8, "EN10056");
                seeded += InsertProfile(conn, "L90x90x9",  "ANGLE", 90,  9, 90, 9, "EN10056");

                Debug.WriteLine($"{LOG_PREFIX} Seeded {seeded} profiles.");
            }
        }

        /// <summary>Insert 1 profile, return 1 nếu thành công</summary>
        private static int InsertProfile(SQLiteConnection conn, string code, string type,
            double height, double webThk, double flangeW, double flangeThk, string standard)
        {
            var sql = @"INSERT INTO profiles (guid, code, type, height, web_thk, flange_w, flange_thk, standard, block_cutout)
                        VALUES (@guid, @code, @type, @height, @web_thk, @flange_w, @flange_thk, @standard, @block_cutout)";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@code", code);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@height", height);
                cmd.Parameters.AddWithValue("@web_thk", webThk);
                cmd.Parameters.AddWithValue("@flange_w", flangeW);
                cmd.Parameters.AddWithValue("@flange_thk", flangeThk);
                cmd.Parameters.AddWithValue("@standard", standard);
                cmd.Parameters.AddWithValue("@block_cutout", $"{code}_Cutout");
                return cmd.ExecuteNonQuery();
            }
        }
    }
}
