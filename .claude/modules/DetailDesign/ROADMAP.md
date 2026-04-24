# ROADMAP.md — DetailDesign Implementation Roadmap

> File này theo dõi tiến độ chi tiết từng step.
> Claude Code đọc file này mỗi khi bắt đầu 1 step mới.
> Cập nhật: đánh ✅ sau khi step PASS verify hoàn toàn.
> Test DWG: `C:\CustomTools\Test Detail Design\CAS-0051566.dwg`

---

## Trạng thái Tổng quan

```
PRE  ✅  STEP 0  — Cleanup placeholders
M1   ✅  STEP 1  — Constants + Logger + Validator
     ✅  STEP 2  — Models (13 files, build-only)
     ✅  STEP 3  — SQLite schema + UI skeleton
─────────────────────────────────────────────────
M2   ✅  STEP 4  — Block traversal + Panel name parser
     ✅  STEP 5  — WCS Transform + Entity collection
     ✅  STEP 6  — Primary classification + OBB
     ✅  STEP 7  — XData write + Geometry hash
     ✅  STEP 8  — Topology + Full tree UI         ← MILESTONE 2
─────────────────────────────────────────────────
M3   ✅  STEP 9  — Throw vector + OB/IB
     ░░  STEP 10 — Thickness + Missing data UI
     ░░  STEP 11 — Profile assignment + Apply All
     ░░  STEP 12 — BOM + CSV export               ← MILESTONE 3
─────────────────────────────────────────────────
M4   ░░  STEP 13 — Block library + Section marker
     ░░  STEP 14 — Intersection engine + Section generator
     ░░  STEP 15 — Dimension engine
     ░░  STEP 16 — Bracket detail + Rescan engine  ← MILESTONE 4
```

---

## PRE-STEP 0 — Cleanup Placeholder Files

**Status:** `✅ DONE (2026-04-13)`

### Mục tiêu
Xóa/rename placeholder files từ session scaffold cũ không khớp architecture mới.

### Việc cần làm
```bash
# Liệt kê placeholder files cần xử lý:
find . -path "*/DetailDesign*" -name "*.cs" \
  | grep -v "/bin/" | grep -v "/obj/"
```

Files cần xóa (placeholder sai structure):
```
Commands/DetailDesign/DetailDesignCommand.cs   → XÓA (tạo lại đúng ở Step 1)
Services/DetailDesign/DetailDesignService.cs   → XÓA (không có file này)
Views/DetailDesign/DetailDesignView.cs         → XÓA (phải là .xaml, tạo Step 3)
Models/DetailDesign/DetailDesignModel.cs       → XÓA (không có file này)
Utilities/DetailDesign/DetailDesignUtility.cs  → XÓA (không có file này)
```

Files cần GIỮ:
```
Models/DetailDesign/Enums/*.cs     ✅ (6 enum files đã tạo đúng)
Services/DetailDesign/Data/SchemaInitializer.cs  ✅
```

### Build Check
```bash
dotnet build -c Debug
```
Sau cleanup, build phải PASS (không có lỗi CS từ placeholder).

### Verify Criteria
```
✓ Build PASS không có lỗi
✓ find DetailDesign -name "*.cs" chỉ còn Enums/ và SchemaInitializer.cs
✓ Không còn file nào chứa "// TODO: placeholder"
```

---

## STEP 1 — Constants + Logger + Validator

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Utilities/DetailDesign/DetailDesignConstants.cs
Utilities/DetailDesign/LogHelper.cs
Utilities/DetailDesign/DrawingUnitsValidator.cs
```

### Nội dung chính
- `DetailDesignConstants.cs`: tất cả constants từ mục 3 của `DetailDesign.md`
- `LogHelper.cs`: ghi file log `C:\CustomTools\Temp\MCG_{task}_{panel}_{ts}.log`
- `DrawingUnitsValidator.cs`: kiểm tra `INSUNITS == Millimeters`, show error tiếng Việt

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. Build → lấy DLL timestamp mới
2. AutoCAD: NETLOAD → browse DLL
3. Gõ: MCG_DetailDesign
```

### Verify Criteria
```
✅ Build PASS (0 errors)
✅ 3 files tạo đúng vị trí + namespace MCGCadPlugin.Utilities.DetailDesign
⬜ Palette hiện ra / Tab active / DebugView → chuyển sang Step 3 (cần View + Command)
```

---

