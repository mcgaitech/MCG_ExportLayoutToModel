# DD_CONSTRUCTION_LINE.md — Construction Line & Throw Thickness Algorithm

> Đọc file này khi implement Pass 1.5 và các updates liên quan.
> Thiết kế từ session Claude AI 2026-04-20.
> Áp dụng cho: BaseEdgeEngine.cs và các services liên quan.

---

## 1. Tổng quan Pipeline

```
Pass 1   : Web-based CL detection (ĐÃ STABLE — không đổi)
Pass 1.5 : Bracket ↔ Stiffener linking (MỚI)
Pass 2   : Throw direction + OB/IB (UPDATE bracket logic)
Render   : CL lines + throw arrows trên DWG
Review   : User xem, override nếu cần
Save     : SQLite + XData khi user confirm
```

---

## 2. Root Cause — Vấn đề Đã Phân tích

```
TRIỆU CHỨNG (image quan sát):
  Edge stiffener: arrow ↑ (OUTWARD)
  OB Bracket kề cận: arrow ↓ (INWARD) ← SAI
  → 2 elements collinear nhưng throw ngược chiều
  → 2 CL lines khác nhau

ROOT CAUSE (3 levels):
  Level 1: Pass 1 không detect Stiffener↔Bracket relationship
           → Cả 2 ISOLATED, xử lý độc lập trong Pass 2
  Level 2: Bracket orientation khác stiffener (transverse vs longitudinal)
           → SnapThrow snap về hướng KHÁC NHAU
           → CL vuông góc với nhau
  Level 3: OB/IB không dùng context của stiffener kề cận
           → Throw tính sai chiều

REVISED OB/IB DEFINITION:
  OB = bracket nằm về phía CÙNG throw của stiffener
       (same side as throw direction)
  IB = bracket nằm phía ngược lại
  
  QUAN TRỌNG: Throw direction của bracket = CÙNG CHIỀU stiffener
  OB/IB chỉ là LABEL vị trí, không xác định throw direction
  → 2 arrows (stiffener + bracket) phải cùng chiều ✓
```

---

## 3. Pass 1.5 — Bracket ↔ Stiffener Linking (MỚI)

### 3.1 Mục tiêu

```
Input : Tất cả Brackets + Stiffeners/BS sau Pass 1
Output: B.LinkedStiffener + B.StiffenerContactEdge cho mỗi bracket
Data  : Tính bằng geometry (chưa có trong SQLite)
        → Lưu vào SQLite chỉ khi user Save
```

### 3.2 Thuật toán

```csharp
// PASS 1.5 — LinkBracketToStiffener()

const double CONTACT_TOLERANCE = 2.0; // mm
const double PROXIMITY_EXPAND  = 5.0; // mm AABB expand

For each Bracket B:

  STEP 1: Tìm stiffener trong proximity
    candidateStiffs = stiffeners/BS có AABB overlap với
                      B.AABB phình thêm PROXIMITY_EXPAND mm
  
  STEP 2: Tìm contact edge
    For each edge E_b in B.ActualEdges (4-5 edges):
      For each Candidate S:
        dist = PerpendicularDistFromEdgeToPolylineBoundary(E_b, S)
        IF dist < CONTACT_TOLERANCE:
          B.LinkedStiffener = S
          B.StiffenerContactEdge = E_b
          GOTO STEP 3  // 1 bracket = 1 stiffener (1-1 relationship)
  
  STEP 3: Validate
    IF B.LinkedStiffener == null:
      B.State = Warning_NoStiffener
      // Hiện ? mark + highlight red trong palette tree
      // Fallback: HugCheck logic cũ
      LOG: "WARNING: Bracket {B.guid} — no stiffener found"
    ELSE:
      B.State = AutoDetected

// NOTE: BS (BucklingStiffener) cũng là stiffener partner hợp lệ
// NOTE: Mọi bracket theo GG3 đều phải có stiffener
//       Nếu không tìm thấy → data issue → warning
```

### 3.3 Tolerance cho Stiffener-Bracket

```
CONTACT_TOLERANCE = 2mm (không dùng 1mm của web-based)
Lý do: bracket và stiffener có thickness khác nhau
       vẽ tay có thể lệch hơn so với web plate

Nếu 2mm vẫn miss: thử 3mm
Không vượt quá min(thk_stiff, thk_bracket) / 2
```

---

## 4. Pass 2 — Bracket Throw Update

### 4.1 Hierarchy Throw Direction

