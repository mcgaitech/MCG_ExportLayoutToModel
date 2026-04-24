using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCGCadPlugin.Services.DetailDesign.DebugSymbols;
using MCGCadPlugin.Views.DetailDesign.ViewModels;

namespace MCGCadPlugin.Views.DetailDesign
{
    /// <summary>
    /// Code-behind cho DetailDesignView — tối thiểu, chỉ xử lý UI events.
    /// </summary>
    public partial class DetailDesignView : UserControl
    {
        private const string LOG_PREFIX = "[DetailDesignView]";

        private Document _subscribedDoc;

        public DetailDesignView()
        {
            InitializeComponent();

            // Set static reference
            var vm = DataContext as DetailDesignViewModel;
            if (vm != null)
                Commands.DetailDesign.DetailDesignCommand.ActiveViewModel = vm;

            // Wire tree selection → properties panel
            TreeView.SelectionChanged += nodes =>
            {
                PropsPanel.UpdateSelection(nodes);
            };

            // Wire flip button → VM.FlipElement()
            TreeView.FlipRequested += guid =>
            {
                vm?.FlipElement(guid);
            };

            // Wire right-click "Find in drawing" → VM.FindInDrawing()
            TreeView.FindInDrawingRequested += guid =>
            {
                vm?.FindInDrawing(guid);
            };

            // Wire flip-completed → refresh Properties Panel (ThrowText đã đổi)
            if (vm != null)
            {
                vm.FlipCompleted += guid =>
                {
                    Dispatcher.BeginInvoke(new Action(() => PropsPanel.RefreshCurrent()));
                };
                vm.TreeNodeFocused += guid =>
                {
                    TreeView.ScrollToGuid(guid);
                };
            }

            // Load profile options + unit vào Properties Panel sau khi DB ready
            Loaded += (s, e) =>
            {
                if (vm != null)
                {
                    PropsPanel.SetProfileOptions(vm.GetProfileOptions());
                    PropsPanel.SetUnit(vm.DisplayUnit);
                }
                SubscribeSelectionSync();
            };

            Unloaded += (s, e) => UnsubscribeSelectionSync();

            // Wire unit change → Properties Panel
            if (vm != null)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DetailDesignViewModel.DisplayUnit))
                        PropsPanel.SetUnit(vm.DisplayUnit);
                };
            }

            Debug.WriteLine($"{LOG_PREFIX} View initialized.");
        }

        /// <summary>Subscribe DWG pick → tree highlight qua ImpliedSelectionChanged.</summary>
        private void SubscribeSelectionSync()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null || doc == _subscribedDoc) return;
            UnsubscribeSelectionSync();
            doc.ImpliedSelectionChanged += OnImpliedSelectionChanged;
            _subscribedDoc = doc;
            Debug.WriteLine($"{LOG_PREFIX} Subscribed ImpliedSelectionChanged on doc {doc.Name}");
        }

        private void UnsubscribeSelectionSync()
        {
            if (_subscribedDoc == null) return;
            try { _subscribedDoc.ImpliedSelectionChanged -= OnImpliedSelectionChanged; }
            catch { /* doc disposed */ }
            _subscribedDoc = null;
        }

        /// <summary>
        /// Khi user pick block trong DWG → nếu là ThrowThickness block → extract GUID → VM highlight tree node.
        /// </summary>
        private void OnImpliedSelectionChanged(object sender, EventArgs e)
        {
            var doc = sender as Document ?? Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var vm = DataContext as DetailDesignViewModel;
            if (vm == null) return;

            try
            {
                var res = doc.Editor.SelectImplied();
                if (res.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
                var ids = res.Value?.GetObjectIds();
                if (ids == null || ids.Length == 0) return;

                Debug.WriteLine($"{LOG_PREFIX} ImpliedSel fired — {ids.Length} entities");

                string foundGuid = null;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        ResultBuffer rb = null;
                        try { rb = br.GetXDataForApplication(DebugSymbolService.DEBUG_XDATA_APP); }
                        catch { rb = null; }
                        if (rb == null) continue;

                        TypedValue[] arr;
                        try { arr = rb.AsArray(); }
                        finally { rb.Dispose(); }

                        foreach (var tv in arr)
                        {
                            if (tv.TypeCode != 1000) continue;
                            var s = tv.Value as string;
                            if (string.IsNullOrEmpty(s)) continue;
                            if (s.Length >= 32 && s.Contains("-"))
                            {
                                foundGuid = s; break;
                            }
                        }
                        if (foundGuid != null) break;
                    }
                    tr.Commit();
                }

                if (!string.IsNullOrEmpty(foundGuid))
                {
                    Debug.WriteLine($"{LOG_PREFIX} Picked ThrowThickness block → GUID {foundGuid.Substring(0,8)}");
                    var guid = foundGuid;
                    Dispatcher.BeginInvoke(new Action(() => vm.SelectTreeNodeByGuid(guid)));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} OnImpliedSelectionChanged ERROR: {ex.Message}");
            }
        }

        private void SelectPanel_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"{LOG_PREFIX} SELECT PANEL clicked.");
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.SendStringToExecute("MCG_SelectPanel ", true, false, true);
        }

        private void IProperties_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"{LOG_PREFIX} iProperties clicked.");
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.SendStringToExecute("MCG_IProperties ", true, false, true);
        }

        private void ShowDebugSymbols_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"{LOG_PREFIX} Show Debug clicked.");
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.SendStringToExecute("MCG_ShowDebugSymbols ", true, false, true);
        }

        private void SaveDB_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"{LOG_PREFIX} Save DB clicked.");
            var vm = DataContext as DetailDesignViewModel;
            vm?.ExecuteSaveToDb();
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as DetailDesignViewModel;
            if (vm == null) return;
            foreach (var node in vm.TreeNodes)
                SetExpandedRecursive(node, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as DetailDesignViewModel;
            if (vm == null) return;
            // Root expanded, sub-groups collapsed (level 1 visible, deeper collapsed)
            foreach (var root in vm.TreeNodes)
            {
                root.IsExpanded = true;
                foreach (var group in root.Children)
                    SetExpandedRecursive(group, false);
            }
        }

        private void SetExpandedRecursive(ViewModels.ElementNodeViewModel node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
                SetExpandedRecursive(child, expanded);
        }
    }
}
