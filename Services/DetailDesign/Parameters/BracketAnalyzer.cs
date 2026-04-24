using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Classify brackets thành sub-types: OB / IB / BF / B.
    /// - Bracket tại BS → BF
    /// - Bracket tại Stiffener → OB/IB (theo throw vector) hoặc B (fallback)
    /// </summary>
    public class BracketAnalyzer : IBracketAnalyzer
    {
        private const string LOG_PREFIX = "[BracketAnalyzer]";

        /// <summary>Classify brackets sub-types</summary>
        public List<BracketModel> ClassifyBrackets(
            List<StructuralElementModel> elements,
            List<StiffenerModel> stiffenerModels,
            PanelContext panel)
        {
            Debug.WriteLine($"{LOG_PREFIX} Classifying brackets...");

            var bracketElements = elements.Where(e => e.ElemType == StructuralType.Bracket).ToList();
            var stiffOnly = elements.Where(e => e.ElemType == StructuralType.Stiffener).ToList();
            var bsOnly = elements.Where(e => e.ElemType == StructuralType.BucklingStiffener).ToList();

            if (bracketElements.Count == 0)
            {
                Debug.WriteLine($"{LOG_PREFIX} No brackets to classify.");
                return new List<BracketModel>();
            }

            var result = new List<BracketModel>();
            int obCount = 0, ibCount = 0, bfCount = 0, bCount = 0;

            foreach (var bracketElem in bracketElements)
            {
                var bracketModel = new BracketModel
                {
                    Guid = Guid.NewGuid().ToString(),
                    PanelGuid = bracketElem.PanelGuid,
                    ElemGuid = bracketElem.Guid,
                    Thickness = bracketElem.ObbWidth
                };

                // Nếu TopologyEngine đã classify BF (long-face contact BS) → giữ nguyên, bỏ qua OB/IB
                if (bracketElem.BracketSubType == "BF")
                {
                    bracketModel.Type = BracketType.OB;
                    bracketModel.SubType = "BF";
                    // Tìm BS gần nhất để set StiffenerGuid (dùng long-face contact)
                    var bsNear = FindNearest(bracketElem, bsOnly);
                    bracketModel.StiffenerGuid = bsNear.elem?.Guid;
                    bfCount++;
                    Debug.WriteLine($"{LOG_PREFIX} Bracket {bracketElem.AcadHandle}: BF (pre-classified by TopologyEngine)");
                    result.Add(bracketModel);
                    continue;
                }

                var (se1, se2) = GetShortEdgeVertexPairs(bracketElem);
                bool useFallback = se1 == null;

                // Kiểm tra tiếp xúc tại cạnh ngắn — chỉ với Stiffener (BS đã xử lý ở TopologyEngine)
                var seStiff = useFallback
                    ? FindNearest(bracketElem, stiffOnly)
                    : FindNearestAtShortEdge(se1, se2, stiffOnly);

                double stiffTol = useFallback ? DetailDesignConstants.BRACKET_END_GAP_MAX : DetailDesignConstants.TOLERANCE_CONTACT;
                bool shortEdgeTouchesStiff = seStiff.elem != null && seStiff.dist < stiffTol;

                if (shortEdgeTouchesStiff)
                {
                    // Cạnh ngắn tiếp xúc Stiffener → OB/IB theo IsEdge của Stiffener:
                    //   Edge stiffener (ở biên panel) → OB
                    //   Interior stiffener           → IB
                    bracketModel.StiffenerGuid = seStiff.elem.Guid;

                    var stiffModel = stiffenerModels.FirstOrDefault(sm => sm.ElemGuid == seStiff.elem.Guid);

                    if (stiffModel != null)
                    {
                        if (stiffModel.IsEdge)
                        {
                            bracketModel.Type = BracketType.OB; bracketModel.SubType = "OB"; obCount++;
                        }
                        else
                        {
                            bracketModel.Type = BracketType.IB; bracketModel.SubType = "IB"; ibCount++;
                        }
                        Debug.WriteLine($"{LOG_PREFIX} Bracket {bracketElem.AcadHandle}: {bracketModel.SubType} (stiff IsEdge={stiffModel.IsEdge}, stiff={seStiff.elem.AcadHandle})");
                    }
                    else
                    {
                        bracketModel.Type = BracketType.Unknown; bracketModel.SubType = "B"; bCount++;
                        Debug.WriteLine($"{LOG_PREFIX} Bracket {bracketElem.AcadHandle}: B (no stiff model)");
                    }
                }
                else
                {
                    bracketModel.Type = BracketType.Unknown; bracketModel.SubType = "B"; bCount++;
                    Debug.WriteLine($"{LOG_PREFIX} Bracket {bracketElem.AcadHandle}: B (no stiff contact at short edge)");
                }

                result.Add(bracketModel);
            }

            Debug.WriteLine($"{LOG_PREFIX} ═══ BRACKET SUMMARY ═══");
            Debug.WriteLine($"{LOG_PREFIX} Total: {result.Count} | OB={obCount} | IB={ibCount} | BF={bfCount} | B={bCount}");
            foreach (var bm in result)
            {
                var elem = bracketElements.FirstOrDefault(e => e.Guid == bm.ElemGuid);
                Debug.WriteLine($"{LOG_PREFIX}   {bm.SubType} handle={elem?.AcadHandle} stiff={bm.StiffenerGuid?.Substring(0, Math.Min(8, bm.StiffenerGuid?.Length ?? 0))}");
            }
            Debug.WriteLine($"{LOG_PREFIX} ═══ END BRACKET SUMMARY ═══");
            return result;
        }

        /// <summary>
        /// Tìm element gần nhất tại cạnh ngắn của bracket.
        /// Chỉ kiểm tra khoảng cách từ vertex SE1/SE2 đến polyline của candidate.
        /// </summary>
        private static (StructuralElementModel elem, double dist) FindNearestAtShortEdge(
            Point2dModel[] se1, Point2dModel[] se2, List<StructuralElementModel> candidates)
        {
            StructuralElementModel nearest = null;
            double minDist = double.MaxValue;

            foreach (var c in candidates)
            {
                if (c.VerticesWCS == null) continue;
                double d = double.MaxValue;
                foreach (var p in se1)
                {
                    double pd = PointToPolyDist(p, c.VerticesWCS);
                    if (pd < d) d = pd;
                }
                foreach (var p in se2)
                {
                    double pd = PointToPolyDist(p, c.VerticesWCS);
                    if (pd < d) d = pd;
                }
                if (d < minDist) { minDist = d; nearest = c; }
            }

            return (nearest, minDist);
        }

        /// <summary>
        /// Lấy 2 cặp vertex cạnh ngắn từ VerticesWCS (length gần ObbWidth nhất).
        /// Trả về (null, null) nếu không xác định được.
        /// </summary>
        private static (Point2dModel[] se1, Point2dModel[] se2) GetShortEdgeVertexPairs(StructuralElementModel elem)
        {
            var verts = elem.VerticesWCS;
            if (verts == null || verts.Length < 4 || elem.ObbWidth <= 0) return (null, null);

            var edges = new List<(Point2dModel p1, Point2dModel p2, double len)>();
            for (int i = 0; i < verts.Length; i++)
            {
                var p1 = verts[i];
                var p2 = verts[(i + 1) % verts.Length];
                edges.Add((p1, p2, p1.DistanceTo(p2)));
            }

            var sorted = edges.OrderBy(e => Math.Abs(e.len - elem.ObbWidth)).Take(2).ToList();
            if (sorted.Count < 2) return (null, null);

            // Sanity: cạnh ngắn không được > 60% cạnh dài
            if (sorted[0].len > elem.ObbLength * 0.6 || sorted[1].len > elem.ObbLength * 0.6)
                return (null, null);

            return (new[] { sorted[0].p1, sorted[0].p2 },
                    new[] { sorted[1].p1, sorted[1].p2 });
        }

        /// <summary>Khoảng cách từ điểm đến polyline</summary>
        private static double PointToPolyDist(Point2dModel point, Point2dModel[] poly)
        {
            if (poly == null || poly.Length < 2) return double.MaxValue;
            double minDist = double.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                double d = PointToSegmentDist(point, poly[i], poly[(i + 1) % poly.Length]);
                if (d < minDist) minDist = d;
                if (minDist < 0.01) return minDist;
            }
            return minDist;
        }

        /// <summary>Tìm element gần nhất trong danh sách</summary>
        private static (StructuralElementModel elem, double dist) FindNearest(
            StructuralElementModel target, List<StructuralElementModel> candidates)
        {
            StructuralElementModel nearest = null;
            double minDist = double.MaxValue;

            foreach (var c in candidates)
            {
                double d = MinDistance(target, c);
                if (d < minDist) { minDist = d; nearest = c; }
            }

            return (nearest, minDist);
        }

        /// <summary>Khoảng cách tối thiểu giữa 2 polylines (point-to-segment)</summary>
        private static double MinDistance(StructuralElementModel a, StructuralElementModel b)
        {
            if (a.VerticesWCS == null || b.VerticesWCS == null) return double.MaxValue;

            double minDist = double.MaxValue;

            // Vertex A → Segment B
            for (int j = 0; j < b.VerticesWCS.Length; j++)
            {
                var seg1 = b.VerticesWCS[j];
                var seg2 = b.VerticesWCS[(j + 1) % b.VerticesWCS.Length];
                foreach (var pa in a.VerticesWCS)
                {
                    double d = PointToSegmentDist(pa, seg1, seg2);
                    if (d < minDist) minDist = d;
                    if (minDist < 0.01) return minDist;
                }
            }

            // Vertex B → Segment A
            for (int i = 0; i < a.VerticesWCS.Length; i++)
            {
                var seg1 = a.VerticesWCS[i];
                var seg2 = a.VerticesWCS[(i + 1) % a.VerticesWCS.Length];
                foreach (var pb in b.VerticesWCS)
                {
                    double d = PointToSegmentDist(pb, seg1, seg2);
                    if (d < minDist) minDist = d;
                    if (minDist < 0.01) return minDist;
                }
            }

            return minDist;
        }

        /// <summary>Khoảng cách từ điểm P đến đoạn thẳng AB</summary>
        private static double PointToSegmentDist(Point2dModel p, Point2dModel a, Point2dModel b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-10) return p.DistanceTo(a);

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Max(0, Math.Min(1, t));

            double projX = a.X + t * dx;
            double projY = a.Y + t * dy;
            double ex = p.X - projX;
            double ey = p.Y - projY;
            return Math.Sqrt(ex * ex + ey * ey);
        }
    }
}