```
Level 1 (cao nhất) : Web Plate
  → HugCheck + Side-based (giữ nguyên)

Level 2 : Stiffener / BS
  IF trong web group (Pass 1): inherit từ web group
  IF isolated: HugCheck + Side-based riêng

Level 3 (thấp nhất) : Bracket
  → LUÔN inherit từ LinkedStiffener
  → KHÔNG tự tính HugCheck cho throw
  → throw_bracket = throw_stiffener (CÙNG CHIỀU)
```

### 4.2 Bracket Pass 2 Logic

```csharp
// TRONG Pass 2, với Bracket B:

IF B.LinkedStiffener != null:

  // 1. Throw direction = cùng chiều stiffener
  B.ThrowDirection = S.ThrowDirection  // CÙNG CHIỀU, không ngược

  // 2. BaseEdge = StiffenerContactEdge (từ Pass 1.5)
  //    KHÔNG dùng PickBase
  B.BaseEdge = B.StiffenerContactEdge
  
  // 3. CL dùng stiffener orientation
  B.CLDirection = S.CLDirection  // không dùng bracket orientation
  B.CLSpan = ProjectEdgeOntoDirection(
               B.StiffenerContactEdge,
               S.CLDirection)
  
  // 4. OB/IB = vị trí relative to stiffener throw
  Vector2d vec = B.Centroid - S.Centroid
  double dot = Dot(vec, S.ThrowDirection)
  B.BracketType = dot > 0 ? BracketType.OB : BracketType.IB

  // 5. SnapThrow dùng BaseEdge direction (= stiffener face direction)
  B.SnapThrow = PerpendiculaTo(B.BaseEdge.Direction)
                signed toward B.ThrowDirection

ELSE (fallback — bracket không linked):
  // Giữ nguyên HugCheck logic hiện tại
  // Chạy khi Pass 1.5 warning (no stiffener found)
```

### 4.3 CL Merge — Stiffener + Linked Brackets

```
Sau khi tất cả brackets có CLSpan:

For each Stiffener S:
  linkedBrackets = brackets có LinkedStiffener == S
  
  For each B in linkedBrackets:
    IF SameConstructionLine(B.CLSpan, S.CLSpan):
      // angle diff < 0.1° AND perp dist < 1mm
      S.CLSpan = Merge(S.CLSpan, B.CLSpan)
      B.CLGroupId = S.CLGroupId  // cùng group → 1 CL entity trên DWG

// Result: 1 CL line dài = stiffener span + bracket spans ✓
```

---

## 5. DataState — State Machine

```csharp
public enum DataState
{
  AutoDetected,    // vừa detect, chưa review
  UserModified,    // user đã override
  Confirmed,       // user đã save
  Warning,         // có vấn đề cần xử lý (no stiffener, etc.)
  HashChanged      // geometry bị sửa sau khi confirm
}
```

### State Transitions

```
AutoDetected
    │ user click [Flip Throw]
    ▼
UserModified ──── user click [Reset to Auto] ──→ AutoDetected
    │ user click [Save]
    ▼
Confirmed
    │ geometry_hash thay đổi (polyline bị sửa)
    ▼
HashChanged ──── user click [Re-detect] ──→ AutoDetected
            ──── user click [Keep Override] ──→ UserModified
            ──── user click [Accept Auto] ──→ AutoDetected

AutoDetected/UserModified
    │ no stiffener found (Pass 1.5)
    ▼
Warning
```

### Visual trong Palette Tree

```
✓  BR-01  OB  t:10mm        ← Confirmed (green)
●  BR-02  IB  t:8mm         ← AutoDetected (white)
◐  BR-03  OB  t:10mm        ← UserModified (orange)
?  BR-04  ??  t:?mm  ████   ← Warning: no stiffener (red row)
⚠  BR-05  OB  t:10mm        ← HashChanged (yellow)

Status bar:
  ✓12  ●8  ◐3  ?1  ⚠2   [SAVE]
[SAVE] disabled khi còn ? hoặc ⚠
```

---

## 6. UI Override — Palette Driven

### 6.1 Flow

```
User click entity trong DWG
  → SELECT (không thay đổi gì)
  → Palette highlight row
  → Properties panel hiện thông tin

User muốn override:
  → Click [Flip Throw] trong Properties panel
  → Tool:
    1. Tính throw direction mới (flip 180°)
    2. Recalculate BaseEdge (face ngược lại)
    3. Recalculate CL position
    4. Xóa block symbol cũ trên DWG
    5. Vẽ block symbol mới tại CL mới
    6. Set state = UserModified
    7. Palette row → orange ◐

User muốn revert:
  → Click [Reset to Auto]
  → Revert về AutoDetected result
```

