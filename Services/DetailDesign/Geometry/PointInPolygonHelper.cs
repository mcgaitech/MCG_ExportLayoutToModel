using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Kiểm tra điểm nằm trong polygon bằng Ray Casting algorithm.
    /// Dùng để xác định polyline nào là cutout (nằm bên trong polyline khác).
    /// </summary>
    public static class PointInPolygonHelper
    {
        /// <summary>
        /// Kiểm tra điểm P có nằm trong polygon không.
        /// Ray casting: bắn tia từ P sang phải, đếm số lần cắt cạnh polygon.
        /// Số lẻ → bên trong, số chẵn → bên ngoài.
        /// </summary>
        /// <param name="point">Điểm cần kiểm tra</param>
        /// <param name="polygon">Mảng đỉnh polygon (closed)</param>
        /// <returns>true nếu điểm nằm trong polygon</returns>
        public static bool IsInside(Point2dModel point, Point2dModel[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;

            bool inside = false;
            int n = polygon.Length;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = polygon[i].Y;
                double yj = polygon[j].Y;
                double xi = polygon[i].X;
                double xj = polygon[j].X;

                // Kiểm tra tia ngang từ point cắt cạnh (i, j)
                if ((yi > point.Y) != (yj > point.Y))
                {
                    double xIntersect = xi + (point.Y - yi) / (yj - yi) * (xj - xi);
                    if (point.X < xIntersect)
                        inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Kiểm tra polygon A nằm hoàn toàn bên trong polygon B.
        /// Dùng centroid của A để test (đủ chính xác cho cutout detection).
        /// </summary>
        /// <param name="inner">Polygon có thể là cutout</param>
        /// <param name="outer">Polygon có thể là outer boundary</param>
        /// <returns>true nếu centroid của inner nằm trong outer</returns>
        public static bool IsContainedIn(Point2dModel[] inner, Point2dModel[] outer)
        {
            if (inner == null || outer == null || inner.Length < 3 || outer.Length < 3)
                return false;

            // Tính centroid của inner
            double cx = 0, cy = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                cx += inner[i].X;
                cy += inner[i].Y;
            }
            cx /= inner.Length;
            cy /= inner.Length;

            return IsInside(new Point2dModel(cx, cy), outer);
        }
    }
}
