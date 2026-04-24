namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Vùng top plate — polyline trên layer "0".
    /// Thickness do user nhập (? yellow nếu chưa có).
    /// </summary>
    public class TopPlateRegionModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel</summary>
        public string PanelGuid { get; set; }

        /// <summary>AutoCAD entity handle</summary>
        public string AcadHandle { get; set; }

        /// <summary>Thứ tự vùng (0, 1, 2...)</summary>
        public int RegionIndex { get; set; }

        /// <summary>Diện tích vùng (mm²)</summary>
        public double? Area { get; set; }

        /// <summary>Chiều dày — null nếu chưa nhập</summary>
        public double? Thickness { get; set; }

        /// <summary>Trọng tâm X (mm)</summary>
        public double? CentroidX { get; set; }

        /// <summary>Trọng tâm Y (mm)</summary>
        public double? CentroidY { get; set; }

        /// <summary>Chu vi (mm)</summary>
        public double? Perimeter { get; set; }
    }
}
