using System;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Tính Oriented Bounding Box (OBB) bằng PCA trên convex hull.
    /// Trả về OBBResult với length, width (thickness), angle, aspect ratio.
    /// </summary>
    public static class OBBCalculator
    {
        private const string LOG_PREFIX = "[OBBCalculator]";

        /// <summary>
        /// Tính OBB cho mảng vertices (WCS).
        /// </summary>
        /// <param name="vertices">Đỉnh polyline đã transform về WCS</param>
        /// <returns>OBBResult hoặc null nếu không đủ dữ liệu</returns>
        public static OBBResult Compute(Point2dModel[] vertices)
        {
            if (vertices == null || vertices.Length < 3)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: Not enough vertices ({vertices?.Length ?? 0})");
                return null;
            }

            // Bước 1: Convex Hull
            var hull = ConvexHullHelper.Compute(vertices);
            if (hull.Length < 3)
            {
                Debug.WriteLine($"{LOG_PREFIX} WARNING: Degenerate hull ({hull.Length} points)");
                return null;
            }

            // Bước 2: PCA — covariance matrix trên hull
            double meanX = hull.Average(p => p.X);
            double meanY = hull.Average(p => p.Y);

            double cxx = 0, cxy = 0, cyy = 0;
            foreach (var p in hull)
            {
                double dx = p.X - meanX;
                double dy = p.Y - meanY;
                cxx += dx * dx;
                cxy += dx * dy;
                cyy += dy * dy;
            }
            int n = hull.Length;
            cxx /= n;
            cxy /= n;
            cyy /= n;

            // Eigenvalues của 2×2 symmetric matrix
            double trace = cxx + cyy;
            double det = cxx * cyy - cxy * cxy;
            double disc = Math.Sqrt(Math.Max(0, trace * trace / 4.0 - det));
            // double lambda1 = trace / 2.0 + disc; // major (không dùng trực tiếp)
            double lambda1 = trace / 2.0 + disc;

            // Eigenvector cho lambda1 (major axis)
            double majX, majY;
            if (Math.Abs(cxy) > 1e-10)
            {
                majX = lambda1 - cyy;
                majY = cxy;
                double len = Math.Sqrt(majX * majX + majY * majY);
                majX /= len;
                majY /= len;
            }
            else
            {
                // Đã axis-aligned
                if (cxx >= cyy) { majX = 1; majY = 0; }
                else { majX = 0; majY = 1; }
            }

            // Minor axis (vuông góc)
            double minX = -majY;
            double minY = majX;

            // Bước 3: Project hull lên 2 trục → extent
            double minMaj = double.MaxValue, maxMaj = double.MinValue;
            double minMin = double.MaxValue, maxMin = double.MinValue;
            foreach (var p in hull)
            {
                double dx = p.X - meanX;
                double dy = p.Y - meanY;
                double pmaj = dx * majX + dy * majY;
                double pmin = dx * minX + dy * minY;
                minMaj = Math.Min(minMaj, pmaj);
                maxMaj = Math.Max(maxMaj, pmaj);
                minMin = Math.Min(minMin, pmin);
                maxMin = Math.Max(maxMin, pmin);
            }

            double length = maxMaj - minMaj;
            double width = maxMin - minMin;

            // Bước 4: Validate với area method
            double areaOBB = length * width;
            double areaPoly = ComputePolygonArea(vertices);
            double ratio = areaOBB > 1e-6 ? areaPoly / areaOBB : 0;

            // Non-rectangular (ratio < 0.85) → dùng area/length cho thickness
            double finalWidth = ratio >= 0.85
                ? width
                : (length > 1e-6 ? areaPoly / length : width);

            double aspectRatio = finalWidth > 0.001 ? length / finalWidth : 0;

            return new OBBResult
            {
                Center = new Point2dModel(meanX, meanY),
                Length = length,
                Width = finalWidth,
                AngleRad = Math.Atan2(majY, majX),
                MajorAxis = new Point2dModel(majX, majY),
                MinorAxis = new Point2dModel(minX, minY),
                AspectRatio = aspectRatio
            };
        }

        /// <summary>
        /// Tính diện tích polygon bằng Shoelace formula.
        /// </summary>
        private static double ComputePolygonArea(Point2dModel[] pts)
        {
            double area = 0;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) / 2.0;
        }
    }
}
