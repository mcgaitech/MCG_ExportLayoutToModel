namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Dòng trong bảng BOM — xuất ra CSV.
    /// </summary>
    public class BomItemModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>GUID structural element nguồn</summary>
        public string SourceElemGuid { get; set; }

        /// <summary>Số thứ tự item</summary>
        public string ItemNo { get; set; }

        /// <summary>Mô tả (Web Plate, HP120x7, ...)</summary>
        public string Description { get; set; }

        /// <summary>Loại: PLATE / PROFILE / BRACKET / DOUBLING</summary>
        public string ItemType { get; set; }

        /// <summary>Số lượng</summary>
        public int Quantity { get; set; }

        /// <summary>Chiều dài (mm)</summary>
        public double? Length { get; set; }

        /// <summary>Chiều rộng (mm)</summary>
        public double? Width { get; set; }

        /// <summary>Chiều dày (mm)</summary>
        public double? Thickness { get; set; }

        /// <summary>Mã profile (nếu có, ví dụ: HP120x7)</summary>
        public string ProfileCode { get; set; }

        /// <summary>Vật liệu</summary>
        public string Material { get; set; }

        /// <summary>Trọng lượng đơn vị (kg/m hoặc kg)</summary>
        public double? UnitWeight { get; set; }

        /// <summary>Trọng lượng tổng (kg)</summary>
        public double? TotalWeight { get; set; }

        /// <summary>Ghi chú</summary>
        public string Remark { get; set; }
    }
}
