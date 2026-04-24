using System;
using System.Collections.Generic;
using System.Diagnostics;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Phát hiện bended plate (L-shape, U-shape, multi-bend) từ polyline.
    ///
    /// PHASE A — Detection only:
    /// - Identify suspected bended plates
    /// - Log warning với chi tiết
    /// - KHÔNG split (sẽ implement Phase B sau khi verify detection accuracy)
    ///
    /// Detection criteria (any of):
    /// 1. Vertex count > 6 (rectangle = 4, chamfered ends thường ≤ 6)
    /// 2. V3 succeeds nhưng total polyline perimeter > BENDED_PERIMETER_RATIO × sum(2 V3 edges)
    ///    → có nhiều segments dài ngoài 2 cạnh V3 picked = bend pieces
    /// </summary>
    public static class BendedPlateAnalyzer
    {
        private const string LOG_PREFIX = "[BendedPlateAnalyzer]";

        // Detection thresholds
        private const int VERTEX_COUNT_THRESHOLD = 6;          // > 6 vertices → suspect bended
        private const double BENDED_PERIMETER_RATIO = 2.5;     // P > 2.5 × (L1 + L2) → bended
        private const double BEND_ANGLE_THRESHOLD_DEG = 5.0;   // direction change > 5° = bend

        /// <summary>
        /// Scan all elements, log suspected bended plates.
        /// Trả về số lượng bended plates phát hiện được.
        /// </summary>
        public static int DetectAndLog(List<StructuralElementModel> elements)
        {
            int count = 0;
            foreach (var elem in elements)
            {
                if (elem.IsHole) continue;
                if (!IsApplicableType(elem.ElemType)) continue;

                if (IsBended(elem, out string reason))
                {
                    count++;
                    Debug.WriteLine($"{LOG_PREFIX} BENDED detected: {ShortGuid(elem)} {elem.ElemType} " +
                                    $"vertices={elem.VerticesWCS?.Length} | reason: {reason}");
                }
            }
            Debug.WriteLine($"{LOG_PREFIX} Total bended plates detected: {count}");
            return count;
        }

        /// <summary>
        /// Detect bended plate cho 1 element. Out reason để debug.
        /// </summary>
        public static bool IsBended(StructuralElementModel elem, out string reason)
        {
            reason = "";
            var verts = elem.VerticesWCS;
            if (verts == null || verts.Length < 4) return false;

            int n = verts.Length;

            // Criterion 1: vertex count
            bool hasManyVertices = n > VERTEX_COUNT_THRESHOLD;

            // Criterion 2: perimeter ratio (cần V3 result)
            bool hasHighPerimeterRatio = false;
            double perimeter = ComputePerimeter(verts);
            if (Parameters.ThicknessCalculator.TryGetParallelPair(verts,
                    out var e1s, out var e1e, out var e2s, out var e2e, out _))
            {
                double l1 = Distance(e1s, e1e);
                double l2 = Distance(e2s, e2e);
                double sum = l1 + l2;
                if (sum > 1e-6)
                {
                    double ratio = perimeter / sum;
                    if (ratio > BENDED_PERIMETER_RATIO)
                    {
                        hasHighPerimeterRatio = true;
                        reason = $"perimeter ratio {ratio:F2} > {BENDED_PERIMETER_RATIO} (P={perimeter:F0}, L1+L2={sum:F0})";
                    }
                }
            }

            // Criterion 3: bend count via direction change > threshold
            int bendCount = CountBendsInPolyline(verts, BEND_ANGLE_THRESHOLD_DEG);
            //   Rectangle = 4 right-angle bends. Bended plate = 4 + extra
            bool hasExtraBends = bendCount > 4;

            if (hasManyVertices || hasHighPerimeterRatio || hasExtraBends)
            {
                if (string.IsNullOrEmpty(reason))
                {
                    var parts = new List<string>();
                    if (hasManyVertices) parts.Add($"vertices={n}>{VERTEX_COUNT_THRESHOLD}");
                    if (hasExtraBends) parts.Add($"bends={bendCount}>4");
                    reason = string.Join(", ", parts);
                }
                return true;
            }
            return false;
        }

        // ─────────── Helpers ───────────

        private static bool IsApplicableType(StructuralType t)
        {
            return t == StructuralType.WebPlate
                || t == StructuralType.Stiffener
                || t == StructuralType.BucklingStiffener
                || t == StructuralType.Bracket
                || t == StructuralType.ClosingBoxWeb
                || t == StructuralType.Flange;
        }

        private static double ComputePerimeter(Point2dModel[] verts)
        {
            double p = 0;
            int n = verts.Length;
            for (int i = 0; i < n; i++)
                p += Distance(verts[i], verts[(i + 1) % n]);
            return p;
        }

        /// <summary>Đếm số bends trong polyline với góc lệch > threshold.</summary>
        private static int CountBendsInPolyline(Point2dModel[] verts, double angleThresholdDeg)
        {
            int n = verts.Length;
            if (n < 3) return 0;
            int count = 0;
            // Direction vectors of 2 consecutive edges meeting at vertex V:
            //   d1 = V - prev, d2 = next - V  (both "outgoing" along polyline traversal)
            //   Straight (no bend) → d1 // d2 → dot ≈ +1
            //   90° bend           → dot ≈ 0
            //   Bend if angle(d1,d2) > threshold → dot < cos(threshold)
            double cosTol = Math.Cos(angleThresholdDeg * Math.PI / 180.0);
            for (int i = 0; i < n; i++)
            {
                var prev = verts[(i - 1 + n) % n];
                var cur = verts[i];
                var next = verts[(i + 1) % n];
                double d1x = cur.X - prev.X, d1y = cur.Y - prev.Y;
                double d2x = next.X - cur.X, d2y = next.Y - cur.Y;
                double l1 = Math.Sqrt(d1x * d1x + d1y * d1y);
                double l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                if (l1 < 1e-6 || l2 < 1e-6) continue;
                d1x /= l1; d1y /= l1; d2x /= l2; d2y /= l2;
                double dot = d1x * d2x + d1y * d2y;
                if (dot < cosTol) count++;
            }
            return count;
        }

        private static double Distance(Point2dModel a, Point2dModel b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string ShortGuid(StructuralElementModel e)
            => e.Guid?.Length >= 8 ? e.Guid.Substring(0, 8) : e.Guid;
    }
}
