using System;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.XData
{
    /// <summary>
    /// Đọc/ghi XData trên AutoCAD entities.
    /// App: MCG_PANEL_TOOL | Per-entity | GUID-based.
    /// </summary>
    public class XDataManager : IXDataManager
    {
        private const string LOG_PREFIX = "[XDataManager]";

        /// <summary>Ghi XData lên entity</summary>
        public void Write(ObjectId entityId, Transaction tr, XDataPayload payload)
        {
            // Register app nếu chưa có
            var db = entityId.Database;
            var regAppTable = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
            if (!regAppTable.Has(DetailDesignConstants.XDATA_APP_NAME))
            {
                var newApp = new RegAppTableRecord { Name = DetailDesignConstants.XDATA_APP_NAME };
                regAppTable.Add(newApp);
                tr.AddNewlyCreatedDBObject(newApp, true);
                Debug.WriteLine($"{LOG_PREFIX} Registered app: {DetailDesignConstants.XDATA_APP_NAME}");
            }

            // Build ResultBuffer
            var rb = new ResultBuffer(
                new TypedValue(1001, DetailDesignConstants.XDATA_APP_NAME),
                new TypedValue(1000, payload.ElemGuid ?? ""),
                new TypedValue(1000, payload.PanelGuid ?? ""),
                new TypedValue(1000, payload.ElemType ?? ""),
                new TypedValue(1000, payload.Status ?? ""),
                new TypedValue(1000, payload.GeometryHash ?? ""),
                new TypedValue(1000, payload.DbVersion ?? ""),
                new TypedValue(1040, payload.Thickness),
                new TypedValue(1000, payload.ProfileCode ?? ""),
                new TypedValue(1000, payload.BracketType ?? "")
            );

            // Gán vào entity
            var ent = tr.GetObject(entityId, OpenMode.ForWrite) as Entity;
            ent.XData = rb;

            Debug.WriteLine($"{LOG_PREFIX} Write: guid={payload.ElemGuid?.Substring(0, Math.Min(8, payload.ElemGuid?.Length ?? 0))}... type={payload.ElemType}");
        }

        /// <summary>Đọc XData từ entity</summary>
        public XDataPayload Read(ObjectId entityId, Transaction tr)
        {
            var ent = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
            var rb = ent.GetXDataForApplication(DetailDesignConstants.XDATA_APP_NAME);
            if (rb == null) return null;

            var values = rb.AsArray();
            if (values.Length < 10) return null;

            var payload = new XDataPayload
            {
                ElemGuid = values[1].Value as string,
                PanelGuid = values[2].Value as string,
                ElemType = values[3].Value as string,
                Status = values[4].Value as string,
                GeometryHash = values[5].Value as string,
                DbVersion = values[6].Value as string,
                Thickness = Convert.ToDouble(values[7].Value),
                ProfileCode = values[8].Value as string,
                BracketType = values[9].Value as string
            };

            Debug.WriteLine($"{LOG_PREFIX} Read: guid={payload.ElemGuid?.Substring(0, Math.Min(8, payload.ElemGuid?.Length ?? 0))}... status={payload.Status}");
            return payload;
        }

        /// <summary>Kiểm tra entity đã có XData MCG chưa</summary>
        public bool HasXData(ObjectId entityId, Transaction tr)
        {
            var ent = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
            var rb = ent.GetXDataForApplication(DetailDesignConstants.XDATA_APP_NAME);
            return rb != null;
        }
    }
}
