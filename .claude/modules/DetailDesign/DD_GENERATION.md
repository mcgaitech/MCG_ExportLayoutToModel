# DD_GENERATION.md — Section & Detail Generation

> Đọc file này khi làm Phase F (Section) và Phase G (Detail).

---

## 1. Section Generation Pipeline

### F1 — Block Library Loader

```csharp
// BlockLibraryLoader.ImportBlock(string blockName, Database targetDb)
// Load block từ C:\CustomTools\Symbol.dwg vào current drawing

using (var sourceDb = new Database(false, true))
{
    sourceDb.ReadDwgFile(SYMBOL_DWG, FileOpenMode.OpenForReadAndAllShare, false, "");

    using (var tr = targetDb.TransactionManager.StartTransaction())
    {
        var bt = tr.GetObject(targetDb.BlockTableId, OpenMode.ForWrite) as BlockTable;

        if (!bt.Has(blockName))
        {
            // Import block definition từ Symbol.dwg
            var objIds = new ObjectIdCollection();
            using (var sourceTr = sourceDb.TransactionManager.StartTransaction())
            {
                var sourceBT = sourceTr.GetObject(
                    sourceDb.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (sourceBT.Has(blockName))
                    objIds.Add(sourceBT[blockName]);
                sourceTr.Commit();
            }
            // Clone vào target database
            var idMap = new IdMapping();
            sourceDb.WblockCloneObjects(objIds, targetDb.BlockTableId, idMap,
                DuplicateRecordCloning.Ignore, false);
        }
        tr.Commit();
    }
}
```

### Block naming convention
```
Cutout blocks:  "{ProfileCode}_Cutout"   → "HP120x6_Cutout", "FB100x8_Cutout"
Section blocks: "{ProfileCode}_Section"  → "HP120x6_Section"
Section marker: "MCG_Section_Marker"     → dynamic block với attributes
```

---

## 2. Section Marker Block (Dynamic)

```
MCG_Section_Marker block attributes:
  TAG: SECTION_NAME  → "A-A"
  TAG: SCALE         → "1:15"
  TAG: PANEL_ID      → "T.6D09P"
  TAG: VIEW_DIR      → "DOWN" / "UP" / "LEFT" / "RIGHT"

Dynamic properties:
  - Flip action: đổi hướng view arrow
  - Stretch: điều chỉnh cutting line length

User workflow:
  1. Click [SECTION] button → SetFocusToDwgView()
  2. Click 2 điểm → cutting line
  3. Click phía nhìn → view direction
  4. Block insert tại vị trí user chọn
  5. User double-click → edit attributes (section name, scale)
  6. User click [GENERATE] → system đọc block → tạo section
```

---

## 3. Intersection Engine

### Cutting plane intersect với PanelModel3D

```
CuttingLine (2D) → CuttingPlane (3D vertical plane)

For each element:

TOP PLATE:
  → Luôn cắt (plane đi qua panel)
  → section_shape = horizontal rect
    width  = top_plate_thk (height in section view)
    length = cutting line length qua top plate
  → Hatch: ANSI31 (standard section hatch)

WEB PLATE:
  intersection = CuttingLine.Intersect(web.Polyline2D)
  IF intersection exists:
    IF cutting_length ≈ web_thk (cắt ⊥ web):
      → vertical rect: width=web_thk, height=web_height
      → Check cutouts: tìm stiffeners tại vị trí cắt
        → Insert {Profile}_Cutout block
    IF cutting_length >> web_thk (cắt // web):
      → elevation view: length=web_length, height=web_height

FLANGE:
  IF CuttingLine intersects flange.Polyline2D:
    → horizontal rect:
      width = flange_thk
      length = flange_width
      Z position = web_height (top) hoặc 0 (bottom/top plate)

STIFFENER:
  IF CuttingLine intersects stiff.Polyline2D:
    intersection_pt = intersection point
    → Insert {ProfileCode}_Section block
    → Position: at intersection_pt in section view
    → Z: stiff hàn vào top plate → đáy = 0, đỉnh = -h_s
    → Rotation: theo throw direction

BUCKLING STIFFENER (FB):
  Tương tự stiffener → Insert FB section block

BRACKET:
  IF CuttingLine intersects bracket.Polyline2D:
    → vertical plate: width=bracket_thk, height=web_height
    → Nếu cắt vào chamfer zone → thể hiện chamfer shape

DOUBLING PLATE:
  IF CuttingLine intersects dp.Polyline2D:
    → horizontal rect phía DƯỚI top plate:
      width = dp extent tại vị trí cắt
      height = dp.Thickness (user input)
      Z = 0 → -dp.Thickness
    → Annotation: "DBL PL. t=XXmm"
```

---

## 4. Section Drawing Assembly

