namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Doubling plate — tấm tăng cứng, aspect ratio ≤ 3.0.
    /// Thickness do user nhập (hiện ? yellow nếu chưa có).
    /// </summary>
    public class DoublingPlateModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>GUID structural element tương ứng</summary>
        public string ElemGuid { get; set; }

        /// <summary>Chiều rộng (mm)</summary>
        public double? Width { get; set; }

        /// <summary>Chiều dài (mm)</summary>
        public double? Length { get; set; }

        /// <summary>Chiều dày — null nếu chưa nhập (? yellow)</summary>
        public double? Thickness { get; set; }

        /// <summary>Trọng tâm X (mm)</summary>
        public double? CentroidX { get; set; }

        /// <summary>Trọng tâm Y (mm)</summary>
        public double? CentroidY { get; set; }

        /// <summary>Vật liệu</summary>
        public string Material { get; set; }
    }
}
