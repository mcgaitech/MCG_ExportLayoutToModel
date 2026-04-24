using Autodesk.AutoCAD.DatabaseServices;

namespace MCGCadPlugin.Services.DetailDesign.Collection
{
    /// <summary>
    /// Interface cho entity collection — thu thập entities từ panel.
    /// BlockEntityCollector (Step 5) và DirectEntityCollector (Step 5) implement interface này.
    /// </summary>
    public interface IEntityCollector
    {
        /// <summary>
        /// Thu thập entities từ BlockReference hoặc spatial query.
        /// </summary>
        /// <param name="sourceId">ObjectId của BlockReference hoặc top plate Polyline</param>
        /// <param name="tr">Transaction đang mở</param>
        /// <returns>RawEntitySet chứa entities phân nhóm</returns>
        RawEntitySet Collect(ObjectId sourceId, Transaction tr);
    }
}
