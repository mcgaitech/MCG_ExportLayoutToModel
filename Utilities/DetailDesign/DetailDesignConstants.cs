using System.Linq;

namespace MCGCadPlugin.Utilities.DetailDesign
{
    /// <summary>
    /// Hằng số dùng chung cho toàn module DetailDesign.
    /// Không khởi tạo — chỉ truy cập static.
    /// </summary>
    public static class DetailDesignConstants
    {
        #region Box Block Keywords

        /// <summary>Keywords nhận diện sub-block closing box — mở rộng khi phát hiện thêm</summary>
        public static readonly string[] BOX_BLOCK_KEYWORDS =
        {
            "BX", "BOX", "CB", "CBOX"
        };

        /// <summary>Check tên block có chứa keyword closing box không (case-insensitive)</summary>
        public static bool IsBoxBlock(string blockName)
        {
            if (string.IsNullOrEmpty(blockName)) return false;
            var upper = blockName.ToUpper();
            return BOX_BLOCK_KEYWORDS.Any(k => upper.Contains(k));
        }

        #endregion

        #region Layer Names

        /// <summary>Layer chứa top plate polylines</summary>
        public const string LAYER_TOPPLATE = "0";

        /// <summary>Layer chứa web plates (AM_0)</summary>
        public const string LAYER_WEB = "Mechanical-AM_0";

        /// <summary>Layer chứa flanges (AM_5)</summary>
        public const string LAYER_FLANGE = "Mechanical-AM_5";

        /// <summary>Layer chứa flanges — variant naming trong một số DWG (AM_11)</summary>
        public const string LAYER_FLANGE_ALT = "Mechanical-AM_11";

        /// <summary>Layer chứa stiffeners và doubling plates (AM_3)</summary>
        public const string LAYER_STIFF = "Mechanical-AM_3";

        #endregion

        #region Color Indices

        /// <summary>Color index của stiffener trên layer AM_3 (variant chính)</summary>
        public const int COLOR_STIFFENER = 40;

        /// <summary>Color index của stiffener variant — một số DWG dùng color 30</summary>
        public const int COLOR_STIFFENER_ALT = 30;

        /// <summary>Color index của buckling stiffener (Magenta)</summary>
        public const int COLOR_BS = 6;

        #endregion

        #region Tolerances

        /// <summary>Khoảng cách tối đa để coi là "chạm nhau" (mm)</summary>
        public const double TOLERANCE_CONTACT = 1.0;

        /// <summary>Khoảng cách tối đa cho bracket detection — rộng hơn TOLERANCE_CONTACT (mm)</summary>
        public const double TOLERANCE_BRACKET = 5.0;

        /// <summary>Khoảng gap tối đa stiffener end → web plate để tạo bracket "B" (mm)</summary>
        public const double BRACKET_END_GAP_MAX = 35.0;

        /// <summary>Khoảng cách tối đa để coi là "gần nhau" (mm)</summary>
        public const double TOLERANCE_GAP = 10.0;

        #endregion

        #region Aspect Ratio Thresholds

        /// <summary>Aspect ratio > 5.0 → Stiffener</summary>
        public const double RATIO_STIFF_MIN = 5.0;

        /// <summary>Aspect ratio ≤ 3.0 → Plate (DoublingPlate)</summary>
        public const double RATIO_PLATE_MAX = 3.0;

        #endregion

        #region Bracket Defaults

        /// <summary>Chiều dài toe mặc định cho bracket (mm)</summary>
        public const double BRACKET_TOE_DEFAULT = 15.0;

        /// <summary>Giới hạn chiều dài (mm) để áp dụng ConnectedLongEdge + SlantedConnected rule.
        /// Plate dài hơn ngưỡng này không apply (quá lớn, có rule riêng khác).</summary>
        public const double CONN_RULE_MAX_LENGTH = 1000.0;

        #endregion

        #region XData

        /// <summary>Tên app đăng ký XData trong AutoCAD</summary>
        public const string XDATA_APP_NAME = "MCG_PANEL_TOOL";

        #endregion

        #region Paths

        /// <summary>Đường dẫn file SQLite database</summary>
        public const string DB_PATH = @"C:\CustomTools\MCGPanelTool.db";

        /// <summary>Đường dẫn file Symbol.dwg chứa block library</summary>
        public const string SYMBOL_DWG = @"C:\CustomTools\Symbol.dwg";

        /// <summary>Thư mục chứa log files</summary>
        public const string LOG_PATH = @"C:\CustomTools\Temp\";

        #endregion

        #region Direction Vectors

        /// <summary>Hướng PORT (lên trên, Y+)</summary>
        public static readonly double[] PORT_DIR = { 0, 1 };

        /// <summary>Hướng STARBOARD (xuống dưới, Y-)</summary>
        public static readonly double[] STBD_DIR = { 0, -1 };

        #endregion
    }
}
