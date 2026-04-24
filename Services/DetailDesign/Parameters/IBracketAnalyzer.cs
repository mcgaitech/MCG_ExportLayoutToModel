using System.Collections.Generic;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Interface cho bracket OB/IB classification.
    /// </summary>
    public interface IBracketAnalyzer
    {
        /// <summary>
        /// Classify brackets thành OB hoặc IB dựa trên throw vector.
        /// </summary>
        /// <param name="elements">Tất cả elements (cần brackets + stiffeners)</param>
        /// <param name="stiffenerModels">StiffenerModels với throw vector đã tính</param>
        /// <returns>Danh sách BracketModel với OB/IB đã gán</returns>
        List<BracketModel> ClassifyBrackets(
            List<StructuralElementModel> elements,
            List<StiffenerModel> stiffenerModels,
            PanelContext panel);
    }
}
