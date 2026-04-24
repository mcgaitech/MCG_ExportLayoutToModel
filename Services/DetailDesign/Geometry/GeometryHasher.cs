using System;
using System.Security.Cryptography;
using System.Text;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Tính MD5 hash từ WCS vertices — dùng cho dirty detection khi rescan.
    /// Hash thay đổi khi user stretch/move polyline.
    /// </summary>
    public static class GeometryHasher
    {
        /// <summary>
        /// Tính MD5 hash từ WCS vertices + area + perimeter.
        /// </summary>
        /// <param name="vertices">Đỉnh polyline đã transform về WCS</param>
        /// <param name="area">Diện tích polyline</param>
        /// <param name="perimeter">Chu vi polyline</param>
        /// <returns>MD5 hash string (lowercase hex, 32 chars)</returns>
        public static string Compute(Point2dModel[] vertices, double area, double perimeter)
        {
            if (vertices == null || vertices.Length == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < vertices.Length; i++)
            {
                sb.Append($"{vertices[i].X:F3},{vertices[i].Y:F3}|");
            }
            sb.Append($"A={area:F3}|P={perimeter:F3}");

            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        /// <summary>
        /// Tính MD5 hash từ WCS vertices (không cần area/perimeter).
        /// </summary>
        public static string Compute(Point2dModel[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < vertices.Length; i++)
            {
                sb.Append($"{vertices[i].X:F3},{vertices[i].Y:F3}|");
            }

            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
    }
}
