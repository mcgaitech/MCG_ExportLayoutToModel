namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Mặt cắt (section cut) — định nghĩa đường cắt và vị trí vẽ section view.
    /// </summary>
    public class SectionModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>Tên section (A-A, B-B, ...)</summary>
        public string Name { get; set; }

        /// <summary>Điểm bắt đầu cutting line X (mm)</summary>
        public double CutStartX { get; set; }

        /// <summary>Điểm bắt đầu cutting line Y (mm)</summary>
        public double CutStartY { get; set; }

        /// <summary>Điểm kết thúc cutting line X (mm)</summary>
        public double CutEndX { get; set; }

        /// <summary>Điểm kết thúc cutting line Y (mm)</summary>
        public double CutEndY { get; set; }

        /// <summary>Hướng nhìn: UP / DOWN / LEFT / RIGHT</summary>
        public string ViewDirection { get; set; }

        /// <summary>Tỷ lệ (0.05 = 1:20)</summary>
        public double Scale { get; set; }

        /// <summary>Điểm đặt section view X (mm)</summary>
        public double InsertX { get; set; }

        /// <summary>Điểm đặt section view Y (mm)</summary>
        public double InsertY { get; set; }

        /// <summary>Handle của section marker block</summary>
        public string BlockHandle { get; set; }

        /// <summary>Section cần vẽ lại (geometry thay đổi)</summary>
        public bool IsDirty { get; set; }
    }
}
