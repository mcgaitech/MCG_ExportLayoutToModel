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
    /// BaseEdgeEngine — Orchestrator 2-pass cho construction line + throw thickness.
    ///
    /// PASS 1 — Construction Line (seed = WEB PLATE only):
    ///   Seed candidate: WebPlate / ClosingBoxWeb có 4 vertices rectangular.
    ///   Stiff / BS / Bracket CHỈ làm PARTNER, không làm seed.
    ///   Loop:
    ///     SEED = longest web unassigned
    ///     Build corridor bám OBB seed (short +3mm mỗi bên, long extend vượt panel)
    ///     Candidates = clParts in corridor (web/stiff/BS/bracket)
    ///     Check collinear: chỉ 2 cạnh DÀI V3 của candidate với 1 trong 2 long edges của seed
    ///                      (angle 0.1° + offset 1mm)
    ///     Pick seed edge có nhiều partners hơn → CL đó cho cả group
    ///     Remove group từ unassigned
    ///   Isolated: parts còn sót → fallback Side-based
    ///     (bao gồm stiff/BS/bracket không gần web NÀO, VÀ inner web không có partners)
    ///
    /// PASS 2 — Throw Thickness (unified centroid rule):
    ///   Per part trong group:
    ///     throw = perpendicular(CL) signed toward PART's OWN centroid
    ///   Isolated (kể cả inner web):
    ///     throw = Side-based per PanelSide (Port→-Y, Stbd→+Y, Center→+X)
    ///
    /// FLANGE: không thuộc CL detection, chỉ nhận Side-based throw + OBB base.
    /// </summary>
    public static class BaseEdgeEngine
    {
        private const string LOG_PREFIX = "[BaseEdgeEngine]";

        // ─── Pass 1: Construction Line detection ───
        private const double CL_ANGLE_TOL_DEG = 0.1;
        private const double CL_OFFSET_TOL_MM = 1.0;
        private const double CL_CORRIDOR_GAP_MM = 3.0;
        private const double CL_CORRIDOR_SAFETY_MM = 100.0;

        // ─── Pass 2: Outer detection ───
        private const double HUG_DIST_WEB     = 750.0;  // web: ≤750mm từ boundary → outer
        private const double HUG_DIST_STIFF   = 200.0;  // stiff/BS: ≤200mm → outer (stiffener spacing ~150-200mm)
        private const double HUG_DIST_BRACKET = 750.0;  // bracket: ≤750mm như web
        private const double HUG_ANGLE_TOL_DEG = 5.0;

        // ─── UI-controlled throw direction cho outer parts ───
        /// <summary>Outer Stiffener/BS: true = OUTWARD (default), false = INWARD.</summary>
        public static bool OuterStiffOutward = true;
        /// <summary>Outer Web/ClosingBoxWeb: false = INWARD (default), true = OUTWARD.</summary>
        public static bool OuterWebOutward = false;

        // ─── Same-thickness alignment tolerance ───
        private const double UNIFORM_THICKNESS_TOL_MM = 0.1;

        // ─── FIX 4: Bent / knuckle stiffener ───
        private const double MIN_CL_SEGMENT        = 200.0;  // đoạn ngắn hơn 200mm bỏ qua
        private const double KNUCKLE_SPLIT_ANGLE_DEG = 10.0; // direction change > 10° = knuckle

        /// <summary>Compute base edge + throw vector cho mọi rectangular element.</summary>
        public static void ComputeAll(List<StructuralElementModel> elements, PanelContext panel)
        {
            Debug.WriteLine($"{LOG_PREFIX} Start — {elements.Count} elements");

            // Step 1 — Build geometry map (V3 parallel edges + orientation)
            var infoMap = new Dictionary<string, ElementGeom>();
            foreach (var elem in elements)
            {
                if (elem.IsHole || !IsRectangularType(elem.ElemType)) continue;
                if (!ThicknessCalculator.TryGetParallelPair(elem.VerticesWCS,
                        out var e1s, out var e1e, out var e2s, out var e2e, out _))
                {
                    // FIX 4: bent / knuckle stiffener — try segment extraction
                    bool handledFix4 = false;
                    if ((elem.ElemType == StructuralType.Stiffener
                      || elem.ElemType == StructuralType.BucklingStiffener)
                        && elem.VerticesWCS != null && elem.VerticesWCS.Length > 4)
                    {
                        var segs = ExtractStructuralSegments(elem.VerticesWCS);
                        if (segs.Count > 0)
                        {
                            // Dùng đoạn dài nhất làm đại diện geometry cho element
                            var best = segs[0];
                            double bestLen = Math.Max(Distance(best.E1s, best.E1e), Distance(best.E2s, best.E2e));
                            foreach (var s in segs)
                            {
                                double sl = Math.Max(Distance(s.E1s, s.E1e), Distance(s.E2s, s.E2e));
                                if (sl > bestLen) { best = s; bestLen = sl; }
                            }
                            double bdx = best.E1e.X - best.E1s.X, bdy = best.E1e.Y - best.E1s.Y;
                            elem.OrientationClass = Math.Abs(bdx) >= Math.Abs(bdy) ? "LONG" : "TRANS";
                            infoMap[elem.Guid] = best;
                            Debug.WriteLine($"{LOG_PREFIX} [FIX4] bent {elem.ElemType} {ShortGuid(elem)}" +
                                $" → {segs.Count} seg(s), best={bestLen:F0}mm verts={elem.VerticesWCS.Length}");
                            handledFix4 = true;
                        }
                    }
                    if (!handledFix4)
                        Debug.WriteLine($"{LOG_PREFIX} {ShortGuid(elem)} {elem.ElemType} — V3 failed");
                    continue;
                }
                double dx = e1e.X - e1s.X;
                double dy = e1e.Y - e1s.Y;
                string orient = Math.Abs(dx) >= Math.Abs(dy) ? "LONG" : "TRANS";
                elem.OrientationClass = orient;
                infoMap[elem.Guid] = new ElementGeom(e1s, e1e, e2s, e2e, orient);
            }

            var topPlates = elements
                .Where(e => e.ElemType == StructuralType.TopPlateRegion && !e.IsHole)
                .ToList();

            // Step 2 — Partition
            var clParts = elements
                .Where(e => IsClEligibleType(e.ElemType) && !e.IsHole && infoMap.ContainsKey(e.Guid))
                .ToList();
            var flanges = elements
                .Where(e => e.ElemType == StructuralType.Flange && !e.IsHole && infoMap.ContainsKey(e.Guid))
                .ToList();

            Debug.WriteLine($"{LOG_PREFIX} clParts={clParts.Count} flanges={flanges.Count} topPlates={topPlates.Count}");

            // Step 3 — Pass 1: Construction Line groups (seed = web only)
            var clAssign = BuildConstructionLineGroups(clParts, infoMap, topPlates);
            int groupCount = clAssign.Values.Select(g => g.GroupId).Distinct().Count();
            int isolatedCount = clAssign.Values.Count(g => g.IsIsolated);
            Debug.WriteLine($"{LOG_PREFIX} Pass 1 done: {groupCount} groups, {isolatedCount} isolated");

            // Step 3.5 — Pass 1.5: Bracket ↔ Stiffener linking
            Pass1_5_LinkBracketToStiffener(clParts);

            // Step 4 — Pass 2: Throw thickness
            Debug.WriteLine($"{LOG_PREFIX} HUG_DIST web={HUG_DIST_WEB:F0} stiff={HUG_DIST_STIFF:F0} bracket={HUG_DIST_BRACKET:F0}mm" +
                $" | OuterStiff={(OuterStiffOutward?"OUTWARD":"INWARD")} OuterWeb={(OuterWebOutward?"OUTWARD":"INWARD")}");
            ApplyThrowToGroups(clParts, clAssign, infoMap, topPlates, panel);

            // Step 4.3 — Same-thickness groups: realign CLSpan on PickBase face
            AlignUniformThicknessGroupCLs(clParts, clAssign);

            // Step 4.5 — CL Merge: extend stiffener CLSpan to include linked bracket spans
            MergeLinkedBracketCLSpans(clParts);

            // Step 5 — Flanges: Side-based (không thuộc CL)
            foreach (var flange in flanges)
            {
                ApplyFlangeSideBased(flange, infoMap[flange.Guid], panel);
            }

            // Step 6 — Priority 4: Web/CBWeb có EXACTLY ONE long edge nhận SE
            // của web khác → CL = edge đó, throw vuông góc về centroid.
            // CHỈ apply cho Inner + Isolated (P1 + P2 wins → skip).
            ApplyConnectedLongEdgeRule(clParts, clAssign);

            // Step 7 — Priority 4: Slanted Web/CBWeb có EXACTLY ONE edge với 2 vertices
            // chạm 2 web khác → CL = edge đó, throw vuông góc về centroid.
            // CHỈ apply cho Inner + Isolated (P1 + P2 wins → skip).
            ApplySlantedConnectedEdgeRule(clParts, clAssign);

            Debug.WriteLine($"{LOG_PREFIX} Done.");
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1 — Construction Line Detection (seed = web plate only)
        // ═══════════════════════════════════════════════════════════

        private class CLAssignment
        {
            public string GroupId;
            public bool IsIsolated;
            /// <summary>Unified construction line span — chung cho cả group.</summary>
            public Point2dModel GroupCLStart;
            public Point2dModel GroupCLEnd;
            /// <summary>
            /// Cạnh collinear CỤ THỂ của member này trên group CL.
            /// Seed   = chosen seed long edge.
            /// Partner = matched collinear edge từ FindCollinearPartnersInCorridor.
            /// Isolated = null (sẽ set sau PickBase trong Pass 2).
            /// </summary>
            public Point2dModel MemberEdgeStart;
            public Point2dModel MemberEdgeEnd;
        }

        private static bool IsWebSeedEligible(StructuralType t)
        {
            return t == StructuralType.WebPlate || t == StructuralType.ClosingBoxWeb;
        }

        private static Dictionary<string, CLAssignment> BuildConstructionLineGroups(
            List<StructuralElementModel> clParts,
            Dictionary<string, ElementGeom> infoMap,
            List<StructuralElementModel> topPlates)
        {
            var result = new Dictionary<string, CLAssignment>();
            var unassigned = new HashSet<string>(clParts.Select(p => p.Guid));

            double panelExtent = ComputePanelBoundsDiagonal(topPlates);
            double extendLong = panelExtent + CL_CORRIDOR_SAFETY_MM;
            if (extendLong < 10000) extendLong = 100000;

            double cosAngleTol = Math.Cos(CL_ANGLE_TOL_DEG * Math.PI / 180.0);
            int groupCounter = 0;

            // MAIN LOOP — seed = longest Web/CBWeb có V3 parallel pair
            // (Cho phép bended webs 5+ vertices; V3 đã filter candidate edge ≥ 50% max length)
            while (true)
            {
                StructuralElementModel seed = null;
                double maxLen = 0;
                foreach (var p in clParts)
                {
                    if (!unassigned.Contains(p.Guid)) continue;
                    if (!IsWebSeedEligible(p.ElemType)) continue;
                    if (!infoMap.TryGetValue(p.Guid, out var g)) continue;
                    double len = Math.Max(Distance(g.E1s, g.E1e), Distance(g.E2s, g.E2e));
                    if (len > maxLen)
                    {
                        maxLen = len;
                        seed = p;
                    }
                }
                if (seed == null) break;

                var seedGeom = infoMap[seed.Guid];
                string groupId = "G" + (groupCounter++);

                var candidates = clParts
                    .Where(p => unassigned.Contains(p.Guid)
                             && p.Guid != seed.Guid
                             && infoMap.ContainsKey(p.Guid))
                    .ToList();

                // Seed có 2 long edges — pick edge nào có nhiều collinear partners hơn
                var edge1 = (seedGeom.E1s, seedGeom.E1e);
                var edge2 = (seedGeom.E2s, seedGeom.E2e);
                var partners1 = FindCollinearPartnersInCorridor(seedGeom, edge1, candidates, infoMap, extendLong, cosAngleTol);
                var partners2 = FindCollinearPartnersInCorridor(seedGeom, edge2, candidates, infoMap, extendLong, cosAngleTol);

                // Inner web: không có collinear partners nào → Side-based như inner stiff/BS
                if (partners1.Count == 0 && partners2.Count == 0)
                {
                    result[seed.Guid] = new CLAssignment
                    {
                        GroupId = "ISO_" + ShortGuid(seed),
                        IsIsolated = true,
                        GroupCLStart = seedGeom.E1s,
                        GroupCLEnd = seedGeom.E1e
                    };
                    unassigned.Remove(seed.Guid);
                    Debug.WriteLine($"{LOG_PREFIX} Inner web (no partners): {ShortGuid(seed)} {seed.ElemType}");
                    continue;
                }

                Point2dModel clA, clB;
                Dictionary<string, (Point2dModel a, Point2dModel b)> groupMemberEdges;
                if (partners1.Count >= partners2.Count)
                {
                    clA = seedGeom.E1s; clB = seedGeom.E1e;
                    groupMemberEdges = partners1;
                }
                else
                {
                    clA = seedGeom.E2s; clB = seedGeom.E2e;
                    groupMemberEdges = partners2;
                }

                // Unified CL span: project tất cả member collinear edge endpoints lên CL direction
                var (spanStart, spanEnd) = ComputeGroupCLSpan(clA, clB, groupMemberEdges);

                result[seed.Guid] = new CLAssignment
                {
                    GroupId = groupId,
                    IsIsolated = false,
                    GroupCLStart = spanStart,
                    GroupCLEnd = spanEnd,
                    MemberEdgeStart = clA,   // seed's chosen reference edge
                    MemberEdgeEnd   = clB
                };
                unassigned.Remove(seed.Guid);

                foreach (var kv in groupMemberEdges)
                {
                    result[kv.Key] = new CLAssignment
                    {
                        GroupId = groupId,
                        IsIsolated = false,
                        GroupCLStart = spanStart,
                        GroupCLEnd = spanEnd,
                        MemberEdgeStart = kv.Value.a,  // partner's matched collinear edge
                        MemberEdgeEnd   = kv.Value.b
                    };
                    unassigned.Remove(kv.Key);
                }

                Debug.WriteLine($"{LOG_PREFIX} {groupId}: seed={ShortGuid(seed)} {seed.ElemType} len={maxLen:F0} members={1 + groupMemberEdges.Count}");
                bool traceGroup = seed.Guid.StartsWith("e406d974") || seed.Guid.StartsWith("7ca630a9")
                    || groupMemberEdges.Keys.Any(k => k.StartsWith("e406d974") || k.StartsWith("7ca630a9"));
                if (traceGroup)
                    foreach (var kv in groupMemberEdges)
                        Debug.WriteLine($"{LOG_PREFIX}   [{groupId}] member={kv.Key}");
            }

            // ISOLATED — không thuộc group web nào (stiff/BS/bracket không gần web, hoặc web không 4-seg)
            foreach (var p in clParts)
            {
                if (result.ContainsKey(p.Guid)) continue;
                if (!infoMap.TryGetValue(p.Guid, out var g)) continue;

                result[p.Guid] = new CLAssignment
                {
                    GroupId = "ISO_" + ShortGuid(p),
                    IsIsolated = true,
                    GroupCLStart = g.E1s,
                    GroupCLEnd = g.E1e
                };
                Debug.WriteLine($"{LOG_PREFIX} Isolated: {ShortGuid(p)} {p.ElemType}");
            }

            return result;
        }

        /// <summary>
        /// Tính unified CL span cho cả group: project endpoints của seed edge VÀ tất cả
        /// partner collinear edges lên CL direction → lấy min/max t → 1 đoạn thẳng duy nhất.
        /// </summary>
        private static (Point2dModel, Point2dModel) ComputeGroupCLSpan(
            Point2dModel clA, Point2dModel clB,
            Dictionary<string, (Point2dModel a, Point2dModel b)> memberEdges)
        {
            double dx = clB.X - clA.X, dy = clB.Y - clA.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return (clA, clB);
            double ux = dx / len, uy = dy / len;

            // Seed endpoints định nghĩa t ∈ [0, len]
            double tMin = 0, tMax = len;

            foreach (var kv in memberEdges)
            {
                var (a, b) = kv.Value;
                double ta = (a.X - clA.X) * ux + (a.Y - clA.Y) * uy;
                double tb = (b.X - clA.X) * ux + (b.Y - clA.Y) * uy;
                if (ta < tMin) tMin = ta;
                if (ta > tMax) tMax = ta;
                if (tb < tMin) tMin = tb;
                if (tb > tMax) tMax = tb;
            }

            return (
                new Point2dModel { X = clA.X + tMin * ux, Y = clA.Y + tMin * uy },
                new Point2dModel { X = clA.X + tMax * ux, Y = clA.Y + tMax * uy }
            );
        }

        /// <summary>
        /// Tìm các candidate có (a) vertex nằm trong corridor seed AND (b) cạnh DÀI V3 collinear
        /// với seedEdge. Trả về map guid → collinear edge (cạnh dài của candidate flush với seedEdge).
        /// </summary>
        private static Dictionary<string, (Point2dModel a, Point2dModel b)> FindCollinearPartnersInCorridor(
            ElementGeom seedGeom,
            (Point2dModel E1s, Point2dModel E1e) seedEdge,
            List<StructuralElementModel> candidates,
            Dictionary<string, ElementGeom> infoMap,
            double extendLong,
            double cosAngleTol)
        {
            var partners = new Dictionary<string, (Point2dModel, Point2dModel)>();

            double ldx = seedEdge.E1e.X - seedEdge.E1s.X;
            double ldy = seedEdge.E1e.Y - seedEdge.E1s.Y;
            double llen = Math.Sqrt(ldx * ldx + ldy * ldy);
            if (llen < 1e-6) return partners;
            ldx /= llen; ldy /= llen;

            double sdx = -ldy, sdy = ldx;

            double scx = (seedGeom.E1s.X + seedGeom.E1e.X + seedGeom.E2s.X + seedGeom.E2e.X) / 4.0;
            double scy = (seedGeom.E1s.Y + seedGeom.E1e.Y + seedGeom.E2s.Y + seedGeom.E2e.Y) / 4.0;

            double halfLen = llen / 2.0;
            double halfWidth = PerpDistBetweenEdges(seedGeom) / 2.0;
            double corridorHalfLenExt = halfLen + extendLong;
            double corridorHalfWidthExt = halfWidth + CL_CORRIDOR_GAP_MM;

            foreach (var cand in candidates)
            {
                if (!infoMap.TryGetValue(cand.Guid, out var cGeom)) continue;

                // (a) Any vertex inside corridor?
                bool anyInside = false;
                var vs = cand.VerticesWCS;
                if (vs != null)
                {
                    foreach (var v in vs)
                    {
                        double dx = v.X - scx, dy = v.Y - scy;
                        double lx = dx * ldx + dy * ldy;
                        double ly = dx * sdx + dy * sdy;
                        if (Math.Abs(lx) <= corridorHalfLenExt && Math.Abs(ly) <= corridorHalfWidthExt)
                        {
                            anyInside = true;
                            break;
                        }
                    }
                }
                if (!anyInside) continue;

                // (b) Chỉ check 2 cạnh DÀI (V3) — bỏ qua cạnh thickness
                var matched = FindCollinearLongEdge(cGeom, seedEdge.E1s, seedEdge.E1e, cosAngleTol);
                if (matched.HasValue)
                {
                    partners[cand.Guid] = matched.Value;
                }
            }

            return partners;
        }

        /// <summary>Chỉ check 2 cạnh dài V3 (E1/E2), không duyệt cạnh thickness.</summary>
        private static (Point2dModel, Point2dModel)? FindCollinearLongEdge(
            ElementGeom candGeom, Point2dModel refA, Point2dModel refB, double cosAngleTol)
        {
            if (IsCollinear(candGeom.E1s, candGeom.E1e, refA, refB, cosAngleTol, CL_OFFSET_TOL_MM))
                return (candGeom.E1s, candGeom.E1e);
            if (IsCollinear(candGeom.E2s, candGeom.E2e, refA, refB, cosAngleTol, CL_OFFSET_TOL_MM))
                return (candGeom.E2s, candGeom.E2e);
            return null;
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1.5 — Bracket ↔ Stiffener Linking
        // ═══════════════════════════════════════════════════════════

        private const double CONTACT_TOL_MM      = 2.0;  // dist tối đa để gọi là "contact"
        private const double PROXIMITY_EXP_MM    = 5.0;  // AABB expand cho proximity filter
        private const double CONTACT_ANGLE_TOL_DEG = 5.0; // góc song song tối đa

        /// <summary>
        /// Pass 1.5 — Link mỗi Bracket với Stiffener/BS partner của nó bằng geometry.
        ///
        /// 3 điều kiện để gọi là contact edge:
        ///   1. dist(E_b, Seg_s) &lt; CONTACT_TOL_MM (segment-to-segment distance)
        ///   2. angle(E_b, Seg_s) &lt; 5° (song song — tránh thickness edge)
        ///   3. projected overlap &gt; 0 (có đoạn chung — tránh end-to-end)
        ///
        /// Kết quả:
        ///   B.LinkedStiffenerGuid + B.StiffenerContactEdgeStart/End → AutoDetected
        ///   Không tìm được → Warning (fallback HugCheck ở Pass 2)
        /// </summary>
        private static void Pass1_5_LinkBracketToStiffener(List<StructuralElementModel> clParts)
        {
            var brackets = clParts
                .Where(p => p.ElemType == StructuralType.Bracket
                         && p.VerticesWCS != null && p.VerticesWCS.Length >= 2)
                .ToList();
            var stiffeners = clParts
                .Where(p => (p.ElemType == StructuralType.Stiffener
                          || p.ElemType == StructuralType.BucklingStiffener)
                         && p.VerticesWCS != null && p.VerticesWCS.Length >= 2)
                .ToList();

            double cosAngleTol = Math.Cos(CONTACT_ANGLE_TOL_DEG * Math.PI / 180.0);
            int linked = 0, warned = 0;

            foreach (var b in brackets)
            {
                // AABB của bracket mở rộng PROXIMITY_EXP_MM
                double bxMin = double.MaxValue, bxMax = double.MinValue;
                double byMin = double.MaxValue, byMax = double.MinValue;
                foreach (var v in b.VerticesWCS)
                {
                    if (v.X < bxMin) bxMin = v.X; if (v.X > bxMax) bxMax = v.X;
                    if (v.Y < byMin) byMin = v.Y; if (v.Y > byMax) byMax = v.Y;
                }
                bxMin -= PROXIMITY_EXP_MM; bxMax += PROXIMITY_EXP_MM;
                byMin -= PROXIMITY_EXP_MM; byMax += PROXIMITY_EXP_MM;

                // STEP 1 — Proximity filter (AABB overlap)
                var candidates = stiffeners.Where(s =>
                {
                    double sxMin = double.MaxValue, sxMax = double.MinValue;
                    double syMin = double.MaxValue, syMax = double.MinValue;
                    foreach (var v in s.VerticesWCS)
                    {
                        if (v.X < sxMin) sxMin = v.X; if (v.X > sxMax) sxMax = v.X;
                        if (v.Y < syMin) syMin = v.Y; if (v.Y > syMax) syMax = v.Y;
                    }
                    return sxMax >= bxMin && sxMin <= bxMax
                        && syMax >= byMin && syMin <= byMax;
                }).ToList();

                if (candidates.Count == 0)
                {
                    b.DataState = DataState.Warning;
                    warned++;
                    Debug.WriteLine($"{LOG_PREFIX} [1.5] WARN {ShortGuid(b)} — 0 stiff candidates in proximity");
                    continue;
                }

                // STEP 2 — Contact edge search (3 conditions)
                bool found = false;
                int nb = b.VerticesWCS.Length;

                for (int i = 0; i < nb && !found; i++)
                {
                    var eb1 = b.VerticesWCS[i];
                    var eb2 = b.VerticesWCS[(i + 1) % nb];
                    double ebDx = eb2.X - eb1.X, ebDy = eb2.Y - eb1.Y;
                    double ebLen = Math.Sqrt(ebDx * ebDx + ebDy * ebDy);
                    if (ebLen < 1e-3) continue;
                    double ebUx = ebDx / ebLen, ebUy = ebDy / ebLen;

                    foreach (var s in candidates)
                    {
                        int ns = s.VerticesWCS.Length;
                        for (int j = 0; j < ns && !found; j++)
                        {
                            var ss1 = s.VerticesWCS[j];
                            var ss2 = s.VerticesWCS[(j + 1) % ns];
                            double ssDx = ss2.X - ss1.X, ssDy = ss2.Y - ss1.Y;
                            double ssLen = Math.Sqrt(ssDx * ssDx + ssDy * ssDy);
                            if (ssLen < 1e-3) continue;
                            double ssUx = ssDx / ssLen, ssUy = ssDy / ssLen;

                            // Condition 1: segment-to-segment distance
                            double dist = SegmentToSegmentDist(eb1, eb2, ss1, ss2);
                            if (dist >= CONTACT_TOL_MM) continue;

                            // Condition 2: song song (angle < 5°)
                            double absDot = Math.Abs(ebUx * ssUx + ebUy * ssUy);
                            if (absDot < cosAngleTol) continue;

                            // Condition 3: projected overlap > 0
                            // Project cả 2 segments lên E_b direction để check overlap
                            double t_eb1 = eb1.X * ebUx + eb1.Y * ebUy;
                            double t_eb2 = eb2.X * ebUx + eb2.Y * ebUy;
                            double t_ss1 = ss1.X * ebUx + ss1.Y * ebUy;
                            double t_ss2 = ss2.X * ebUx + ss2.Y * ebUy;
                            double minA = Math.Min(t_eb1, t_eb2), maxA = Math.Max(t_eb1, t_eb2);
                            double minB = Math.Min(t_ss1, t_ss2), maxB = Math.Max(t_ss1, t_ss2);
                            double overlap = Math.Min(maxA, maxB) - Math.Max(minA, minB);
                            if (overlap <= 0) continue;

                            // Link confirmed
                            b.LinkedStiffenerGuid          = s.Guid;
                            b.StiffenerContactEdgeStart    = eb1;
                            b.StiffenerContactEdgeEnd      = eb2;
                            b.DataState                    = DataState.AutoDetected;
                            found = true;

                            Debug.WriteLine($"{LOG_PREFIX} [1.5] {ShortGuid(b)} Bracket " +
                                $"→ Stiff {ShortGuid(s)} ({s.ElemType}) " +
                                $"edge[{i}] dist={dist:F2}mm overlap={overlap:F0}mm " +
                                $"angle={Math.Acos(absDot) * 180.0 / Math.PI:F1}°");
                        }
                    }
                }

                // STEP 3 — Validate
                if (!found)
                {
                    b.DataState = DataState.Warning;
                    warned++;
                    Debug.WriteLine($"{LOG_PREFIX} [1.5] WARN {ShortGuid(b)} Bracket — no stiff contact edge found");
                }
                else
                {
                    linked++;
                }
            }

            Debug.WriteLine($"{LOG_PREFIX} [1.5] SUMMARY — linked={linked} warned={warned} / {brackets.Count} brackets");
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1.5 MERGE — Extend stiffener CLSpan to include linked bracket spans
        // ═══════════════════════════════════════════════════════════

        private const double CL_MERGE_ANGLE_TOL_DEG = 0.5;
        private const double CL_MERGE_PERP_TOL_MM   = 2.0;

        /// <summary>
        /// Sau Pass 2, với mỗi bracket có LinkedStiffenerGuid:
        /// Project TẤT CẢ vertices của bracket lên hướng CLSpan của stiffener.
        /// → Extend stiffener CLSpan + set bracket CLSpan = merged span (snap to stiffener CL line).
        ///
        /// KHÔNG dùng perp tolerance vì Pass 1.5 đã đảm bảo physical contact (dist&lt;2mm).
        /// Project-and-snap xử lý đúng cả bracket song song (extend lớn) và
        /// bracket vuông góc (extend nhỏ = chỉ bracket thickness).
        /// </summary>
        private static void MergeLinkedBracketCLSpans(List<StructuralElementModel> clParts)
        {
            var partMap = clParts.ToDictionary(p => p.Guid);

            int mergeCount = 0;
            int skipCount  = 0;

            var brackets = clParts.Where(p =>
                p.ElemType == StructuralType.Bracket
                && p.LinkedStiffenerGuid != null).ToList();

            foreach (var bracket in brackets)
            {
                if (!partMap.TryGetValue(bracket.LinkedStiffenerGuid, out var stiff)) continue;

                var sA = stiff.CLSpanStart;
                var sB = stiff.CLSpanEnd;
                if (sA == null || sB == null) continue;

                // Stiffener CL direction
                double sdx = sB.X - sA.X, sdy = sB.Y - sA.Y;
                double sLen = Math.Sqrt(sdx * sdx + sdy * sdy);
                if (sLen < 1e-6) continue;
                double sUx = sdx / sLen, sUy = sdy / sLen;

                // Project all bracket vertices onto stiffener CL direction
                double tMin = 0, tMax = sLen;   // start with existing stiffener span [0, sLen]
                var verts = bracket.VerticesWCS;
                if (verts == null || verts.Length == 0)
                {
                    // fallback: use bracket CLSpan endpoints
                    var bA2 = bracket.CLSpanStart;
                    var bB2 = bracket.CLSpanEnd;
                    if (bA2 == null || bB2 == null) { skipCount++; continue; }
                    double ta = (bA2.X - sA.X) * sUx + (bA2.Y - sA.Y) * sUy;
                    double tb = (bB2.X - sA.X) * sUx + (bB2.Y - sA.Y) * sUy;
                    if (ta < tMin) tMin = ta;
                    if (ta > tMax) tMax = ta;
                    if (tb < tMin) tMin = tb;
                    if (tb > tMax) tMax = tb;
                }
                else
                {
                    foreach (var v in verts)
                    {
                        double t = (v.X - sA.X) * sUx + (v.Y - sA.Y) * sUy;
                        if (t < tMin) tMin = t;
                        if (t > tMax) tMax = t;
                    }
                }

                var mergedStart = new Point2dModel { X = sA.X + tMin * sUx, Y = sA.Y + tMin * sUy };
                var mergedEnd   = new Point2dModel { X = sA.X + tMax * sUx, Y = sA.Y + tMax * sUy };

                stiff.CLSpanStart   = mergedStart;
                stiff.CLSpanEnd     = mergedEnd;
                // Snap bracket CLSpan onto stiffener's CL line → 1 shared line entity
                bracket.CLSpanStart = mergedStart;
                bracket.CLSpanEnd   = mergedEnd;

                mergeCount++;
                Debug.WriteLine($"{LOG_PREFIX} [CL-Merge] {ShortGuid(stiff)}({stiff.ElemType}) ← {ShortGuid(bracket)} ext=[{tMin:F0},{tMax:F0}]mm");
            }

            Debug.WriteLine($"{LOG_PREFIX} [CL-Merge] merged={mergeCount} skipped={skipCount} / {brackets.Count} linked brackets");
        }

        // ═══════════════════════════════════════════════════════════
        // Step 6 — Connected Long-Edge Rule (final override)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Priority 4: WebPlate/ClosingBoxWeb A có EXACTLY ONE long edge nhận SE của web khác
        /// (WebPlate/ClosingBoxWeb), đặt CL của A = edge đó + throw vuông góc về centroid.
        /// 0 hoặc 2 edges có connect → skip (giữ logic trước).
        /// SKIP nếu part thuộc collinear group (P1 wins) hoặc IsEdgeElement (P2 wins).
        /// </summary>
        private static void ApplyConnectedLongEdgeRule(
            List<StructuralElementModel> clParts,
            Dictionary<string, CLAssignment> clAssign)
        {
            var webLike = clParts.Where(p => p.ElemType == StructuralType.WebPlate
                                          || p.ElemType == StructuralType.ClosingBoxWeb
                                          || p.ElemType == StructuralType.GirderEnd).ToList();

            int overrideCount = 0, skipMulti = 0, skipLong = 0, skipP1 = 0;
            foreach (var a in webLike)
            {
                // P1 wins: trong collinear group → skip
                if (clAssign.TryGetValue(a.Guid, out var cl) && !cl.IsIsolated) { skipP1++; continue; }
                // P2 (outer) chỉ xét cho web > 1000mm → cho short plates, P4 có thể override.
                // → KHÔNG skip theo IsEdgeElement ở đây.

                if (a.ObbLength >= DetailDesignConstants.CONN_RULE_MAX_LENGTH) { skipLong++; continue; }
                if (!ThicknessCalculator.TryGetParallelPair(a.VerticesWCS,
                        out var e1s, out var e1e, out var e2s, out var e2e, out _)) continue;

                bool e1HasConnect = LongEdgeReceivesShortEdges(e1s, e1e, a, webLike);
                bool e2HasConnect = LongEdgeReceivesShortEdges(e2s, e2e, a, webLike);

                if (e1HasConnect == e2HasConnect)
                {
                    if (e1HasConnect) skipMulti++;
                    continue; // 0 hoặc 2 → skip
                }

                Point2dModel bs, be;
                if (e1HasConnect) { bs = e1s; be = e1e; } else { bs = e2s; be = e2e; }

                double bdx = be.X - bs.X, bdy = be.Y - bs.Y;
                double blen = Math.Sqrt(bdx * bdx + bdy * bdy);
                if (blen < 1e-6) continue;
                double perpX = -bdy / blen, perpY = bdx / blen;

                // Direction: hướng về centroid của A
                double bmx = (bs.X + be.X) / 2.0, bmy = (bs.Y + be.Y) / 2.0;
                double toCx = a.CentroidX - bmx, toCy = a.CentroidY - bmy;
                if (perpX * toCx + perpY * toCy < 0) { perpX = -perpX; perpY = -perpY; }

                a.BaseStart   = bs;
                a.BaseEnd     = be;
                a.ThrowVecX   = perpX;
                a.ThrowVecY   = perpY;
                a.CLSpanStart = bs;
                a.CLSpanEnd   = be;

                overrideCount++;
                Debug.WriteLine($"{LOG_PREFIX} [ConnLE] {ShortGuid(a)} override: base=({bs.X:F0},{bs.Y:F0})-({be.X:F0},{be.Y:F0}) throw=({perpX:F2},{perpY:F2})");
            }
            Debug.WriteLine($"{LOG_PREFIX} [ConnLE] applied={overrideCount} skipped-P1={skipP1} skipped-long={skipLong} skipped-multi={skipMulti} / {webLike.Count} web-like");
        }

        /// <summary>
        /// Check: có web khác B (≠ self) với 1 trong 2 short edge của B nằm trên long edge
        /// (leS→leE) của self (cả 2 endpoint của SE đều nằm trên line LE và trong extent)?
        /// </summary>
        private static bool LongEdgeReceivesShortEdges(
            Point2dModel leS, Point2dModel leE,
            StructuralElementModel self, List<StructuralElementModel> allWebs)
        {
            foreach (var b in allWebs)
            {
                if (b.Guid == self.Guid) continue;
                var seList = GetShortEdgesOfRect(b);
                if (seList == null) continue;
                foreach (var se in seList)
                {
                    // SE của B vuông góc LE của A: chỉ 1 endpoint nằm trên LE là đủ
                    // (endpoint kia cách LE ≈ thickness của B).
                    if (PointOnSegmentExtent(se.s, leS, leE, DetailDesignConstants.TOLERANCE_CONTACT)
                     || PointOnSegmentExtent(se.e, leS, leE, DetailDesignConstants.TOLERANCE_CONTACT))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Trả các short edges của polyline — edges có length gần ObbWidth (± 30%).
        /// Support cả bended polyline (&gt;4 vertices). Null nếu không xác định.
        /// </summary>
        private static List<(Point2dModel s, Point2dModel e)> GetShortEdgesOfRect(StructuralElementModel p)
        {
            var v = p.VerticesWCS;
            if (v == null || v.Length < 4 || p.ObbWidth <= 0) return null;
            var result = new List<(Point2dModel, Point2dModel)>();
            double target = p.ObbWidth;
            double minLen = target * 0.7;
            double maxLen = target * 1.3;
            for (int i = 0; i < v.Length; i++)
            {
                var a = v[i]; var b = v[(i + 1) % v.Length];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len >= minLen && len <= maxLen)
                    result.Add((a, b));
            }
            return result.Count > 0 ? result : null;
        }

        // ═══════════════════════════════════════════════════════════
        // Step 7 — Slanted Connected-Edge Rule
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Priority 4: WebPlate/ClosingBoxWeb SLANTED (OBB angle không align trục), có
        /// EXACTLY ONE edge mà 2 endpoint của edge chạm 2 web khác nhau →
        /// CL = edge đó, throw vuông góc về centroid.
        /// SKIP nếu part thuộc collinear group (P1 wins) hoặc IsEdgeElement (P2 wins).
        /// </summary>
        private static void ApplySlantedConnectedEdgeRule(
            List<StructuralElementModel> clParts,
            Dictionary<string, CLAssignment> clAssign)
        {
            var webLike = clParts.Where(p => p.ElemType == StructuralType.WebPlate
                                          || p.ElemType == StructuralType.ClosingBoxWeb
                                          || p.ElemType == StructuralType.GirderEnd).ToList();
            int applied = 0, skipMulti = 0, skipLong = 0, skipP1 = 0;

            foreach (var s in webLike)
            {
                // P1 wins: collinear group → skip
                if (clAssign.TryGetValue(s.Guid, out var cl) && !cl.IsIsolated) { skipP1++; continue; }
                // P2 (outer) chỉ xét cho web > 1000mm → không skip theo IsEdgeElement.

                if (s.ObbLength >= DetailDesignConstants.CONN_RULE_MAX_LENGTH) { skipLong++; continue; }
                if (!IsSlantedOrientation(s.ObbAngle)) continue;
                if (s.VerticesWCS == null || s.VerticesWCS.Length < 3) continue;

                int qualifyEdge = -1;
                int qualifyCount = 0;
                for (int i = 0; i < s.VerticesWCS.Length; i++)
                {
                    var va = s.VerticesWCS[i];
                    var vb = s.VerticesWCS[(i + 1) % s.VerticesWCS.Length];
                    var wa = FindConnectedWeb(va, s, webLike);
                    var wb = FindConnectedWeb(vb, s, webLike);
                    if (wa != null && wb != null && wa.Guid != wb.Guid)
                    {
                        qualifyEdge = i;
                        qualifyCount++;
                    }
                }

                if (qualifyCount != 1)
                {
                    if (qualifyCount > 1) skipMulti++;
                    continue;
                }

                var bs = s.VerticesWCS[qualifyEdge];
                var be = s.VerticesWCS[(qualifyEdge + 1) % s.VerticesWCS.Length];
                double bdx = be.X - bs.X, bdy = be.Y - bs.Y;
                double blen = Math.Sqrt(bdx * bdx + bdy * bdy);
                if (blen < 1e-6) continue;
                double perpX = -bdy / blen, perpY = bdx / blen;

                double bmx = (bs.X + be.X) / 2.0, bmy = (bs.Y + be.Y) / 2.0;
                double toCx = s.CentroidX - bmx, toCy = s.CentroidY - bmy;
                if (perpX * toCx + perpY * toCy < 0) { perpX = -perpX; perpY = -perpY; }

                s.BaseStart   = bs;
                s.BaseEnd     = be;
                s.ThrowVecX   = perpX;
                s.ThrowVecY   = perpY;
                s.CLSpanStart = bs;
                s.CLSpanEnd   = be;

                applied++;
                Debug.WriteLine($"{LOG_PREFIX} [SlantedConn] {ShortGuid(s)} override: edge{qualifyEdge} base=({bs.X:F0},{bs.Y:F0})-({be.X:F0},{be.Y:F0}) throw=({perpX:F2},{perpY:F2})");
            }
            Debug.WriteLine($"{LOG_PREFIX} [SlantedConn] applied={applied} skipped-P1={skipP1} skipped-long={skipLong} skipped-multi={skipMulti} / {webLike.Count} web-like");
        }

        /// <summary>
        /// Slanted: OBB angle không align với trục X/Y (± 1° tolerance).
        /// </summary>
        private static bool IsSlantedOrientation(double angleRad)
        {
            double a = Math.Abs(angleRad) % (Math.PI / 2);
            const double tol = 1.0 * Math.PI / 180.0;
            return a > tol && a < (Math.PI / 2 - tol);
        }

        /// <summary>
        /// Tìm web khác (≠ self) có boundary chạm point V trong tolerance.
        /// Return web đầu tiên tìm thấy, null nếu không có.
        /// </summary>
        private static StructuralElementModel FindConnectedWeb(
            Point2dModel v, StructuralElementModel self, List<StructuralElementModel> webs)
        {
            foreach (var w in webs)
            {
                if (w.Guid == self.Guid) continue;
                if (w.VerticesWCS == null) continue;
                for (int i = 0; i < w.VerticesWCS.Length; i++)
                {
                    var wa = w.VerticesWCS[i];
                    var wb = w.VerticesWCS[(i + 1) % w.VerticesWCS.Length];
                    if (PointOnSegmentExtent(v, wa, wb, DetailDesignConstants.TOLERANCE_CONTACT))
                        return w;
                }
            }
            return null;
        }

        /// <summary>
        /// Check: point P có nằm trên segment (AB) — perpendicular distance < tol AND
        /// projection parameter trong [0, 1] (with small slack).
        /// </summary>
        private static bool PointOnSegmentExtent(Point2dModel p, Point2dModel a, Point2dModel b, double tol)
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

        // ─── Geometry helpers cho Pass 1.5 ───

        /// <summary>
        /// Minimum distance giữa 2 line segments trong 2D.
        /// Trả 0 nếu chúng intersect.
        /// </summary>
        private static double SegmentToSegmentDist(
            Point2dModel a1, Point2dModel a2,
            Point2dModel b1, Point2dModel b2)
        {
            if (Seg2dIntersects(a1, a2, b1, b2)) return 0.0;
            return Math.Min(
                Math.Min(PointToSegmentDist(a1.X, a1.Y, b1.X, b1.Y, b2.X, b2.Y),
                         PointToSegmentDist(a2.X, a2.Y, b1.X, b1.Y, b2.X, b2.Y)),
                Math.Min(PointToSegmentDist(b1.X, b1.Y, a1.X, a1.Y, a2.X, a2.Y),
                         PointToSegmentDist(b2.X, b2.Y, a1.X, a1.Y, a2.X, a2.Y)));
        }

        /// <summary>Kiểm tra 2 segments có intersect không (proper intersection).</summary>
        private static bool Seg2dIntersects(
            Point2dModel a1, Point2dModel a2,
            Point2dModel b1, Point2dModel b2)
        {
            double d1 = Cross2d(b2.X - b1.X, b2.Y - b1.Y, a1.X - b1.X, a1.Y - b1.Y);
            double d2 = Cross2d(b2.X - b1.X, b2.Y - b1.Y, a2.X - b1.X, a2.Y - b1.Y);
            double d3 = Cross2d(a2.X - a1.X, a2.Y - a1.Y, b1.X - a1.X, b1.Y - a1.Y);
            double d4 = Cross2d(a2.X - a1.X, a2.Y - a1.Y, b2.X - a1.X, b2.Y - a1.Y);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        private static double Cross2d(double ux, double uy, double vx, double vy)
            => ux * vy - uy * vx;

        // ═══════════════════════════════════════════════════════════
        // PASS 2 — Throw Thickness (type + outer/inner rule)
        // ═══════════════════════════════════════════════════════════

        private static void ApplyThrowToGroups(
            List<StructuralElementModel> clParts,
            Dictionary<string, CLAssignment> clAssign,
            Dictionary<string, ElementGeom> infoMap,
            List<StructuralElementModel> topPlates,
            PanelContext panel)
        {
            // Sub-pass A — Web / Stiff / BS / others (KHÔNG bracket)
            foreach (var p in clParts)
            {
                if (p.ElemType == StructuralType.Bracket) continue;
                if (!clAssign.TryGetValue(p.Guid, out var cl)) continue;
                if (!infoMap.TryGetValue(p.Guid, out var g)) continue;
                ApplyThrowByTypeAndBoundary(p, g, panel, topPlates, cl);
            }

            // Sub-pass B — Bracket inherit từ parent (web group-mate hoặc linked stiffener)
            var partMap = clParts.ToDictionary(x => x.Guid);
            var groupMembers = clAssign
                .GroupBy(kv => kv.Value.GroupId)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

            foreach (var p in clParts)
            {
                if (p.ElemType != StructuralType.Bracket) continue;
                if (!clAssign.TryGetValue(p.Guid, out var cl)) continue;
                if (!infoMap.TryGetValue(p.Guid, out var g)) continue;
                ApplyBracketThrowInherit(p, g, panel, topPlates, cl, partMap, groupMembers);
            }
        }

        /// <summary>
        /// Bracket throw = nghe theo parent:
        ///   Priority 1: trong web group (cl.IsIsolated=false) → inherit từ web member của group.
        ///   Priority 2: có LinkedStiffenerGuid (Pass 1.5) → inherit từ stiffener đó.
        ///   Fallback : ApplyThrowByTypeAndBoundary (HugCheck → INWARD if outer).
        /// </summary>
        private static void ApplyBracketThrowInherit(
            StructuralElementModel bracket, ElementGeom g,
            PanelContext panel, List<StructuralElementModel> topPlates,
            CLAssignment cl,
            Dictionary<string, StructuralElementModel> partMap,
            Dictionary<string, List<string>> groupMembers)
        {
            StructuralElementModel parent = null;
            string parentSrc = "";

            // BF isolated → Side-based throw (không kế thừa BS hướng OUTWARD)
            if (bracket.BracketSubType == "BF" && cl.IsIsolated)
            {
                ComputeSideBasedThrow(g.Orient, panel, out double tx, out double ty);
                PickBase(g.E1s, g.E1e, g.E2s, g.E2e, tx, ty, out var bs, out var be);
                SnapThrowToBasePerpendicular(bs, be, ref tx, ref ty);
                bracket.BaseStart     = bs;
                bracket.BaseEnd       = be;
                bracket.ThrowVecX     = tx;
                bracket.ThrowVecY     = ty;
                bracket.IsEdgeElement = false;
                bracket.CLSpanStart   = bs;
                bracket.CLSpanEnd     = be;
                Debug.WriteLine($"{LOG_PREFIX} BF isolated → Side-based: {ShortGuid(bracket)} throw=({tx:F2},{ty:F2})");
                return;
            }

            // Priority 1: web group-mate
            if (!cl.IsIsolated && groupMembers.TryGetValue(cl.GroupId, out var mates))
            {
                foreach (var guid in mates)
                {
                    if (guid == bracket.Guid) continue;
                    if (!partMap.TryGetValue(guid, out var m)) continue;
                    if (m.ElemType == StructuralType.WebPlate
                     || m.ElemType == StructuralType.ClosingBoxWeb)
                    {
                        parent = m; parentSrc = $"web-grp {cl.GroupId}";
                        break;
                    }
                }
            }

            // Priority 2: linked stiffener
            if (parent == null && !string.IsNullOrEmpty(bracket.LinkedStiffenerGuid)
                && partMap.TryGetValue(bracket.LinkedStiffenerGuid, out var stiff))
            {
                parent = stiff; parentSrc = $"link-stf {ShortGuid(stiff)}";
            }

            if (parent != null && (Math.Abs(parent.ThrowVecX) > 1e-6 || Math.Abs(parent.ThrowVecY) > 1e-6))
            {
                double tx, ty;

                // OB bracket (parent là edge stiffener): override OUTWARD throw bằng inner direction
                // theo Panel.Side — same direction as inner stiffener/web. Tránh ambiguity khi
                // parent throw perpendicular với bracket long axis (PickBase không phân biệt được).
                if (parent.IsEdgeElement)
                {
                    switch (panel.Side)
                    {
                        case PanelSide.Port:
                            tx = DetailDesignConstants.STBD_DIR[0];
                            ty = DetailDesignConstants.STBD_DIR[1];
                            break;
                        case PanelSide.Starboard:
                            tx = DetailDesignConstants.PORT_DIR[0];
                            ty = DetailDesignConstants.PORT_DIR[1];
                            break;
                        default:
                            tx = DetailDesignConstants.STBD_DIR[0];
                            ty = DetailDesignConstants.STBD_DIR[1];
                            break;
                    }
                }
                else
                {
                    // IB bracket: parent inner stiff đã có inner direction, kế thừa trực tiếp
                    tx = parent.ThrowVecX;
                    ty = parent.ThrowVecY;
                }

                PickBase(g.E1s, g.E1e, g.E2s, g.E2e, tx, ty, out var bs, out var be);
                SnapThrowToBasePerpendicular(bs, be, ref tx, ref ty);

                bracket.BaseStart     = bs;
                bracket.BaseEnd       = be;
                bracket.ThrowVecX     = tx;
                bracket.ThrowVecY     = ty;
                bracket.IsEdgeElement = parent.IsEdgeElement;

                if (!cl.IsIsolated)
                {
                    bracket.CLSpanStart = cl.GroupCLStart;
                    bracket.CLSpanEnd   = cl.GroupCLEnd;
                }
                else
                {
                    bracket.CLSpanStart = bs;
                    bracket.CLSpanEnd   = be;
                }

                Debug.WriteLine($"{LOG_PREFIX} Bracket inherit: {ShortGuid(bracket)} ← {parentSrc} edge={parent.IsEdgeElement} throw=({tx:F2},{ty:F2})");
                return;
            }

            // Fallback: logic cũ (HugCheck)
            ApplyThrowByTypeAndBoundary(bracket, g, panel, topPlates, cl);
        }

        /// <summary>
        /// Step 4.3 — Cho nhóm collinear có thickness ĐỒNG NHẤT: recompute CLSpan trên PickBase face.
        /// Project BaseStart/End của tất cả members lên CL direction → unified span trên PickBase face.
        /// Logic: throw đã xác định ở Pass 2 → CL "nghe theo" throw (CL nằm trên face ngược chiều throw).
        /// Nhóm thickness khác nhau: giữ CL trên MemberEdge face (logic cũ).
        /// </summary>
        private static void AlignUniformThicknessGroupCLs(
            List<StructuralElementModel> clParts,
            Dictionary<string, CLAssignment> clAssign)
        {
            var byGroup = clParts
                .Where(p => clAssign.TryGetValue(p.Guid, out var cl) && !cl.IsIsolated)
                .GroupBy(p => clAssign[p.Guid].GroupId)
                .Where(g => g.Count() >= 2)
                .ToList();

            int aligned = 0, skipped = 0;

            foreach (var grp in byGroup)
            {
                var members = grp.ToList();

                // Uniform thickness?
                var thks = members.Where(m => m.Thickness.HasValue).Select(m => m.Thickness.Value).ToList();
                if (thks.Count != members.Count) { skipped++; continue; }
                double t0 = thks[0];
                bool uniform = thks.All(t => Math.Abs(t - t0) < UNIFORM_THICKNESS_TOL_MM);
                if (!uniform) { skipped++; continue; }

                var cl = clAssign[members[0].Guid];
                var sA = cl.GroupCLStart;
                var sB = cl.GroupCLEnd;
                if (sA == null || sB == null) { skipped++; continue; }

                double dx = sB.X - sA.X, dy = sB.Y - sA.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) { skipped++; continue; }
                double ux = dx / len, uy = dy / len;

                var ref0 = members[0].BaseStart;
                if (ref0 == null) { skipped++; continue; }

                double tMin = 0, tMax = 0;
                foreach (var m in members)
                {
                    if (m.BaseStart == null || m.BaseEnd == null) continue;
                    double ta = (m.BaseStart.X - ref0.X) * ux + (m.BaseStart.Y - ref0.Y) * uy;
                    double tb = (m.BaseEnd.X - ref0.X) * ux + (m.BaseEnd.Y - ref0.Y) * uy;
                    if (ta < tMin) tMin = ta;
                    if (ta > tMax) tMax = ta;
                    if (tb < tMin) tMin = tb;
                    if (tb > tMax) tMax = tb;
                }

                var newStart = new Point2dModel { X = ref0.X + tMin * ux, Y = ref0.Y + tMin * uy };
                var newEnd   = new Point2dModel { X = ref0.X + tMax * ux, Y = ref0.Y + tMax * uy };

                foreach (var m in members)
                {
                    m.CLSpanStart = newStart;
                    m.CLSpanEnd   = newEnd;
                }

                aligned++;
                Debug.WriteLine($"{LOG_PREFIX} [UniformCL] {grp.Key} t={t0:F1}mm members={members.Count} → CL on PickBase face");
            }

            Debug.WriteLine($"{LOG_PREFIX} [UniformCL] aligned={aligned} skipped={skipped} / {byGroup.Count} groups");
        }

        /// <summary>
        /// Unified throw rule:
        ///
        /// THROW DIRECTION — theo type + HugCheck:
        ///   Web / ClosingBoxWeb  : HugCheck → INWARD  ; else Side-based
        ///   Stiff / BS           : HugCheck → OUTWARD ; else Side-based
        ///   Bracket              : HugCheck → INWARD  ; else Side-based  ← bracket outer = INWARD như web
        ///   Flange               : Side-based (xử lý riêng bên ngoài)
        ///
        /// BASE EDGE (nơi đặt symbol + vẽ CL):
        ///   Group member (IsIsolated=false) → dùng MemberEdge đã match ở Pass 1 (collinear edge).
        ///                                     CLSpan = GroupCLSpan (unified span cả group).
        ///   Isolated             (IsIsolated=true)  → PickBase từ E1/E2 theo throw direction.
        ///                                     CLSpan = BaseStart/BaseEnd (đảm bảo CL trùng symbol).
        /// </summary>
        private static void ApplyThrowByTypeAndBoundary(
            StructuralElementModel p, ElementGeom g,
            PanelContext panel, List<StructuralElementModel> topPlates,
            CLAssignment cl)
        {
            bool isWeb     = p.ElemType == StructuralType.WebPlate
                          || p.ElemType == StructuralType.ClosingBoxWeb;
            bool isStiff   = p.ElemType == StructuralType.Stiffener
                          || p.ElemType == StructuralType.BucklingStiffener;
            bool isBracket = p.ElemType == StructuralType.Bracket;

            bool isOuter = false;
            double tx, ty;

            if (isWeb)
            {
                isOuter = HugCheckWithDistance(g, topPlates, HUG_DIST_WEB, p.VerticesWCS);
                if (isOuter)
                {
                    if (OuterWebOutward)
                    {
                        ComputeOutwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer web: {ShortGuid(p)} OUTWARD ({tx:F2},{ty:F2})");
                    }
                    else
                    {
                        ComputeInwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer web: {ShortGuid(p)} INWARD ({tx:F2},{ty:F2})");
                    }
                }
                else
                    ComputeSideBasedThrow(g.Orient, panel, out tx, out ty);
            }
            else if (isStiff)
            {
                isOuter = HugCheckWithDistance(g, topPlates, HUG_DIST_STIFF, p.VerticesWCS);
                if (isOuter)
                {
                    if (OuterStiffOutward)
                    {
                        ComputeOutwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer stiff: {ShortGuid(p)} OUTWARD ({tx:F2},{ty:F2})");
                    }
                    else
                    {
                        ComputeInwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer stiff: {ShortGuid(p)} INWARD ({tx:F2},{ty:F2})");
                    }
                }
                else
                    ComputeSideBasedThrow(g.Orient, panel, out tx, out ty);
            }
            else if (isBracket)
            {
                // Fallback khi bracket không inherit được từ parent — nghe theo OuterWeb setting
                isOuter = HugCheckWithDistance(g, topPlates, HUG_DIST_BRACKET, p.VerticesWCS);
                if (isOuter)
                {
                    if (OuterWebOutward)
                    {
                        ComputeOutwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer bracket: {ShortGuid(p)} OUTWARD ({tx:F2},{ty:F2})");
                    }
                    else
                    {
                        ComputeInwardThrow(g.Orient, p, panel, out tx, out ty);
                        Debug.WriteLine($"{LOG_PREFIX} Outer bracket: {ShortGuid(p)} INWARD ({tx:F2},{ty:F2})");
                    }
                }
                else
                    ComputeSideBasedThrow(g.Orient, panel, out tx, out ty);
            }
            else
            {
                ComputeSideBasedThrow(g.Orient, panel, out tx, out ty);
            }

            Point2dModel bs, be;

            if (!cl.IsIsolated && cl.MemberEdgeStart != null)
            {
                // Group member: CLSpan = unified group span (trên MemberEdge face)
                p.CLSpanStart = cl.GroupCLStart;
                p.CLSpanEnd   = cl.GroupCLEnd;

                // BaseEdge: dùng PickBase (throw-aware) thay vì MemberEdge cứng
                // MemberEdge từ Pass 1 chọn theo collinearity, không phải theo throw direction
                // → có thể là face SAI (cùng chiều throw thay vì ngược chiều)
                // PickBase luôn chọn face NGƯỢC CHIỀU throw → đúng cho inner lẫn outer
                PickBase(g.E1s, g.E1e, g.E2s, g.E2e, tx, ty, out bs, out be);

                // Nếu PickBase chọn khác MemberEdge → log để trace
                double mMidY = (cl.MemberEdgeStart.Y + cl.MemberEdgeEnd.Y) / 2.0;
                double bMidY = (bs.Y + be.Y) / 2.0;
                if (Math.Abs(mMidY - bMidY) > 1.0)
                    Debug.WriteLine($"{LOG_PREFIX} [FaceSwap] {ShortGuid(p)} MemberEdge→PickBase swap Δ={(bMidY-mMidY):F1}mm");

                SnapThrowToBasePerpendicular(bs, be, ref tx, ref ty);
            }
            else
            {
                // Isolated: PickBase → CLSpan = base edge (đảm bảo symbol nằm trên CL)
                PickBase(g.E1s, g.E1e, g.E2s, g.E2e, tx, ty, out bs, out be);
                SnapThrowToBasePerpendicular(bs, be, ref tx, ref ty);
                p.CLSpanStart = bs;
                p.CLSpanEnd   = be;
            }

            p.BaseStart     = bs;
            p.BaseEnd       = be;
            p.ThrowVecX     = tx;
            p.ThrowVecY     = ty;
            p.IsEdgeElement = isOuter;

            if (p.Guid != null && (p.Guid.StartsWith("e406d974") || p.Guid.StartsWith("7ca630a9")))
            {
                Debug.WriteLine($"{LOG_PREFIX} [TRACE] guid={p.Guid}");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   type={p.ElemType}  orient={g.Orient}  isolated={cl.IsIsolated}  isOuter={isOuter}");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   centroid=({p.CentroidX:F0},{p.CentroidY:F0})");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   E1=({g.E1s.X:F0},{g.E1s.Y:F0})→({g.E1e.X:F0},{g.E1e.Y:F0})");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   E2=({g.E2s.X:F0},{g.E2s.Y:F0})→({g.E2e.X:F0},{g.E2e.Y:F0})");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   throw=({p.ThrowVecX:F3},{p.ThrowVecY:F3})");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   base=({bs.X:F0},{bs.Y:F0})→({be.X:F0},{be.Y:F0})");
                Debug.WriteLine($"{LOG_PREFIX} [TRACE]   CLSpan=({p.CLSpanStart?.X:F0},{p.CLSpanStart?.Y:F0})→({p.CLSpanEnd?.X:F0},{p.CLSpanEnd?.Y:F0})");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Targeted FLIP — dùng cho UI radio button toggle + per-element flip
        // ═══════════════════════════════════════════════════════════

        /// <summary>Flip throw direction cho 1 element: đảo vector throw + chuyển base sang face đối diện.</summary>
        public static void FlipSingleElementThrow(StructuralElementModel p)
        {
            if (p == null || p.VerticesWCS == null
                || p.BaseStart == null || p.BaseEnd == null) return;
            if (!ThicknessCalculator.TryGetParallelPair(p.VerticesWCS,
                    out var e1s, out var e1e, out var e2s, out var e2e, out _)) return;

            double bmx = (p.BaseStart.X + p.BaseEnd.X) / 2.0;
            double bmy = (p.BaseStart.Y + p.BaseEnd.Y) / 2.0;
            double m1x = (e1s.X + e1e.X) / 2.0, m1y = (e1s.Y + e1e.Y) / 2.0;
            double m2x = (e2s.X + e2e.X) / 2.0, m2y = (e2s.Y + e2e.Y) / 2.0;
            double d1 = (bmx - m1x) * (bmx - m1x) + (bmy - m1y) * (bmy - m1y);
            double d2 = (bmx - m2x) * (bmx - m2x) + (bmy - m2y) * (bmy - m2y);

            // Base hiện tại ở face nào → chuyển sang face còn lại
            if (d1 <= d2) { p.BaseStart = e2s; p.BaseEnd = e2e; }
            else          { p.BaseStart = e1s; p.BaseEnd = e1e; }

            p.ThrowVecX = -p.ThrowVecX;
            p.ThrowVecY = -p.ThrowVecY;
        }

        /// <summary>Flip chỉ outer Stiffener/BS (các part khác giữ nguyên).</summary>
        public static int FlipOuterStiffThrow(List<StructuralElementModel> elements)
        {
            int n = 0;
            foreach (var p in elements)
            {
                if (p.IsHole) continue;
                if ((p.ElemType == StructuralType.Stiffener
                  || p.ElemType == StructuralType.BucklingStiffener)
                  && p.IsEdgeElement)
                {
                    FlipSingleElementThrow(p);
                    n++;
                }
            }
            Debug.WriteLine($"{LOG_PREFIX} FlipOuterStiff: flipped {n} elements");
            return n;
        }

        /// <summary>Flip chỉ outer Web/ClosingBoxWeb (các part khác giữ nguyên).</summary>
        public static int FlipOuterWebThrow(List<StructuralElementModel> elements)
        {
            int n = 0;
            foreach (var p in elements)
            {
                if (p.IsHole) continue;
                if ((p.ElemType == StructuralType.WebPlate
                  || p.ElemType == StructuralType.ClosingBoxWeb)
                  && p.IsEdgeElement)
                {
                    FlipSingleElementThrow(p);
                    n++;
                }
            }
            Debug.WriteLine($"{LOG_PREFIX} FlipOuterWeb: flipped {n} elements");
            return n;
        }

        private static void ApplyFlangeSideBased(StructuralElementModel p, ElementGeom g, PanelContext panel)
        {
            ComputeSideBasedThrow(g.Orient, panel, out double tx, out double ty);
            PickBase(g.E1s, g.E1e, g.E2s, g.E2e, tx, ty, out var bs, out var be);
            SnapThrowToBasePerpendicular(bs, be, ref tx, ref ty);
            p.BaseStart = bs; p.BaseEnd = be;
            p.ThrowVecX = tx; p.ThrowVecY = ty;
            p.IsEdgeElement = false;
        }

        // ═══════════════════════════════════════════════════════════
        // Outer web detection — hug check
        // ═══════════════════════════════════════════════════════════

        /// <summary>Dominant stiffener spacing × 2 (cho hug web); fallback 600mm.</summary>
        /// <summary>
        /// Web "hug boundary" check — có ≥ 1 long edge parallel với top plate segment
        /// AND bất kỳ vertex nào của element nằm trong hugDist từ boundary segment đó.
        /// Dùng tất cả vertices (không chỉ midpoint) để tránh miss corner/angled elements.
        /// </summary>
        private static bool HugCheckWithDistance(ElementGeom geom,
            List<StructuralElementModel> topPlates, double hugDist,
            Point2dModel[] elemVertices = null)
        {
            var partEdges = new[] { (geom.E1s, geom.E1e), (geom.E2s, geom.E2e) };
            double cosTol = Math.Cos(HUG_ANGLE_TOL_DEG * Math.PI / 180.0);

            // Fallback vertices: dùng endpoints của 2 long edges nếu không có VerticesWCS
            var checkVerts = (elemVertices != null && elemVertices.Length > 0)
                ? elemVertices
                : new[] { geom.E1s, geom.E1e, geom.E2s, geom.E2e };

            foreach (var tp in topPlates)
            {
                var verts = tp.VerticesWCS;
                if (verts == null || verts.Length < 2) continue;
                int n = verts.Length;
                for (int i = 0; i < n; i++)
                {
                    var bs = verts[i];
                    var be = verts[(i + 1) % n];
                    double bx = be.X - bs.X, by = be.Y - bs.Y;
                    double bLen = Math.Sqrt(bx * bx + by * by);
                    if (bLen < 1e-6) continue;
                    bx /= bLen; by /= bLen;

                    // Angle check: ≥ 1 long edge của element phải song song với boundary segment
                    bool anyParallel = false;
                    foreach (var (sa, sb) in partEdges)
                    {
                        double sx = sb.X - sa.X, sy = sb.Y - sa.Y;
                        double sLen = Math.Sqrt(sx * sx + sy * sy);
                        if (sLen < 1e-6) continue;
                        if (Math.Abs(bx * (sx / sLen) + by * (sy / sLen)) >= cosTol)
                        {
                            anyParallel = true;
                            break;
                        }
                    }
                    if (!anyParallel) continue;

                    // Distance check: bất kỳ vertex nào của element trong hugDist
                    foreach (var v in checkVerts)
                    {
                        double d = PointToSegmentDist(v.X, v.Y, bs.X, bs.Y, be.X, be.Y);
                        if (d <= hugDist) return true;
                    }
                }
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════
        // Throw direction helpers (Side-based cho Isolated + Flange)
        // ═══════════════════════════════════════════════════════════

        /// <summary>Inward throw: aligned trục orient, hướng về tâm panel.</summary>
        private static void ComputeInwardThrow(string orient, StructuralElementModel elem,
            PanelContext panel, out double x, out double y)
        {
            double pcx = panel.CentroidX ?? 0;
            double pcy = panel.CentroidY ?? 0;
            if (orient == "LONG")
            {
                double dy = pcy - elem.CentroidY;
                if (Math.Abs(dy) < 1e-3) { ComputeSideBasedThrow(orient, panel, out x, out y); return; }
                x = 0; y = dy > 0 ? 1 : -1;
            }
            else
            {
                double dx = pcx - elem.CentroidX;
                if (Math.Abs(dx) < 1e-3) { ComputeSideBasedThrow(orient, panel, out x, out y); return; }
                x = dx > 0 ? 1 : -1; y = 0;
            }
        }

        /// <summary>Outward throw: aligned trục orient, hướng ra xa tâm panel.</summary>
        private static void ComputeOutwardThrow(string orient, StructuralElementModel elem,
            PanelContext panel, out double x, out double y)
        {
            double pcx = panel.CentroidX ?? 0;
            double pcy = panel.CentroidY ?? 0;
            if (orient == "LONG")
            {
                double dy = elem.CentroidY - pcy;
                if (Math.Abs(dy) < 1e-3) { ComputeSideBasedThrow(orient, panel, out x, out y); return; }
                x = 0; y = dy > 0 ? 1 : -1;
            }
            else
            {
                double dx = elem.CentroidX - pcx;
                if (Math.Abs(dx) < 1e-3) { ComputeSideBasedThrow(orient, panel, out x, out y); return; }
                x = dx > 0 ? 1 : -1; y = 0;
            }
        }

        private static void ComputeSideBasedThrow(string orient, PanelContext panel,
            out double x, out double y)
        {
            if (orient == "LONG")
            {
                switch (panel.Side)
                {
                    case PanelSide.Port: x = 0; y = -1; return;
                    case PanelSide.Starboard: x = 0; y = +1; return;
                    case PanelSide.Center: default: x = +1; y = 0; return;
                }
            }
            else { x = +1; y = 0; }
        }

        private static void PickBase(Point2dModel e1s, Point2dModel e1e,
            Point2dModel e2s, Point2dModel e2e, double throwX, double throwY,
            out Point2dModel baseStart, out Point2dModel baseEnd)
        {
            double m1x = (e1s.X + e1e.X) / 2.0, m1y = (e1s.Y + e1e.Y) / 2.0;
            double m2x = (e2s.X + e2e.X) / 2.0, m2y = (e2s.Y + e2e.Y) / 2.0;
            double proj1 = m1x * throwX + m1y * throwY;
            double proj2 = m2x * throwX + m2y * throwY;
            if (proj1 <= proj2) { baseStart = e1s; baseEnd = e1e; }
            else { baseStart = e2s; baseEnd = e2e; }
        }

        private static void SnapThrowToBasePerpendicular(Point2dModel baseStart, Point2dModel baseEnd,
            ref double throwX, ref double throwY)
        {
            double bdx = baseEnd.X - baseStart.X, bdy = baseEnd.Y - baseStart.Y;
            double blen = Math.Sqrt(bdx * bdx + bdy * bdy);
            if (blen < 1e-6) return;
            bdx /= blen; bdy /= blen;
            double perpX = -bdy, perpY = bdx;
            if (perpX * throwX + perpY * throwY < 0) { perpX = bdy; perpY = -bdx; }
            throwX = perpX; throwY = perpY;
        }

        // ═══════════════════════════════════════════════════════════
        // FIX 4 — ExtractStructuralSegments (bent / knuckle stiffener)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Tách stiffener bẻ cong (>4 vertices, fail TryGetParallelPair) thành các đoạn thẳng.
        ///
        /// Thuật toán:
        ///   1. Tìm trục chính qua cặp đỉnh xa nhau nhất.
        ///   2. Chiếu tất cả vertices lên trục chính (longProj) và trục vuông góc (perpProj).
        ///   3. Chia vertices thành Side A (n/2 vertices perpProj nhỏ nhất) và Side B (lớn nhất).
        ///   4. Sắp xếp mỗi side theo longProj.
        ///   5. Tìm điểm bẻ cong (knuckle) trong Side A: góc lệch hướng > KNUCKLE_SPLIT_ANGLE_DEG.
        ///   6. Tại mỗi knuckle, split → đoạn mới. Mỗi đoạn phải >= MIN_CL_SEGMENT (200mm).
        ///   7. Với Side B: split tại t-value gần nhất với knuckle của Side A.
        ///   8. Trả về list ElementGeom (E1 = Side A edge, E2 = Side B edge) cho từng đoạn.
        /// </summary>
        private static List<ElementGeom> ExtractStructuralSegments(Point2dModel[] verts)
        {
            var result = new List<ElementGeom>();
            int n = verts.Length;
            if (n < 6) return result;

            // Tìm trục chính: cặp đỉnh xa nhau nhất
            int idxA = 0, idxB = 1;
            double maxDist = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = Distance(verts[i], verts[j]);
                    if (d > maxDist) { maxDist = d; idxA = i; idxB = j; }
                }
            double mdx = verts[idxB].X - verts[idxA].X;
            double mdy = verts[idxB].Y - verts[idxA].Y;
            double mLen = Math.Sqrt(mdx * mdx + mdy * mdy);
            if (mLen < 1e-6) return result;
            double ux = mdx / mLen, uy = mdy / mLen;
            double pxN = -uy, pyN = ux;  // perp unit normal

            double[] longProj = new double[n];
            double[] perpProj = new double[n];
            for (int i = 0; i < n; i++)
            {
                longProj[i] = verts[i].X * ux + verts[i].Y * uy;
                perpProj[i] = verts[i].X * pxN + verts[i].Y * pyN;
            }

            // Chia Side A / Side B: n/2 vertices có perpProj thấp / cao nhất
            int halfN = n / 2;
            int[] sortedByPerp = Enumerable.Range(0, n).OrderBy(i => perpProj[i]).ToArray();
            var sideAIdx = sortedByPerp.Take(halfN).OrderBy(i => longProj[i]).ToList();
            var sideBIdx = sortedByPerp.Skip(n - halfN).OrderBy(i => longProj[i]).ToList();
            if (sideAIdx.Count < 2 || sideBIdx.Count < 2) return result;

            // Tìm knuckle points trong Side A (góc lệch > KNUCKLE_SPLIT_ANGLE_DEG)
            double cosKnuckle = Math.Cos(KNUCKLE_SPLIT_ANGLE_DEG * Math.PI / 180.0);
            var knuckleTValues = new List<double>();
            for (int k = 1; k < sideAIdx.Count - 1; k++)
            {
                int prev = sideAIdx[k - 1], cur = sideAIdx[k], next = sideAIdx[k + 1];
                double d1x = verts[cur].X - verts[prev].X, d1y = verts[cur].Y - verts[prev].Y;
                double d2x = verts[next].X - verts[cur].X,  d2y = verts[next].Y - verts[cur].Y;
                double l1 = Math.Sqrt(d1x * d1x + d1y * d1y);
                double l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                if (l1 < 1e-6 || l2 < 1e-6) continue;
                double dot = (d1x / l1) * (d2x / l2) + (d1y / l1) * (d2y / l2);
                if (dot < cosKnuckle)
                    knuckleTValues.Add(longProj[cur]);
            }

            // Build t-range boundaries: [tMin, knuckle1, knuckle2, ..., tMax]
            double tMin = longProj[sideAIdx[0]];
            double tMax = longProj[sideAIdx[sideAIdx.Count - 1]];
            var splitT = new List<double> { tMin };
            foreach (var t in knuckleTValues.OrderBy(t => t))
                if (t > tMin + 1.0 && t < tMax - 1.0)
                    splitT.Add(t);
            splitT.Add(tMax);

            // Tạo ElementGeom cho từng đoạn
            for (int seg = 0; seg < splitT.Count - 1; seg++)
            {
                double tS = splitT[seg], tE = splitT[seg + 1];
                if (tE - tS < MIN_CL_SEGMENT) continue;

                // Lấy vertices trong range [tS, tE] ±1mm cho mỗi side
                var aInSeg = sideAIdx.Where(i => longProj[i] >= tS - 1.0 && longProj[i] <= tE + 1.0).ToList();
                var bInSeg = sideBIdx.Where(i => longProj[i] >= tS - 1.0 && longProj[i] <= tE + 1.0).ToList();
                if (aInSeg.Count < 2 || bInSeg.Count < 2) continue;

                var e1sv = verts[aInSeg[0]];
                var e1ev = verts[aInSeg[aInSeg.Count - 1]];
                var e2sv = verts[bInSeg[0]];
                var e2ev = verts[bInSeg[bInSeg.Count - 1]];

                double len = Math.Max(Distance(e1sv, e1ev), Distance(e2sv, e2ev));
                if (len < MIN_CL_SEGMENT) continue;

                double segDx = e1ev.X - e1sv.X, segDy = e1ev.Y - e1sv.Y;
                string orient = Math.Abs(segDx) >= Math.Abs(segDy) ? "LONG" : "TRANS";
                result.Add(new ElementGeom(e1sv, e1ev, e2sv, e2ev, orient));
                Debug.WriteLine($"{LOG_PREFIX} [FIX4]   seg[{seg}] t=[{tS:F0},{tE:F0}] len={len:F0}mm orient={orient}");
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // Geometry helpers
        // ═══════════════════════════════════════════════════════════

        private static bool IsCollinear(Point2dModel a1, Point2dModel a2,
            Point2dModel b1, Point2dModel b2, double cosTol, double offsetTol)
        {
            double ax = a2.X - a1.X, ay = a2.Y - a1.Y;
            double aLen = Math.Sqrt(ax * ax + ay * ay);
            double bx = b2.X - b1.X, by = b2.Y - b1.Y;
            double bLen = Math.Sqrt(bx * bx + by * by);
            if (aLen < 1e-6 || bLen < 1e-6) return false;
            ax /= aLen; ay /= aLen; bx /= bLen; by /= bLen;

            if (Math.Abs(ax * bx + ay * by) < cosTol) return false;

            double nx = -ay, ny = ax;
            double off = Math.Abs((b1.X - a1.X) * nx + (b1.Y - a1.Y) * ny);
            return off <= offsetTol;
        }

        private static double PerpDistBetweenEdges(ElementGeom g)
        {
            double dx = g.E1e.X - g.E1s.X;
            double dy = g.E1e.Y - g.E1s.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return 0;
            dx /= len; dy /= len;
            double nx = -dy, ny = dx;
            double mx = (g.E2s.X + g.E2e.X) / 2.0;
            double my = (g.E2s.Y + g.E2e.Y) / 2.0;
            return Math.Abs((mx - g.E1s.X) * nx + (my - g.E1s.Y) * ny);
        }

        private static double ComputePanelBoundsDiagonal(List<StructuralElementModel> topPlates)
        {
            double xmin = double.MaxValue, xmax = double.MinValue;
            double ymin = double.MaxValue, ymax = double.MinValue;
            foreach (var tp in topPlates)
            {
                var verts = tp.VerticesWCS;
                if (verts == null) continue;
                foreach (var v in verts)
                {
                    if (v.X < xmin) xmin = v.X;
                    if (v.X > xmax) xmax = v.X;
                    if (v.Y < ymin) ymin = v.Y;
                    if (v.Y > ymax) ymax = v.Y;
                }
            }
            if (xmin > xmax) return 0;
            double dx = xmax - xmin, dy = ymax - ymin;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Distance(Point2dModel a, Point2dModel b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PointToSegmentDist(double px, double py,
            double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-10)
            {
                double dxp = px - ax, dyp = py - ay;
                return Math.Sqrt(dxp * dxp + dyp * dyp);
            }
            double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            t = Math.Max(0, Math.Min(1, t));
            double projX = ax + t * dx, projY = ay + t * dy;
            double ex = px - projX, ey = py - projY;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static bool IsRectangularType(StructuralType t)
        {
            return t == StructuralType.WebPlate
                || t == StructuralType.Bracket
                || t == StructuralType.ClosingBoxWeb
                || t == StructuralType.Flange
                || t == StructuralType.Stiffener
                || t == StructuralType.BucklingStiffener;
        }

        private static bool IsClEligibleType(StructuralType t)
        {
            return t == StructuralType.WebPlate
                || t == StructuralType.Bracket
                || t == StructuralType.ClosingBoxWeb
                || t == StructuralType.Stiffener
                || t == StructuralType.BucklingStiffener;
        }

        private static string ShortGuid(StructuralElementModel e)
            => e.Guid?.Length >= 8 ? e.Guid.Substring(0, 8) : e.Guid;

        private struct ElementGeom
        {
            public Point2dModel E1s, E1e, E2s, E2e;
            public string Orient;
            public ElementGeom(Point2dModel e1s, Point2dModel e1e,
                Point2dModel e2s, Point2dModel e2e, string orient)
            {
                E1s = e1s; E1e = e1e; E2s = e2s; E2e = e2e; Orient = orient;
            }
        }
    }
}
