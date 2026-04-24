using System;
using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using MCGCadPlugin.Views.DetailDesign;
using MCGCadPlugin.Views.FittingManagement;
using MCGCadPlugin.Views.PanelData;
using MCGCadPlugin.Views.TableOfContent;
using MCGCadPlugin.Views.Weight;

namespace MCGCadPlugin.Commands
{
    /// <summary>
    /// PaletteSet gom chung 5 module của MCG Plugin.
    ///
    /// Pattern CHUẨN (đã verify nút X hoạt động với single tab):
    /// - 2-arg constructor: new PaletteSet(name, guid)
    /// - KHÔNG set Style tường minh → AutoCAD dùng default flags (có nút X + AutoHide)
    /// - KeepFocus = true
    /// - Thứ tự: Visible=true → Dock=Right → Activate(index)
    ///
    /// Multi-tab vẫn dùng cùng pattern — chỉ khác: gọi AddVisual() nhiều lần.
    /// </summary>
    public class MCGPluginPaletteCommand
    {
        private const string LOG_PREFIX = "[MCGPluginPalette]";

        private static PaletteSet _ps = null;

        // GUID cố định — AutoCAD nhớ vị trí dock. KHÔNG đổi sau deploy.
        private static readonly Guid PaletteGuid =
            new Guid("d42d9d08-37d7-4cbe-8428-50e046571667");

        // Index các tab — phải khớp thứ tự AddVisual() trong EnsureInit()
        private const int TAB_DETAIL_DESIGN      = 0;
        private const int TAB_FITTING_MANAGEMENT = 1;
        private const int TAB_PANEL_DATA         = 2;
        private const int TAB_TABLE_OF_CONTENT   = 3;
        private const int TAB_WEIGHT             = 4;

        /// <summary>Khởi tạo PaletteSet 1 lần duy nhất.</summary>
        private static void EnsureInit()
        {
            if (_ps != null) return;

            _ps = new PaletteSet("MCG Plugin", PaletteGuid);

            // AddVisual — thứ tự phải khớp constants TAB_*
            _ps.AddVisual("Detail Design",      new DetailDesignView());
            _ps.AddVisual("Fitting Management", new FittingManagementView());
            _ps.AddVisual("Panel Data",         new PanelDataView());
            _ps.AddVisual("Table of Content",   new TableOfContentView());
            _ps.AddVisual("Weight",             new WeightView());

            _ps.DockEnabled = DockSides.Right | DockSides.Left;
            _ps.Size        = new System.Drawing.Size(400, 600);
            _ps.KeepFocus   = true;
            // KHÔNG set _ps.Style — để AutoCAD dùng default (có ShowCloseButton)
        }

        /// <summary>Hiển thị palette và activate tab theo index.</summary>
        private static void ShowAndActivate(int tabIndex)
        {
            try
            {
                EnsureInit();

                _ps.Visible = true;
                _ps.Dock    = DockSides.Right;
                if (_ps.Count > tabIndex)
                    _ps.Activate(tabIndex);

                Debug.WriteLine($"{LOG_PREFIX} Shown, tab={tabIndex}.");
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: {ex.Message}");
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nLỗi mở MCG Plugin palette: {ex.Message}");
            }
        }

        // -------------------- AutoCAD Commands --------------------

        /// <summary>MCG_Plugin — mở palette (default tab: Detail Design).</summary>
        [CommandMethod("MCG_Plugin", CommandFlags.NoHistory)]
        public void ShowMCGPlugin() => ShowAndActivate(TAB_DETAIL_DESIGN);

        /// <summary>MCG_DetailDesign — mở palette + focus tab Detail Design.</summary>
        [CommandMethod("MCG_DetailDesign", CommandFlags.NoHistory)]
        public void ShowDetailDesign() => ShowAndActivate(TAB_DETAIL_DESIGN);

        /// <summary>MCG_FittingManagement — focus tab Fitting Management.</summary>
        [CommandMethod("MCG_FittingManagement", CommandFlags.NoHistory)]
        public void ShowFittingManagement() => ShowAndActivate(TAB_FITTING_MANAGEMENT);

        /// <summary>MCG_PanelData — focus tab Panel Data.</summary>
        [CommandMethod("MCG_PanelData", CommandFlags.NoHistory)]
        public void ShowPanelData() => ShowAndActivate(TAB_PANEL_DATA);

        /// <summary>MCG_TableOfContent — focus tab Table of Content.</summary>
        [CommandMethod("MCG_TableOfContent", CommandFlags.NoHistory)]
        public void ShowTableOfContent() => ShowAndActivate(TAB_TABLE_OF_CONTENT);

        /// <summary>MCG_Weight — focus tab Weight.</summary>
        [CommandMethod("MCG_Weight", CommandFlags.NoHistory)]
        public void ShowWeight() => ShowAndActivate(TAB_WEIGHT);
    }
}