## STEP 2 — Models (13 files, build-only)

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Models/DetailDesign/Point2dModel.cs
Models/DetailDesign/PanelContext.cs
Models/DetailDesign/StructuralElementModel.cs
Models/DetailDesign/OBBResult.cs
Models/DetailDesign/XDataPayload.cs
Models/DetailDesign/GirderModel.cs
Models/DetailDesign/StiffenerModel.cs
Models/DetailDesign/BracketModel.cs
Models/DetailDesign/DoublingPlateModel.cs
Models/DetailDesign/TopPlateRegionModel.cs
Models/DetailDesign/ClosingBoxModel.cs
Models/DetailDesign/SectionModel.cs
Models/DetailDesign/BomItemModel.cs
Models/DetailDesign/ProfileModel.cs
```

### Rule bắt buộc
```
KHÔNG có using Autodesk.* trong bất kỳ file nào
Point2dModel thay thế Autodesk.AutoCAD.Geometry.Point2d
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### Verify Criteria (KHÔNG cần NETLOAD)
```bash
# Chạy lệnh này — kết quả phải RỖNG:
grep -r "using Autodesk" Models/DetailDesign/

# Kết quả mong đợi: không có output nào
✓ grep không tìm thấy Autodesk.* trong Models
✓ Build PASS
✓ 14 files (13 mới + Enums đã có)
```

---

## STEP 3 — SQLite Schema + UI Skeleton

**Status:** `✅ DONE (2026-04-13)`

### Files tạo/sửa
```
Services/DetailDesign/Data/IDetailDesignRepository.cs
Services/DetailDesign/Data/DetailDesignRepository.cs
Services/DetailDesign/Data/ProfileCatalogSeeder.cs  ← seed EN10067 profiles
Views/DetailDesign/ViewModels/DetailDesignViewModel.cs
Views/DetailDesign/DetailDesignView.xaml             ← UI skeleton
Views/DetailDesign/DetailDesignView.xaml.cs
```

### UI Layout tối thiểu
```
┌─────────────────────────────────────────────┐
│  ● Block Mode   ○ Entity Mode               │
│  [SELECT PANEL]                             │
│  Panel: (chưa chọn)  Side: —               │
│  Web Height: [___] mm   Material: [AH36 ▼] │
│  ─────────────────────────────────────────  │
│  (TreeView area — empty placeholder)        │
│  ─────────────────────────────────────────  │
│  [SCAN] [RESCAN]          Status: Ready     │
└─────────────────────────────────────────────┘
```

### Profile Catalog Seed (EN10067 defaults)
```
HP  : 100x6, 120x7, 140x7, 160x8, 180x9, 200x10
FB  : 75x8, 100x8, 120x10, 150x10, 150x12, 200x12
Angle: L65x65x8, L75x75x8, L90x90x9
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. NETLOAD DLL mới
2. Gõ MCG_DetailDesign
```

### Verify Criteria
```
✓ UI skeleton hiện trong tab Detail Design
✓ [SELECT PANEL] click → AutoCAD nhận focus (cursor đổi)
✓ File DB tạo ra: C:\CustomTools\MCGPanelTool.db
✓ Mở DB bằng DB Browser for SQLite:
    - Tất cả tables tồn tại (22 tables)
    - Bảng profiles: có 16+ rows (HP + FB + Angle)
✓ DebugView:
    "[SchemaInitializer] Schema initialization COMPLETE"
    "[ProfileCatalogSeeder] Seeded 16 profiles"
```

---

## STEP 4 — Block Traversal + Panel Name Parser

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Collection/RawEntitySet.cs
Services/DetailDesign/Collection/IEntityCollector.cs
Services/DetailDesign/Classification/SubBlockClassifier.cs
Services/DetailDesign/Parameters/PanelNameParser.cs
Services/DetailDesign/IPanelScanService.cs
Services/DetailDesign/PanelScanService.cs  ← entry point cho scan
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. Mở DWG: C:\CustomTools\Test Detail Design\CAS-0051566.dwg
2. NETLOAD DLL mới
3. Gõ MCG_DetailDesign
4. Click [SELECT PANEL] → click vào Assy block trong DWG
```

### Verify Criteria
```
✓ Chỉ click được block (filter hoạt động, không click được line/polyline)
✓ Status bar hiện: "Panel: [tên panel] | Side: [P/S/C] (auto)"
✓ DebugView:
    "[SubBlockClassifier] AssyRoot: T.XXXXC_Assy"
    "[SubBlockClassifier] Found sub-blocks: TopPlate, Structure, Corner"
    "[PanelNameParser] Name=T.XXXXC | Side=CENTER | AutoDetected=true"
