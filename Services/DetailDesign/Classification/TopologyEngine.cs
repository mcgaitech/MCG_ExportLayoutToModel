using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Phân loại AM0_Unclassified dựa trên quan hệ topology.
    /// AM0 touches Stiff AND Web → Bracket.
    /// AM0 share edge tại góc → ClosingBoxWeb.
    /// Còn lại → WebPlate.
    /// Web + Flange touching → GirderModel.
    /// </summary>
    public class TopologyEngine : ITopologyEngine
    {
        private const string LOG_PREFIX = "[TopologyEngine]";

        /// <summary>Phân tích topology cho tất cả elements</summary>
        public TopologyResult Analyze(List<StructuralElementModel> elements)
        {
            Debug.WriteLine($"{LOG_PREFIX} Starting topology analysis — {elements.Count} elements...");

            var result = new TopologyResult();
            result.Elements = elements;

            // Tách theo type
            var am0List = elements.Where(e => e.ElemType == StructuralType.AM0_Unclassified).ToList();
            var stiffeners = elements.Where(e => e.ElemType == StructuralType.Stiffener).ToList();
            var bsElements = elements.Where(e => e.ElemType == StructuralType.BucklingStiffener).ToList();
            var flanges = elements.Where(e => e.ElemType == StructuralType.Flange).ToList();

            Debug.WriteLine($"{LOG_PREFIX} AM0 to classify: {am0List.Count} | Stiffeners: {stiffeners.Count} | BS: {bsElements.Count} | Flanges: {flanges.Count}");

            // Gộp stiffeners + BS
            var allStiffening = new List<StructuralElementModel>();
            allStiffening.AddRange(stiffeners);
            allStiffening.AddRange(bsElements);

            // ═══════════════════════════════════════════════
            // Bước 1: Gán tất cả AM0 → WebPlate (tạm thời)
            // ═══════════════════════════════════════════════
            foreach (var am0 in am0List)
            {
                am0.ElemType = StructuralType.WebPlate;
            }

            // ═══════════════════════════════════════════════
            // Bước 2: Web-Flange Pairing → Girders (TRƯỚC bracket)
            // Web nào PAIRED với flange = WebPlate chắc chắn
            // ═══════════════════════════════════════════════
            var pairedWebGuids = new HashSet<string>();

            foreach (var web in am0List)
            {
                var pairedFlanges = flanges
                    .Where(f =>
                    {
                        double angleDiff = Math.Abs(f.ObbAngle - web.ObbAngle);
                        bool parallel = angleDiff < 0.1 || Math.Abs(angleDiff - Math.PI) < 0.1;
                        bool touching = MinDistance(f, web) < DetailDesignConstants.TOLERANCE_CONTACT;
                        return parallel && touching;
                    })
                    .ToList();

                if (pairedFlanges.Count > 0)
                {
                    pairedWebGuids.Add(web.Guid);
                    var girder = new GirderModel
                    {
                        Guid = Guid.NewGuid().ToString(),
                        PanelGuid = web.PanelGuid,
                        WebElemGuid = web.Guid,
                        FlangeTopGuid = pairedFlanges.FirstOrDefault(f => f.CentroidY > web.CentroidY)?.Guid,
                        FlangeBotGuid = pairedFlanges.FirstOrDefault(f => f.CentroidY <= web.CentroidY)?.Guid,
                        WebThickness = web.ObbWidth,
                        FlangeWidth = pairedFlanges.FirstOrDefault()?.ObbLength,
                        FlangeThickness = pairedFlanges.FirstOrDefault()?.ObbWidth,
                        Orientation = DetectOrientation(web.ObbAngle)
                    };
                    result.Girders.Add(girder);
                }
            }
            Debug.WriteLine($"{LOG_PREFIX} Girders paired: {result.Girders.Count} (paired webs: {pairedWebGuids.Count})");

            // ═══════════════════════════════════════════════
            // Bước 3: Classify AM0 — source_context PRIORITY + geometric
            // Priority:
            //   1. source_context == "BOX"    → ClosingBoxWeb (skip tiếp)
            //   2. source_context == "CORNER" → WebPlate       (skip tiếp)
            //   3. STRUCTURE: long-face BS contact → Bracket BF
            //                 SE stiffener contact → Bracket (OB/IB/B later)
            //                 else                 → WebPlate
            // ═══════════════════════════════════════════════
            var brackets = new List<StructuralElementModel>();
            var unpaired = am0List.Where(e => !pairedWebGuids.Contains(e.Guid)).ToList();

            Debug.WriteLine($"{LOG_PREFIX} Bracket detection — {unpaired.Count} unpaired AM0, {pairedWebGuids.Count} paired webs");

            foreach (var am0 in unpaired)
            {
                // Priority 1: BOX context → ClosingBoxWeb
                if (am0.SourceContext == "BOX")
                {
                    am0.ElemType = StructuralType.ClosingBoxWeb;
                    Debug.WriteLine($"{LOG_PREFIX} [BOX] {am0.AcadHandle} → ClosingBoxWeb (source={am0.SourceBlock})");
                    continue;
                }

                // Priority 2: CORNER context → WebPlate (đã default, không cần set lại)
                if (am0.SourceContext == "CORNER")
                {
                    Debug.WriteLine($"{LOG_PREFIX} [CORNER] {am0.AcadHandle} → WebPlate (source={am0.SourceBlock})");
                    continue;
                }

                // Priority 3: STRUCTURE — geometric detection
                // 3a. Long-face contact với BS → BF
                double contactLen = FindLongestContactLength(am0, bsElements);
                double longThreshold = am0.ObbWidth * 2.0;
                if (contactLen >= longThreshold && am0.ObbWidth > 0)
                {
                    am0.ElemType = StructuralType.Bracket;
                    am0.BracketSubType = "BF";
                    brackets.Add(am0);
                    Debug.WriteLine($"{LOG_PREFIX} {am0.AcadHandle} long-edge contact BS ({contactLen:F1}mm ≥ {longThreshold:F1}) → BF");
                    continue;
                }

                // 3b. SE tiếp xúc stiffener (Stiff only, không BS) → Bracket OB/IB/B
                if (TouchesStiffAtShortEdge(am0, stiffeners, DetailDesignConstants.BRACKET_END_GAP_MAX))
                {
                    am0.ElemType = StructuralType.Bracket;
                    brackets.Add(am0);
                    continue;
                }

                // 3c. Short contact với BS (SE touching BS end-to-end) → WebPlate (log)
                if (contactLen > 0 && contactLen < longThreshold)
                {
                    Debug.WriteLine($"{LOG_PREFIX} {am0.AcadHandle} short-edge contact BS ({contactLen:F1}mm < {longThreshold:F1}) → WebPlate");
                }
            }

            // GE detection (strict geometric pattern):
            //   WebPlate có:
            //     - 1 SE tiếp xúc WebPlate (web-like)
            //     - 1 LE tiếp xúc WebPlate (web-like)
            //     - 1 SE (cạnh còn lại) tiếp xúc Stiffener/BS
            //   → ElemType = GirderEnd
            var webLikeForGE = am0List.Where(e => e.ElemType == StructuralType.WebPlate).ToList();
            foreach (var am0 in unpaired)
            {
                if (am0.ElemType != StructuralType.WebPlate) continue;
                if (am0.VerticesWCS == null || am0.VerticesWCS.Length != 4) continue;
                if (am0.ObbWidth <= 0 || am0.ObbLength <= 0) continue;

                var (seList, leList) = SplitShortLongEdges(am0);
                if (seList == null || leList == null) continue;

                int seToWeb = 0, seToStiff = 0;
                foreach (var se in seList)
                {
                    if (EdgeTouchesAny(se.s, se.e, am0, webLikeForGE, DetailDesignConstants.TOLERANCE_CONTACT)) seToWeb++;
                    else if (EdgeTouchesAny(se.s, se.e, am0, allStiffening, DetailDesignConstants.TOLERANCE_CONTACT)) seToStiff++;
                }
                int leToWeb = 0;
                foreach (var le in leList)
                {
                    if (EdgeTouchesAny(le.s, le.e, am0, webLikeForGE, DetailDesignConstants.TOLERANCE_CONTACT)) leToWeb++;
                }

                if (seToWeb >= 1 && seToStiff >= 1 && leToWeb >= 1)
                {
                    am0.ElemType = StructuralType.GirderEnd;
                    am0.AnnotationType = "GE";
                    Debug.WriteLine($"{LOG_PREFIX} GirderEnd: {am0.AcadHandle} (SE→web={seToWeb}, SE→stiff={seToStiff}, LE→web={leToWeb})");
                }
            }

            var webPlates = am0List.Where(e => e.ElemType == StructuralType.WebPlate).ToList();
            Debug.WriteLine($"{LOG_PREFIX} After classify — WebPlate: {webPlates.Count} | Bracket: {brackets.Count}");

            // ═══════════════════════════════════════════════
            // Bước 4: Closing Box Detection
            // ═══════════════════════════════════════════════
            var closingBoxDetector = new ClosingBoxDetector();
            result.ClosingBoxes = closingBoxDetector.Detect(webPlates);
            Debug.WriteLine($"{LOG_PREFIX} Closing boxes detected: {result.ClosingBoxes.Count}");

            foreach (var cb in result.ClosingBoxes)
            {
                foreach (var memberGuid in cb.MemberGuids)
                {
                    var member = elements.FirstOrDefault(e => e.Guid == memberGuid);
                    if (member != null)
                        member.ElemType = StructuralType.ClosingBoxWeb;
                }
            }

            // Log final summary
            var summary = elements.GroupBy(e => e.ElemType)
                                  .OrderBy(g => g.Key)
                                  .Select(g => $"{g.Key}: {g.Count()}");
            Debug.WriteLine($"{LOG_PREFIX} Topology COMPLETE — {string.Join(" | ", summary)}");

            // ═══════════════════════════════════════════════
            // DETAILED LOG — từng type với handles
            // ═══════════════════════════════════════════════
            Debug.WriteLine($"{LOG_PREFIX} ═══ DETAILED BREAKDOWN ═══");

            // TopPlate
            var tpList = elements.Where(e => e.ElemType == StructuralType.TopPlateRegion && !e.IsHole).ToList();
            var tpHoles = elements.Where(e => e.ElemType == StructuralType.TopPlateRegion && e.IsHole).ToList();
            Debug.WriteLine($"{LOG_PREFIX} TOP PLATE: {tpList.Count} outer + {tpHoles.Count} holes");
            foreach (var tp in tpList)
                Debug.WriteLine($"{LOG_PREFIX}   TP {tp.AcadHandle}: area={tp.AreaPoly:F0} net={tp.NetArea:F0} annotation={tp.AnnotationType ?? "-"}");

            // WebPlate
            var wpList = elements.Where(e => e.ElemType == StructuralType.WebPlate).ToList();
            Debug.WriteLine($"{LOG_PREFIX} WEB PLATE: {wpList.Count}");

            // Flange
            var flList = elements.Where(e => e.ElemType == StructuralType.Flange && !e.IsHole).ToList();
            var flHoles = elements.Where(e => e.ElemType == StructuralType.Flange && e.IsHole).ToList();
            Debug.WriteLine($"{LOG_PREFIX} FLANGE: {flList.Count} outer + {flHoles.Count} holes");

            // Stiffener
            var stList = elements.Where(e => e.ElemType == StructuralType.Stiffener).ToList();
            Debug.WriteLine($"{LOG_PREFIX} STIFFENER: {stList.Count}");

            // BS
            var bsList = elements.Where(e => e.ElemType == StructuralType.BucklingStiffener).ToList();
            Debug.WriteLine($"{LOG_PREFIX} BUCKLING STIFFENER: {bsList.Count}");

            // ClosingBox
            var cbList = elements.Where(e => e.ElemType == StructuralType.ClosingBoxWeb).ToList();
            Debug.WriteLine($"{LOG_PREFIX} CLOSING BOX WEB: {cbList.Count} ({result.ClosingBoxes.Count} boxes)");

            // GirderEnd
            var geElemList = elements.Where(e => e.ElemType == StructuralType.GirderEnd).ToList();
            Debug.WriteLine($"{LOG_PREFIX} GIRDER END: {geElemList.Count}");
            foreach (var ge in geElemList)
                Debug.WriteLine($"{LOG_PREFIX}   GE {ge.AcadHandle}: obb={ge.ObbLength:F0}x{ge.ObbWidth:F0}");

            // Brackets
            var brList = elements.Where(e => e.ElemType == StructuralType.Bracket).ToList();
            Debug.WriteLine($"{LOG_PREFIX} BRACKET: {brList.Count}");
            foreach (var br in brList)
                Debug.WriteLine($"{LOG_PREFIX}   BR {br.AcadHandle}: area={br.AreaPoly:F0} obb={br.ObbLength:F0}x{br.ObbWidth:F0}");

            // GE annotations
            var geList = elements.Where(e => e.AnnotationType == "GE").ToList();
            Debug.WriteLine($"{LOG_PREFIX} GE ANNOTATIONS: {geList.Count}");
            foreach (var ge in geList)
                Debug.WriteLine($"{LOG_PREFIX}   GE {ge.AcadHandle}: type={ge.ElemType}");

            // Total visible (không đếm holes)
            int visible = elements.Count(e => !e.IsHole);
            int holes = elements.Count(e => e.IsHole);
            Debug.WriteLine($"{LOG_PREFIX} TOTAL: {visible} visible + {holes} holes = {elements.Count}");
            Debug.WriteLine($"{LOG_PREFIX} ═══ END BREAKDOWN ═══");

            return result;
        }

        #region Private Helpers

        /// <summary>
        /// Tìm contact length DÀI NHẤT giữa 1 edge của elem và boundary của bất kỳ candidate nào.
        /// Kiểm tra: mỗi edge của elem so với mỗi edge của candidate — parallel + close →
        /// tính overlap length theo projection. Return max overlap (0 nếu không có contact).
        /// Dùng để phân biệt end-to-end contact (short) vs face contact (long).
        /// </summary>
        private static double FindLongestContactLength(
            StructuralElementModel elem, List<StructuralElementModel> candidates)
        {
            if (elem.VerticesWCS == null || elem.VerticesWCS.Length < 2) return 0;
            double bestLen = 0;

            for (int i = 0; i < elem.VerticesWCS.Length; i++)
            {
                var a1 = elem.VerticesWCS[i];
                var a2 = elem.VerticesWCS[(i + 1) % elem.VerticesWCS.Length];

                foreach (var c in candidates)
                {
                    if (c.VerticesWCS == null) continue;
                    for (int j = 0; j < c.VerticesWCS.Length; j++)
                    {
                        var b1 = c.VerticesWCS[j];
                        var b2 = c.VerticesWCS[(j + 1) % c.VerticesWCS.Length];

                        double overlap = ComputeParallelOverlap(a1, a2, b1, b2);
                        if (overlap > bestLen) bestLen = overlap;
                    }
                }
            }
            return bestLen;
        }

        /// <summary>
        /// Tính overlap length giữa 2 đoạn thẳng nếu chúng PARALLEL + CLOSE.
        /// Return 0 nếu không parallel hoặc không close.
        /// Parallel tol: góc lệch < 5°. Close tol: khoảng cách vuông góc < 2mm.
        /// </summary>
        private static double ComputeParallelOverlap(
            Point2dModel a1, Point2dModel a2, Point2dModel b1, Point2dModel b2)
        {
            double dax = a2.X - a1.X, day = a2.Y - a1.Y;
            double dbx = b2.X - b1.X, dby = b2.Y - b1.Y;
            double aLen = Math.Sqrt(dax * dax + day * day);
            double bLen = Math.Sqrt(dbx * dbx + dby * dby);
            if (aLen < 1e-6 || bLen < 1e-6) return 0;

            // Parallel check: |cross| / (aLen * bLen) = sin(angle) < sin(5°) ≈ 0.087
            double cross = Math.Abs(dax * dby - day * dbx);
            if (cross / (aLen * bLen) > 0.087) return 0;

            // Perpendicular distance (b1 to line a) < 2mm
            double nx = -day / aLen, ny = dax / aLen;
            double perpDist = Math.Abs((b1.X - a1.X) * nx + (b1.Y - a1.Y) * ny);
            if (perpDist > 2.0) return 0;

            // Project b1, b2 lên line a (parametric t ∈ [0, aLen])
            double ux = dax / aLen, uy = day / aLen;
            double t1 = (b1.X - a1.X) * ux + (b1.Y - a1.Y) * uy;
            double t2 = (b2.X - a1.X) * ux + (b2.Y - a1.Y) * uy;
            double bMin = Math.Min(t1, t2);
            double bMax = Math.Max(t1, t2);

            double aMin = 0, aMax = aLen;
            double overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
            return overlap > 0 ? overlap : 0;
        }

        /// <summary>
        /// Split polyline vertices thành 2 short edges + 2 long edges theo length.
        /// Trả về (null, null) nếu shape không rectangular 4-vertex.
        /// </summary>
        private static (List<(Point2dModel s, Point2dModel e)> seList,
                        List<(Point2dModel s, Point2dModel e)> leList)
            SplitShortLongEdges(StructuralElementModel elem)
        {
            var v = elem.VerticesWCS;
            if (v == null || v.Length != 4) return (null, null);
            var edges = new List<(Point2dModel s, Point2dModel e, double len)>();
            for (int i = 0; i < 4; i++)
            {
                var a = v[i]; var b = v[(i + 1) % 4];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                edges.Add((a, b, Math.Sqrt(dx * dx + dy * dy)));
            }
            edges.Sort((x, y) => x.len.CompareTo(y.len));
            return (
                new List<(Point2dModel, Point2dModel)> { (edges[0].s, edges[0].e), (edges[1].s, edges[1].e) },
                new List<(Point2dModel, Point2dModel)> { (edges[2].s, edges[2].e), (edges[3].s, edges[3].e) }
            );
        }

        /// <summary>
        /// Check: edge (ea,eb) có tiếp xúc BẤT KỲ element nào trong candidates không?
        /// Tiếp xúc = cả 2 endpoints nằm trên polyline của candidate (trong tolerance).
        /// </summary>
        private static bool EdgeTouchesAny(
            Point2dModel ea, Point2dModel eb,
            StructuralElementModel self,
            List<StructuralElementModel> candidates,
            double tol)
        {
            foreach (var c in candidates)
            {
                if (c.Guid == self.Guid) continue;
                if (c.VerticesWCS == null) continue;
                bool aOn = false, bOn = false;
                for (int i = 0; i < c.VerticesWCS.Length; i++)
                {
                    var pa = c.VerticesWCS[i];
                    var pb = c.VerticesWCS[(i + 1) % c.VerticesWCS.Length];
                    if (!aOn && PointOnSegment(ea, pa, pb, tol)) aOn = true;
                    if (!bOn && PointOnSegment(eb, pa, pb, tol)) bOn = true;
                    if (aOn && bOn) return true;
                }
            }
            return false;
        }

        /// <summary>Point nằm trên segment với tolerance + projection t ∈ [0,1].</summary>
        private static bool PointOnSegment(Point2dModel p, Point2dModel a, Point2dModel b, double tol)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-10) return p.DistanceTo(a) < tol;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            if (t < -0.001 || t > 1.001) return false;
            double projX = a.X + t * dx, projY = a.Y + t * dy;
            double ex = p.X - projX, ey = p.Y - projY;
            return ex * ex + ey * ey < tol * tol;
        }

        /// <summary>
        /// Kiểm tra cạnh ngắn của am0 có tiếp xúc stiff/BS trong tolerance không.
        /// Fallback sang MinDistance cho shape bị bended (ObbWidth không xác định được cạnh ngắn).
        /// </summary>
        private static bool TouchesStiffAtShortEdge(
            StructuralElementModel am0,
            List<StructuralElementModel> allStiffening,
            double tolerance)
        {
            var (se1, se2) = GetShortEdgeVertexPairs(am0);
            if (se1 == null)
            {
                // Bended/irregular shape — fallback: MinDistance toàn bộ shape
                return allStiffening.Any(s => MinDistance(am0, s) < tolerance);
            }

            foreach (var stiff in allStiffening)
            {
                if (stiff.VerticesWCS == null) continue;
                foreach (var p in se1)
                    if (PointToPolyDist(p, stiff.VerticesWCS) < tolerance) return true;
                foreach (var p in se2)
                    if (PointToPolyDist(p, stiff.VerticesWCS) < tolerance) return true;
            }
            return false;
        }

        /// <summary>
        /// Lấy 2 cặp vertex của cạnh ngắn từ VerticesWCS.
        /// Cạnh ngắn = 2 cạnh có length gần ObbWidth nhất.
        /// Trả về (null, null) nếu không xác định được (shape bất thường).
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

            // 2 cạnh có length gần ObbWidth nhất = cạnh ngắn
            var sorted = edges.OrderBy(e => Math.Abs(e.len - elem.ObbWidth)).Take(2).ToList();
            if (sorted.Count < 2) return (null, null);

            // Sanity check: 2 cạnh ngắn không được quá dài (tránh nhầm với cạnh dài)
            double longLen = elem.ObbLength;
            if (sorted[0].len > longLen * 0.6 || sorted[1].len > longLen * 0.6) return (null, null);

            return (new[] { sorted[0].p1, sorted[0].p2 },
                    new[] { sorted[1].p1, sorted[1].p2 });
        }

        /// <summary>
        /// Khoảng cách tối thiểu giữa 2 polylines (point-to-segment).
        /// Kiểm tra mỗi vertex của A đến mỗi cạnh của B, và ngược lại.
        /// </summary>
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

            if (lenSq < 1e-10)
                return p.DistanceTo(a);

            // Project P lên AB, clamp t vào [0,1]
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Max(0, Math.Min(1, t));

            double projX = a.X + t * dx;
            double projY = a.Y + t * dy;

            double ex = p.X - projX;
            double ey = p.Y - projY;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>Khoảng cách từ 1 điểm đến polyline (min point-to-segment)</summary>
        private static double PointToPolyDist(Point2dModel point, Point2dModel[] poly)
        {
            if (poly == null || poly.Length < 2) return double.MaxValue;
            double minDist = double.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                var seg1 = poly[i];
                var seg2 = poly[(i + 1) % poly.Length];
                double d = PointToSegmentDist(point, seg1, seg2);
                if (d < minDist) minDist = d;
                if (minDist < 0.01) return minDist;
            }
            return minDist;
        }

        /// <summary>Xác định hướng web: LONG hoặc TRANS</summary>
        private static string DetectOrientation(double angleRad)
        {
            double a = Math.Abs(angleRad) % Math.PI;
            if (a < Math.PI / 4 || a > 3 * Math.PI / 4)
                return "LONG";
            return "TRANS";
        }

        #endregion
    }
}
