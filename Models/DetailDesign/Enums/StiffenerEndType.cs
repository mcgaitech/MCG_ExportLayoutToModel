namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Kiểu đầu mút của stiffener (end A hoặc end B).
    /// </summary>
    public enum StiffenerEndType
    {
        /// <summary>Kết thúc bằng bracket</summary>
        Bracket,

        /// <summary>Kết thúc bằng snip (cắt vát)</summary>
        Snip,

        /// <summary>Kết thúc bằng cutout block</summary>
        Cutout,

        /// <summary>Đầu tự do — không có kết nối</summary>
        Free
    }
}