✓ Sai entity (không phải Assy block) → không làm gì / thông báo lỗi nhẹ
```

**Câu hỏi verify:** Tên panel và side detect có đúng với DWG thực tế không?

---

## STEP 5 — WCS Transform + Entity Collection

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Geometry/WCSTransformer.cs
Services/DetailDesign/Collection/BlockEntityCollector.cs
Services/DetailDesign/Collection/DirectEntityCollector.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. Mở DWG test
2. SELECT PANEL → click Assy block
3. Click [SCAN]
```

### Verify Criteria
```
✓ Status: "Collected: X entities" (đếm manual trong DWG để verify X)
✓ DebugView liệt kê đúng count per layer:
    "[BlockEntityCollector] AM_0: X | AM_3: Y | AM_5: Z | Layer0: 1"
✓ Mirror check: DebugView KHÔNG có "WARNING: mirror transform"
  (hoặc nếu có → xử lý đúng, tọa độ không bị flip)
✓ Entity Mode: chọn top plate polyline → collect đúng số entities
```

**Câu hỏi verify:** Tổng số entities có khớp với số polylines trong DWG không? (dùng lệnh QSELECT trong AutoCAD để đếm)

---

## STEP 6 — Primary Classification + OBB Calculator

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Geometry/ConvexHullHelper.cs
Services/DetailDesign/Geometry/OBBCalculator.cs
Services/DetailDesign/Classification/IPrimaryClassifier.cs
Services/DetailDesign/Classification/PrimaryClassifier.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
SCAN panel → xem kết quả classify trong tree (nhóm đơn giản)
```

### Verify Criteria
```
✓ Tree hiện groups:
    TopPlateRegion (1)
    WebPlate (N — verify với DWG)
    Flange (N)
    Stiffener (N)
    BucklingStiffener (N)
    DoublingPlate (N)
    Ambiguous (0 hoặc nhỏ)
    AM0_Unclassified (N — chờ topology phase)
✓ DebugView: "[PrimaryClassifier] Stiffener:12, WebPlate:3, Flange:6..."
✓ Ambiguous list: click để highlight entity trong DWG → inspect
✓ Không có Unknown (nếu có → flag + log lý do)
```

**Câu hỏi verify:** Click vào từng Ambiguous entity trong DWG. Có hợp lý không (aspect ratio 3-5 là đúng)?

---

## STEP 7 — XData Write + Geometry Hash

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Geometry/GeometryHasher.cs
Services/DetailDesign/XData/IXDataManager.cs
Services/DetailDesign/XData/XDataManager.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. SCAN panel
2. Click vào 1 entity trong DWG (web plate)
3. Mở Properties palette: Ctrl+1
```

### Verify Criteria
```
✓ Ctrl+1 → Properties palette → có section "MCG_PANEL_TOOL" trong XData
✓ Status bar update khi click entity:
    "Selected: WebPlate | T:? | PENDING"
✓ DebugView:
    "[XDataManager] Write: guid=a3f2... type=WebPlate"
    "[XDataManager] Read: guid=a3f2... status=PENDING"
✓ DB Browser → bảng structural_elements:
    - Có records với guid đúng
    - geometry_hash không rỗng
✓ RESCAN sau khi không thay đổi gì:
    DebugView: "[RescanEngine] 0 dirty, 0 new" (hash match)
```

**Câu hỏi verify:** GUID trong XData (xem qua LIST command trong AutoCAD) có khớp với GUID trong SQLite không?

---

## STEP 8 — Topology Engine + Full Tree UI ✦ MILESTONE 2

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Classification/ITopologyEngine.cs
Services/DetailDesign/Classification/TopologyEngine.cs
Services/DetailDesign/Classification/ClosingBoxDetector.cs
Views/DetailDesign/ViewModels/ElementNodeViewModel.cs
Views/DetailDesign/Controls/StructureTreeView.xaml
Views/DetailDesign/Controls/StructureTreeView.xaml.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
SCAN panel → verify full tree
```

### Verify Criteria — MILESTONE 2 CHECKPOINT
```
✓ Tree hiện đúng hierarchy:
    ▼ [PanelName] [CENTER/PORT/STBD]
      ▼ Top Plate
        □ Region 1  [?]
      ▼ Structure
        ▼ Girders
          ▼ PG1 (Long/Trans)
            □ Web      [?]
            □ Flange T [?]
            □ Flange B [?]
        ▼ Stiffeners (N)
          □ S01 [?]
          □ ...
        ▼ Buckling Stiffeners (N)
          □ BS01 [?]
        ▼ Brackets (N)
          □ BR-01 [?]
        ▼ Doubling Plates (N)
        ▼ Closing Boxes (N)
      ▼ Ambiguous (N) [⚠️]

