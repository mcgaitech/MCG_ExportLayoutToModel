using System.Diagnostics;
using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Services.DetailDesign.Parameters
{
    /// <summary>
    /// Parse tên panel từ block name → tách tên + xác định PanelSide.
    /// Quy tắc: hậu tố cuối cùng P=Port, S=Starboard, C=Center.
    /// Ví dụ: "T.6D09C_Assy" → Name="T.6D09C", Side=Center
    /// </summary>
    public static class PanelNameParser
    {
        private const string LOG_PREFIX = "[PanelNameParser]";

        /// <summary>Kết quả parse tên panel</summary>
        public class ParseResult
        {
            /// <summary>Tên panel (không có hậu tố _Assy)</summary>
            public string Name { get; set; }

            /// <summary>Vị trí panel trên tàu</summary>
            public PanelSide Side { get; set; }

            /// <summary>Side có được auto-detect từ tên không</summary>
            public bool AutoDetected { get; set; }
        }

        /// <summary>
        /// Parse tên block → tên panel + side.
        /// </summary>
        /// <param name="blockName">Tên block gốc (ví dụ: T.6D09C_Assy)</param>
        /// <returns>ParseResult với tên + side</returns>
        public static ParseResult Parse(string blockName)
        {
            Debug.WriteLine($"{LOG_PREFIX} Parsing: {blockName}");

            // Bỏ hậu tố _Assy / _Assembly
            var panelName = RemoveAssySuffix(blockName);

            // Lấy ký tự cuối cùng để xác định side
            var side = PanelSide.Center;
            var autoDetected = false;

            if (!string.IsNullOrEmpty(panelName))
            {
                char lastChar = char.ToUpper(panelName[panelName.Length - 1]);
                switch (lastChar)
                {
                    case 'P':
                        side = PanelSide.Port;
                        autoDetected = true;
                        break;
                    case 'S':
                        side = PanelSide.Starboard;
                        autoDetected = true;
                        break;
                    case 'C':
                        side = PanelSide.Center;
                        autoDetected = true;
                        break;
                    default:
                        // Không nhận diện được → mặc định Center
                        side = PanelSide.Center;
                        autoDetected = false;
                        break;
                }
            }

            var result = new ParseResult
            {
                Name = panelName,
                Side = side,
                AutoDetected = autoDetected
            };

            Debug.WriteLine($"{LOG_PREFIX} Name={result.Name} | Side={result.Side} | AutoDetected={result.AutoDetected}");
            return result;
        }

        /// <summary>
        /// Bỏ hậu tố _Assy, _Assembly, _assy khỏi tên block.
        /// </summary>
        private static string RemoveAssySuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var lower = name.ToLower();
            if (lower.EndsWith("_assy"))
                return name.Substring(0, name.Length - 5);
            if (lower.EndsWith("_assembly"))
                return name.Substring(0, name.Length - 9);
            if (lower.EndsWith(" assy"))
                return name.Substring(0, name.Length - 5);

            return name;
        }
    }
}
