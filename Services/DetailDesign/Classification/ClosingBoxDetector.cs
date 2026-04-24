using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Detect closing boxes — nhóm web plates share edge tại góc panel.
    /// </summary>
    public class ClosingBoxDetector
    {
        private const string LOG_PREFIX = "[ClosingBoxDetector]";

        /// <summary>
        /// Detect closing boxes từ danh sách web plates.
        /// </summary>
        /// <param name="webPlates">Web plates (đã classify từ AM0)</param>
        /// <returns>Danh sách ClosingBoxModel</returns>
        public List<ClosingBoxModel> Detect(List<StructuralElementModel> webPlates)
        {
            Debug.WriteLine($"{LOG_PREFIX} Detecting closing boxes from {webPlates.Count} web plates...");

            var closingBoxes = new List<ClosingBoxModel>();
            var visited = new HashSet<string>();

            foreach (var wp in webPlates)
            {
                if (visited.Contains(wp.Guid)) continue;

                // Tìm nhóm web plates share edge (BFS)
                var group = new List<StructuralElementModel> { wp };
                var queue = new Queue<StructuralElementModel>();
                queue.Enqueue(wp);
                visited.Add(wp.Guid);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var other in webPlates)
                    {
                        if (visited.Contains(other.Guid)) continue;
                        if (SharesEdge(current, other))
                        {
                            group.Add(other);
                            visited.Add(other.Guid);
                            queue.Enqueue(other);
                        }
                    }
                }

                // Closing box cần ≥ 3 web plates share edge
                if (group.Count >= 3)
                {
                    var cb = new ClosingBoxModel
                    {
                        Guid = Guid.NewGuid().ToString(),
                        PanelGuid = wp.PanelGuid,
                        MemberGuids = group.Select(g => g.Guid).ToList()
                    };

                    // Tính bounding box
                    cb.OuterMinX = group.SelectMany(g => g.VerticesWCS).Min(v => v.X);
                    cb.OuterMinY = group.SelectMany(g => g.VerticesWCS).Min(v => v.Y);
                    cb.OuterMaxX = group.SelectMany(g => g.VerticesWCS).Max(v => v.X);
                    cb.OuterMaxY = group.SelectMany(g => g.VerticesWCS).Max(v => v.Y);

                    closingBoxes.Add(cb);
                    Debug.WriteLine($"{LOG_PREFIX} Closing box: {group.Count} members, bounds ({cb.OuterMinX:F0},{cb.OuterMinY:F0})-({cb.OuterMaxX:F0},{cb.OuterMaxY:F0})");
                }
            }

            Debug.WriteLine($"{LOG_PREFIX} Detected {closingBoxes.Count} closing boxes.");
            return closingBoxes;
        }

        /// <summary>
        /// Kiểm tra 2 polylines có share edge không (vertex gần nhau < tolerance).
        /// Cần ≥ 2 vertex pairs gần nhau để coi là share edge.
        /// </summary>
        private static bool SharesEdge(StructuralElementModel a, StructuralElementModel b)
        {
            if (a.VerticesWCS == null || b.VerticesWCS == null) return false;

            int closeVertexCount = 0;
            foreach (var pa in a.VerticesWCS)
            {
                foreach (var pb in b.VerticesWCS)
                {
                    if (pa.DistanceTo(pb) < DetailDesignConstants.TOLERANCE_CONTACT)
                    {
                        closeVertexCount++;
                        if (closeVertexCount >= 2) return true;
                    }
                }
            }
            return false;
        }
    }
}
