using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Stiffener hoặc Buckling Stiffener — gắn profile và throw vector.
    /// </summary>
    public class StiffenerModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel chứa stiffener</summary>
        public string PanelGuid { get; set; }

        /// <summary>GUID của structural element tương ứng</summary>
        public string ElemGuid { get; set; }

        /// <summary>GUID của profile được gán (từ catalog)</summary>
        public string ProfileGuid { get; set; }

        /// <summary>Loại: STIFF hoặc BS (buckling stiffener)</summary>
        public string StiffType { get; set; }

        /// <summary>Hướng: LONG hoặc TRANS</summary>
        public string Orientation { get; set; }

        /// <summary>Throw vector X (hướng đặt profile)</summary>
        public double ThrowVecX { get; set; }

        /// <summary>Throw vector Y</summary>
        public double ThrowVecY { get; set; }

        /// <summary>Stiffener nằm ở mép panel</summary>
        public bool IsEdge { get; set; }

        /// <summary>Kiểu đầu A</summary>
        public StiffenerEndType EndAType { get; set; }

        /// <summary>Kiểu đầu B</summary>
        public StiffenerEndType EndBType { get; set; }

        /// <summary>Chiều dài span giữa 2 web (mm)</summary>
        public double? SpanLength { get; set; }

        /// <summary>Chiều dài tổng (mm)</summary>
        public double? TotalLength { get; set; }
    }
}
