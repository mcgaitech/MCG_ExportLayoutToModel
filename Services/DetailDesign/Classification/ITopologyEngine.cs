using System.Collections.Generic;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Classification
{
    /// <summary>
    /// Kết quả sau khi TopologyEngine phân tích.
    /// </summary>
    public class TopologyResult
    {
        /// <summary>Danh sách elements đã cập nhật type (AM0 → WebPlate/Bracket/...)</summary>
        public List<StructuralElementModel> Elements { get; set; } = new List<StructuralElementModel>();

        /// <summary>Girders (web + flange pairings)</summary>
        public List<GirderModel> Girders { get; set; } = new List<GirderModel>();

        /// <summary>Closing boxes detected</summary>
        public List<ClosingBoxModel> ClosingBoxes { get; set; } = new List<ClosingBoxModel>();
    }

    /// <summary>
    /// Interface cho topology engine — phân loại AM0_Unclassified dựa trên quan hệ không gian.
    /// </summary>
    public interface ITopologyEngine
    {
        /// <summary>
        /// Phân tích topology: web-flange pairing, bracket detection, closing box.
        /// </summary>
        /// <param name="elements">Danh sách elements từ PrimaryClassifier (có AM0_Unclassified)</param>
        /// <returns>TopologyResult với elements đã cập nhật + girders + closing boxes</returns>
        TopologyResult Analyze(List<StructuralElementModel> elements);
    }
}
