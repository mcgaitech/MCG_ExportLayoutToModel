namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Trạng thái dữ liệu của element trong workflow review + save.
    /// </summary>
    public enum DataState
    {
        /// <summary>Vừa detect, chưa user review</summary>
        AutoDetected,

        /// <summary>User đã override (flip throw, v.v.)</summary>
        UserModified,

        /// <summary>User đã xác nhận và save vào SQLite</summary>
        Confirmed,

        /// <summary>Có vấn đề cần xử lý — e.g. bracket không tìm được stiffener</summary>
        Warning,

        /// <summary>Geometry hash thay đổi sau khi đã Confirmed</summary>
        HashChanged
    }
}
