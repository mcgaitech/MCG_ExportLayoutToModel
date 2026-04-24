# DetailDesign.md — Module Brain

> "Bộ não" của module DetailDesign.
> Đọc sau CLAUDE.md → CONTEXT.md.
> Roadmap chi tiết: `.claude/modules/DetailDesign/ROADMAP.md`

---

## 1. Overview

```
Mục tiêu  : Scan panel kết cấu tàu Ro-Ro từ AutoCAD DWG
            → Phân loại entity → Topology analysis
            → BOM / Section / Detail generation
Input     : BlockReference (Assy block) HOẶC Polyline (top plate boundary)
Output    : SQLite DB + CSV BOM + Section drawings + Detail drawings
Non-scope : Fittings weight, web doubler plate, multi-panel relationship
```

---

## 2. Test Configuration

```
DWG test file : C:\CustomTools\Test Detail Design\CAS-0051566.dwg
DB path       : C:\CustomTools\MCGPanelTool.db
Symbol lib    : C:\CustomTools\Symbol.dwg
Log folder    : C:\CustomTools\Temp\
Profile catalog: Auto-generated EN10067 defaults (seed khi init DB)
```

---

## 3. csproj Exception

```xml
<!-- Được phép thêm (xác nhận 2026-04-12): -->
<PackageReference Include="System.Data.SQLite" Version="1.0.117" />
```

---

## 4. Constants — Copy trực tiếp vào code

```csharp
// Utilities/DetailDesign/DetailDesignConstants.cs
// namespace MCGCadPlugin.Utilities.DetailDesign

public const string LAYER_TOPPLATE       = "0";
public const string LAYER_WEB            = "Mechanical-AM_0";
public const string LAYER_FLANGE         = "Mechanical-AM_5";
public const string LAYER_STIFF          = "Mechanical-AM_3";
public const int    COLOR_STIFFENER      = 40;
public const int    COLOR_BS             = 6;        // Magenta
public const double TOLERANCE_CONTACT    = 1.0;      // mm
public const double TOLERANCE_GAP        = 10.0;     // mm
public const double RATIO_STIFF_MIN      = 5.0;      // > 5 → Stiffener
public const double RATIO_PLATE_MAX      = 3.0;      // ≤ 3 → Plate
public const double BRACKET_TOE_DEFAULT  = 15.0;     // mm
public const string XDATA_APP_NAME       = "MCG_PANEL_TOOL";
public const string DB_PATH              = @"C:\CustomTools\MCGPanelTool.db";
public const string SYMBOL_DWG           = @"C:\CustomTools\Symbol.dwg";
public const string LOG_PATH             = @"C:\CustomTools\Temp\";
public static readonly double[] PORT_DIR = { 0,  1 };
public static readonly double[] STBD_DIR = { 0, -1 };
```

---

## 5. Classification Quick Reference

| Layer | Color | Aspect Ratio | StructuralType |
|---|---|---|---|
| `"0"` | any | any | `TopPlateRegion` |
| `Mechanical-AM_5` | any | any | `Flange` |
| `Mechanical-AM_3` | 40 | > 5.0 | `Stiffener` |
| `Mechanical-AM_3` | 40 | ≤ 3.0 | `DoublingPlate` |
| `Mechanical-AM_3` | 40 | 3.0–5.0 | `Ambiguous` ⚠️ |
| `Mechanical-AM_3` | 6 | any | `BucklingStiffener` |
| `Mechanical-AM_0` | any | any | `AM0_Unclassified` → Topology |

**AM0 Secondary (TopologyEngine):**
```
touches STIFF AND touches WEB          → Bracket
share_edge với AM0 khác tại góc        → ClosingBoxWeb
còn lại                                → WebPlate
```

---

## 6. Sub-block Keywords

Normalize: `suffix.ToLower().Replace(" ","").Replace("_","").Replace("-","")`

| Normalized | Category | Nội dung |
|---|---|---|
| `"assy"`, `"assembly"` | ASSY_ROOT | Entry point |
| `"topplate"`, `"tpt"` | TOP_PLATE | Layer "0" polylines only |
| `"structure"` | STRUCTURE | AM_0/3/5 + nested blocks |
| contains `"corner"` | CORNER | AM_0 web plates only |
| `"rigging"`, `"wire*"`, `"lashing*"`, `"holes*"` | SKIP | |
| `"CAS-*"` | SKIP | Section reference |
| unknown | FLAG_UNKNOWN | Log warning |

---

## 7. Panel Side & Throw Vector

| Name suffix | PanelSide | Inner Stiffener Throw |
|---|---|---|
| `P` | PORT | `STBD_DIR (0,-1)` |
| `S` | STARBOARD | `PORT_DIR (0,+1)` |
| `C` | CENTER | `normalize(center - stiff)` |
| `C` on axis | CENTER | `STBD_DIR` (ưu tiên) |
| Edge stiffener | — | `normalize(stiff - center)` |

---

## 8. Dual Input Mode

```
MODE A — Block:  click BlockReference (Assy) → traverse + WCS transform
MODE B — Entity: click top plate Polyline → spatial query, NO nested blocks
```

---

## 9. XData Strategy

```
App: "MCG_PANEL_TOOL" | Per-entity | GUID-based
Fields: elem_guid, panel_guid, elem_type, status,
        geometry_hash, db_version, thickness, profile_code, bracket_type
SQLite = SOURCE OF TRUTH | XData = cache
Dirty detection: geometry_hash (MD5 WCS vertices)
Copy detection: duplicate GUID scan sau mỗi rescan
```

