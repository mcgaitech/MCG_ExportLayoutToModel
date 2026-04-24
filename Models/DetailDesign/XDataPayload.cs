namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// DTO cho đọc/ghi XData trên entity AutoCAD.
    /// XData là cache — SQLite là source of truth.
    /// </summary>
    public class XDataPayload
    {
        /// <summary>GUID của element — link chính với SQLite</summary>
        public string ElemGuid { get; set; }

        /// <summary>GUID của panel chứa element</summary>
        public string PanelGuid { get; set; }

        /// <summary>Loại kết cấu (WEB_PLATE, STIFFENER, ...)</summary>
        public string ElemType { get; set; }

        /// <summary>Trạng thái (COMPLETE, PENDING, DIRTY, AMBIGUOUS)</summary>
        public string Status { get; set; }

        /// <summary>MD5 hash của WCS vertices — dirty detection</summary>
        public string GeometryHash { get; set; }

        /// <summary>Timestamp lần sync cuối với DB (ISO format)</summary>
        public string DbVersion { get; set; }

        /// <summary>Chiều dày cached (mm)</summary>
        public double Thickness { get; set; }

        /// <summary>Mã profile nếu là stiffener/BS (ví dụ: HP120x7)</summary>
        public string ProfileCode { get; set; }

        /// <summary>Loại bracket nếu là bracket (OB/IB)</summary>
        public string BracketType { get; set; }
    }
}
