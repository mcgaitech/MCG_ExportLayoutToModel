using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Services.DetailDesign.Geometry;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Phân loại entity theo layer + color + OBB aspect ratio.
    /// Bảng phân loại: xem DetailDesign.md mục 5.
    /// </summary>
    public class PrimaryClassifier : IPrimaryClassifier
    {
        private const string LOG_PREFIX = "[PrimaryClassifier]";

        /// <summary>Phân loại 1 entity</summary>
        public StructuralElementModel Classify(ObjectId entityId, Transaction tr,
            Matrix3d transform, string panelGuid, string sourceContext)
        {
            var ent = tr.GetObject(entityId, OpenMode.ForRead) as Polyline;
            if (ent == null) return null;

            // Whitelist layers — skip nếu ngoài rule (tránh ghost WebPlate từ annotation/AM_7/etc.)
            var normLayer = NormalizeLayerName(ent.Layer);
            bool isKnownLayer = normLayer == DetailDesignConstants.LAYER_TOPPLATE
                             || normLayer == DetailDesignConstants.LAYER_WEB
                             || normLayer == DetailDesignConstants.LAYER_STIFF
                             || normLayer == DetailDesignConstants.LAYER_FLANGE
                             || normLayer == DetailDesignConstants.LAYER_FLANGE_ALT;
            if (!isKnownLayer)
            {
                Debug.WriteLine($"{LOG_PREFIX} SKIP unknown layer '{ent.Layer}' in {sourceContext} (handle={ent.Handle})");
                return null;
            }
            // Layer "0" CHỈ hợp lệ trong TOP_PLATE context
            if (normLayer == DetailDesignConstants.LAYER_TOPPLATE && sourceContext != "TOP_PLATE")
            {
                Debug.WriteLine($"{LOG_PREFIX} SKIP layer '0' in {sourceContext} (only valid in TOP_PLATE, handle={ent.Handle})");
                return null;
            }

            // Transform vertices về WCS
            var wcsVertices = WCSTransformer.TransformVertices(ent, transform);

            // Tính OBB
            var obb = OBBCalculator.Compute(wcsVertices);
            if (obb == null) return null;

            // Resolve color — ByLayer (256) → lấy từ LayerTableRecord
            int colorIndex = ent.ColorIndex;
            if (colorIndex == 256)
            {
                var layerTable = tr.GetObject(ent.Database.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (layerTable != null && layerTable.Has(ent.Layer))
                {
                    var layerRecord = tr.GetObject(layerTable[ent.Layer], OpenMode.ForRead) as LayerTableRecord;
                    if (layerRecord != null)
                        colorIndex = layerRecord.Color.ColorIndex;
                }
            }

            // Phân loại theo layer + color + aspect ratio + source context
            var elemType = ClassifyByRules(ent.Layer, colorIndex, obb.AspectRatio, sourceContext);

            // Tính centroid
            double centroidX = wcsVertices.Average(p => p.X);
            double centroidY = wcsVertices.Average(p => p.Y);

            return new StructuralElementModel
            {
                Guid = System.Guid.NewGuid().ToString(),
                PanelGuid = panelGuid,
                ElemType = elemType,
                AcadHandle = ent.Handle.ToString(),
                Layer = ent.Layer,
                ColorIndex = colorIndex,
                VerticesWCS = wcsVertices,
                CentroidX = centroidX,
                CentroidY = centroidY,
                ObbLength = obb.Length,
                ObbWidth = obb.Width,
                ObbAngle = obb.AngleRad,
                AreaPoly = Math.Abs(ComputeArea(wcsVertices)),
                Status = elemType == StructuralType.Ambiguous ? ElementStatus.Ambiguous : ElementStatus.Pending,
                IsFlagged = elemType == StructuralType.Ambiguous,
                FlagReason = elemType == StructuralType.Ambiguous
                    ? $"Aspect ratio {obb.AspectRatio:F2} in range 3.0-5.0"
                    : null,
                SourceContext = sourceContext
            };
        }

        /// <summary>Phân loại batch — mỗi entity có transform riêng từ root → entity location.</summary>
        public List<StructuralElementModel> ClassifyBatch(
            List<Collection.RawEntitySet.EntityRef> entityRefs, Transaction tr,
            string panelGuid, string sourceContext)
        {
            var results = new List<StructuralElementModel>();
            foreach (var eref in entityRefs)
            {
                var elem = Classify(eref.Id, tr, eref.Transform, panelGuid, sourceContext);
                if (elem != null)
                {
                    elem.SourceBlock = eref.SourceBlock;
                    results.Add(elem);
                }
            }

            // TopPlate: polyline lớn nhất = main, bên trong = cutout, còn lại = equipment
            GroupTopPlate(results);
            // Flange: outer-inner cutout detection
            GroupCutouts(results, StructuralType.Flange);

            // Log summary
            var groups = results.GroupBy(e => e.ElemType)
                                .OrderBy(g => g.Key)
                                .Select(g => $"{g.Key}: {g.Count()}");
            int holeCount = results.Count(e => e.IsHole);
            Debug.WriteLine($"{LOG_PREFIX} {string.Join(", ", groups)} (holes: {holeCount})");

            return results;
        }

        /// <summary>
        /// Bảng phân loại: layer → color → aspect ratio → StructuralType.
        /// </summary>
        private static StructuralType ClassifyByRules(string layer, int colorIndex, double aspectRatio, string sourceContext)
        {
            // Normalize layer: bỏ space thừa, chuẩn hóa separator
            var normalizedLayer = NormalizeLayerName(layer);

            // Layer "0" → TopPlateRegion CHỈ KHI từ TopPlate sub-block
            if (normalizedLayer == DetailDesignConstants.LAYER_TOPPLATE)
            {
                if (sourceContext == "TOP_PLATE")
                    return StructuralType.TopPlateRegion;
                else
                {
                    // Layer "0" trong Structure/Corner → bỏ qua (không phải structural element)
                    Debug.WriteLine($"{LOG_PREFIX} Layer '0' in {sourceContext} → skipping (not TopPlate sub-block)");
                    return StructuralType.AM0_Unclassified;
                }
            }

            // AM_5 hoặc AM_11 → Flange
            if (normalizedLayer == DetailDesignConstants.LAYER_FLANGE || normalizedLayer == DetailDesignConstants.LAYER_FLANGE_ALT)
                return StructuralType.Flange;

            // AM_3 → color + aspect ratio
            if (normalizedLayer == DetailDesignConstants.LAYER_STIFF)
            {
                // Color 6 (Magenta) → BucklingStiffener
                if (colorIndex == DetailDesignConstants.COLOR_BS)
                    return StructuralType.BucklingStiffener;

                // Color 40 (chính) hoặc 30 (variant) → aspect ratio check
                if (colorIndex == DetailDesignConstants.COLOR_STIFFENER
                 || colorIndex == DetailDesignConstants.COLOR_STIFFENER_ALT)
                {
                    if (aspectRatio > DetailDesignConstants.RATIO_STIFF_MIN)
                        return StructuralType.Stiffener;
                    if (aspectRatio <= DetailDesignConstants.RATIO_PLATE_MAX)
                        return StructuralType.DoublingPlate;

                    // 3.0 < ratio ≤ 5.0 → Ambiguous
                    return StructuralType.Ambiguous;
                }

                // AM_3 with other color → treat as Stiffener (log warning)
                Debug.WriteLine($"{LOG_PREFIX} WARNING: AM_3 unexpected color {colorIndex}, treating as Stiffener");
                return StructuralType.Stiffener;
            }

            // AM_0 → chờ topology phase
            if (normalizedLayer == DetailDesignConstants.LAYER_WEB)
                return StructuralType.AM0_Unclassified;

            // Unknown layer → flag
            Debug.WriteLine($"{LOG_PREFIX} WARNING: Unknown layer '{layer}' (normalized: '{normalizedLayer}'), treating as AM0_Unclassified");
            return StructuralType.AM0_Unclassified;
        }

        /// <summary>
        /// TopPlate: polyline lớn nhất = main top plate.
        /// Polylines nằm bên trong main = cutouts.
        /// Polylines không nằm trong main = equipment pads (đổi type, không đếm TopPlate).
        /// </summary>
        private static void GroupTopPlate(List<StructuralElementModel> elements)
        {
            var topPlates = elements.Where(e => e.ElemType == StructuralType.TopPlateRegion && !e.IsHole).ToList();
            if (topPlates.Count < 2) return;

            // Polyline lớn nhất = main top plate
            var main = topPlates.OrderByDescending(e => e.AreaPoly).First();
            double cutoutAreaSum = 0;

            foreach (var tp in topPlates)
            {
                if (tp.Guid == main.Guid) continue;

                if (PointInPolygonHelper.IsContainedIn(tp.VerticesWCS, main.VerticesWCS))
                {
                    // Nằm trong main → cutout
                    tp.IsHole = true;
                    tp.ParentGuid = main.Guid;
                    cutoutAreaSum += tp.AreaPoly;
                    Debug.WriteLine($"{LOG_PREFIX} TopPlate cutout: {tp.AcadHandle} inside main {main.AcadHandle} (area: {tp.AreaPoly:F0})");
                }
                else
                {
                    // Không nằm trong main → equipment pad, không phải TopPlate
                    // Giữ TopPlateRegion type nhưng đánh dấu là equipment
                    tp.AnnotationType = "EQUIPMENT_PAD";
                    tp.IsHole = true; // ẩn khỏi tree TopPlate count
                    Debug.WriteLine($"{LOG_PREFIX} Equipment pad: {tp.AcadHandle} (area: {tp.AreaPoly:F0})");
                }
            }

            main.NetArea = main.AreaPoly - cutoutAreaSum;
            Debug.WriteLine($"{LOG_PREFIX} Main TopPlate {main.AcadHandle}: gross={main.AreaPoly:F0} net={main.NetArea:F0} cutouts={cutoutAreaSum:F0}");
        }

        /// <summary>
        /// Nhóm outer-inner cho Flange.
        /// Polyline nhỏ nằm bên trong polyline lớn → cutout (IsHole = true).
        /// Tính NetArea = outer area - sum(inner areas).
        /// </summary>
        private static void GroupCutouts(List<StructuralElementModel> elements, StructuralType type)
        {
            var sameType = elements.Where(e => e.ElemType == type && !e.IsHole).ToList();
            if (sameType.Count < 2) return;

            // Sort by area descending — lớn nhất là outer candidate
            var sorted = sameType.OrderByDescending(e => e.AreaPoly).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var outer = sorted[i];
                if (outer.IsHole) continue;

                double cutoutAreaSum = 0;

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var inner = sorted[j];
                    if (inner.IsHole) continue;

                    // Kiểm tra inner nằm trong outer
                    if (PointInPolygonHelper.IsContainedIn(inner.VerticesWCS, outer.VerticesWCS))
                    {
                        inner.IsHole = true;
                        inner.ParentGuid = outer.Guid;
                        cutoutAreaSum += inner.AreaPoly;
                        Debug.WriteLine($"{LOG_PREFIX} Cutout: {inner.AcadHandle} inside {outer.AcadHandle} (area: {inner.AreaPoly:F0})");
                    }
                }

                if (cutoutAreaSum > 0)
                {
                    outer.NetArea = outer.AreaPoly - cutoutAreaSum;
                    Debug.WriteLine($"{LOG_PREFIX} {type} {outer.AcadHandle}: gross={outer.AreaPoly:F0} net={outer.NetArea:F0} cutouts={cutoutAreaSum:F0}");
                }
                else
                {
                    outer.NetArea = outer.AreaPoly;
                }
            }
        }

        /// <summary>
        /// Normalize layer name: bỏ space thừa, chuẩn hóa hyphen → underscore.
        /// "Mechanical -AM_0" → "Mechanical-AM_0"
        /// "Mechanical-AM-3" → "Mechanical-AM_3"
        /// </summary>
        private static string NormalizeLayerName(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return layer;

            // Bỏ space trước/sau hyphen: "Mechanical -AM_0" → "Mechanical-AM_0"
            var result = System.Text.RegularExpressions.Regex.Replace(layer, @"\s*-\s*", "-");

            // Chuẩn hóa "AM-X" → "AM_X" (hyphen → underscore sau AM)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"AM-(\d)", "AM_$1");

            return result;
        }

        /// <summary>Shoelace area</summary>
        private static double ComputeArea(Point2dModel[] pts)
        {
            double area = 0;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            }
            return area / 2.0;
        }
    }
}