✓ Số lượng mỗi loại ĐÚNG (so với đếm manual trong DWG)
✓ Click node → entity highlight trong DWG
✓ Closing boxes ở đúng vị trí góc/support
✓ AM0_Unclassified = 0 (tất cả đã được phân loại)
```

**Câu hỏi verify (quan trọng):**
1. Mở DWG → đếm số brackets bằng tay → khớp với tree?
2. Click từng Closing Box node → entity đúng vị trí không?
3. Web-Flange pairing: mỗi girder có đủ 2 flanges không?

---

## STEP 9 — Throw Vector + OB/IB Classification

**Status:** `✅ DONE (2026-04-13)`

### Files tạo mới
```
Services/DetailDesign/Parameters/IThrowVectorEngine.cs
Services/DetailDesign/Parameters/ThrowVectorEngine.cs
Services/DetailDesign/Parameters/IBracketAnalyzer.cs
Services/DetailDesign/Parameters/BracketAnalyzer.cs  ← OB/IB only
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
SCAN → xem OB/IB labels trong tree
```

### Verify Criteria
```
✓ Brackets trong tree có label OB hoặc IB
✓ Edge stiffeners: throw ra ngoài (xa tâm panel)
✓ Panel CENTER: inner stiffeners throw về tâm (đối xứng)
✓ Spot check 3-4 brackets:
    Click bracket → highlight trong DWG
    → OB: throw thickness phía ngoài bracket
    → IB: throw thickness phía trong bracket
✓ DebugView: "[ThrowVectorEngine] BR-01: dot=0.85 → OB"
```

**Câu hỏi verify:** Chọn 2 bracket đối xứng qua girder — 1 phải là OB, 1 phải là IB. Đúng không?

---

## STEP 10 — Thickness Calculator + Missing Data UI

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Parameters/IThicknessCalculator.cs
Services/DetailDesign/Parameters/ThicknessCalculator.cs
Views/DetailDesign/Controls/PropertiesPanel.xaml
Views/DetailDesign/Controls/PropertiesPanel.xaml.cs
Views/DetailDesign/ViewModels/DetailDesignViewModel.cs  ← update
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
SCAN → click node trong tree → xem Properties panel
```

### Verify Criteria
```
✓ Click WebPlate node:
    Properties: Type=WebPlate | Thickness=[X.X]mm | Length=Xmm | ✓ COMPLETE
✓ Click DoublingPlate node:
    Properties: Thickness=[?] với YELLOW background → PENDING
✓ Click TopPlateRegion node:
    Properties: Thickness=[?] YELLOW → user nhập → Enter → ✓ COMPLETE
✓ Tree node icon đổi từ ? sang ✓ sau khi nhập đủ data
✓ Thickness tự tính cho WebPlate: đo bằng DIST trong AutoCAD → so sánh

Cross-check:
  Dùng AutoCAD DIST command đo chiều rộng web polyline
  So sánh với thickness tính toán → sai số < 0.5mm
```

**Câu hỏi verify:** Chọn 2 web plates có thickness khác nhau → tính đúng cả 2 không?

---

## STEP 11 — Profile Assignment + Apply All

**Status:** `░░ NOT STARTED`

### Files tạo/sửa
```
Views/DetailDesign/Controls/PropertiesPanel.xaml  ← thêm profile dropdown
Views/DetailDesign/ViewModels/DetailDesignViewModel.cs  ← profile logic
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
SCAN → click Stiffener node → assign profile
```

### Verify Criteria
```
✓ Dropdown hiện profiles từ catalog:
    HP: 100x6, 120x7, 140x7, 160x8, 180x9, 200x10
    FB: 75x8, 100x8, 120x10, 150x10...
✓ Chọn HP120x7 cho S01 → S01 hiện "HP120x7" trong tree
✓ [Apply All Same] → tất cả stiffener cùng OBB width → nhận HP120x7
✓ Stiffener khác width → KHÔNG bị apply (verify bằng cách check tree)
✓ SQLite: bảng stiffeners → profile_guid đã có giá trị
✓ DebugView: "[ProfileAssignment] Applied HP120x7 to 12/14 stiffeners"
    (2 stiffener khác width không bị apply)
```

