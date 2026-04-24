namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Trạng thái của structural element trong workflow.
    /// </summary>
    public enum ElementStatus
    {
        /// <summary>Chờ xử lý — thiếu dữ liệu (thickness, profile...)</summary>
        Pending,

        /// <summary>Đã đủ dữ liệu — sẵn sàng xuất BOM</summary>
        Complete,

        /// <summary>Geometry đã thay đổi — cần rescan</summary>
        Dirty,

        /// <summary>Không rõ loại — cần user resolve</summary>
        Ambiguous
    }
}
