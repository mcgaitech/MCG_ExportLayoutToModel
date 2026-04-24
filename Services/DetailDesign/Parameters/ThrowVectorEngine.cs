using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Tính throw direction cho mỗi stiffener.
    /// Edge → ra xa tâm panel. Inner → theo PanelSide.
    /// </summary>
    public class ThrowVectorEngine : IThrowVectorEngine
    {
        private const string LOG_PREFIX = "[ThrowVectorEngine]";

        /// <summary>Tính throw vectors cho tất cả stiffeners</summary>
        public List<StiffenerModel> ComputeThrowVectors(
            List<StructuralElementModel> elements, PanelContext panel)
        {
            Debug.WriteLine($"{LOG_PREFIX} Computing throw vectors...");

            var stiffElements = elements
                .Where(e => e.ElemType == StructuralType.Stiffener || e.ElemType == StructuralType.BucklingStiffener)
                .ToList();

            if (stiffElements.Count == 0)
            {
                Debug.WriteLine($"{LOG_PREFIX} No stiffeners found.");
                return new List<StiffenerModel>();
            }

            // Tính panel centroid từ TopPlateRegion
            var topPlates = elements.Where(e => e.ElemType == StructuralType.TopPlateRegion).ToList();
            double panelCenterX = topPlates.Count > 0 ? topPlates.Average(t => t.CentroidX) : 0;
            double panelCenterY = topPlates.Count > 0 ? topPlates.Average(t => t.CentroidY) : 0;

            panel.CentroidX = panelCenterX;
            panel.CentroidY = panelCenterY;

            Debug.WriteLine($"{LOG_PREFIX} Panel centroid: ({panelCenterX:F1}, {panelCenterY:F1})");

            var result = new List<StiffenerModel>();
            int edgeCount = 0;

            foreach (var elem in stiffElements)
            {
                var stiffModel = new StiffenerModel
                {
                    Guid = Guid.NewGuid().ToString(),
                    PanelGuid = panel.Guid,
                    ElemGuid = elem.Guid,
                    StiffType = elem.ElemType == StructuralType.BucklingStiffener ? "BS" : "STIFF",
                    Orientation = DetectOrientation(elem.ObbAngle)
                };

                // Kiểm tra edge stiffener
                bool isEdge = IsEdgeStiffener(elem, stiffElements, topPlates);
                stiffModel.IsEdge = isEdge;

                // Tính throw vector
                double throwX, throwY;

                if (isEdge)
                {
                    // Edge → đổ ra xa tâm panel
                    double dx = elem.CentroidX - panelCenterX;
                    double dy = elem.CentroidY - panelCenterY;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0.001)
                    {
                        throwX = dx / len;
                        throwY = dy / len;
                    }
                    else
                    {
                        throwX = DetailDesignConstants.STBD_DIR[0];
                        throwY = DetailDesignConstants.STBD_DIR[1];
                    }
                    edgeCount++;
                }
                else
                {
                    // Inner → theo PanelSide
                    switch (panel.Side)
                    {
                        case PanelSide.Port:
                            throwX = DetailDesignConstants.STBD_DIR[0];
                            throwY = DetailDesignConstants.STBD_DIR[1];
                            break;
                        case PanelSide.Starboard:
                            throwX = DetailDesignConstants.PORT_DIR[0];
                            throwY = DetailDesignConstants.PORT_DIR[1];
                            break;
                        case PanelSide.Center:
                        default:
                            // Kiểm tra trên trục đối xứng
                            double distToAxis = Math.Abs(elem.CentroidY - panelCenterY);
                            if (distToAxis < DetailDesignConstants.TOLERANCE_CONTACT)
                            {
                                throwX = DetailDesignConstants.STBD_DIR[0];
                                throwY = DetailDesignConstants.STBD_DIR[1];
                            }
                            else
                            {
                                double dx = panelCenterX - elem.CentroidX;
                                double dy = panelCenterY - elem.CentroidY;
                                double len = Math.Sqrt(dx * dx + dy * dy);
                                throwX = len > 0.001 ? dx / len : 0;
                                throwY = len > 0.001 ? dy / len : -1;
                            }
                            break;
                    }
                }

                stiffModel.ThrowVecX = throwX;
                stiffModel.ThrowVecY = throwY;

                result.Add(stiffModel);
            }

            Debug.WriteLine($"{LOG_PREFIX} Computed {result.Count} throw vectors ({edgeCount} edge stiffeners).");
            return result;
        }

        /// <summary>Edge stiffener: gần mép top plate nhất trong nhóm cùng hướng</summary>
        private static bool IsEdgeStiffener(StructuralElementModel stiff,
            List<StructuralElementModel> allStiffs, List<StructuralElementModel> topPlates)
        {
            if (topPlates.Count == 0) return false;

            // Tính khoảng cách đến mép top plate gần nhất (dùng bounding box)
            double minX = topPlates.SelectMany(t => t.VerticesWCS).Min(v => v.X);
            double maxX = topPlates.SelectMany(t => t.VerticesWCS).Max(v => v.X);
            double minY = topPlates.SelectMany(t => t.VerticesWCS).Min(v => v.Y);
            double maxY = topPlates.SelectMany(t => t.VerticesWCS).Max(v => v.Y);

            double myDistToEdge = Math.Min(
                Math.Min(stiff.CentroidX - minX, maxX - stiff.CentroidX),
                Math.Min(stiff.CentroidY - minY, maxY - stiff.CentroidY)
            );

            // So sánh với min dist của stiffeners cùng hướng
            string myOrientation = DetectOrientation(stiff.ObbAngle);
            var sameDir = allStiffs.Where(s => DetectOrientation(s.ObbAngle) == myOrientation);

            double minDistAll = sameDir.Min(s =>
            {
                return Math.Min(
                    Math.Min(s.CentroidX - minX, maxX - s.CentroidX),
                    Math.Min(s.CentroidY - minY, maxY - s.CentroidY)
                );
            });

            return Math.Abs(myDistToEdge - minDistAll) < DetailDesignConstants.TOLERANCE_GAP;
        }

        /// <summary>Xác định hướng: LONG hoặc TRANS</summary>
        private static string DetectOrientation(double angleRad)
        {
            double a = Math.Abs(angleRad) % Math.PI;
            if (a < Math.PI / 4 || a > 3 * Math.PI / 4)
                return "LONG";
            return "TRANS";
        }
    }
}
