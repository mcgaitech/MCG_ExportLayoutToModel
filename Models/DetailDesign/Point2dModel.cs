using System;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Điểm 2D thuần — thay thế Autodesk.AutoCAD.Geometry.Point2d.
    /// Dùng trong Models và Services để tránh phụ thuộc AutoCAD namespace.
    /// </summary>
    public class Point2dModel
    {
        /// <summary>Tọa độ X (mm)</summary>
        public double X { get; set; }

        /// <summary>Tọa độ Y (mm)</summary>
        public double Y { get; set; }

        /// <summary>Khởi tạo mặc định (0, 0)</summary>
        public Point2dModel() { }

        /// <summary>Khởi tạo với tọa độ</summary>
        public Point2dModel(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Tính khoảng cách đến điểm khác</summary>
        public double DistanceTo(Point2dModel other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString() => $"({X:F3}, {Y:F3})";
    }
}
