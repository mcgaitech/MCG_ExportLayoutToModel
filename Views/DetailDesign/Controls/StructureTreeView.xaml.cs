using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MCGCadPlugin.Views.DetailDesign.ViewModels;

namespace MCGCadPlugin.Views.DetailDesign.Controls
{
    /// <summary>
    /// StructureTreeView — SpaceClaim-style text-only tree.
    /// Hỗ trợ multi-select (Ctrl+Click) và single-select.
    /// </summary>
    public partial class StructureTreeView : UserControl
    {
        private const string LOG_PREFIX = "[StructureTreeView]";

        /// <summary>Danh sách nodes đang multi-select</summary>
        public List<ElementNodeViewModel> SelectedNodes { get; } = new List<ElementNodeViewModel>();

        /// <summary>Event khi selection thay đổi — DetailDesignView listen để update Properties Panel</summary>
        public event System.Action<List<ElementNodeViewModel>> SelectionChanged;

        /// <summary>Event khi user click flip button — DetailDesignView route vào VM.FlipElement()</summary>
        public event System.Action<string> FlipRequested;

        /// <summary>Event khi user chọn "Find in drawing" từ context menu</summary>
        public event System.Action<string> FindInDrawingRequested;

        /// <summary>Click handler cho Flip button trên tree node</summary>
        private void FlipButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var node = btn?.Tag as ElementNodeViewModel;
            if (node == null || string.IsNullOrEmpty(node.ElementGuid)) return;
            Debug.WriteLine($"{LOG_PREFIX} Flip clicked: {node.DisplayName} ({node.ElementGuid.Substring(0,8)})");
            FlipRequested?.Invoke(node.ElementGuid);
            e.Handled = true;  // không trigger select
        }

        /// <summary>Right-click → "Find in drawing"</summary>
        private void FindInDrawing_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var node = mi?.Tag as ElementNodeViewModel;
            if (node == null || string.IsNullOrEmpty(node.ElementGuid)) return;
            Debug.WriteLine($"{LOG_PREFIX} FindInDrawing: {node.DisplayName} ({node.ElementGuid.Substring(0,8)})");
            FindInDrawingRequested?.Invoke(node.ElementGuid);
        }

        /// <summary>Scroll tree item tương ứng guid vào view + highlight. Dispatch sau khi UI update.</summary>
        public void ScrollToGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                var container = FindContainerRecursive(StructureTree.ItemContainerGenerator, StructureTree.Items, guid);
                container?.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private TreeViewItem FindContainerRecursive(ItemContainerGenerator gen, ItemCollection items, string guid)
        {
            foreach (var obj in items)
            {
                var node = obj as ElementNodeViewModel;
                if (node == null) continue;
                var tvi = gen.ContainerFromItem(node) as TreeViewItem;
                if (tvi == null) continue;
                if (node.ElementGuid == guid) return tvi;
                // Ensure children generated
                tvi.ApplyTemplate();
                tvi.UpdateLayout();
                if (tvi.HasItems)
                {
                    var f = FindContainerRecursive(tvi.ItemContainerGenerator, tvi.Items, guid);
                    if (f != null) return f;
                }
            }
            return null;
        }

        public StructureTreeView()
        {
            InitializeComponent();
        }

        /// <summary>Ctrl+Click = multi-select, Click = single-select</summary>
        private void StructureTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Tìm TreeViewItem được click
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is TreeViewItem))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem item && item.DataContext is ElementNodeViewModel node)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Ctrl+Click → toggle multi-select
                    if (node.IsMultiSelected)
                    {
                        node.IsMultiSelected = false;
                        SelectedNodes.Remove(node);
                    }
                    else
                    {
                        node.IsMultiSelected = true;
                        SelectedNodes.Add(node);
                    }
                    e.Handled = true; // ngăn TreeView single-select
                    Debug.WriteLine($"{LOG_PREFIX} Multi-select: {SelectedNodes.Count} nodes");
                    SelectionChanged?.Invoke(SelectedNodes);
                }
                else
                {
                    // Click thường → clear multi-select, single-select
                    ClearMultiSelect();
                    node.IsMultiSelected = true;
                    SelectedNodes.Add(node);
                    SelectionChanged?.Invoke(SelectedNodes);
                }
            }
        }

        /// <summary>Single-select từ TreeView built-in</summary>
        private void StructureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as ElementNodeViewModel;
            if (node == null) return;

            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Notify Properties Panel
                SelectionChanged?.Invoke(new List<ElementNodeViewModel> { node });
            }

            // Log selection (zoom sẽ implement sau)
            if (!string.IsNullOrEmpty(node.ElementGuid))
            {
                Debug.WriteLine($"{LOG_PREFIX} Selected: {node.DisplayName} ({node.AcadHandle}) at ({node.CentroidX:F0}, {node.CentroidY:F0})");
            }
        }

        /// <summary>Clear tất cả multi-select</summary>
        private void ClearMultiSelect()
        {
            foreach (var node in SelectedNodes)
                node.IsMultiSelected = false;
            SelectedNodes.Clear();
        }
    }
}
