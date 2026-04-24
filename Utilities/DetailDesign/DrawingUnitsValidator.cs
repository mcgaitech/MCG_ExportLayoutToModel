using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCGCadPlugin.Utilities.DetailDesign
{
    /// <summary>
    /// Kiểm tra đơn vị bản vẽ trước khi scan.
    /// Bắt buộc gọi trước MỌI scan operation.
    /// </summary>
    public static class DrawingUnitsValidator
    {
        private const string LOG_PREFIX = "[DrawingUnitsValidator]";

        /// <summary>
        /// Kiểm tra INSUNITS == Millimeters.
        /// Nếu sai → hiển thị alert tiếng Việt, return false.
        /// </summary>
        /// <param name="db">Database của bản vẽ hiện tại</param>
        /// <returns>true nếu đơn vị là Millimeters, false nếu không</returns>
        public static bool Validate(Database db)
        {
            Debug.WriteLine($"{LOG_PREFIX} Checking drawing units...");

            var insunits = (UnitsValue)(int)db.Insunits;

            if (insunits != UnitsValue.Millimeters)
            {
                Debug.WriteLine($"{LOG_PREFIX} FAILED — Current units: {insunits}");

                Application.ShowAlertDialog(
                    "Đơn vị bản vẽ không phải Millimeter.\n" +
                    $"Đơn vị hiện tại: {insunits}\n\n" +
                    "Vui lòng gõ lệnh UNITS trong AutoCAD\n" +
                    "và đổi về Millimeters trước khi scan.");

                return false;
            }

            Debug.WriteLine($"{LOG_PREFIX} PASSED — Units: Millimeters");
            return true;
        }
    }
}
