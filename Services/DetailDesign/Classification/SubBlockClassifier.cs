using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Phân loại sub-blocks bên trong Assy block.
    /// Normalize tên block → map vào category.
    /// </summary>
    public static class SubBlockClassifier
    {
        private const string LOG_PREFIX = "[SubBlockClassifier]";

        /// <summary>Category của sub-block</summary>
        public enum SubBlockCategory
        {
            ASSY_ROOT,
            TOP_PLATE,
            STRUCTURE,
            CORNER,
            BOX,
            SKIP,
            FLAG_UNKNOWN
        }

        /// <summary>
        /// Phân loại tên block vào category.
        /// </summary>
        /// <param name="blockName">Tên block gốc (chưa normalize)</param>
        /// <returns>Category tương ứng</returns>
        public static SubBlockCategory Classify(string blockName)
        {
            var normalized = Normalize(blockName);

            // ASSY_ROOT
            if (normalized.EndsWith("assy") || normalized.EndsWith("assembly"))
                return SubBlockCategory.ASSY_ROOT;

            // TOP_PLATE
            if (normalized.EndsWith("topplate") || normalized.EndsWith("tpt"))
                return SubBlockCategory.TOP_PLATE;

            // BOX — check trước STRUCTURE/CORNER (BX/BOX/CB/CBOX keywords)
            if (DetailDesignConstants.IsBoxBlock(blockName))
                return SubBlockCategory.BOX;

            // STRUCTURE
            if (normalized.EndsWith("structure") || normalized.Contains("structure"))
                return SubBlockCategory.STRUCTURE;

            // CORNER
            if (normalized.Contains("corner"))
                return SubBlockCategory.CORNER;

            // SKIP patterns
            if (normalized.Contains("rigging") ||
                normalized.StartsWith("wire") || normalized.Contains("wire") ||
                normalized.StartsWith("lashing") || normalized.Contains("lashing") ||
                normalized.StartsWith("holes") || normalized.Contains("holes"))
                return SubBlockCategory.SKIP;

            // SKIP — section references (CAS-*)
            if (blockName.StartsWith("CAS-"))
                return SubBlockCategory.SKIP;

            return SubBlockCategory.FLAG_UNKNOWN;
        }

        /// <summary>
        /// Traverse Assy block và log danh sách sub-blocks + categories.
        /// </summary>
        /// <param name="blockRefId">ObjectId của Assy BlockReference</param>
        /// <param name="tr">Transaction đang mở</param>
        /// <returns>Dictionary: block name → category</returns>
        public static Dictionary<string, SubBlockCategory> ClassifySubBlocks(ObjectId blockRefId, Transaction tr)
        {
            Debug.WriteLine($"{LOG_PREFIX} Classifying sub-blocks...");
            var result = new Dictionary<string, SubBlockCategory>();

            var blockRef = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            if (blockRef == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: Not a BlockReference.");
                return result;
            }

            var rootName = blockRef.Name;
            var rootCategory = Classify(rootName);
            result[rootName] = rootCategory;
            Debug.WriteLine($"{LOG_PREFIX} AssyRoot: {rootName} → {rootCategory}");

            // Đọc BlockTableRecord để lấy sub-entities
            var btr = tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return result;

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is BlockReference nestedRef)
                {
                    var nestedName = nestedRef.Name;
                    if (!result.ContainsKey(nestedName))
                    {
                        var cat = Classify(nestedName);
                        result[nestedName] = cat;
                    }
                }
            }

            // Log summary
            var groups = result.GroupBy(kv => kv.Value)
                               .Select(g => $"{g.Key}: {string.Join(", ", g.Select(kv => kv.Key))}");
            foreach (var g in groups)
                Debug.WriteLine($"{LOG_PREFIX} {g}");

            Debug.WriteLine($"{LOG_PREFIX} Found {result.Count} sub-blocks.");
            return result;
        }

        /// <summary>
        /// Traverse Assy block tìm CAS_HEAD sub-block và đọc attribute ARAS_DOCREVISION.
        /// Trả về "" nếu không tìm thấy CAS_HEAD hoặc attribute.
        /// </summary>
        public static string ReadRevisionFromAssy(ObjectId assyBlockRefId, Transaction tr)
        {
            var blockRef = tr.GetObject(assyBlockRefId, OpenMode.ForRead) as BlockReference;
            if (blockRef == null) return "";

            var btr = tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return "";

            foreach (ObjectId entId in btr)
            {
                var nestedRef = tr.GetObject(entId, OpenMode.ForRead) as BlockReference;
                if (nestedRef == null) continue;
                // Match tên CAS_HEAD (case/underscore tolerant)
                if (!string.Equals(Normalize(nestedRef.Name), "cashead", System.StringComparison.Ordinal))
                    continue;

                // Tìm attribute ARAS_DOCREVISION trong AttributeCollection
                foreach (ObjectId attId in nestedRef.AttributeCollection)
                {
                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att == null) continue;
                    if (string.Equals(att.Tag, "ARAS_DOCREVISION", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var rev = att.TextString ?? "";
                        Debug.WriteLine($"{LOG_PREFIX} CAS_HEAD found, revision={rev}");
                        return rev;
                    }
                }
                Debug.WriteLine($"{LOG_PREFIX} CAS_HEAD found but ARAS_DOCREVISION attribute missing");
                return "";
            }

            Debug.WriteLine($"{LOG_PREFIX} CAS_HEAD not found in Assy");
            return "";
        }

        /// <summary>
        /// Normalize tên block: lowercase, bỏ space/underscore/hyphen.
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.ToLower()
                       .Replace(" ", "")
                       .Replace("_", "")
                       .Replace("-", "");
        }
    }
}