### 6.2 Properties Panel Layout

```
┌─────────────────────────────────────────────┐
│ Properties — [elem type] [elem id]           │
│ ─────────────────────────────────────────── │
│ Type:      [StructuralType]                  │
│ State:     [icon] [DataState]               │
│ CL:        [LONG/TRANS/DIAG] [position]mm   │
│ Throw:     [direction label] ([vector])      │
│                                             │
│ [Flip Throw]        [Reset to Auto]         │
│ ─────────────────────────────────────────── │
│ OB/IB:     [OB/IB] (nếu là bracket)        │
│ Linked:    [stiffener id] (nếu là bracket)  │
└─────────────────────────────────────────────┘
```

---

## 7. Multi-Drawing Session Management

### 7.1 Data Structure

```csharp
// Trong PaletteManager hoặc DetailDesignService

private Dictionary<string, PanelSession> _sessions;
// key = document.Name hoặc document.Database.Filename

public class PanelSession
{
    public string       DocumentName   { get; set; }
    public PanelContext ActivePanel    { get; set; } // null nếu chưa scan
    public ScanResults  Results        { get; set; }
    public bool         HasUnsaved     { get; set; }
    public ScanState    State          { get; set; }
    public DateTime     LastScanTime   { get; set; }
}

public enum ScanState
{
    None,      // chưa scan
    Done,      // scan xong, data valid
    Stale      // có hash changes, cần review
}
```

### 7.2 Events cần Subscribe

```csharp
// Trong App.cs hoặc PaletteManager Initialize():

var docMgr = Application.DocumentManager;

docMgr.DocumentActivated    += OnDocumentActivated;
docMgr.DocumentDestroyed    += OnDocumentDestroyed;
docMgr.DocumentCreated      += OnDocumentCreated;
```

### 7.3 Event Handlers

```csharp
private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
{
    var docName = e.Document.Name;
    
    // Check unsaved changes của session cũ
    if (_currentSession?.HasUnsaved == true)
        ShowUnsavedBanner(_currentSession.DocumentName);
    
    // Load hoặc tạo session cho doc mới
    if (_sessions.ContainsKey(docName))
    {
        _currentSession = _sessions[docName];
        
        // Check geometry hashes
        var changed = CheckHashChanges(_currentSession);
        if (changed.Count > 0)
        {
            MarkHashChanged(changed);
            ShowBanner($"⚠ {changed.Count} elements changed since last scan");
        }
        
        RestorePalette(_currentSession); // restore tree, properties
    }
    else
    {
        _currentSession = new PanelSession { DocumentName = docName };
        _sessions[docName] = _currentSession;
        ResetPaletteToInitial(); // "Ready to scan"
    }
    
    UpdatePaletteHeader(docName, _currentSession.ActivePanel);
}

private void OnDocumentDestroyed(object sender, DocumentCollectionEventArgs e)
{
    var docName = e.Document.Name;
    
    if (_sessions.TryGetValue(docName, out var session))
    {
        if (session.HasUnsaved)
            PromptSaveBeforeClose(session); // [Save] [Discard]
        
        _sessions.Remove(docName);
    }
    
    // Load session của document còn lại (nếu có)
    var activeDoc = Application.DocumentManager.MdiActiveDocument;
    if (activeDoc != null && _sessions.ContainsKey(activeDoc.Name))
        RestorePalette(_sessions[activeDoc.Name]);
    else
        ResetPaletteToInitial();
}
```

### 7.4 Palette Header

```xml
<!-- Palette header showing drawing context -->
<StackPanel Orientation="Horizontal" Margin="4,2">
    <TextBlock Text="DWG: " Foreground="#FF999999"/>
    <TextBlock Text="{Binding CurrentDocName}"
               Foreground="#FFEEEEEE" FontWeight="Bold"/>
    <TextBlock Text=" | Panel: "
               Foreground="#FF999999" Margin="8,0,0,0"/>
    <TextBlock Text="{Binding ActivePanelName}"
               Foreground="{Binding PanelNameColor}"/>
    <!-- Orange nếu HasUnsaved, white nếu không -->
</StackPanel>
```

---

## 8. Save Flow