**Câu hỏi verify:** Sau Apply All, có stiffener nào bị assign sai profile không? Click từng stiffener trong DWG để kiểm tra.

---

## STEP 12 — BOM Calculator + CSV Export ✦ MILESTONE 3

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Bom/IBomCalculator.cs
Services/DetailDesign/Bom/BomCalculator.cs
Services/DetailDesign/Bom/ICogCalculator.cs
Services/DetailDesign/Bom/CogCalculator.cs
Services/DetailDesign/Bom/ICsvExporter.cs
Services/DetailDesign/Bom/CsvExporter.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
Sau khi data đầy đủ (không còn ? yellow) → click [BOM/CSV]
```

### Verify Criteria — MILESTONE 3 CHECKPOINT
```
✓ File tạo ra:
    C:\CustomTools\Temp\[PanelName]_BOM_[timestamp].csv
✓ Mở CSV trong Excel:
    - Header row đúng format
    - Đủ tất cả groups: TopPlate, WebPlate, Flange, Stiffener, BS, Bracket, DoublingPlate
    - Missing data → ô hiện "?"
    - Dòng cuối: TOTAL WEIGHT = X.XX kg
✓ Manual spot-check weight:
    Chọn 1 web plate → L × W × T × 7.85e-6 kg/mm³ → so sánh
    Chọn 1 HP stiffener → unit_weight × length → so sánh
✓ COG (nếu bật): X, Y, Z có giá trị hợp lý
✓ Encoding: mở CSV trên Excel không bị lỗi ký tự
```

**Câu hỏi verify:** Tổng weight có hợp lý không? (Ước tính thủ công cho panel nhỏ ~2-5 tấn)

---

## STEP 13 — Block Library Loader + Section Marker

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Generation/IBlockLibraryLoader.cs
Services/DetailDesign/Generation/BlockLibraryLoader.cs
Services/DetailDesign/Generation/SectionMarkerInserter.cs
```

### Prerequisite
```
File Symbol.dwg phải có block: "MCG_Section_Marker"
Path: C:\CustomTools\Symbol.dwg
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. Mở DWG test
2. Click [SECTION] → SetFocusToDwgView
3. Click 2 điểm trên plan view → cutting line
4. Click phía nhìn → view direction
5. Click điểm đặt section marker
```

### Verify Criteria
```
✓ Section marker block insert vào DWG đúng vị trí
✓ Block có attributes có thể edit: Section Name, Scale
✓ Double-click block → edit attributes dialog
✓ Cutting arrow hướng đúng chiều nhìn
✓ DebugView:
    "[BlockLibraryLoader] Imported MCG_Section_Marker from Symbol.dwg"
    "[SectionMarkerInserter] Inserted at (X,Y) direction=DOWN"
```

**Câu hỏi verify:** Arrow hướng có đúng không? Edit attributes có lưu được không?

---

## STEP 14 — Intersection Engine + Section Generator

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Geometry/IIntersectionEngine.cs
Services/DetailDesign/Geometry/IntersectionEngine.cs
Services/DetailDesign/Generation/ISectionGenerator.cs
Services/DetailDesign/Generation/SectionGenerator.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
1. Insert section marker (Step 13)
2. Select section marker block
3. Click [GENERATE SECTION]
4. Click điểm đặt section view trong model space
```

### Verify Criteria
```
✓ Section view vẽ trong model space tại đúng vị trí
✓ Scale đúng theo attribute (1:15 hoặc 1:20)
✓ Thấy trong section:
    - Top plate nằm ngang đúng chiều dày
    - Web plates thẳng đứng đúng chiều cao
    - Flanges ở đúng phía (top/bottom)
    - Stiffener cutout blocks tại đúng vị trí
✓ SQLite: bảng sections có record mới
    bảng section_elements có entities linked
✓ DebugView: "[SectionGenerator] Drew X elements for section A-A"
```

**Câu hỏi verify:** So sánh section view với geometry DWG:
- Chiều cao web có đúng = web_height input không?
- Số stiffener cutouts có khớp với số stiffeners qua cutting line không?

---

## STEP 15 — Dimension Engine

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Generation/IDimensionEngine.cs
Services/DetailDesign/Generation/DimensionEngine.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
GENERATE SECTION → xem dimensions xuất hiện
```

