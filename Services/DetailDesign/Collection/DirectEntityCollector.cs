using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCGCadPlugin.Services.DetailDesign.Collection
{
    /// <summary>
    /// Thu thập entities bằng Entity Mode — chọn top plate Polyline trực tiếp.
    /// Spatial query quanh top plate boundary, không traverse nested blocks.
    /// TODO: Implement spatial query ở step sau nếu cần.
    /// </summary>
    public class DirectEntityCollector : IEntityCollector
    {
        private const string LOG_PREFIX = "[DirectEntityCollector]";

        /// <summary>
        /// Thu thập entities bằng spatial query quanh top plate.
        /// Hiện tại chỉ trả RawEntitySet rỗng — implement đầy đủ ở step sau.
        /// </summary>
        public RawEntitySet Collect(ObjectId sourceId, Transaction tr)
        {
            Debug.WriteLine($"{LOG_PREFIX} Entity mode collection — not fully implemented yet.");

            var result = new RawEntitySet();

            var pline = tr.GetObject(sourceId, OpenMode.ForRead) as Polyline;
            if (pline == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: Selected entity is not a Polyline.");
                return result;
            }

            result.RootHandle = pline.Handle.ToString();
            result.TopPlateEntities.Add(new RawEntitySet.EntityRef(sourceId, Matrix3d.Identity));

            Debug.WriteLine($"{LOG_PREFIX} Top plate handle: {result.RootHandle}");
            Debug.WriteLine($"{LOG_PREFIX} Entity mode: spatial query will be implemented in future step.");

            return result;
        }
    }
}