```csharp
// MCG_Save command hoặc [SAVE] button

public void SaveToDatabase()
{
    // Block save nếu còn Warning hoặc HashChanged
    var blocking = elements.Where(e =>
        e.State == DataState.Warning ||
        e.State == DataState.HashChanged).ToList();
    
    if (blocking.Any())
    {
        ShowMessage($"Không thể save: {blocking.Count} elements cần xử lý");
        HighlightBlockingElements(blocking);
        return;
    }
    
    // Save tất cả AutoDetected + UserModified
    var toSave = elements.Where(e =>
        e.State == DataState.AutoDetected ||
        e.State == DataState.UserModified).ToList();
    
    using (var tr = db.TransactionManager.StartTransaction())
    {
        foreach (var elem in toSave)
        {
            // 1. Ghi SQLite
            _repository.UpsertConstructionLine(elem.CLData);
            _repository.UpsertBracket(elem.BracketData);
            _repository.UpsertStiffener(elem.StiffData);
            
            // 2. Ghi XData lên AutoCAD entity
            _xdataManager.Write(elem.EntityId, tr, new XDataPayload
            {
                ElemGuid      = elem.Guid,
                ThrowX        = elem.ThrowDirection.X,
                ThrowY        = elem.ThrowDirection.Y,
                BracketType   = elem.BracketType?.ToString(),
                CLGuid        = elem.CLGroupId,
                State         = "Confirmed",
                DbVersion     = DateTime.UtcNow.ToString("O")
            });
            
            // 3. Update state
            elem.State = DataState.Confirmed;
        }
        tr.Commit();
    }
    
    _currentSession.HasUnsaved = false;
    RefreshPaletteTree(); // update icons
    ShowStatus($"Saved {toSave.Count} elements");
}
```

---

## 9. Checklist Implement

```
PHASE CL-1: Pass 1.5
  [ ] Implement LinkBracketToStiffener()
  [ ] PROXIMITY_EXPAND + CONTACT_TOLERANCE constants
  [ ] Warning state cho bracket không linked
  [ ] Unit test: bracket tìm đúng stiffener

PHASE CL-2: Pass 2 Update
  [ ] Bracket throw = inherit từ stiffener (cùng chiều)
  [ ] BaseEdge = StiffenerContactEdge
  [ ] OB/IB từ dot product centroid vector
  [ ] CL merge stiffener + bracket spans
  [ ] SnapThrow dùng stiffener CLDirection

PHASE CL-3: DataState
  [ ] DataState enum
  [ ] State transitions
  [ ] Visual indicators trong palette tree (✓ ● ◐ ? ⚠)
  [ ] Status bar count

PHASE CL-4: UI Override
  [ ] [Flip Throw] button trong Properties panel
  [ ] [Reset to Auto] button
  [ ] Recalculate CL khi flip
  [ ] Update DWG symbol khi flip

PHASE CL-5: Save Flow
  [ ] Block save khi có ? hoặc ⚠
  [ ] Save to SQLite (construction_lines + members)
  [ ] Write XData (throw, OB/IB, state, cl_guid)
  [ ] Session.HasUnsaved tracking

PHASE CL-6: Multi-Drawing
  [ ] PanelSession class
  [ ] _sessions Dictionary
  [ ] DocumentActivated handler
  [ ] DocumentDestroyed handler
  [ ] Palette header with DWG name
  [ ] Restore palette khi switch back
  [ ] Hash check khi restore
  [ ] Unsaved banner (non-modal)
```

---

## 10. Verify Criteria Sau Mỗi Phase

```
PHASE CL-1 PASS khi:
  Bracket tìm đúng stiffener partner
  Debug log: "BR-01 linked to S-03 via edge [E2]"
  Warning hiện đúng cho bracket không linked

PHASE CL-2 PASS khi:
  Edge stiffener + OB bracket: 2 arrows CÙNG chiều ✓
  CL của bracket = CL của stiffener (cùng line) ✓
  OB/IB label đúng với vị trí relative stiffener

PHASE CL-3 PASS khi:
  Icons hiện đúng trong tree
  [SAVE] disabled khi có ? hoặc ⚠

PHASE CL-4 PASS khi:
  [Flip Throw] → arrow flip trên DWG
  Symbol di chuyển sang CL mới
  Row → orange trong tree

PHASE CL-5 PASS khi:
  SQLite có construction_line_members records
  XData trên entity có throw + OB/IB + state
  Session.HasUnsaved = false sau save

PHASE CL-6 PASS khi:
  Switch A→B: palette load B (hoặc empty)
  Switch B→A: palette RESTORE data của A ✓
  Close A: session cleared, no memory leak
  Header hiện đúng DWG name + panel name
```