### Verify Criteria
```
✓ Dimension lines xuất hiện trên section:
    - Overall height dimension (= web_height)
    - Top plate thickness dimension
    - Flange width dimension
✓ Text annotations: "HP120x7", "t=10", "WEB PL t=10"
✓ Dimensions không overlap nhau
✓ Dimension values đúng số thực (spot-check bằng DIST)
```

**Câu hỏi verify:** Đo kích thước trong section view bằng DIST → có khớp với dimension text không?

---

## STEP 16 — Bracket Detail + Rescan Engine ✦ MILESTONE 4

**Status:** `░░ NOT STARTED`

### Files tạo mới
```
Services/DetailDesign/Generation/IDetailGenerator.cs
Services/DetailDesign/Generation/DetailGenerator.cs
Services/DetailDesign/Generation/RescanEngine.cs
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test — Part A: Bracket Detail
```
1. Right-click bracket node trong tree → [Generate Detail]
2. Click điểm đặt detail view
```

### NETLOAD Test — Part B: Rescan
```
1. Trong DWG: stretch 1 stiffener polyline (thay đổi geometry)
2. Click [RESCAN]
```

### Verify Criteria — MILESTONE 4 CHECKPOINT
```
BRACKET DETAIL:
✓ Detail view hiện plan + elevation 5-edge shape
✓ OB/IB label có leader arrow
✓ Dimensions: leg_a, leg_b, toe_length, thickness
✓ Elevation shape: đúng 5 cạnh (web + flange + stiff + chamfer + toe)

RESCAN:
✓ Dialog: "1 entity modified | Sections affected: [list]"
✓ User confirm → section view regenerate tự động
✓ Old section entities bị xóa trước khi vẽ lại
✓ DebugView:
    "[RescanEngine] Dirty: 1, New: 0, Clean: 46"
    "[RescanEngine] Regenerating section A-A..."
    "[SectionGenerator] Drew X elements for section A-A"
✓ RESCAN lần 2 ngay sau (không đổi gì): "Dirty: 0, New: 0"
```

**Câu hỏi verify:**
1. 5-edge bracket shape có đúng geometry không? (so với elevation view trong DWG gốc)
2. Sau rescan, section view có cập nhật đúng vị trí stiffener không?

---

## Notes — Phát hiện trong quá trình implement

*(Claude Code ghi vào đây khi gặp vấn đề đặc thù với DWG test file)*

| Step | Phát hiện | Xử lý |
|---|---|---|
| 3 | `CopyLocalLockFileAssemblies=false` ngăn Costura embed SQLite managed DLL | Đổi sang `true`, AutoCAD DLLs vẫn OK nhờ `ExcludeAssets="runtime"` |
| 3 | Costura `Unmanaged64Assemblies` đổi entry points → SQLite P/Invoke fail | Bỏ unmanaged config, dùng static constructor set PATH tới `x64/` folder |
| 3 | `RecalculateSize` không có trong AutoCAD 2023 API | Xóa, không dùng |
| 3 | `Exception` ambiguous giữa `Autodesk.AutoCAD.Runtime` và `System` | Dùng `System.Exception` tường minh |
| 3 | Palette không auto dock right | Thêm `_paletteSet.Dock = DockSides.Right` SAU `Visible = true` |
| 6 | DWG có layer `Mechanical -AM_0` (dấu cách), `Mechanical-AM-3` (hyphen) | Hiện → AM0_Unclassified. Cần normalize layer name hoặc thêm variant |
| 6 | DWG có layer `Mechanical-AM_7`, `Mechanical-AM_11` không trong constants | Hiện → AM0_Unclassified. Xác định ý nghĩa từ DWG samples khác |
| 6 | AM_3 color 30 (không phải 40/6) | Hiện → Stiffener với warning. Cần xác nhận color mapping |

# ROADMAP — New Steps: Construction Line Phase

> Thêm các steps này vào ROADMAP.md hiện tại, SAU step 16.
> Đọc DD_CONSTRUCTION_LINE.md trước khi implement bất kỳ step nào.

---

## STEP CL-1 — Pass 1.5: Bracket ↔ Stiffener Linking

**Status:** `░░ NOT STARTED`

### Files tạo/sửa
```
Services/DetailDesign/BaseEdgeEngine.cs   ← thêm method mới
Services/DetailDesign/Parameters/BracketAnalyzer.cs  ← update
Models/DetailDesign/CLAssignment.cs       ← thêm fields mới
```

### Fields mới trong CLAssignment
```csharp
public string   LinkedStiffenerGuid      { get; set; }
public Point2dModel StiffenerContactEdgeStart { get; set; }
public Point2dModel StiffenerContactEdgeEnd   { get; set; }
public DataState State                   { get; set; }
```

### Method cần implement
```
Pass1_5_LinkBracketToStiffener():
  - PROXIMITY_EXPAND = 5mm (AABB expand)
  - CONTACT_TOLERANCE = 2mm (edge-to-boundary dist)
  - Cover cả Stiffener lẫn BucklingStiffener
  - 1 bracket = 1 stiffener (dừng khi tìm thấy đầu tiên)
  - Warning flag nếu không tìm được stiffener
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
Mở DWG test
SCAN panel
Kiểm tra DebugView:
  "[Pass1_5] BR-01 linked to S-03 via edge E2, dist=0.8mm"
  "[Pass1_5] WARNING: BR-07 no stiffener found"
