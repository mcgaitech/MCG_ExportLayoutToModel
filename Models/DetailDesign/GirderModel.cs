namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Girder = Web plate + Flange top + Flange bottom.
    /// Được tạo bởi TopologyEngine khi ghép web-flange.
    /// </summary>
    public class GirderModel
    {
        /// <summary>GUID duy nhất của girder</summary>
        public string Guid { get; set; }

        /// <summary>GUID panel chứa girder</summary>
        public string PanelGuid { get; set; }

        /// <summary>GUID của web element (structural_elements)</summary>
        public string WebElemGuid { get; set; }

        /// <summary>GUID của flange trên (có thể null)</summary>
        public string FlangeTopGuid { get; set; }

        /// <summary>GUID của flange dưới (có thể null)</summary>
        public string FlangeBotGuid { get; set; }

        /// <summary>Chiều cao web (mm)</summary>
        public double? WebHeight { get; set; }

        /// <summary>Chiều dày web (mm)</summary>
        public double? WebThickness { get; set; }

        /// <summary>Chiều rộng flange (mm)</summary>
        public double? FlangeWidth { get; set; }

        /// <summary>Chiều dày flange (mm)</summary>
        public double? FlangeThickness { get; set; }

        /// <summary>Hướng: LONG (dọc) hoặc TRANS (ngang)</summary>
        public string Orientation { get; set; }

        /// <summary>Chiều dài span (mm)</summary>
        public double? SpanLength { get; set; }
    }
}
