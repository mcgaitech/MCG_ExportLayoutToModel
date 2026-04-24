using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Interface cho primary classification — phân loại entity theo layer/color/OBB.
    /// </summary>
    public interface IPrimaryClassifier
    {
        /// <summary>
        /// Phân loại 1 entity Polyline.
        /// </summary>
        /// <param name="entityId">ObjectId của Polyline</param>
        /// <param name="tr">Transaction đang mở</param>
        /// <param name="transform">WCS transform matrix</param>
        /// <param name="panelGuid">GUID panel chứa entity</param>
        /// <param name="sourceContext">Nguồn: STRUCTURE / CORNER / DIRECT</param>
        /// <returns>StructuralElementModel đã phân loại</returns>
        StructuralElementModel Classify(ObjectId entityId, Transaction tr,
            Matrix3d transform, string panelGuid, string sourceContext);

        /// <summary>
        /// Phân loại batch — mỗi entity đi kèm transform riêng (từ root → entity location).
        /// </summary>
        List<StructuralElementModel> ClassifyBatch(
            List<Collection.RawEntitySet.EntityRef> entityRefs, Transaction tr,
            string panelGuid, string sourceContext);
    }
}