```csharp
// SectionGenerator.DrawSection(SectionModel section, PanelModel3D panel)

// STEP 1: Collect tất cả cross-section shapes
var shapes = IntersectionEngine.ComputeAll(section.CutLine, panel);

// STEP 2: Sort theo local_u (position along cut line)
shapes.Sort(s => s.LocalU);

// STEP 3: Draw trong model space tại insert_x, insert_y
var origin = new Point3d(section.InsertX, section.InsertY, 0);
double scale = section.Scale; // 0.05 = 1:20

foreach (var shape in shapes)
{
    double u = shape.LocalU * scale;
    double v = shape.LocalV * scale;
    var pos = origin + new Vector3d(u, v, 0);

    switch (shape.ElementType)
    {
        case "WEB_CUT":
            DrawRectangle(pos, shape.Width * scale, shape.Height * scale);
            break;
        case "STIFF_PROFILE":
        case "BS_PROFILE":
            InsertBlock(shape.BlockName, pos, shape.Rotation, scale);
            break;
        case "FLANGE_CUT":
            DrawRectangle(pos, shape.Width * scale, shape.Height * scale);
            break;
        // ...
    }
}

// STEP 4: Auto dimension
DimensionEngine.AddDimensions(section, shapes, origin, scale);

// STEP 5: Save acad_handles vào SQLite → cho regeneration
```

---

## 5. Bracket Detail Generation

### Plan view (từ trên nhìn xuống)

```
Bracket plan view = closed polyline gốc (đã có trong DWG)
→ Chỉ cần add dimensions:
  - leg_web dimension
  - leg_stiffener dimension
  - thickness callout
  - "OB" hoặc "IB" text với leader
```

### Elevation view (nhìn từ cạnh)

```
5-edge shape từ BracketModel:
  P1 = (0, web_height)                   ← top-left
  P2 = (toe_length, web_height)          ← top-right (flange side)
  P3 = (toe_length + leg_stiff, web_height)  ← nếu stiffener ở phải
  P4 = (toe_length + leg_stiff, 0)       ← bottom-right
  P5 = (0, 0)                            ← bottom-left
  Chamfer edge: P3 → P5 (diagonal closing)

Dimensions trong elevation:
  - web_height (overall height)
  - toe_length (top horizontal)
  - leg_stiffener (right vertical)
  - thickness callout "t=XXmm"

Gap tolerance = 10mm:
  Khi vẽ elevation, check khoảng cách bracket với entities lân cận
  IF gap < 10mm → vẽ bracket sát entity (contact)
  IF gap > 10mm → vẽ đúng khoảng cách thực
```

---

## 6. Regeneration Strategy

```csharp
// RescanEngine.Rescan(PanelContext panel, Database db)

// STEP 1: Collect all entities với XData của panel
var panelEntities = CollectEntitiesWithXData(panel.Guid, db);

// STEP 2: Compare geometry hash
var dirty  = new List<ObjectId>();
var clean  = new List<ObjectId>();
var newOnes = new List<ObjectId>();

foreach (var entityId in allEntitiesInBoundary)
{
    var xdata = XDataManager.Read(entityId, tr);
    if (xdata == null) { newOnes.Add(entityId); continue; }

    string currentHash = GeometryHasher.Compute(entityId, tr);
    if (currentHash != xdata.GeometryHash)
        dirty.Add(entityId);
    else
        clean.Add(entityId);
}

// STEP 3: Show rescan dialog
// "+ X new entities | ~ Y modified | ✓ Z unchanged"
// Sections/details bị ảnh hưởng: list ra
// [Update All] [Select] [Skip]

// STEP 4: Nếu user confirm
// - Re-classify dirty entities
// - Re-run topology cho affected area
// - Erase old section entities (by acad_handles in SQLite)
// - Regenerate sections/details
// - Update XData + SQLite
```

---

## 7. BOM CSV Format

```
File: C:\CustomTools\Temp\{PanelName}_BOM_{yyyyMMdd_HHmmss}.csv

Header:
No,Description,Type,Profile/Thk,Length(mm),Width(mm),Qty,Unit_Weight(kg),Total_Weight(kg),Material,Remark

Groups (theo thứ tự):
1. TOP PLATE regions
2. WEB PLATES (theo girder)
3. FLANGES (theo girder)
4. STIFFENERS (group theo profile code)
5. BUCKLING STIFFENERS (group theo profile)
6. BRACKETS (group OB/IB + thickness)
7. DOUBLING PLATES
8. CLOSING BOX PLATES

Weight formula:
  Plate: L × W × T × 7.85e-6 kg/mm³
  Profile: unit_weight(kg/m) × length(m)

Footer rows:
TOTAL WEIGHT: xxxx.xx kg
COG: X=xxxx.x Y=xxxx.x Z=xxxx.x mm   ← nếu user chọn show COG

Missing data → cell = "?" (không dùng 0)
```

---

## 8. COG Calculation

```csharp
// CogCalculator.Compute(PanelModel3D panel) → CogResult

double totalMass = 0;
double cogX = 0, cogY = 0, cogZ = 0;

foreach (var element in panel.AllElements)
{
    double mass = element.UnitWeight; // đã tính trong BomCalculator
    double cx   = element.Centroid.X;
    double cy   = element.Centroid.Y;
    double cz   = element.CentroidZ; // Z theo 3D model

    totalMass += mass;
    cogX += mass * cx;
    cogY += mass * cy;
    cogZ += mass * cz;
}

if (totalMass > 0)
{
    cogX /= totalMass;
    cogY /= totalMass;
    cogZ /= totalMass;
}

// Z centroids:
// Top plate (horizontal): cz = top_plate_thk / 2
// Web (vertical):         cz = web_height / 2
// Stiffener:              cz = h_s / 2
// Doubling plate:         cz = -(dp_thk / 2) [below top plate]

// COG marker: insert "MCG_COG_Marker" block tại (cogX, cogY)
// với attribute COG_Z = cogZ value
```
