using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Services.DetailDesign;
using MCGCadPlugin.Services.DetailDesign.Data;
using MCGCadPlugin.Services.DetailDesign.DebugSymbols;
using MCGCadPlugin.Services.DetailDesign.Parameters;

namespace MCGCadPlugin.Views.DetailDesign.ViewModels
{
    /// <summary>
    /// ViewModel cho tab Detail Design — MVVM bindings.
    /// </summary>
    public class DetailDesignViewModel : INotifyPropertyChanged
    {
        private const string LOG_PREFIX = "[DetailDesignViewModel]";

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly IDetailDesignRepository _repository;
        private readonly IPanelScanService _scanService;
        private PanelContext _currentPanel;
        private List<StructuralElementModel> _classifiedElements;
        private List<BracketModel> _bracketModels = new List<BracketModel>();
        private List<StiffenerModel> _stiffenerModels = new List<StiffenerModel>();
        private List<string> _profileOptions = new List<string>();

        #region Properties

        private InputMode _inputMode = InputMode.Block;
        /// <summary>Chế độ input hiện tại</summary>
        public InputMode CurrentInputMode
        {
            get => _inputMode;
            set { _inputMode = value; OnPropertyChanged(nameof(CurrentInputMode)); OnPropertyChanged(nameof(IsBlockMode)); OnPropertyChanged(nameof(IsEntityMode)); }
        }

        /// <summary>Block mode active</summary>
        public bool IsBlockMode
        {
            get => _inputMode == InputMode.Block;
            set { if (value) CurrentInputMode = InputMode.Block; }
        }

        /// <summary>Entity mode active</summary>
        public bool IsEntityMode
        {
            get => _inputMode == InputMode.Entity;
            set { if (value) CurrentInputMode = InputMode.Entity; }
        }

        private string _panelName = "(chưa chọn)";
        /// <summary>Tên panel đang chọn</summary>
        public string PanelName
        {
            get => _panelName;
            set { _panelName = value; OnPropertyChanged(nameof(PanelName)); }
        }

        private string _panelSide = "—";
        /// <summary>Side của panel</summary>
        public string PanelSideText
        {
            get => _panelSide;
            set { _panelSide = value; OnPropertyChanged(nameof(PanelSideText)); }
        }

        private string _statusText = "Ready";
        /// <summary>Trạng thái hiển thị trên status bar</summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private string _webHeight = "";
        /// <summary>Web height do user nhập</summary>
        public string WebHeight
        {
            get => _webHeight;
            set { _webHeight = value; OnPropertyChanged(nameof(WebHeight)); }
        }

        private string _selectedMaterial = "AH36";
        /// <summary>Vật liệu được chọn</summary>
        public string SelectedMaterial
        {
            get => _selectedMaterial;
            set { _selectedMaterial = value; OnPropertyChanged(nameof(SelectedMaterial)); }
        }

        /// <summary>Danh sách vật liệu</summary>
        public string[] Materials { get; } = { "AH36", "DH36", "EH36", "AH32", "DH32", "A", "B", "D" };

        // ═══ Default Parameters ═══

        private string _defaultTopPlateThk = "6";
        /// <summary>Default top plate thickness (mm)</summary>
        public string DefaultTopPlateThk
        {
            get => _defaultTopPlateThk;
            set { _defaultTopPlateThk = value; OnPropertyChanged(nameof(DefaultTopPlateThk)); }
        }

        private string _defaultFlangeThk = "20";
        /// <summary>Default flange thickness (mm)</summary>
        public string DefaultFlangeThk
        {
            get => _defaultFlangeThk;
            set { _defaultFlangeThk = value; OnPropertyChanged(nameof(DefaultFlangeThk)); }
        }

        private string _defaultStiffProfile = "HP120x6";
        /// <summary>Default stiffener profile</summary>
        public string DefaultStiffProfile
        {
            get => _defaultStiffProfile;
            set { _defaultStiffProfile = value; OnPropertyChanged(nameof(DefaultStiffProfile)); }
        }

        private string _defaultBSProfile = "FB80x6";
        /// <summary>Default BS profile</summary>
        public string DefaultBSProfile
        {
            get => _defaultBSProfile;
            set { _defaultBSProfile = value; OnPropertyChanged(nameof(DefaultBSProfile)); }
        }

