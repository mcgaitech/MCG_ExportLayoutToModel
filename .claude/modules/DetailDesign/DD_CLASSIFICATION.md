# DD_CLASSIFICATION.md — Entity Classification Rules

> Đọc file này khi làm Phase B (Collection & Classification).
> Chứa toàn bộ rules phân loại entity kèm pseudo-code sẵn sàng implement.

---

## 1. Primary Classification — PrimaryClassifier.cs

### Logic chính

```csharp
// PrimaryClassifier.Classify(Polyline pline) → StructuralType
// Input: polyline đã transform về WCS
// Output: StructuralType enum

string layer = pline.Layer;
int    color = GetEffectiveColor(pline); // resolve ByLayer/ByBlock
double ratio = obb.Length / obb.Width;   // OBBResult từ OBBCalculator

// STEP 1: Layer 0 → Top Plate
if (layer == LAYER_TOPPLATE)
    return StructuralType.TopPlateRegion;

// STEP 2: Flange
if (layer == LAYER_FLANGE)
    return StructuralType.Flange;

// STEP 3: AM_3 → Stiffener / BS / Doubling
if (layer == LAYER_STIFF)
{
    if (color == COLOR_BS)                  // Magenta = ACI 6
        return StructuralType.BucklingStiffener;

    if (color == COLOR_STIFFENER)           // Color 40
    {
        if (ratio > RATIO_STIFF_MIN)        // > 5.0
            return StructuralType.Stiffener;
        if (ratio <= RATIO_PLATE_MAX)       // ≤ 3.0
            return StructuralType.DoublingPlate;
        // 3.0 < ratio ≤ 5.0
        return StructuralType.Ambiguous;    // flag for user
    }
}

// STEP 4: AM_0 → Secondary classification (Phase C)
if (layer == LAYER_WEB)
    return StructuralType.AM0_Unclassified;

// Fallback
return StructuralType.Unknown;
```

### GetEffectiveColor — Quan trọng

```csharp
// AutoCAD color có thể là ByLayer (256) hoặc ByBlock (0)
// Phải resolve về actual color index
private int GetEffectiveColor(Entity ent)
{
    if (ent.ColorIndex == 256) // ByLayer
    {
        // Lấy color từ layer definition
        var layerTable = tr.GetObject(
            db.LayerTableId, OpenMode.ForRead) as LayerTable;
        var ltr = tr.GetObject(
            layerTable[ent.Layer], OpenMode.ForRead) as LayerTableRecord;
        return ltr.Color.ColorIndex;
    }
    if (ent.ColorIndex == 0)  // ByBlock
        return 7;             // default white
    return ent.ColorIndex;
}
```

---

## 2. Sub-block Classification — SubBlockClassifier.cs

### Normalize + Match

```csharp
// SubBlockClassifier.Classify(string blockName) → BlockCategory
// Input: "T.6D09P_Top Plate" hoặc "T.6D09P_Structure" v.v.

// STEP 1: Tách suffix
string suffix = blockName.Contains("_")
    ? blockName.Substring(blockName.IndexOf('_') + 1)
    : blockName;

// STEP 2: Normalize
string normalized = suffix.ToLower()
                         .Replace(" ", "")
                         .Replace("_", "")
                         .Replace("-", "");

// STEP 3: Match
if (normalized == "assy" || normalized == "assembly")
    return BlockCategory.AssyRoot;

if (normalized == "topplate" || normalized == "tpt")
    return BlockCategory.TopPlate;

if (normalized == "structure" || normalized == "struct")
    return BlockCategory.Structure;

if (normalized.Contains("corner"))
    return BlockCategory.Corner;

// Skip categories
if (normalized.StartsWith("rigging")   ||
    normalized.StartsWith("wire")       ||
    normalized.StartsWith("lashing")    ||
    normalized.StartsWith("holes"))
    return BlockCategory.Skip;

// Nested blocks trong Structure
if (blockName.StartsWith("CAS-"))
    return BlockCategory.Skip;  // Section reference, bỏ qua

return BlockCategory.Unknown;  // Flag for user
```

### Content xử lý theo Category

```
AssyRoot   → Traverse sub-blocks, parse panel name
TopPlate   → Chỉ lấy Layer "0" closed polylines
Structure  → Lấy AM_0 + AM_3 + AM_5 + nested blocks
Corner     → Chỉ lấy AM_0 (web plates), tag context=CORNER
Skip       → Bỏ qua hoàn toàn
Unknown    → Log warning + flag, không process
```

