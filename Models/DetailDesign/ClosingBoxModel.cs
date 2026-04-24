using System.Collections.Generic;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Closing box — nhóm AM_0 web plates tại góc/support panel.
    /// Phát hiện bởi ClosingBoxDetector (topology phase).
    /// </summary>
    public class ClosingBoxModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>Bounding box — X nhỏ nhất (mm)</summary>
        public double OuterMinX { get; set; }

        /// <summary>Bounding box — Y nhỏ nhất (mm)</summary>
        public double OuterMinY { get; set; }

        /// <summary>Bounding box — X lớn nhất (mm)</summary>
        public double OuterMaxX { get; set; }

        /// <summary>Bounding box — Y lớn nhất (mm)</summary>
        public double OuterMaxY { get; set; }

        /// <summary>Vị trí góc: TL / TR / BL / BR / MID</summary>
        public string CornerPosition { get; set; }

        /// <summary>Danh sách GUID các structural element thuộc closing box</summary>
        public List<string> MemberGuids { get; set; } = new List<string>();
    }
}