        /// <summary>Profile options cho dropdown</summary>
        public List<string> ProfileOptions => _profileOptions;

        // ═══ Display Unit ═══

        // ═══ Outer throw direction (user-controlled radio) ═══

        private bool _outerStiffOutward = true;
        /// <summary>Outer stiffener/BS direction: true=OUTWARD (default), false=INWARD</summary>
        public bool OuterStiffOutward
        {
            get => _outerStiffOutward;
            set
            {
                if (_outerStiffOutward == value) return;
                _outerStiffOutward = value;
                BaseEdgeEngine.OuterStiffOutward = value;
                OnPropertyChanged(nameof(OuterStiffOutward));
                OnPropertyChanged(nameof(OuterStiffInward));
                // Targeted flip — CHỈ outer stiffener, part khác giữ nguyên
                if (_classifiedElements != null)
                {
                    BaseEdgeEngine.FlipOuterStiffThrow(_classifiedElements);
                    if (_debugSymbolsShown) DebugSymbolService.Refresh(_classifiedElements, _currentPanel);
                    StatusText = $"Outer stiffener: {(value ? "OUTWARD" : "INWARD")}";
                }
            }
        }
        /// <summary>Bind vào RadioButton "Inward" (auto-sync với OuterStiffOutward)</summary>
        public bool OuterStiffInward
        {
            get => !_outerStiffOutward;
            set { if (value) OuterStiffOutward = false; }
        }

        private bool _outerWebOutward = false;
        /// <summary>Outer web/beam direction: false=INWARD (default), true=OUTWARD</summary>
        public bool OuterWebOutward
        {
            get => _outerWebOutward;
            set
            {
                if (_outerWebOutward == value) return;
                _outerWebOutward = value;
                BaseEdgeEngine.OuterWebOutward = value;
                OnPropertyChanged(nameof(OuterWebOutward));
                OnPropertyChanged(nameof(OuterWebInward));
                // Targeted flip — CHỈ outer web, part khác giữ nguyên
                if (_classifiedElements != null)
                {
                    BaseEdgeEngine.FlipOuterWebThrow(_classifiedElements);
                    if (_debugSymbolsShown) DebugSymbolService.Refresh(_classifiedElements, _currentPanel);
                    StatusText = $"Outer web: {(value ? "OUTWARD" : "INWARD")}";
                }
            }
        }
        /// <summary>Bind vào RadioButton "Inward" (auto-sync với OuterWebOutward)</summary>
        public bool OuterWebInward
        {
            get => !_outerWebOutward;
            set { if (value) OuterWebOutward = false; }
        }

        private string _displayUnit = "m";
        /// <summary>Display unit: mm hoặc m</summary>
        public string DisplayUnit
        {
            get => _displayUnit;
            set
            {
                _displayUnit = value;
                OnPropertyChanged(nameof(DisplayUnit));
                // KHÔNG rebuild tree — tránh collapse trạng thái expand của user.
                // Properties Panel tự refresh unit qua SetUnit() (wired trong DetailDesignView).
                // Tree ValueText hardcode mm nên không ảnh hưởng.
            }
        }

        /// <summary>Danh sách units</summary>
        public string[] UnitOptions { get; } = { "mm", "m" };

        /// <summary>Tree nodes cho StructureTreeView</summary>
        public ObservableCollection<ElementNodeViewModel> TreeNodes { get; set; }
            = new ObservableCollection<ElementNodeViewModel>();

        /// <summary>Plannar groups — DataGrid binding dưới tree.</summary>
        public ObservableCollection<PlannarGroup> PlannarGroups { get; set; }
            = new ObservableCollection<PlannarGroup>();

        #endregion

        public DetailDesignViewModel()
        {
            Debug.WriteLine($"{LOG_PREFIX} Initializing...");
            _repository = new DetailDesignRepository();
            _repository.InitializeDatabase();
            _scanService = new PanelScanService();

            // Load profile options
            var profiles = _repository.GetAllProfiles();
            _profileOptions = profiles.Select(p => p.Code).ToList();
            _profileOptions.Insert(0, "?");

            Debug.WriteLine($"{LOG_PREFIX} Initialized — DB ready, {_profileOptions.Count} profiles loaded.");
        }

        #region Public Methods