---

## 10. Profile Catalog — EN10067 Defaults

Seed vào SQLite khi khởi tạo DB (ProfileCatalogSeeder.cs):

```
HP  : 100x6 | 120x7 | 140x7 | 160x8 | 180x9 | 200x10
FB  : 75x8  | 100x8 | 120x10 | 150x10 | 150x12 | 200x12
Angle: L65x65x8 | L75x75x8 | L90x90x9
```

Block cutout naming: `{ProfileCode}_Cutout` → e.g. `HP120x7_Cutout`

---

## 11. File Architecture

```
Commands/DetailDesign/
  DetailDesignCommand.cs            [MCG_DetailDesign → PaletteManager.Show()]

Models/DetailDesign/                [NO Autodesk.* namespace — VERIFY: grep check]
  Enums/
    StructuralType.cs               ✅
    BracketType.cs                  ✅
    StiffenerEndType.cs             ✅
    PanelSide.cs                    ✅
    InputMode.cs                    ✅
    ElementStatus.cs                ✅
  Point2dModel.cs
  PanelContext.cs
  StructuralElementModel.cs
  OBBResult.cs
  XDataPayload.cs
  GirderModel.cs
  StiffenerModel.cs
  BracketModel.cs
  DoublingPlateModel.cs
  TopPlateRegionModel.cs
  ClosingBoxModel.cs
  SectionModel.cs
  BomItemModel.cs
  ProfileModel.cs

Services/DetailDesign/              [AutoCAD namespace OK]
  IPanelScanService.cs + PanelScanService.cs
  Collection/
    IEntityCollector.cs
    BlockEntityCollector.cs
    DirectEntityCollector.cs
    RawEntitySet.cs
  Classification/
    IPrimaryClassifier.cs + PrimaryClassifier.cs
    ITopologyEngine.cs + TopologyEngine.cs
    ClosingBoxDetector.cs
    SubBlockClassifier.cs
  Geometry/
    IOBBCalculator.cs + OBBCalculator.cs
    IWCSTransformer.cs + WCSTransformer.cs
    ConvexHullHelper.cs
    IIntersectionEngine.cs + IntersectionEngine.cs
    GeometryHasher.cs
  Parameters/
    IThicknessCalculator.cs + ThicknessCalculator.cs
    IBracketAnalyzer.cs + BracketAnalyzer.cs
    IThrowVectorEngine.cs + ThrowVectorEngine.cs
    PanelNameParser.cs
  XData/
    IXDataManager.cs + XDataManager.cs
  Data/
    IDetailDesignRepository.cs + DetailDesignRepository.cs
    SchemaInitializer.cs            ✅
    ProfileCatalogSeeder.cs
  Generation/
    ISectionGenerator.cs + SectionGenerator.cs
    IDetailGenerator.cs + DetailGenerator.cs
    IDimensionEngine.cs + DimensionEngine.cs
    IBlockLibraryLoader.cs + BlockLibraryLoader.cs
    SectionMarkerInserter.cs
    RescanEngine.cs
  Bom/
    IBomCalculator.cs + BomCalculator.cs
    ICogCalculator.cs + CogCalculator.cs
    ICsvExporter.cs + CsvExporter.cs

Views/DetailDesign/
  DetailDesignView.xaml/.cs
  Controls/
    StructureTreeView.xaml/.cs
    PropertiesPanel.xaml/.cs
  ViewModels/
    DetailDesignViewModel.cs
    ElementNodeViewModel.cs
    SectionViewModel.cs

Utilities/DetailDesign/
  DetailDesignConstants.cs
  DrawingUnitsValidator.cs
  LogHelper.cs
```

---

## 12. Special Rules

```
1. Models/DetailDesign/ — KHÔNG import Autodesk.*
   Verify: grep -r "using Autodesk" Models/DetailDesign/ → phải rỗng

2. DrawingUnitsValidator.Validate(db) — gọi trước mọi scan
   INSUNITS ≠ mm → show error tiếng Việt, dừng

3. Entity lookup qua GUID: XData.elem_guid → SQLite WHERE guid=@guid

4. SetFocusToDwgView() — bắt buộc cho: [SELECT PANEL] [SECTION] [DETAIL]

5. Profile catalog seeded khi init DB — không lazy load
```

---

## 13. Knowledge Files theo Phase

```
Tất cả nằm trong: .claude/modules/DetailDesign/

Phase PRE/1/2/3 : DD_XDATA_SQLITE.md + DD_GEOMETRY.md
Phase 4/5/6     : DD_CLASSIFICATION.md + DD_GEOMETRY.md
Phase 7         : DD_XDATA_SQLITE.md
Phase 8/9       : DD_TOPOLOGY.md
Phase 10/11     : DD_GEOMETRY.md
Phase 12-16     : DD_GENERATION.md
```

---

## 14. Roadmap

Xem chi tiết từng step tại:
```
.claude/modules/DetailDesign/ROADMAP.md
```

Current step: **PRE-STEP 0 — Cleanup placeholders**

---

## 15. API Quirks

| API / Method | Vấn đề | Workaround |
|---|---|---|
| `db.Insunits` | Trả `UnitsValue` enum, cần cast | `(UnitsValue)(int)db.Insunits` |
| `ent.ColorIndex` | ByLayer=256, ByBlock=0 | Resolve qua LayerTableRecord |
| `pline.GetPoint2dAt(i)` | Block space, chưa WCS | Apply `Matrix3d` transform |
| *(thêm khi phát hiện)* | | |
