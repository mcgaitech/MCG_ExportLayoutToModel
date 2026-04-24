namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Kết quả tính Oriented Bounding Box (OBB) bằng PCA.
    /// Dùng để xác định chiều dài, chiều rộng (thickness), và hướng entity.
    /// </summary>
    public class OBBResult
    {
        /// <summary>Tâm OBB (WCS)</summary>
        public Point2dModel Center { get; set; }

        /// <summary>Chiều dài — major axis (mm)</summary>
        public double Length { get; set; }

        /// <summary>Chiều rộng — minor axis = thickness (mm)</summary>
        public double Width { get; set; }

        /// <summary>Góc major axis so với trục X (rad)</summary>
        public double AngleRad { get; set; }

        /// <summary>Vector major axis (đã normalize)</summary>
        public Point2dModel MajorAxis { get; set; }

        /// <summary>Vector minor axis (đã normalize, vuông góc major)</summary>
        public Point2dModel MinorAxis { get; set; }

        /// <summary>Tỷ lệ Length/Width — dùng phân loại stiffener vs plate</summary>
        public double AspectRatio { get; set; }
    }
}
