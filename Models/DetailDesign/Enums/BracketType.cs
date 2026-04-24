namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Loại bracket theo hướng throw thickness.
    /// OB = Outside Bracket, IB = Inside Bracket.
    /// </summary>
    public enum BracketType
    {
        /// <summary>Outside Bracket — throw ra ngoài</summary>
        OB,

        /// <summary>Inside Bracket — throw vào trong</summary>
        IB,

        /// <summary>Chưa xác định</summary>
        Unknown
    }
}
