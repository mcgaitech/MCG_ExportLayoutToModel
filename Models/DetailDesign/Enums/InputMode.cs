namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Chế độ input khi chọn panel.
    /// Block = click BlockReference, Entity = click Polyline trực tiếp.
    /// </summary>
    public enum InputMode
    {
        /// <summary>Chọn Assy BlockReference — traverse sub-blocks</summary>
        Block,

        /// <summary>Chọn top plate Polyline — spatial query</summary>
        Entity
    }
}
