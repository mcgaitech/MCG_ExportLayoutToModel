using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;

namespace MCGCadPlugin.Services.LayoutToModel
{
    public interface ILayoutToModelService
    {
        /// <summary>
        /// Thực hiện export tất cả các Layout (không phải Model) vào không gian Model của chính file đó.
        /// </summary>
        void ExportAllLayoutsToModel(Database db);

        /// <summary>
        /// Xử lý hàng loạt danh sách các file DWG.
        /// </summary>
        void BatchProcessFiles(List<string> filePaths);
    }
}