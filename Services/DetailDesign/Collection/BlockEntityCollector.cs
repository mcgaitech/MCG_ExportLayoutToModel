using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Services.DetailDesign.Classification;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Collection
{
    /// <summary>
    /// Thu thập entities từ Assy BlockReference (Block Mode).
    /// Traverse sub-blocks theo category, tích lũy WCS transform.
    /// </summary>
    public class BlockEntityCollector : IEntityCollector
    {
        private const string LOG_PREFIX = "[BlockEntityCollector]";

        // Đếm entities per layer cho log
        private int _countAM0;
        private int _countAM3;
        private int _countAM5;
        private int _countAM11;
        private int _countLayer0;

        /// <summary>
        /// Thu thập tất cả Polyline entities từ Assy block.
        /// </summary>
        /// <param name="blockRefId">ObjectId của root Assy BlockReference</param>
        /// <param name="tr">Transaction đang mở</param>
        /// <returns>RawEntitySet phân nhóm theo sub-block category</returns>
        public RawEntitySet Collect(ObjectId blockRefId, Transaction tr)
        {
            Debug.WriteLine($"{LOG_PREFIX} Starting collection...");

            _countAM0 = 0;
            _countAM3 = 0;
            _countAM5 = 0;
            _countAM11 = 0;
            _countLayer0 = 0;

            var result = new RawEntitySet();

            var rootRef = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            if (rootRef == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: Not a BlockReference.");
                return result;
            }

            result.RootBlockName = rootRef.Name;
            result.RootHandle = rootRef.Handle.ToString();

            // Root transform
            var rootTransform = rootRef.BlockTransform;

            // Đọc BlockTableRecord của root
            var rootBtr = tr.GetObject(rootRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (rootBtr == null) return result;

            // Traverse sub-blocks
            foreach (ObjectId entId in rootBtr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);

                if (ent is BlockReference subRef)
                {
                    var category = SubBlockClassifier.Classify(subRef.Name);
                    var subTransform = Geometry.WCSTransformer.AccumulateTransform(rootTransform, subRef);

                    // Auto-detect box block khi FLAG_UNKNOWN: sub-block chỉ có AM_0 → đoán là Box
                    if (category == SubBlockClassifier.SubBlockCategory.FLAG_UNKNOWN)
                    {
                        if (IsLikelyBoxBlock(subRef, tr))
                        {
                            Debug.WriteLine($"{LOG_PREFIX} [BoxDetect] Auto-detected box block: {subRef.Name}");
                            category = SubBlockClassifier.SubBlockCategory.BOX;
                        }
                    }

                    switch (category)
                    {
                        case SubBlockClassifier.SubBlockCategory.TOP_PLATE:
                            CollectFromBlock(subRef, tr, subTransform, result.TopPlateEntities, subRef.Name);
                            break;

                        case SubBlockClassifier.SubBlockCategory.STRUCTURE:
                            CollectFromBlock(subRef, tr, subTransform, result.StructureEntities, subRef.Name);
                            break;

                        case SubBlockClassifier.SubBlockCategory.CORNER:
                            CollectFromBlock(subRef, tr, subTransform, result.CornerEntities, subRef.Name);
                            break;

                        case SubBlockClassifier.SubBlockCategory.BOX:
                            CollectFromBlock(subRef, tr, subTransform, result.BoxEntities, subRef.Name);
                            Debug.WriteLine($"{LOG_PREFIX} BOX: {subRef.Name} → {result.BoxEntities.Count} entities");
                            break;

                        case SubBlockClassifier.SubBlockCategory.SKIP:
                            result.SkippedBlocks.Add(subRef.Name);
                            break;

                        case SubBlockClassifier.SubBlockCategory.FLAG_UNKNOWN:
                            result.UnknownBlocks.Add(subRef.Name);
                            Debug.WriteLine($"{LOG_PREFIX} WARNING: Unknown sub-block skipped: {subRef.Name}");
                            break;
                    }
                }
                else if (ent is Polyline pline)
                {
                    // Polyline trực tiếp trong root block (hiếm nhưng có thể xảy ra)
                    TrackLayer(pline.Layer);
                    result.StructureEntities.Add(new RawEntitySet.EntityRef(entId, rootTransform, rootRef.Name));
                }
            }

            // UPDATE 3 — Debug summary: group by source_block + layer
            LogBlockSummary(result, tr);

            Debug.WriteLine($"{LOG_PREFIX} Collection complete — " +
                            $"AM_0: {_countAM0} | AM_3: {_countAM3} | AM_5: {_countAM5} | AM_11: {_countAM11} | Layer0: {_countLayer0}");
            Debug.WriteLine($"{LOG_PREFIX} Total: {result.TotalCount} entities " +
                            $"(TopPlate: {result.TopPlateEntities.Count}, " +
                            $"Structure: {result.StructureEntities.Count}, " +
                            $"Corner: {result.CornerEntities.Count}, " +
                            $"Box: {result.BoxEntities.Count})");
            Debug.WriteLine($"{LOG_PREFIX} Skipped: {result.SkippedBlocks.Count} | Unknown: {result.UnknownBlocks.Count}");

            return result;
        }

        /// <summary>
        /// Thu thập Polyline entities từ 1 sub-block (đệ quy cho nested blocks).
        /// sourceBlock: tên sub-block gốc để gán vào EntityRef (dùng cho verify/log).
        /// </summary>
        private void CollectFromBlock(BlockReference blockRef, Transaction tr,
            Matrix3d transform, List<RawEntitySet.EntityRef> targetList, string sourceBlock)
        {
            var btr = tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return;

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);

                if (ent is Polyline pline)
                {
                    TrackLayer(pline.Layer);
                    // Lưu transform tích lũy TỪ ROOT → entity này (map entity-local → WCS)
                    targetList.Add(new RawEntitySet.EntityRef(entId, transform, sourceBlock));
                }
                else if (ent is BlockReference nestedRef)
                {
                    // Đệ quy vào nested block — giữ nguyên sourceBlock của parent
                    var nestedTransform = Geometry.WCSTransformer.AccumulateTransform(transform, nestedRef);
                    CollectFromBlock(nestedRef, tr, nestedTransform, targetList, sourceBlock);
                }
            }
        }

        /// <summary>
        /// Heuristic: sub-block chỉ chứa AM_0 polylines (không có AM_3, AM_5, AM_11) → đoán là Box.
        /// Dùng cho auto-detect blocks chưa nằm trong BOX_BLOCK_KEYWORDS.
        /// </summary>
        private static bool IsLikelyBoxBlock(BlockReference subRef, Transaction tr)
        {
            var btr = tr.GetObject(subRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return false;

            int am0 = 0, other = 0;
            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is Polyline pline)
                {
                    if (pline.Layer == DetailDesignConstants.LAYER_WEB) am0++;
                    else if (pline.Layer == DetailDesignConstants.LAYER_STIFF
                          || pline.Layer == DetailDesignConstants.LAYER_FLANGE
                          || pline.Layer == DetailDesignConstants.LAYER_FLANGE_ALT) other++;
                }
            }

            // Phải có ≥2 AM_0 polyline và KHÔNG có stiff/flange → likely box
            return am0 >= 2 && other == 0;
        }

        /// <summary>
        /// UPDATE 3 — Log summary entities group by source_block + layer.
        /// Dùng để verify BOX_BLOCK_KEYWORDS coverage sau mỗi scan.
        /// </summary>
        private void LogBlockSummary(RawEntitySet result, Transaction tr)
        {
            var all = new List<RawEntitySet.EntityRef>();
            all.AddRange(result.TopPlateEntities);
            all.AddRange(result.StructureEntities);
            all.AddRange(result.CornerEntities);
            all.AddRange(result.BoxEntities);

            var groups = new Dictionary<string, int>();
            foreach (var er in all)
            {
                string layer = "?";
                var pline = tr.GetObject(er.Id, OpenMode.ForRead) as Polyline;
                if (pline != null) layer = pline.Layer;
                string key = $"{er.SourceBlock ?? "-"}|{layer}";
                if (!groups.ContainsKey(key)) groups[key] = 0;
                groups[key]++;
            }

            foreach (var kv in groups.OrderBy(x => x.Key))
                Debug.WriteLine($"{LOG_PREFIX} [BlockSummary] {kv.Key}={kv.Value}");
        }

        /// <summary>Đếm entities theo layer cho log</summary>
        private void TrackLayer(string layer)
        {
            if (layer == DetailDesignConstants.LAYER_TOPPLATE) _countLayer0++;
            else if (layer == DetailDesignConstants.LAYER_WEB) _countAM0++;
            else if (layer == DetailDesignConstants.LAYER_STIFF) _countAM3++;
            else if (layer == DetailDesignConstants.LAYER_FLANGE) _countAM5++;
            else if (layer == DetailDesignConstants.LAYER_FLANGE_ALT) _countAM11++;
        }
    }
}
