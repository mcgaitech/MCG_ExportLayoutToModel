using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Thông tin panel đang được scan — context cho toàn bộ workflow.
    /// </summary>
    public class PanelContext
    {
        /// <summary>GUID duy nhất của panel</summary>
        public string Guid { get; set; }

        /// <summary>GUID của project chứa panel</summary>
        public string ProjectGuid { get; set; }

        /// <summary>Tên panel (ví dụ: T.6D09C)</summary>
        public string Name { get; set; }

        /// <summary>Vị trí panel: Port / Starboard / Center</summary>
        public PanelSide Side { get; set; }

        /// <summary>Side có được auto-detect từ tên không</summary>
        public bool SideAutoDetected { get; set; }

        /// <summary>Chế độ input: Block hoặc Entity</summary>
        public InputMode Mode { get; set; }

        /// <summary>Handle của root Assy block (mode Block)</summary>
        public string RootBlockHandle { get; set; }

        /// <summary>Handle của top plate polyline (mode Entity)</summary>
        public string TopPlateHandle { get; set; }

        /// <summary>Chiều cao web — user nhập (mm)</summary>
        public double? WebHeight { get; set; }

        /// <summary>Chiều dày top plate — user nhập (mm)</summary>
        public double? TopPlateThickness { get; set; }

        /// <summary>Vật liệu (ví dụ: AH36)</summary>
        public string Material { get; set; }

        /// <summary>Trọng tâm panel X (mm)</summary>
        public double? CentroidX { get; set; }

        /// <summary>Trọng tâm panel Y (mm)</summary>
        public double? CentroidY { get; set; }

        // ═══ Default Parameters ═══

        /// <summary>Default top plate thickness (mm)</summary>
        public double? DefaultTopPlateThk { get; set; }

        /// <summary>Default flange thickness (mm)</summary>
        public double? DefaultFlangeThk { get; set; }

        /// <summary>Default stiffener profile code</summary>
        public string DefaultStiffProfile { get; set; }

        /// <summary>Default BS profile code</summary>
        public string DefaultBSProfile { get; set; }

        /// <summary>
        /// Document revision từ attribute ARAS_DOCREVISION của CAS_HEAD block trong Assy.
        /// Null/"" nếu không tìm thấy CAS_HEAD hoặc attribute.
        /// </summary>
        public string Revision { get; set; }
    }
}
