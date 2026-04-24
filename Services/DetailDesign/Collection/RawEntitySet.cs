using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCGCadPlugin.Services.DetailDesign.Collection
{
    /// <summary>
    /// Container chứa danh sách entities sau khi traverse Assy block.
    /// Mỗi entity đi kèm transform tích lũy từ root → entity location (cần cho WCS).
    /// Phân nhóm theo sub-block category.
    /// </summary>
    public class RawEntitySet
    {
        /// <summary>1 entity kèm transform tích lũy từ root đến vị trí của nó.</summary>
        public struct EntityRef
        {
            public ObjectId Id;
            public Matrix3d Transform; // map từ entity-local → WCS
            public string SourceBlock; // tên sub-block chứa entity (để verify/log)
            public EntityRef(ObjectId id, Matrix3d transform, string sourceBlock = null)
            { Id = id; Transform = transform; SourceBlock = sourceBlock; }
        }

        /// <summary>Tên root Assy block</summary>
        public string RootBlockName { get; set; }

        /// <summary>Handle của root BlockReference</summary>
        public string RootHandle { get; set; }

        /// <summary>Entities từ sub-block TopPlate (layer "0" polylines)</summary>
        public List<EntityRef> TopPlateEntities { get; set; } = new List<EntityRef>();

        /// <summary>Entities từ sub-block Structure (AM_0/3/5)</summary>
        public List<EntityRef> StructureEntities { get; set; } = new List<EntityRef>();

        /// <summary>Entities từ sub-block Corner (AM_0 web plates)</summary>
        public List<EntityRef> CornerEntities { get; set; } = new List<EntityRef>();

        /// <summary>Entities từ sub-block Box (BX/BOX/CB/CBOX) — closing box AM_0</summary>
        public List<EntityRef> BoxEntities { get; set; } = new List<EntityRef>();

        /// <summary>Tên các sub-blocks đã bị skip</summary>
        public List<string> SkippedBlocks { get; set; } = new List<string>();

        /// <summary>Tên các sub-blocks không nhận diện được</summary>
        public List<string> UnknownBlocks { get; set; } = new List<string>();

        /// <summary>Tổng số entities thu thập được</summary>
        public int TotalCount => TopPlateEntities.Count + StructureEntities.Count + CornerEntities.Count + BoxEntities.Count;
    }
}
