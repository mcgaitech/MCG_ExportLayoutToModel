using System;
using System.Collections.Generic;
using System.Diagnostics;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Tính kích thước nhỏ nhất (perpendicular distance giữa 2 cạnh dài nhất song song).
    ///
    /// Ứng dụng:
    /// - Web plate / Bracket / ClosingBoxWeb → thickness (plan view là rectangular)
    /// - Flange → width (plan view là rectangular)
    ///
    /// Tất cả 3 loại đều là "hình chữ nhật (có thể có vát đầu)" trên plan view
    /// → kích thước ngắn = khoảng cách vuông góc giữa 2 cạnh dài nhất song song.
    /// </summary>
    public static class ThicknessCalculator
    {
        private const string LOG_PREFIX = "[ThicknessCalculator]";

        // Parallel tolerance — góc lệch tối đa giữa 2 cạnh để coi là song song.
        // 1° là ngưỡng CAD-strict: chỉ nhận sai số snap/rounding, loại shape lệch thật.
        private const double PARALLEL_ANGLE_TOLERANCE_DEG = 1.0;

        // Candidate filter — chỉ xét cạnh >= 50% cạnh dài nhất khi search cặp parallel.
        // Bỏ qua các cạnh ngắn như taper/chamfer đầu mút.
        private const double CANDIDATE_LENGTH_RATIO = 0.5;

        /// <summary>
        /// Thickness cho Web plate / Bracket / ClosingBoxWeb.
        /// Trả về 0 nếu không tính được.
        /// </summary>
        public static double Calculate(Point2dModel[] vertices)
        {
            double d = CalculateMinDimension(vertices, out string reason);
            if (d > 0) return RoundThickness(d);

            Debug.WriteLine($"{LOG_PREFIX} Thickness calc failed: {reason}");
            return 0;
        }

        /// <summary>
        /// Flange width — cùng algorithm, trả về -1 nếu không phải rectangular.
        /// </summary>
        public static double CalculateFlangeWidth(Point2dModel[] vertices)
        {
            double d = CalculateMinDimension(vertices, out string reason);
            if (d > 0) return RoundThickness(d);

            Debug.WriteLine($"{LOG_PREFIX} Flange width calc failed: {reason}");
            return -1; // "Other" — non-rectangular
        }

        /// <summary>
        /// Lấy thông tin 2 cạnh parallel được V3 pick (cho base-edge determination).
        /// Trả về false nếu shape không rectangular.
        /// </summary>
        public static bool TryGetParallelPair(Point2dModel[] vertices,
            out Point2dModel e1Start, out Point2dModel e1End,
            out Point2dModel e2Start, out Point2dModel e2End,
            out double distance)
        {
            e1Start = e1End = e2Start = e2End = default(Point2dModel);
            distance = 0;

            var edges = GetCandidateEdges(vertices);
            if (edges == null) return false;

            if (!FindBestParallelPair(edges, out var e1, out var e2, out _)) return false;

            int j1 = (e1.StartIdx + 1) % vertices.Length;
            int j2 = (e2.StartIdx + 1) % vertices.Length;
            e1Start = vertices[e1.StartIdx];
            e1End = vertices[j1];
            e2Start = vertices[e2.StartIdx];
            e2End = vertices[j2];
            distance = PerpendicularDistance(e1, vertices[e1.StartIdx], vertices[e2.StartIdx]);
            return true;
        }

        /// <summary>
        /// Algorithm V3 — search all edge pairs for best parallel pair.
        ///
        /// Các bước:
        /// 1. Compute tất cả cạnh kèm length + direction.
        /// 2. Filter: chỉ giữ cạnh length >= 50% cạnh dài nhất (bỏ taper ngắn).
        /// 3. Với mỗi cặp (i, j): check parallel (angle <= 1°).
        ///    Cặp parallel nào có score = min(len_i, len_j) lớn nhất → thắng.
        /// 4. Perpendicular distance giữa cặp thắng = min dimension.
        /// 5. Nếu không có cặp parallel → trả về -1.
        /// </summary>
        /// <returns>Min dimension (mm), hoặc -1 nếu shape không rectangular</returns>
        private static double CalculateMinDimension(Point2dModel[] vertices, out string reason)
        {
            reason = "";
            var edges = GetCandidateEdges(vertices);
            if (edges == null)
            {
                reason = $"too few vertices ({vertices?.Length ?? 0})";
                return -1;
            }
            if (edges.Count < 2)
            {
                reason = "degenerate shape (<2 non-zero edges)";
                return -1;
            }
            if (!FindBestParallelPair(edges, out var e1, out var e2, out double angleDeg))
            {
                reason = $"no parallel pair found among {edges.Count} candidates (n={vertices.Length})";
                return -1;
            }

            double dist = PerpendicularDistance(e1, vertices[e1.StartIdx], vertices[e2.StartIdx]);
            Debug.WriteLine($"{LOG_PREFIX} V3 OK: n={vertices.Length} cand={edges.Count} e1={e1.Length:F1} e2={e2.Length:F1} angle={angleDeg:F2}° dist={dist:F2}");
            return dist;
        }

        /// <summary>Compute edges + filter theo CANDIDATE_LENGTH_RATIO. Return null nếu vertices không hợp lệ.</summary>
        private static List<Edge> GetCandidateEdges(Point2dModel[] vertices)
        {
            if (vertices == null || vertices.Length < 3) return null;

            int n = vertices.Length;
            var all = new List<Edge>(n);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double dx = vertices[j].X - vertices[i].X;
                double dy = vertices[j].Y - vertices[i].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-6)
                {
                    all.Add(new Edge { StartIdx = i, Length = len, DirX = dx / len, DirY = dy / len });
                }
            }

            double maxLen = 0;
            foreach (var e in all) if (e.Length > maxLen) maxLen = e.Length;
            double minLen = maxLen * CANDIDATE_LENGTH_RATIO;
            return all.FindAll(e => e.Length >= minLen);
        }

        /// <summary>Tìm cặp (a,b) parallel có score min(lenA, lenB) lớn nhất.</summary>
        private static bool FindBestParallelPair(List<Edge> candidates,
            out Edge bestE1, out Edge bestE2, out double bestAngleDeg)
        {
            bestE1 = bestE2 = default(Edge);
            bestAngleDeg = 0;
            double bestScore = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    var a = candidates[i];
                    var b = candidates[j];
                    double dot = Math.Min(1.0, Math.Abs(a.DirX * b.DirX + a.DirY * b.DirY));
                    double angleDeg = Math.Acos(dot) * 180.0 / Math.PI;
                    if (angleDeg > PARALLEL_ANGLE_TOLERANCE_DEG) continue;

                    double score = Math.Min(a.Length, b.Length);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestE1 = a;
                        bestE2 = b;
                        bestAngleDeg = angleDeg;
                    }
                }
            }
            return bestScore > 0;
        }

        /// <summary>Khoảng cách vuông góc từ p đến đường thẳng qua anchorOnE1 theo hướng e1.</summary>
        private static double PerpendicularDistance(Edge e1, Point2dModel anchorOnE1, Point2dModel p)
        {
            double nx = -e1.DirY;
            double ny = e1.DirX;
            return Math.Abs((p.X - anchorOnE1.X) * nx + (p.Y - anchorOnE1.Y) * ny);
        }

        /// <summary>Làm tròn thickness: 9.999→10, 10.0001→10.</summary>
        public static double RoundThickness(double value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private struct Edge
        {
            public int StartIdx;
            public double Length;
            public double DirX;
            public double DirY;
        }
    }
}