```

### Verify Criteria
```
✓ Mỗi bracket có LinkedStiffener (trừ warning cases)
✓ Debug log hiện stiffener guid và edge index
✓ Warning brackets hiện ? mark trong palette tree
✓ BS cũng được detect như stiffener partner
✓ 0 null reference exceptions
```

---

## STEP CL-2 — Pass 2 Bracket Update: Inherit Throw

**Status:** `░░ NOT STARTED`

### Files sửa
```
Services/DetailDesign/BaseEdgeEngine.cs   ← update Pass 2 bracket section
```

### Logic thay đổi
```
TRƯỚC: Bracket throw = HugCheck → INWARD/OUTWARD (độc lập)
SAU:   IF LinkedStiffener != null:
         throw = stiffener.throw (CÙNG CHIỀU)
         BaseEdge = StiffenerContactEdge
         CLDirection = stiffener.CLDirection
         OB/IB = dot(centroid_vec, stiff_throw) > 0 ? OB : IB
       ELSE: giữ HugCheck cũ (fallback)
```

### Build Check
```bash
dotnet build -c Debug → PASS
```

### NETLOAD Test
```
Mở DWG test — zoom vào vùng edge stiffener + bracket OB
```

### Verify Criteria
```
✓ Edge stiffener arrow ↑ + OB bracket arrow ↑ (CÙNG CHIỀU)
✓ CL của stiffener và bracket = cùng line (1 CL)
✓ OB bracket = nằm cùng phía throw stiffener
✓ IB bracket = nằm ngược phía throw stiffener
✓ Brackets từ image 1: không còn ngược chiều
```

**CÂU HỎI VERIFY:** So sánh với DWG gốc — OB/IB labels có khớp với bản vẽ thiết kế không?

---

## STEP CL-3 — DataState + Palette Visual

**Status:** `░░ NOT STARTED`

### Files tạo/sửa
```
Models/DetailDesign/Enums/DataState.cs    ← enum mới
Views/DetailDesign/Controls/StructureTreeView.xaml   ← icons
Views/DetailDesign/ViewModels/ElementNodeViewModel.cs ← state binding
Views/DetailDesign/Controls/PropertiesPanel.xaml      ← [Flip] [Reset]
```

### DataState Enum
```csharp
public enum DataState
{
    AutoDetected,  // ● white
    UserModified,  // ◐ orange
    Confirmed,     // ✓ green
    Warning,       // ? red row
    HashChanged    // ⚠ yellow
}
```

### UI Requirements
```
Tree icons: ✓ ● ◐ ? ⚠ theo state
Status bar: "✓12  ●8  ◐3  ?1  ⚠2"
[SAVE] button: disabled khi count(?) > 0 OR count(⚠) > 0
Warning row: background đỏ nhạt, ? prefix
HashChanged row: background vàng nhạt, ⚠ prefix
```

### Build Check + NETLOAD Test
```
✓ Icons hiện đúng màu theo state
✓ [SAVE] disabled khi có warning/hash
✓ Properties panel có [Flip Throw] + [Reset to Auto]
```

---

## STEP CL-4 — UI Override: Flip Throw

**Status:** `░░ NOT STARTED`

### Files sửa
```
Views/DetailDesign/Controls/PropertiesPanel.xaml.cs  ← button handlers
Services/DetailDesign/BaseEdgeEngine.cs               ← RecalcAfterFlip()
Services/DetailDesign/Generation/ (symbol redraw)
```

### Flow
```
[Flip Throw] click:
  1. Flip throw direction 180°
  2. Recalc BaseEdge (face ngược lại)
  3. Recalc CLSpan
  4. Xóa block symbol cũ trên DWG
  5. Vẽ block symbol mới tại CL mới
  6. State → UserModified
  7. Row → orange ◐
  8. Session.HasUnsaved = true

