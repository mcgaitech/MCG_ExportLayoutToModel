using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
using MCGCadPlugin.Services.LayoutToModel;
using System.Windows.Forms;
using System;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MCGCadPlugin.Commands.LayoutToModel.LayoutToModelCommand))]

namespace MCGCadPlugin.Commands.LayoutToModel
{
    public class LayoutToModelCommand
    {
        private readonly ILayoutToModelService _service;

        public LayoutToModelCommand()
        {
            _service = new LayoutToModelService();
        }

        [CommandMethod("MCG_LayoutToModelBatch")]
        public void ExecuteBatch()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Chọn các file AutoCAD cần Export Layout";
    dialog.Filter = "AutoCAD Drawing (*.dwg)|*.dwg";
    // dialog.Multiline = true; // <--- Dòng này sai
    
    dialog.Multiselect = true; // <--- Sửa thành dòng này

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var filePaths = new System.Collections.Generic.List<string>(dialog.FileNames);
                    ed.WriteMessage($"\n[MCG] Đã chọn {filePaths.Count} file. Bắt đầu xử lý...");
                    
                    _service.BatchProcessFiles(filePaths);
                    
                    ed.WriteMessage("\n[MCG] Hoàn thành xử lý các file đã chọn.");
                }
            }
        }
    }
}