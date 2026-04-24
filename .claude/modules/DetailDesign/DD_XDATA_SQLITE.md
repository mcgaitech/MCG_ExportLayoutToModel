# DD_XDATA_SQLITE.md — XData & SQLite Data Layer

> Đọc file này khi làm Phase A (Foundation — Data layer).
> Chứa XData schema đầy đủ và SQLite CREATE TABLE sẵn sàng execute.

---

## 1. XData Schema

### Registered App Name
```
APP_NAME = "MCG_PANEL_TOOL"
```

### Fields

| Group | DXF Type | Field | Ghi chú |
|---|---|---|---|
| 1001 | AppName | `"MCG_PANEL_TOOL"` | Required — bắt đầu XData block |
| 1000 | String | `elem_guid` | PRIMARY LINK → SQLite.guid |
| 1000 | String | `panel_guid` | Panel owner GUID |
| 1000 | String | `elem_type` | "WEB_PLATE" / "STIFFENER" / ... |
| 1000 | String | `status` | "COMPLETE" / "PENDING" / "DIRTY" / "AMBIGUOUS" |
| 1000 | String | `geometry_hash` | MD5 của WCS vertices |
| 1000 | String | `db_version` | ISO timestamp last sync |
| 1040 | Real | `thickness` | Cached mm value |
| 1000 | String | `profile_code` | "HP120x6" nếu là stiffener/BS |
| 1000 | String | `bracket_type` | "OB" / "IB" nếu là bracket |

### XDataManager — Write pattern

```csharp
// XDataManager.Write(ObjectId entityId, Transaction tr, XDataPayload payload)

// STEP 1: Register app nếu chưa có
var regAppTable = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
if (!regAppTable.Has(XDATA_APP_NAME))
{
    var newApp = new RegAppTableRecord { Name = XDATA_APP_NAME };
    regAppTable.Add(newApp);
    tr.AddNewlyCreatedDBObject(newApp, true);
}

// STEP 2: Build ResultBuffer
var rb = new ResultBuffer(
    new TypedValue(1001, XDATA_APP_NAME),
    new TypedValue(1000, payload.ElemGuid),
    new TypedValue(1000, payload.PanelGuid),
    new TypedValue(1000, payload.ElemType),
    new TypedValue(1000, payload.Status),
    new TypedValue(1000, payload.GeometryHash),
    new TypedValue(1000, payload.DbVersion),
    new TypedValue(1040, payload.Thickness),
    new TypedValue(1000, payload.ProfileCode ?? ""),
    new TypedValue(1000, payload.BracketType ?? "")
);

// STEP 3: Gán vào entity
var ent = tr.GetObject(entityId, OpenMode.ForWrite) as Entity;
ent.XData = rb;
```

### XDataManager — Read pattern

```csharp
// XDataManager.Read(ObjectId entityId, Transaction tr) → XDataPayload?

var ent = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
var rb  = ent.GetXDataForApplication(XDATA_APP_NAME);
if (rb == null) return null;

var values = rb.AsArray();
// values[0] = AppName (skip)
return new XDataPayload
{
    ElemGuid     = values[1].Value as string,
    PanelGuid    = values[2].Value as string,
    ElemType     = values[3].Value as string,
    Status       = values[4].Value as string,
    GeometryHash = values[5].Value as string,
    DbVersion    = values[6].Value as string,
    Thickness    = (double)values[7].Value,
    ProfileCode  = values[8].Value as string,
    BracketType  = values[9].Value as string
};
```

---

## 2. Geometry Hash

```csharp
// GeometryHasher.Compute(ObjectId entityId, Transaction tr) → string

var pline = tr.GetObject(entityId, OpenMode.ForRead) as Polyline;
var sb = new System.Text.StringBuilder();

for (int i = 0; i < pline.NumberOfVertices; i++)
{
    var pt = pline.GetPoint2dAt(i);
    sb.Append($"{pt.X:F3},{pt.Y:F3}|");
}
sb.Append($"A={pline.Area:F3}|P={pline.Length:F3}");

using var md5 = System.Security.Cryptography.MD5.Create();
var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
var hash  = md5.ComputeHash(bytes);
return BitConverter.ToString(hash).Replace("-", "").ToLower();
```

