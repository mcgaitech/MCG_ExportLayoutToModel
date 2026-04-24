using System;
using System.Collections.Generic;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Tính Convex Hull bằng Graham Scan.
    /// Dùng cho OBB Calculator (PCA trên hull points).
    /// </summary>
    public static class ConvexHullHelper
    {
        /// <summary>
        /// Tính convex hull từ mảng điểm.
        /// </summary>
        /// <param name="points">Mảng điểm đầu vào (≥ 3 điểm)</param>
        /// <returns>Mảng điểm tạo thành convex hull (counter-clockwise)</returns>
        public static Point2dModel[] Compute(Point2dModel[] points)
        {
            if (points == null || points.Length < 3)
                return points;

            // Loại bỏ điểm trùng
            var unique = points.GroupBy(p => $"{p.X:F6},{p.Y:F6}")
                               .Select(g => g.First())
                               .ToList();

            if (unique.Count < 3)
                return unique.ToArray();

            // Tìm pivot — điểm thấp nhất (min Y, tie-break min X)
            var pivot = unique.OrderBy(p => p.Y).ThenBy(p => p.X).First();

            // Sort theo góc polar từ pivot
            var sorted = unique
                .Where(p => !(Math.Abs(p.X - pivot.X) < 1e-10 && Math.Abs(p.Y - pivot.Y) < 1e-10))
                .OrderBy(p => Math.Atan2(p.Y - pivot.Y, p.X - pivot.X))
                .ThenBy(p => pivot.DistanceTo(p))
                .ToList();

            // Graham scan
            var stack = new Stack<Point2dModel>();
            stack.Push(pivot);
            if (sorted.Count > 0) stack.Push(sorted[0]);

            for (int i = 1; i < sorted.Count; i++)
            {
                while (stack.Count > 1 && CrossProduct(SecondTop(stack), stack.Peek(), sorted[i]) <= 0)
                    stack.Pop();
                stack.Push(sorted[i]);
            }

            return stack.Reverse().ToArray();
        }

        /// <summary>
        /// Cross product (O→A) × (O→B).
        /// > 0 → counter-clockwise, ≤ 0 → clockwise or collinear.
        /// </summary>
        private static double CrossProduct(Point2dModel o, Point2dModel a, Point2dModel b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>Lấy phần tử thứ 2 từ đỉnh stack (không pop)</summary>
        private static Point2dModel SecondTop(Stack<Point2dModel> stack)
        {
            var top = stack.Pop();
            var second = stack.Peek();
            stack.Push(top);
            return second;
        }
    }
}
