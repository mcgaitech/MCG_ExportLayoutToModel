using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Bracket — nối stiffener với web/girder.
    /// OB/IB xác định bởi ThrowVectorEngine.
    /// </summary>
    public class BracketModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>GUID structural element tương ứng</summary>
        public string ElemGuid { get; set; }

        /// <summary>Loại: OB / IB / Unknown</summary>
        public BracketType Type { get; set; }

        /// <summary>GUID stiffener mà bracket nối</summary>
        public string StiffenerGuid { get; set; }

        /// <summary>GUID web element mà bracket chạm</summary>
        public string WebElemGuid { get; set; }

        /// <summary>GUID girder (nếu có)</summary>
        public string GirderGuid { get; set; }

        /// <summary>Chiều dài chân bracket phía web (mm)</summary>
        public double? LegWeb { get; set; }

        /// <summary>Chiều dài chân bracket phía stiffener (mm)</summary>
        public double? LegStiffener { get; set; }

        /// <summary>Chiều dày bracket (mm)</summary>
        public double? Thickness { get; set; }

        /// <summary>Chiều dài toe — mặc định 15mm hoặc b_f/2</summary>
        public double? ToeLength { get; set; }

        /// <summary>Bracket có flange không</summary>
        public bool HasFlange { get; set; }

        /// <summary>Chiều cao web tại vị trí bracket (mm)</summary>
        public double? WebHeight { get; set; }

        /// <summary>Bracket nằm trong closing box</summary>
        public bool InClosingBox { get; set; }

        /// <summary>GUID closing box chứa bracket (nếu có)</summary>
        public string ClosingBoxGuid { get; set; }

        /// <summary>
        /// Sub-type của bracket: OB / IB / BF / B.
        /// OB = Outside Bracket (tại Stiffener, throw ra ngoài).
        /// IB = Inside Bracket (tại Stiffener, throw vào trong).
        /// BF = Bracket tại BucklingStiffener.
        /// B  = Bracket tại Stiffener, chưa xác định OB/IB.
        /// </summary>
        public string SubType { get; set; }
    }
}
