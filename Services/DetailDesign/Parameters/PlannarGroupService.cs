using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Gộp elements có construction line collinear → plannar groups.
    /// Groups: T (transversal web), L (longitudinal web), A (arbitrary),
    ///         ST (stiff trans), SL (stiff long), BS (buckling).
    /// </summary>
    public static class PlannarGroupService
    {
        private const string LOG_PREFIX = "[PlannarGroupService]";
        private const double GROUP_TOLERANCE_MM = 5.0;

        public static List<PlannarGroup> ComputeGroups(List<StructuralElementModel> elements)
        {
            var result = new List<PlannarGroup>();
            var eligible = elements.Where(e => !e.IsHole && e.BaseStart != null && e.BaseEnd != null).ToList();

            // Classify each element into group type
            var entries = new List<(string groupType, double position, double angleDeg, StructuralElementModel elem)>();
            foreach (var e in eligible)
            {
                string gt = GetGroupType(e);
                if (gt == null) continue;
                double pos = GetPlanePosition(e);
                double angle = GetAngleDeg(e);
                entries.Add((gt, pos, angle, e));
            }

            // Group by type, then cluster by position within tolerance
            foreach (var typeGroup in entries.GroupBy(x => x.groupType).OrderBy(g => GroupSortKey(g.Key)))
            {
                var sorted = typeGroup.OrderBy(x => x.position).ToList();
                var clusters = ClusterByPosition(sorted, GROUP_TOLERANCE_MM);

                int idx = 1;
                foreach (var cluster in clusters)
                {
                    var pg = new PlannarGroup
                    {
                        GroupType = typeGroup.Key,
                        Index = idx++,
                        Position = cluster.Average(c => c.position),
                        AngleDeg = cluster.First().angleDeg,
                        MemberGuids = cluster.Select(c => c.elem.Guid).ToList(),
                        ThrowText = cluster.First().elem.ThrowVecX != 0 || cluster.First().elem.ThrowVecY != 0
                            ? $"({cluster.First().elem.ThrowVecX:F1},{cluster.First().elem.ThrowVecY:F1})"
                            : "?"
                    };
                    result.Add(pg);
                }
            }

            Debug.WriteLine($"{LOG_PREFIX} {result.Count} plannar groups computed");
            return result;
        }

        private static string GetGroupType(StructuralElementModel e)
        {
            // Dùng base edge angle thực tế thay vì OrientationClass (45° threshold quá thô)
            double angleDeg = GetBaseAngleDeg(e);
            bool isLong = angleDeg < 20.0 || angleDeg > 160.0;  // gần ngang (X)
            bool isTrans = angleDeg > 70.0 && angleDeg < 110.0; // gần đứng (Y)
            bool isArbitrary = !isLong && !isTrans;

            switch (e.ElemType)
            {
                case StructuralType.WebPlate:
                case StructuralType.ClosingBoxWeb:
                    return isArbitrary ? "A" : (isTrans ? "T" : "L");
                case StructuralType.Stiffener:
                    return isTrans ? "ST" : "SL";
                case StructuralType.BucklingStiffener:
                    return "BS";
                default:
                    return null;
            }
        }

        private static double GetBaseAngleDeg(StructuralElementModel e)
        {
            if (e.BaseStart == null || e.BaseEnd == null) return 0;
            double dx = e.BaseEnd.X - e.BaseStart.X;
            double dy = e.BaseEnd.Y - e.BaseStart.Y;
            return Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        }

        private static double GetPlanePosition(StructuralElementModel e)
        {
            // Construction line midpoint perpendicular position:
            // LONG (base gần ngang) → plane y=b → perpendicular = Y của construction line
            // TRANS (base gần đứng) → plane x=a → perpendicular = X
            // Arbitrary → dùng perpendicular distance from origin
            double my = (e.BaseStart.Y + e.BaseEnd.Y) / 2.0;
            double mx = (e.BaseStart.X + e.BaseEnd.X) / 2.0;
            double angleDeg = GetBaseAngleDeg(e);
            bool isLong = angleDeg < 20.0 || angleDeg > 160.0;
            bool isTrans = angleDeg > 70.0 && angleDeg < 110.0;

            if (isLong) return my;
            if (isTrans) return mx;
            // Arbitrary: perpendicular distance from origin to construction line
            double dx = e.BaseEnd.X - e.BaseStart.X, dy = e.BaseEnd.Y - e.BaseStart.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return my;
            double nx = -dy / len, ny = dx / len;
            return Math.Abs(mx * nx + my * ny);
        }

        private static double GetAngleDeg(StructuralElementModel e)
        {
            double dx = e.BaseEnd.X - e.BaseStart.X;
            double dy = e.BaseEnd.Y - e.BaseStart.Y;
            return Math.Atan2(Math.Abs(dy), Math.Abs(dx)) * 180.0 / Math.PI;
        }

        private static List<List<(string groupType, double position, double angleDeg, StructuralElementModel elem)>>
            ClusterByPosition(List<(string groupType, double position, double angleDeg, StructuralElementModel elem)> sorted, double tol)
        {
            var clusters = new List<List<(string, double, double, StructuralElementModel)>>();
            if (sorted.Count == 0) return clusters;

            var current = new List<(string, double, double, StructuralElementModel)> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].position - sorted[i - 1].position) <= tol)
                    current.Add(sorted[i]);
                else
                {
                    clusters.Add(current);
                    current = new List<(string, double, double, StructuralElementModel)> { sorted[i] };
                }
            }
            clusters.Add(current);
            return clusters;
        }

        private static int GroupSortKey(string gt)
        {
            switch (gt) { case "T": return 0; case "L": return 1; case "A": return 2; case "ST": return 3; case "SL": return 4; case "BS": return 5; default: return 9; }
        }
    }
}