        /// <summary>
        /// Gọi từ View code-behind khi click SELECT PANEL.
        /// Chạy trên AutoCAD document context.
        /// </summary>
        private bool _debugSymbolsShown = false;
        /// <summary>True nếu debug symbols đang hiện trên DWG (dùng cho toggle button state).</summary>
        public bool DebugSymbolsShown
        {
            get => _debugSymbolsShown;
            set { _debugSymbolsShown = value; OnPropertyChanged(nameof(DebugSymbolsShown)); }
        }

        /// <summary>
        /// Toggle debug symbols: click 1 → insert, click 2 → erase.
        /// Trả về trạng thái mới (true = shown).
        /// </summary>
        public bool ExecuteToggleDebugSymbols()
        {
            Debug.WriteLine($"{LOG_PREFIX} ExecuteToggleDebugSymbols — current: shown={_debugSymbolsShown}");

            if (_debugSymbolsShown)
            {
                int erased = DebugSymbolService.EraseAll();
                DebugSymbolsShown = false;
                StatusText = $"Debug symbols hidden ({erased} erased).";
                return false;
            }

            if (_currentPanel == null || _classifiedElements == null)
            {
                StatusText = "Chưa có scan data — SELECT PANEL trước.";
                return false;
            }
            DebugSymbolService.Refresh(_classifiedElements, _currentPanel);
            DebugSymbolsShown = true;
            StatusText = "Debug symbols shown.";
            return true;
        }

        /// <summary>
        /// Flip throw direction của 1 element riêng lẻ (từ tree Flip button hoặc code khác).
        /// </summary>
        public void FlipElement(string guid)
        {
            if (string.IsNullOrEmpty(guid) || _classifiedElements == null) return;
            var elem = _classifiedElements.FirstOrDefault(e => e.Guid == guid);
            if (elem == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} FlipElement: GUID {guid} not found.");
                return;
            }
            BaseEdgeEngine.FlipSingleElementThrow(elem);
            Debug.WriteLine($"{LOG_PREFIX} FlipElement: {guid.Substring(0,8)} {elem.ElemType} throw=({elem.ThrowVecX:F2},{elem.ThrowVecY:F2})");

            // Update node.ThrowText để PropertiesPanel refresh
            var node = FindTreeNodeByGuid(guid);
            if (node != null)
                node.ThrowText = $"→({elem.ThrowVecX:F1},{elem.ThrowVecY:F1})";
            FlipCompleted?.Invoke(guid);

            if (_debugSymbolsShown)
                DebugSymbolService.Refresh(_classifiedElements, _currentPanel);
            StatusText = $"Flipped: {elem.ElemType} {guid.Substring(0,8)}";
        }

        /// <summary>Event triggered sau khi flip 1 element — View listen để refresh PropsPanel.</summary>
        public event System.Action<string> FlipCompleted;

