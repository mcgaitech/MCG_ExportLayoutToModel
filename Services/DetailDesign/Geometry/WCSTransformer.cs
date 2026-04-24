using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Geometry
{
    /// <summary>
    /// Transform Polyline vertices từ block space về World Coordinate System (WCS).
    /// Tích lũy Matrix3d khi traverse nested blocks.
    /// </summary>
    public static class WCSTransformer
    {
        private const string LOG_PREFIX = "[WCSTransformer]";

        /// <summary>
        /// Transform tất cả vertices của Polyline về WCS.
        /// </summary>
        /// <param name="pline">Polyline trong block space</param>
        /// <param name="totalTransform">Tích lũy transform matrix từ root đến entity</param>
        /// <returns>Mảng Point2dModel đã transform về WCS</returns>
        public static Point2dModel[] TransformVertices(Polyline pline, Matrix3d totalTransform)
        {
            var result = new Point2dModel[pline.NumberOfVertices];
            for (int i = 0; i < pline.NumberOfVertices; i++)
            {
                // Polyline vertex là Point2d (block space)
                var pt2d = pline.GetPoint2dAt(i);
                // Nâng lên 3D (Z=0 vì polyline nằm trong XY plane)
                var pt3d = new Point3d(pt2d.X, pt2d.Y, 0);
                // Apply accumulated transform
                var transformed = pt3d.TransformBy(totalTransform);
                result[i] = new Point2dModel(transformed.X, transformed.Y);
            }
            return result;
        }

        /// <summary>
        /// Tính tích lũy transform khi đi sâu vào nested block.
        /// </summary>
        /// <param name="parentTransform">Transform tích lũy từ cấp trên</param>
        /// <param name="childBlockRef">BlockReference con</param>
        /// <returns>Transform mới = parent * child</returns>
        public static Matrix3d AccumulateTransform(Matrix3d parentTransform, BlockReference childBlockRef)
        {
            return parentTransform * childBlockRef.BlockTransform;
        }

        /// <summary>
        /// Kiểm tra transform có chứa mirror (reflection) không.
        /// Mirror xảy ra khi determinant của rotation part < 0.
        /// </summary>
        /// <param name="transform">Transform matrix cần kiểm tra</param>
        /// <returns>true nếu có mirror</returns>
        public static bool IsMirrored(Matrix3d transform)
        {
            // Lấy 3x3 rotation part từ 4x4 matrix
            // Determinant < 0 → có reflection
            var inv = transform.Inverse();
            var transposed = inv.Transpose();
            // Workaround: tính determinant từ 3x3 trên trái
            double[] data = transposed.ToArray();
            double det = data[0] * (data[5] * data[10] - data[6] * data[9])
                       - data[1] * (data[4] * data[10] - data[6] * data[8])
                       + data[2] * (data[4] * data[9] - data[5] * data[8]);

            bool mirrored = det < 0;
            if (mirrored)
                Debug.WriteLine($"{LOG_PREFIX} WARNING: Mirror transform detected (det={det:F4})");

            return mirrored;
        }
    }
}
