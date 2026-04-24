using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MCGCadPlugin.Views.DetailDesign.ViewModels;

namespace MCGCadPlugin.Views.DetailDesign.Controls
{
    /// <summary>
    /// Properties Panel — hiển thị + edit properties của node đang chọn.
    /// </summary>
    public partial class PropertiesPanel : UserControl
    {
        private const string LOG_PREFIX = "[PropertiesPanel]";

        private List<ElementNodeViewModel> _currentNodes = new List<ElementNodeViewModel>();
        private List<string> _profileOptions = new List<string>();
        private string _unit = "m";

        public PropertiesPanel()
        {
            InitializeComponent();
        }

        /// <summary>Set danh sách profiles cho dropdown</summary>
        public void SetProfileOptions(List<string> options)
        {
            _profileOptions = options;
            ProfileDropdown.ItemsSource = _profileOptions;
        }

        /// <summary>Set display unit (mm hoặc m)</summary>
        public void SetUnit(string unit)
        {
            _unit = unit;
            // Refresh display if có node đang hiện
            if (_currentNodes.Count > 0)
                UpdateSelection(_currentNodes);
        }

        /// <summary>Re-render panel với data nodes hiện tại (dùng sau flip/save).</summary>
        public void RefreshCurrent()
        {
            if (_currentNodes.Count > 0)
                UpdateSelection(_currentNodes);
        }

        private string FormatLen(double mm) => _unit == "m" ? $"{mm / 1000.0:F2} m" : $"{mm:F0} mm";
        private string FormatArea(double mm2) => _unit == "m" ? $"{mm2 / 1_000_000.0:F2} m²" : $"{mm2:F0} mm²";

        /// <summary>Cập nhật panel khi selection thay đổi</summary>
        public void UpdateSelection(List<ElementNodeViewModel> nodes)
        {
            _currentNodes = nodes ?? new List<ElementNodeViewModel>();

            if (_currentNodes.Count == 0)
            {
                HeaderText.Text = "Properties";
                ClearProps();
                return;
            }

            if (_currentNodes.Count == 1)
            {
                var node = _currentNodes[0];
                HeaderText.Text = $"Properties — {node.DisplayName}";
                PropName.Text = node.DisplayName;
                PropGuid.Text = node.ElementGuid ?? "";
                PropType.Text = node.ElemTypeStr;
                PropLayer.Text = node.LayerName;
                PropThickness.Text = node.IsPlate && node.Thickness.HasValue ? $"{node.Thickness.Value:F0}" : "";
                PropArea.Text = node.Area.HasValue ? FormatArea(node.Area.Value) : "—";
                PropLength.Text = node.Length.HasValue ? FormatLen(node.Length.Value) : "—";
                PropHeight.Text = node.Height.HasValue ? FormatLen(node.Height.Value) : "—";
                PropThrow.Text = node.ThrowText;

                // Flange: hiện FL Width (computed)
                if (!string.IsNullOrEmpty(node.FlangeWidthText))
                {
                    WidthLabel.Visibility = Visibility.Visible;
                    PropFlangeWidth.Visibility = Visibility.Visible;
                    PropFlangeWidth.Text = node.FlangeWidthText;
                }
                else
                {
                    WidthLabel.Visibility = Visibility.Collapsed;
                    PropFlangeWidth.Visibility = Visibility.Collapsed;
                }

                // Profile dropdown cho Stiffener/BS
                if (node.IsProfile)
                {
                    ProfilePanel.Visibility = Visibility.Visible;
                    ProfileDropdown.ItemsSource = _profileOptions;
                    ProfileDropdown.SelectedItem = node.ValueText != "" && node.ValueText != "?" ? node.ValueText : null;
                    ThkLabel.Text = "OBB Width:";
                    PropThickness.Text = $"{node.ObbWidth:F0}";
                }
                else
                {
                    ProfilePanel.Visibility = Visibility.Collapsed;
                    ThkLabel.Text = "Thickness:";
                }

                BtnApplySelected.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Multi-select
                HeaderText.Text = $"Properties — {_currentNodes.Count} selected";
                PropName.Text = $"{_currentNodes.Count} elements";
                PropGuid.Text = "";
                PropType.Text = _currentNodes.First().ElemTypeStr;
                PropLayer.Text = "";
                PropThickness.Text = "";
                PropThrow.Text = "";

                bool allProfile = _currentNodes.All(n => n.IsProfile);
                ProfilePanel.Visibility = allProfile ? Visibility.Visible : Visibility.Collapsed;
                BtnApplySelected.Visibility = _currentNodes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ClearProps()
        {
            PropName.Text = "";
            PropGuid.Text = "";
            PropType.Text = "";
            PropLayer.Text = "";
            PropThickness.Text = "";
            PropThrow.Text = "";
            ProfilePanel.Visibility = Visibility.Collapsed;
            BtnApplySelected.Visibility = Visibility.Collapsed;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNodes.Count != 1) return;
            Debug.WriteLine($"{LOG_PREFIX} Apply: {_currentNodes[0].DisplayName}");
            // TODO: Save thickness/profile → SQLite + XData
        }

        private void ApplyAllSame_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNodes.Count != 1) return;
            Debug.WriteLine($"{LOG_PREFIX} Apply All Same Width: {_currentNodes[0].ObbWidth:F1}mm");
            // TODO: Find all same OBB width → apply same profile
        }

        private void ApplySelected_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"{LOG_PREFIX} Apply to {_currentNodes.Count} selected");
            // TODO: Apply profile to all multi-selected nodes
        }
    }
}