---

## 3. Panel Name Parser — PanelNameParser.cs

### Parse pattern

```csharp
// PanelNameParser.Parse("T.6D09P_Assy") → PanelContext
// Pattern: [Prefix].[DeckID][FrameNo][SideCode]_[Suffix]

// STEP 1: Tách phần trước "_"
string baseName = blockName.Split('_')[0]; // "T.6D09P"

// STEP 2: Tách sau dấu "."
string code = baseName.Contains(".")
    ? baseName.Substring(baseName.IndexOf('.') + 1)
    : baseName; // "6D09P"

// STEP 3: Extract Side từ ký tự cuối
char sideChar = code[code.Length - 1]; // 'P'
PanelSide side = sideChar switch
{
    'P' or 'p' => PanelSide.Port,
    'S' or 's' => PanelSide.Starboard,
    'C' or 'c' => PanelSide.Center,
    _          => PanelSide.Unknown
};

// STEP 4: Extract frame number (digits trước sideChar)
string frameAndSide = code.TrimStart(new[]{'0','1','2','3','4','5','6','7','8','9',' '});
// Dùng regex đơn giản hơn:
var match = System.Text.RegularExpressions.Regex.Match(
    code, @"^([A-Za-z]+)(\d+)([PSCpsc])$");
// match.Groups[1] = DeckID ("6D")
// match.Groups[2] = FrameNo ("09")
// match.Groups[3] = SideCode ("P")

return new PanelContext
{
    Name    = baseName,
    DeckId  = match.Groups[1].Value,
    FrameNo = match.Groups[2].Value,
    Side    = side,
    SideAutoDetected = (side != PanelSide.Unknown)
};
```

---

## 4. AM0 Secondary Classification — TopologyEngine.cs (Phase C)

### Decision tree (implement trong Phase C, reference ở đây)

```
For each AM0_Unclassified polyline P:

STEP 1: Tính OBB
  ratio = obb.Length / obb.Width

STEP 2: Check closing box
  neighbors = FindAM0PolylinesWithSharedEdge(P, tolerance=1mm)
  IF neighbors.Count > 0 AND IsAtPanelCornerOrSupport(P):
      → CLOSING_BOX_WEB
      → GroupIntoClosingBox(P, neighbors)
      CONTINUE

STEP 3: Check bracket
  stiff = FindTouchingEntity(P, StructuralType.Stiffener, tol=1mm)
         OR FindTouchingEntity(P, StructuralType.BucklingStiffener, tol=1mm)
  web   = FindTouchingEntity(P, StructuralType.WEB_PLATE, tol=1mm)
  IF stiff != null AND web != null:
      → BRACKET
      CONTINUE

STEP 4: Default → WEB_PLATE
  (ratio cao, không touch stiffener)
  → WEB_PLATE
```

---

## 5. Ambiguous Elements — Handling

```csharp
// Khi StructuralType = Ambiguous:
// 1. Lưu vào bảng ambiguous_elements trong SQLite
// 2. Hiển thị trong tree với icon ⚠️ màu cam
// 3. User có thể click → chọn type thực sự
// 4. Sau khi user resolve → update SQLite + XData

// ElementStatus cho UI:
// COMPLETE  → ✓ xanh lá
// PENDING   → ? vàng (thiếu thickness/profile)
// DIRTY     → ⟳ đỏ (geometry_hash thay đổi)
// AMBIGUOUS → ⚠️ cam (cần user xác nhận type)
```

---

## 6. Block Mode vs Entity Mode — Collection difference

```
BLOCK MODE (BlockEntityCollector):
  1. GetObject(rootBlockRefId) → BlockReference
  2. GetObject(ref.BlockTableRecord) → BlockTableRecord
  3. Foreach entity in BTR:
     - IF BlockReference → SubBlockClassifier.Classify()
       → Recurse với accumulated transform
     - IF Polyline && Closed → collect với transform applied
  4. Transform accumulation:
     Matrix3d M_total = M_parent × ref.BlockTransform

ENTITY MODE (DirectEntityCollector):
  1. GetObject(topPlateId) → Polyline (boundary)
  2. Editor.SelectCrossingWindow(boundary.AABB) với filter: LWPOLYLINE
  3. Foreach selected:
     - IF Closed → collect (WCS coords directly)
     - IF BlockReference (depth=1) → SubBlockClassifier.Classify()
       → Nếu fitting block → add to fittings (NOT structural)
  4. Không traverse nested blocks sâu hơn
```
