using System.Collections.Generic;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Interface cho throw vector engine — tính hướng đặt profile cho stiffeners.
    /// </summary>
    public interface IThrowVectorEngine
    {
        /// <summary>
        /// Tính throw vector cho tất cả stiffeners.
        /// </summary>
        /// <param name="elements">Tất cả elements (cần TopPlateRegion để tính centroid)</param>
        /// <param name="panel">PanelContext (side, centroid)</param>
        /// <returns>Danh sách StiffenerModel với throw vector đã tính</returns>
        List<StiffenerModel> ComputeThrowVectors(
            List<StructuralElementModel> elements, PanelContext panel);
    }
}