        /// <summary>Lookup StructuralElementModel theo GUID (dùng bởi iProperties command).</summary>
        public StructuralElementModel GetElementByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid) || _classifiedElements == null) return null;
            return _classifiedElements.FirstOrDefault(e => e.Guid == guid);
        }

        private ElementNodeViewModel FindTreeNodeByGuid(string guid)
        {
            foreach (var root in TreeNodes)
            {
                var found = FindNodeRecursive(root, guid);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Event — View listen để scroll tree node vào view.</summary>
        public event System.Action<string> TreeNodeFocused;

        /// <summary>
        /// Highlight node trong tree theo GUID — gọi khi user pick block trong DWG.
        /// Expand parents + set IsSelected + fire TreeNodeFocused để View scroll.
        /// </summary>
        public void SelectTreeNodeByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            ElementNodeViewModel found = null;
            foreach (var root in TreeNodes)
            {
                found = FindNodeRecursive(root, guid);
                if (found != null) break;
            }
            if (found == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} SelectTreeNodeByGuid: {guid} not in tree.");
                return;
            }
            // Clear previous selection
            foreach (var root in TreeNodes) ClearSelectionRecursive(root);
            // Expand parents + select
            ExpandPathRecursive(TreeNodes, found, new System.Collections.Generic.List<ElementNodeViewModel>());
            found.IsSelected = true;
            Debug.WriteLine($"{LOG_PREFIX} Selected tree node: {found.DisplayName} ({guid.Substring(0,8)})");
            TreeNodeFocused?.Invoke(guid);
        }

        /// <summary>
        /// Tree → DWG: zoom + select block ThrowThickness tương ứng GUID.
        /// </summary>
        public void FindInDrawing(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (Autodesk.AutoCAD.DatabaseServices.BlockTable)
                        tr.GetObject(db.BlockTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                    var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)
                        tr.GetObject(bt[Autodesk.AutoCAD.DatabaseServices.BlockTableRecord.ModelSpace],
                                     Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);

                    Autodesk.AutoCAD.DatabaseServices.ObjectId foundId
                        = Autodesk.AutoCAD.DatabaseServices.ObjectId.Null;
                    Autodesk.AutoCAD.Geometry.Point3d pos = default;

                    foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)
                    {
                        var br = tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead)
                                 as Autodesk.AutoCAD.DatabaseServices.BlockReference;
                        if (br == null) continue;
                        var rb = br.GetXDataForApplication(DebugSymbolService.DEBUG_XDATA_APP);
                        if (rb == null) continue;
                        string xGuid = null;
                        foreach (var tv in rb.AsArray())
                        {
                            if (tv.TypeCode == 1000 && tv.Value is string s
                                && s.Length >= 32 && s.Contains("-"))
                            { xGuid = s; break; }
                        }
                        rb.Dispose();
                        if (xGuid == guid) { foundId = id; pos = br.Position; break; }
                    }
                    tr.Commit();

                    if (foundId.IsNull)
                    {
                        StatusText = "Block not found in drawing (show Debug trước).";
                        Debug.WriteLine($"{LOG_PREFIX} FindInDrawing: no block for {guid.Substring(0,8)}");
                        return;
                    }

                    // Zoom + select pickfirst
                    var ed = doc.Editor;
                    ed.SetImpliedSelection(new[] { foundId });
                    var view = ed.GetCurrentView();
                    view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(pos.X, pos.Y);
                    if (view.Width < 2000) view.Width = 2000;
                    if (view.Height < 2000) view.Height = 2000;
                    ed.SetCurrentView(view);
                    ed.Regen();
                    Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                    StatusText = $"Zoomed to {guid.Substring(0,8)}";
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} FindInDrawing ERROR: {ex.Message}");
            }
        }

        /// <summary>Save current scan data xuống SQLite DB (panel + elements).</summary>
        public void ExecuteSaveToDb()
        {
            if (_currentPanel == null || _classifiedElements == null)
            {
                StatusText = "No scan data — SELECT PANEL trước.";
                return;
            }
            try
            {
                string finalGuid = _repository.UpsertPanel(_currentPanel);
                int n = 0;
                foreach (var elem in _classifiedElements)
                {
                    if (elem.IsHole) continue;
                    elem.PanelGuid = finalGuid;
                    _repository.UpsertElement(elem);
                    n++;
                }
                StatusText = $"Saved: 1 panel + {n} elements to DB.";
                Debug.WriteLine($"{LOG_PREFIX} Saved {n} elements + panel {_currentPanel.Guid}");
            }
            catch (System.Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                Debug.WriteLine($"{LOG_PREFIX} Save ERROR: {ex.Message}");
            }
        }

        private ElementNodeViewModel FindNodeRecursive(ElementNodeViewModel node, string guid)
        {
            if (node.ElementGuid == guid) return node;
            foreach (var ch in node.Children)
            {
                var f = FindNodeRecursive(ch, guid);
                if (f != null) return f;
            }
            return null;
        }

        private void ClearSelectionRecursive(ElementNodeViewModel node)
        {
            node.IsSelected = false;
            foreach (var ch in node.Children) ClearSelectionRecursive(ch);
        }

        private bool ExpandPathRecursive(System.Collections.ObjectModel.ObservableCollection<ElementNodeViewModel> nodes,
            ElementNodeViewModel target, System.Collections.Generic.List<ElementNodeViewModel> path)
        {
            foreach (var n in nodes)
            {
                if (n == target)
                {
                    foreach (var p in path) p.IsExpanded = true;
                    return true;
                }
                path.Add(n);
                if (ExpandPathRecursive(
                    new System.Collections.ObjectModel.ObservableCollection<ElementNodeViewModel>(n.Children),
                    target, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        public void ExecuteSelectPanel()
        {
            Debug.WriteLine($"{LOG_PREFIX} ExecuteSelectPanel...");
            StatusText = "Selecting panel...";

            var panel = _scanService.SelectPanel();
            if (panel == null)
            {
                StatusText = "Selection cancelled.";
                Debug.WriteLine($"{LOG_PREFIX} Selection cancelled or failed.");
                return;
            }

            _currentPanel = panel;
            PanelName = panel.Name;
            PanelSideText = panel.Side.ToString() + (panel.SideAutoDetected ? " (auto)" : "");
            StatusText = $"Panel: {panel.Name} | Scanning...";

            Debug.WriteLine($"{LOG_PREFIX} Panel selected: {panel.Name} | Side: {panel.Side} — auto-scan");

            // Chain Scan tự động sau khi select thành công
            ExecuteScan();
        }

        /// <summary>Lấy danh sách profile options cho dropdown</summary>
        public System.Collections.Generic.List<string> GetProfileOptions()
        {
            if (_profileOptions.Count == 0)
            {
                var profiles = _repository.GetAllProfiles();
                _profileOptions = profiles.Select(p => p.Code).ToList();
                _profileOptions.Insert(0, "?");
            }
            return _profileOptions;
        }

        /// <summary>
        /// Gọi từ View code-behind khi click SCAN.
        /// Chạy trên AutoCAD document context.
        /// </summary>
        public void ExecuteScan()
        {
            Debug.WriteLine($"{LOG_PREFIX} ExecuteScan...");

            if (_currentPanel == null)
            {
                StatusText = "No panel selected. Click SELECT PANEL first.";
                Debug.WriteLine($"{LOG_PREFIX} No panel selected.");
                return;
            }

            StatusText = "Scanning...";

            // Pass defaults + building height vào panel context
            double.TryParse(_defaultTopPlateThk, out double tpThk);
            double.TryParse(_defaultFlangeThk, out double flThk);
            double.TryParse(_webHeight, out double buildingH);
            _currentPanel.WebHeight = buildingH > 0 ? buildingH : (double?)null;
            _currentPanel.DefaultTopPlateThk = tpThk > 0 ? tpThk : 6;
            _currentPanel.DefaultFlangeThk = flThk > 0 ? flThk : 20;
            _currentPanel.DefaultStiffProfile = _defaultStiffProfile;
            _currentPanel.DefaultBSProfile = _defaultBSProfile;

            var elements = _scanService.ScanPanel(_currentPanel);
            if (elements == null)
            {
                StatusText = "Scan failed.";
                return;
            }

            _classifiedElements = elements;

            // Lấy bracket + stiffener models từ scan service
            var scanSvc = _scanService as PanelScanService;
            _bracketModels = scanSvc?.LastBracketModels ?? new List<BracketModel>();
            _stiffenerModels = scanSvc?.LastStiffenerModels ?? new List<StiffenerModel>();

            // Sync BracketSubType từ BracketAnalyzer → StructuralElementModel
            // (để DebugSymbolService skip insert ThrowThickness cho SubType="B")
            foreach (var bm in _bracketModels)
            {
                var elem = _classifiedElements.FirstOrDefault(e => e.Guid == bm.ElemGuid);
                if (elem != null) elem.BracketSubType = bm.SubType;
            }

            // Load profile options từ DB
            if (_profileOptions.Count == 0)
            {
                var profiles = _repository.GetAllProfiles();
                _profileOptions = profiles.Select(p => p.Code).ToList();
                _profileOptions.Insert(0, "?");
            }

            // Build tree
            BuildTree(elements);

            int visibleCount = elements.Count(e => !e.IsHole);
            int holeCount = elements.Count(e => e.IsHole);
            StatusText = $"Scanned: {visibleCount} elements ({holeCount} holes)";

            // Compute plannar groups
            PlannarGroups.Clear();
            var groups = PlannarGroupService.ComputeGroups(_classifiedElements);
            foreach (var g in groups) PlannarGroups.Add(g);
            OnPropertyChanged(nameof(PlannarGroups));
            Debug.WriteLine($"{LOG_PREFIX} Scan complete — {visibleCount} elements + {holeCount} holes, tree built.");
        }

        #endregion

        #region Private Helpers

        /// <summary>Build tree hierarchy từ classified elements</summary>
        private void BuildTree(List<StructuralElementModel> elements)
        {
            TreeNodes.Clear();

            var root = new ElementNodeViewModel
            {
                DisplayName = $"{_currentPanel.Name} [{_currentPanel.Side}] — Total: {elements.Count(e => !e.IsHole)}",
                Icon = "▼",
                IsExpanded = true
            };

            // Group theo type — brackets nhóm riêng theo SubType
            var nonBrackets = elements.Where(e => e.ElemType != StructuralType.Bracket && !e.IsHole);
            var bracketElems = elements.Where(e => e.ElemType == StructuralType.Bracket);

            // Non-bracket groups
            var groups = nonBrackets.GroupBy(e => e.ElemType).OrderBy(g => g.Key);
            foreach (var group in groups)
            {
                var groupNode = new ElementNodeViewModel
                {
                    DisplayName = $"{group.Key} ({group.Count()})",
                    Icon = "▶",
                    IsExpanded = false
                };

                int index = 1;
                foreach (var elem in group)
                {
                    var node = CreateLeafNode(elem, elem.ElemType, index);
                    groupNode.Children.Add(node);
                    index++;
                }

                root.Children.Add(groupNode);
            }

            // Bracket group — sub-folders OB/IB/BF/B
            if (bracketElems.Any())
            {
                var bracketGroupNode = new ElementNodeViewModel
                {
                    DisplayName = $"Brackets ({bracketElems.Count()})",
                    Icon = "▶",
                    IsExpanded = false
                };

                var subTypeOrder = new[] { "OB", "IB", "BF", "B" };
                foreach (var subType in subTypeOrder)
                {
                    var matching = bracketElems.Where(b =>
                    {
                        var bm = _bracketModels.FirstOrDefault(m => m.ElemGuid == b.Guid);
                        return (bm?.SubType ?? "B") == subType;
                    }).ToList();

                    if (matching.Count == 0) continue;

                    var subNode = new ElementNodeViewModel
                    {
                        DisplayName = $"{subType} ({matching.Count})",
                        Icon = "▶",
                        IsExpanded = false
                    };

                    int idx = 1;
                    foreach (var elem in matching)
                    {
                        var node = CreateLeafNode(elem, StructuralType.Bracket, idx);
                        node.DisplayName = $"{subType}-{idx:D2}";
                        node.BracketSubType = subType;
                        node.ThrowText = ""; // không hiện sub-type trên tree, đã có trong tên
                        subNode.Children.Add(node);
                        idx++;
                    }

                    bracketGroupNode.Children.Add(subNode);
                }

                root.Children.Add(bracketGroupNode);
            }

            TreeNodes.Add(root);
            Debug.WriteLine($"{LOG_PREFIX} Tree built — {root.Children.Count} groups.");
        }

        /// <summary>Format length với unit hiện tại</summary>
        private string FormatLength(double mm)
        {
            if (_displayUnit == "m")
                return $"{mm / 1000.0:F2}m";
            return $"{mm:F0}mm";
        }

        /// <summary>Format area với unit hiện tại</summary>
        private string FormatArea(double mm2)
        {
            if (_displayUnit == "m")
                return $"{mm2 / 1_000_000.0:F2}m²";
            return $"{mm2:F0}mm²";
        }

        /// <summary>Format thickness (luôn mm vì quá nhỏ)</summary>
        private string FormatThickness(double mm)
        {
            return $"{mm:F0}mm";
        }

        /// <summary>Tạo leaf node — text-only, data cho Properties Panel</summary>
        private ElementNodeViewModel CreateLeafNode(StructuralElementModel elem, StructuralType type, int index)
        {
            bool flippable = type == StructuralType.WebPlate
                          || type == StructuralType.ClosingBoxWeb
                          || type == StructuralType.Stiffener
                          || type == StructuralType.BucklingStiffener
                          || type == StructuralType.Bracket
                          || type == StructuralType.Flange;

            var node = new ElementNodeViewModel
            {
                DisplayName = GetElementLabel(type, index),
                Icon = type == StructuralType.Ambiguous ? "⚠" : "○",
                ElementGuid = elem.Guid,
                AcadHandle = elem.AcadHandle,
                StatusIcon = elem.Status == ElementStatus.Complete ? "✓" : "?",
                CentroidX = elem.CentroidX,
                CentroidY = elem.CentroidY,
                ObbWidth = elem.ObbWidth,
                ObbLength = elem.ObbLength,
                ElemTypeStr = type.ToString(),
                LayerName = elem.Layer,
                IsFlippable = flippable
            };

            // Populate thickness + area + length/height chung
            node.Thickness = elem.Thickness;
            node.Area = elem.AreaPoly;
            node.Length = elem.ObbLength;

            // Height cho Web/Bracket = webPlateHeight (từ building - tp - fl)
            double buildingH = _currentPanel?.WebHeight ?? 0;
            double tpThk = _currentPanel?.DefaultTopPlateThk ?? 6;
            double flThk = _currentPanel?.DefaultFlangeThk ?? 20;
            double webHeight = buildingH > 0 ? (buildingH - tpThk - flThk) : 0;

            if (type == StructuralType.WebPlate || type == StructuralType.Bracket ||
                type == StructuralType.ClosingBoxWeb)
            {
                if (webHeight > 0) node.Height = webHeight;
            }

            // Plate elements — ValueText = thickness (rounded)
            if (type == StructuralType.WebPlate || type == StructuralType.Bracket ||
                type == StructuralType.ClosingBoxWeb)
            {
                node.IsPlate = true;
                double t = elem.Thickness ?? 0;
                node.Thickness = t > 0.1 ? t : (double?)null;
                node.ValueText = t > 0.1 ? $"t:{t:F0}mm" : "t:?";
            }
            else if (type == StructuralType.TopPlateRegion)
            {
                node.IsPlate = true;
                // TopPlate: nếu service chưa tính được → fallback về default từ palette
                double t = elem.Thickness ?? 0;
                if (t <= 0.1) t = tpThk; // default từ palette
                node.Thickness = t;
                node.ValueText = $"t:{t:F0}mm";
            }
            else if (type == StructuralType.DoublingPlate)
            {
                node.IsPlate = true;
                node.ValueText = "t:?"; // user nhập tay
            }
            else if (type == StructuralType.Flange)
            {
                node.IsPlate = true;
                // Flange: thickness = default từ palette (không đo được từ polyline)
                //         width = midpoint method (4 segments), hiện ở Properties Panel
                node.Thickness = flThk;
                node.ValueText = $"t:{flThk:F0}mm";

                double fw = ThicknessCalculator.CalculateFlangeWidth(elem.VerticesWCS);
                if (fw > 0)
                {
                    node.FlangeWidthText = FormatLength(fw);
                }
                else
                {
                    node.FlangeWidthText = "Other (>4 segments)";
                }
            }

            // Profile elements — ValueText = default profile
            if (type == StructuralType.Stiffener)
            {
                node.IsProfile = true;
                node.ValueText = _currentPanel?.DefaultStiffProfile ?? "HP120x6";

                var sm = _stiffenerModels.FirstOrDefault(s => s.ElemGuid == elem.Guid);
                if (sm != null)
                    node.ThrowText = $"→({sm.ThrowVecX:F1},{sm.ThrowVecY:F1})";
            }
            else if (type == StructuralType.BucklingStiffener)
            {
                node.IsProfile = true;
                node.ValueText = _currentPanel?.DefaultBSProfile ?? "FB80x6";

                var sm = _stiffenerModels.FirstOrDefault(s => s.ElemGuid == elem.Guid);
                if (sm != null)
                    node.ThrowText = $"→({sm.ThrowVecX:F1},{sm.ThrowVecY:F1})";
            }

            return node;
        }

        /// <summary>Tạo label cho element node</summary>
        private static string GetElementLabel(StructuralType type, int index)
        {
            switch (type)
            {
                case StructuralType.TopPlateRegion: return $"Region {index}";
                case StructuralType.WebPlate: return $"WEB-{index:D2}";
                case StructuralType.Flange: return $"FL-{index:D2}";
                case StructuralType.Stiffener: return $"S{index:D2}";
                case StructuralType.BucklingStiffener: return $"BS{index:D2}";
                case StructuralType.Bracket: return $"BR-{index:D2}";
                case StructuralType.DoublingPlate: return $"DP-{index:D2}";
                case StructuralType.ClosingBoxWeb: return $"CB-{index:D2}";
                case StructuralType.Ambiguous: return $"AMB-{index:D2}";
                default: return $"EL-{index:D2}";
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
