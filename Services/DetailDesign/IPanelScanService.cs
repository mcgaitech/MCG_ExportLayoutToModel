using System.Collections.Generic;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Services.DetailDesign.Collection;

namespace MCGCadPlugin.Services.DetailDesign
{
    /// <summary>
    /// Interface cho panel scan service — entry point cho toàn bộ scan workflow.
    /// </summary>
    public interface IPanelScanService
    {
        /// <summary>
        /// Prompt user chọn panel → parse tên + side.
        /// </summary>
        /// <returns>PanelContext nếu chọn thành công, null nếu user cancel</returns>
        PanelContext SelectPanel();

        /// <summary>
        /// Scan panel đã chọn — thu thập + phân loại entities.
        /// </summary>
        /// <param name="panel">PanelContext từ SelectPanel()</param>
        /// <returns>Danh sách StructuralElementModel đã phân loại, null nếu fail</returns>
        List<StructuralElementModel> ScanPanel(PanelContext panel);
    }
}
