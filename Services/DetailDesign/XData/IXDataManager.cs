using Autodesk.AutoCAD.DatabaseServices;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.XData
{
    /// <summary>
    /// Interface cho XData read/write trên AutoCAD entities.
    /// XData là cache — SQLite là source of truth.
    /// </summary>
    public interface IXDataManager
    {
        /// <summary>Ghi XData lên entity</summary>
        void Write(ObjectId entityId, Transaction tr, XDataPayload payload);

        /// <summary>Đọc XData từ entity</summary>
        XDataPayload Read(ObjectId entityId, Transaction tr);

        /// <summary>Kiểm tra entity đã có XData MCG chưa</summary>
        bool HasXData(ObjectId entityId, Transaction tr);
    }
}