---

## 3. GUID Strategy

```csharp
// Tạo GUID mới cho entity chưa có XData:
string newGuid = Guid.NewGuid().ToString(); // "a3f2c1d4-8b5e-4f9a-..."

// Lookup từ XData sang SQLite:
// SELECT * FROM structural_elements WHERE guid = @guid

// Copy detection (sau mỗi scan):
// Tìm entities có duplicate guid trong XData
// → entity có AcadHandle ≠ DB.acad_handle là bản copy
// → Clear XData + generate GUID mới + ProcessAsNew()
```

---

## 4. SQLite — CREATE TABLE Statements

### SchemaInitializer.cs — ExecuteAll()

```sql
-- ═══════════════════════════════════════════════════
-- PROJECTS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS projects (
    guid        TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    dwg_path    TEXT,
    ship_name   TEXT,
    created_at  TEXT,
    updated_at  TEXT
);

-- ═══════════════════════════════════════════════════
-- PANELS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS panels (
    guid                TEXT PRIMARY KEY,
    project_guid        TEXT REFERENCES projects(guid),
    name                TEXT NOT NULL,
    side                TEXT,           -- CENTER/PORT/STARBOARD
    input_mode          TEXT,           -- BLOCK/ENTITY
    root_block_handle   TEXT,
    top_plate_handle    TEXT,
    web_height          REAL,
    top_plate_thk       REAL,
    material            TEXT,
    centroid_x          REAL,
    centroid_y          REAL,
    side_auto_detected  INTEGER DEFAULT 0,
    model_hash          TEXT,
    created_at          TEXT,
    updated_at          TEXT
);

-- ═══════════════════════════════════════════════════
-- STRUCTURAL ELEMENTS (base table)
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS structural_elements (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_type       TEXT,
    acad_handle     TEXT,
    layer           TEXT,
    color_index     INTEGER,
    vertices_wcs    TEXT,               -- JSON [[x,y],...]
    centroid_x      REAL,
    centroid_y      REAL,
    obb_length      REAL,
    obb_width       REAL,
    obb_angle       REAL,
    area_poly       REAL,
    thickness       REAL,               -- NULL = ? yellow
    geometry_hash   TEXT,
    status          TEXT DEFAULT 'PENDING',
    is_flagged      INTEGER DEFAULT 0,
    flag_reason     TEXT,
    source_context  TEXT,               -- STRUCTURE/CORNER/DIRECT
    source_block    TEXT,
    created_at      TEXT,
    updated_at      TEXT
);
CREATE INDEX IF NOT EXISTS idx_elem_panel ON structural_elements(panel_guid);
CREATE INDEX IF NOT EXISTS idx_elem_type  ON structural_elements(elem_type);

-- ═══════════════════════════════════════════════════
-- TOP PLATE REGIONS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS top_plate_regions (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    acad_handle     TEXT,
    region_index    INTEGER,
    area            REAL,
    thickness       REAL,               -- NULL = ? yellow
    centroid_x      REAL,
    centroid_y      REAL,
    perimeter       REAL
);

-- ═══════════════════════════════════════════════════
-- PROFILES CATALOG
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS profiles (
    guid            TEXT PRIMARY KEY,
    code            TEXT UNIQUE NOT NULL,  -- "HP120x6", "FB100x8"
    type            TEXT,                  -- HP/FB/ANGLE
    height          REAL,
    web_thk         REAL,
    flange_w        REAL,
    flange_thk      REAL,
    area            REAL,
    Ix              REAL,
    unit_weight     REAL,
    standard        TEXT,
    block_cutout    TEXT,                  -- "HP120x6_Cutout"
    block_section   TEXT
);

-- ═══════════════════════════════════════════════════
-- GIRDERS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS girders (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    web_elem_guid   TEXT REFERENCES structural_elements(guid),
    flange_top_guid TEXT REFERENCES structural_elements(guid),
    flange_bot_guid TEXT REFERENCES structural_elements(guid),
    web_height      REAL,
    web_thk         REAL,
    flange_width    REAL,
    flange_thk      REAL,
    orientation     TEXT,       -- LONG/TRANS
    span_length     REAL
);

-- ═══════════════════════════════════════════════════
-- STIFFENERS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS stiffeners (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_guid       TEXT REFERENCES structural_elements(guid),
    profile_guid    TEXT REFERENCES profiles(guid),
    stiff_type      TEXT,       -- STIFF/BS
    orientation     TEXT,       -- LONG/TRANS
    throw_vec_x     REAL,
    throw_vec_y     REAL,
    is_edge         INTEGER,
    end_a_type      TEXT,       -- BRACKET/SNIP/CUTOUT
    end_b_type      TEXT,
    span_length     REAL,
    total_length    REAL
);
CREATE INDEX IF NOT EXISTS idx_stiff_panel ON stiffeners(panel_guid);

-- ═══════════════════════════════════════════════════
-- STIFFENER CUTOUTS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS stiffener_cutouts (
    guid            TEXT PRIMARY KEY,
    stiffener_guid  TEXT REFERENCES stiffeners(guid),
    web_elem_guid   TEXT REFERENCES structural_elements(guid),
    position_x      REAL,
    position_y      REAL,
    block_name      TEXT,       -- "HP120x6_Cutout"
    acad_handle     TEXT
);

-- ═══════════════════════════════════════════════════
-- BRACKETS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS brackets (
    guid                TEXT PRIMARY KEY,
    panel_guid          TEXT REFERENCES panels(guid),
    elem_guid           TEXT REFERENCES structural_elements(guid),
    bracket_type        TEXT,       -- OB/IB
    stiffener_guid      TEXT REFERENCES stiffeners(guid),
    web_elem_guid       TEXT REFERENCES structural_elements(guid),
    girder_guid         TEXT REFERENCES girders(guid),
    leg_web             REAL,
    leg_stiffener       REAL,
    thickness           REAL,
    toe_length          REAL,       -- b_f/2 hoặc 15mm
    has_flange          INTEGER,
    web_height          REAL,
    in_closing_box      INTEGER DEFAULT 0,
    closing_box_guid    TEXT REFERENCES closing_boxes(guid)
);
CREATE INDEX IF NOT EXISTS idx_bracket_stiff ON brackets(stiffener_guid);

-- ═══════════════════════════════════════════════════
-- CLOSING BOXES
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS closing_boxes (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    outer_minx      REAL,
    outer_miny      REAL,
    outer_maxx      REAL,
    outer_maxy      REAL,
    corner_position TEXT        -- TL/TR/BL/BR/MID
);

CREATE TABLE IF NOT EXISTS closing_box_members (
    guid            TEXT PRIMARY KEY,
    closing_box_guid TEXT REFERENCES closing_boxes(guid),
    elem_guid       TEXT REFERENCES structural_elements(guid)
);

-- ═══════════════════════════════════════════════════
-- DOUBLING PLATES
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS doubling_plates (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_guid       TEXT REFERENCES structural_elements(guid),
    width           REAL,
    length          REAL,
    thickness       REAL,       -- NULL = ? yellow (user phải nhập)
    centroid_x      REAL,
    centroid_y      REAL,
    material        TEXT
);

-- ═══════════════════════════════════════════════════
-- SECTIONS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS sections (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    name            TEXT,       -- "A-A", "B-B"
    cut_start_x     REAL,
    cut_start_y     REAL,
    cut_end_x       REAL,
    cut_end_y       REAL,
    view_direction  TEXT,       -- UP/DOWN/LEFT/RIGHT
    scale           REAL,       -- 0.05 = 1:20
    insert_x        REAL,
    insert_y        REAL,
    block_handle    TEXT,       -- section marker block handle
    is_dirty        INTEGER DEFAULT 0,
    created_at      TEXT,
    updated_at      TEXT
);

CREATE TABLE IF NOT EXISTS section_elements (
    guid            TEXT PRIMARY KEY,
    section_guid    TEXT REFERENCES sections(guid),
    source_elem_guid TEXT REFERENCES structural_elements(guid),
    elem_type       TEXT,
    local_u         REAL,
    local_v         REAL,
    width           REAL,
    height          REAL,
    rotation        REAL,
    block_name      TEXT,
    acad_handles    TEXT        -- JSON array
);

-- ═══════════════════════════════════════════════════
-- DETAILS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS details (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    source_type     TEXT,       -- BRACKET/CLOSING_BOX
    source_guid     TEXT,
    detail_ref      TEXT,       -- "DET-1"
    scale           REAL,
    insert_x        REAL,
    insert_y        REAL,
    is_dirty        INTEGER DEFAULT 0,
    acad_handles    TEXT,
    created_at      TEXT,
    updated_at      TEXT
);

-- ═══════════════════════════════════════════════════
-- BOM ITEMS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS bom_items (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    source_elem_guid TEXT REFERENCES structural_elements(guid),
    item_no         TEXT,
    description     TEXT,
    item_type       TEXT,       -- PLATE/PROFILE/BRACKET/DOUBLING
    quantity        INTEGER,
    length          REAL,
    width           REAL,
    thickness       REAL,
    profile_code    TEXT,
    material        TEXT,
    unit_weight     REAL,
    total_weight    REAL,
    remark          TEXT
);

-- ═══════════════════════════════════════════════════
-- BLOCK TRANSFORMS (WCS transform tracking)
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS block_transforms (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    block_handle    TEXT,
    block_name      TEXT,
    parent_handle   TEXT,
    depth_level     INTEGER,
    transform_m44   TEXT,       -- JSON [16 values row-major]
    is_mirrored     INTEGER DEFAULT 0,
    mirror_axis     TEXT
);

-- ═══════════════════════════════════════════════════
-- AMBIGUOUS ELEMENTS
-- ═══════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS ambiguous_elements (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    acad_handle     TEXT,
    detected_type   TEXT,
    reason          TEXT,
    user_resolved   INTEGER DEFAULT 0,
    resolved_type   TEXT,
    resolved_at     TEXT
);
```