[Reset to Auto] click:
  1. Revert về AutoDetected values (cache lại từ đầu)
  2. State → AutoDetected
  3. Row → white ●
  4. Check HasUnsaved (nếu không còn modified → false)
```

### Verify Criteria
```
✓ Flip → arrow đổi chiều trong DWG
✓ Symbol di chuyển sang CL mới
✓ Row → orange ◐
✓ Reset → về state cũ, row → white ●
✓ [SAVE] enable sau khi flip (không có ? hay ⚠)
```

---

## STEP CL-5 — Save Flow: SQLite + XData

**Status:** `░░ NOT STARTED`

### Files sửa
```
Services/DetailDesign/Data/DetailDesignRepository.cs  ← upsert methods
Services/DetailDesign/XData/XDataManager.cs           ← update payload
Views/DetailDesign/DetailDesignView.xaml              ← [SAVE] button
Views/DetailDesign/ViewModels/DetailDesignViewModel.cs ← save command
```

### XData Payload Update
```csharp
// Thêm fields vào XDataPayload:
public double   ThrowX       { get; set; }
public double   ThrowY       { get; set; }
public string   BracketType  { get; set; } // OB/IB
public string   CLGuid       { get; set; }
public string   DataState    { get; set; } // Confirmed
```

### Save Rules
```
Block save nếu: count(Warning) > 0 OR count(HashChanged) > 0
Save: AutoDetected + UserModified → Confirmed
After save: HasUnsaved = false, refresh icons
```

### Verify Criteria
```
✓ SQLite: construction_line_members có records mới
✓ XData: entity có throw_x, throw_y, bracket_type, state
✓ Sau save: tất cả rows → ✓ green
✓ [SAVE] disabled sau save (không có pending)
✓ DB Browser: verify data đúng
```

---

## STEP CL-6 — Multi-Drawing Session Management

**Status:** `░░ NOT STARTED`

### Files tạo/sửa
```
Models/DetailDesign/PanelSession.cs        ← class mới
Commands/PaletteManager.cs                 ← subscribe events
Views/DetailDesign/ViewModels/DetailDesignViewModel.cs ← session mgmt
Views/DetailDesign/DetailDesignView.xaml   ← header binding
```

### Core Classes
```csharp
public class PanelSession
{
    public string       DocumentName  { get; set; }
    public PanelContext ActivePanel   { get; set; }
    public ScanResults  Results       { get; set; }
    public bool         HasUnsaved    { get; set; }
    public ScanState    State         { get; set; }
    public DateTime     LastScanTime  { get; set; }
}

// In service/viewmodel:
private Dictionary<string, PanelSession> _sessions = new();
```

### Events to Subscribe
```csharp
Application.DocumentManager.DocumentActivated  += OnDocActivated;
Application.DocumentManager.DocumentDestroyed  += OnDocDestroyed;
```

### Behavior
```
Switch A → B:
  - Check A.HasUnsaved → show non-modal banner nếu có
  - Load/create session B
  - Restore palette với session B

Switch B → A:
  - AUTO RESTORE session A (không reset) ✓
  - Check hash changes → mark ⚠ nếu có
  - Show banner "X elements changed" nếu có

Close A:
  - Prompt save nếu HasUnsaved
  - Remove session A
  - Load next active document session
```

### Verify Criteria
```
✓ Switch A→B: palette load B
✓ Switch B→A: palette RESTORE A (không reset)
✓ Close A với unsaved: prompt hiện
✓ Header hiện đúng: "DWG: T.6D09P | Panel: T.6D09P"
✓ Header orange khi HasUnsaved = true
✓ Không memory leak khi đóng nhiều drawings
```

**CÂU HỎI VERIFY:** Mở 3 drawings, scan cả 3, switch qua lại — palette có restore đúng mỗi cái không?
