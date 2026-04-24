using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MCGCadPlugin.Views.DetailDesign.ViewModels
{
    /// <summary>
    /// ViewModel cho mỗi node trong StructureTreeView.
    /// SpaceClaim-style: text-only display, edit ở Properties Panel.
    /// </summary>
    public class ElementNodeViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Tên hiển thị (ví dụ: "S01", "WEB-01")</summary>
        public string DisplayName { get; set; }

        /// <summary>Icon: ▼ group expanded, ▶ group collapsed, ○ leaf</summary>
        public string Icon { get; set; }

        /// <summary>GUID của StructuralElement</summary>
        public string ElementGuid { get; set; }

        /// <summary>GUID rút gọn hiển thị trên tree (8 ký tự đầu)</summary>
        public string ShortGuid => string.IsNullOrEmpty(ElementGuid) ? "" : ElementGuid.Substring(0, System.Math.Min(8, ElementGuid.Length));

        /// <summary>AutoCAD handle (internal use)</summary>
        public string AcadHandle { get; set; }

        /// <summary>Giá trị chính hiển thị: "t:10mm" hoặc "HP120x7" hoặc "9 members"</summary>
        public string ValueText { get; set; } = "";

        /// <summary>Throw direction text: "→(0,-1)" hoặc "OB"</summary>
        public string ThrowText { get; set; } = "";

        /// <summary>Status: ? pending, ✓ complete</summary>
        public string StatusIcon { get; set; } = "";

        /// <summary>Node con</summary>
        public ObservableCollection<ElementNodeViewModel> Children { get; set; }
            = new ObservableCollection<ElementNodeViewModel>();

        /// <summary>WCS centroid X</summary>
        public double CentroidX { get; set; }

        /// <summary>WCS centroid Y</summary>
        public double CentroidY { get; set; }

        /// <summary>OBB width (raw)</summary>
        public double ObbWidth { get; set; }

        /// <summary>OBB length</summary>
        public double ObbLength { get; set; }

        /// <summary>Thickness thật (đã round, từ default hoặc compute) — mm</summary>
        public double? Thickness { get; set; }

        /// <summary>Area (mm²) — cho plates</summary>
        public double? Area { get; set; }

        /// <summary>Length (mm) — OBB length / plan view length</summary>
        public double? Length { get; set; }

        /// <summary>Height (mm) — web height cho web/bracket</summary>
        public double? Height { get; set; }

        /// <summary>Flange width (computed) hoặc "Other"</summary>
        public string FlangeWidthText { get; set; } = "";

        /// <summary>Element type string cho Properties Panel</summary>
        public string ElemTypeStr { get; set; } = "";

        /// <summary>Layer name</summary>
        public string LayerName { get; set; } = "";

        /// <summary>Bracket sub-type: OB/IB/BF/B</summary>
        public string BracketSubType { get; set; } = "";

        /// <summary>Là plate element (có thickness)</summary>
        public bool IsPlate { get; set; }

        /// <summary>Là profile element (có dropdown section)</summary>
        public bool IsProfile { get; set; }

        /// <summary>Hiện flip button trên tree (Web/Stiff/BS/Bracket/Flange leaf nodes)</summary>
        public bool IsFlippable { get; set; }

        // ═══ Multi-select ═══

        private bool _isMultiSelected;
        /// <summary>Node đang được multi-select (Ctrl+Click)</summary>
        public bool IsMultiSelected
        {
            get => _isMultiSelected;
            set { _isMultiSelected = value; OnPropertyChanged(nameof(IsMultiSelected)); }
        }

        // ═══ Tree state ═══

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