---

## 5. Repository Pattern

```csharp
// IDetailDesignRepository.cs
public interface IDetailDesignRepository
{
    // Projects
    void UpsertProject(ProjectRecord project);
    ProjectRecord GetProjectByDwgPath(string dwgPath);

    // Panels
    void UpsertPanel(PanelRecord panel);
    List<PanelRecord> GetPanelsByProject(string projectGuid);

    // Elements
    void UpsertElement(StructuralElementRecord elem);
    StructuralElementRecord GetElementByGuid(string guid);
    List<StructuralElementRecord> GetElementsByPanel(string panelGuid);

    // Stiffeners, Brackets, etc.
    void UpsertStiffener(StiffenerRecord stiff);
    void UpsertBracket(BracketRecord bracket);
    void UpsertSection(SectionRecord section);

    // BOM
    void RegenerateBom(string panelGuid);
    List<BomItemRecord> GetBomItems(string panelGuid);
}

// DetailDesignRepository — dùng System.Data.SQLite
// Connection string: $"Data Source={DB_PATH};Version=3;"
// Mọi query dùng parameterized (@guid, @panel_guid...)
```

---

## 6. Log File Convention

```csharp
// LogHelper.CreateLogFile(string task, string project, string panel)
// Output: C:\CustomTools\Temp\MCG_{Task}_{Project}_{Panel}_{Timestamp}.log
// Ví dụ: MCG_SCAN_ProjectA_T.6D09P_20240115_103045.log

// Log levels: [INFO] [WARN] [ERROR] [DEBUG]
// Format: [LEVEL] yyyy-MM-dd HH:mm:ss | message
```
