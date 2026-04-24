namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Profile từ catalog (EN10067) — seed vào SQLite khi init DB.
    /// Ví dụ: HP120x7, FB100x8, L65x65x8.
    /// </summary>
    public class ProfileModel
    {
        /// <summary>GUID duy nhất</summary>
        public string Guid { get; set; }

        /// <summary>Mã profile (HP120x7, FB100x8, L65x65x8)</summary>
        public string Code { get; set; }

        /// <summary>Loại: HP / FB / ANGLE</summary>
        public string Type { get; set; }

        /// <summary>Chiều cao profile (mm)</summary>
        public double? Height { get; set; }

        /// <summary>Chiều dày web (mm)</summary>
        public double? WebThickness { get; set; }

        /// <summary>Chiều rộng flange (mm)</summary>
        public double? FlangeWidth { get; set; }

        /// <summary>Chiều dày flange (mm)</summary>
        public double? FlangeThickness { get; set; }

        /// <summary>Diện tích mặt cắt (mm²)</summary>
        public double? Area { get; set; }

        /// <summary>Moment quán tính Ix (mm⁴)</summary>
        public double? Ix { get; set; }

        /// <summary>Trọng lượng trên mét dài (kg/m)</summary>
        public double? UnitWeight { get; set; }

        /// <summary>Tiêu chuẩn (EN10067, ...)</summary>
        public string Standard { get; set; }

        /// <summary>Tên block cutout tương ứng (HP120x7_Cutout)</summary>
        public string BlockCutout { get; set; }

        /// <summary>Tên block section view tương ứng</summary>
        public string BlockSection { get; set; }
    }
}
