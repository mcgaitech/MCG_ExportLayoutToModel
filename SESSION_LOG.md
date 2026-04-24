# SESSION_LOG.md — MCGCadPlugin

> Claude Code tự cập nhật. KHÔNG sửa thủ công.
> Session mới → THÊM VÀO ĐẦU FILE. Tag: `[MODULE:X]`.

---

## [MODULE:DetailDesign] Session 2026-04-21 cont.8 — Step 6/7 Bug Fixes + Priority Refinement

### Đã làm

**Priority enforcement initially implemented rồi refine:**
- `BaseEdgeEngine.cs` — Ban đầu add P1 + P2 skip ở Step 6/7 (cont.7 ending)
- User clarify: "P2 chỉ xét cho web > 1000mm" → bỏ P2 skip ở Step 6/7
- Logic cuối: P2 (outer/inner) áp dụng cho long plates (>1000mm), P4 (Step 6/7) áp dụng cho short plates (<1000mm). 2 phạm vi rời nhau theo length → chỉ giữ P1 skip (collinear group).

**3 bugs fix ở Step 6 `ApplyConnectedLongEdgeRule`:**

1. **AND → OR cho SE endpoints check**: SE của B vuông góc với LE của A → chỉ 1 endpoint của SE nằm trên LE (endpoint còn lại cách LE ≈ thickness của B). Điều kiện `&&` không bao giờ match case perpendicular touch (case chính). Fix: `||`.

2. **`GetShortEdgesOfRect` support bended polyline**: trước chỉ 4-vertex. Bended web (vertex > 4) từ `BendedPlateAnalyzer` không match. Fix: iterate all edges, match theo `ObbWidth ± 30%` range.

3. **`webLike` filter thiếu GirderEnd**: Web đã classify là GE không được xét như "A" hay "B". Fix: thêm `p.ElemType == GirderEnd` vào filter cho cả Step 6 + Step 7.

### Bug analysis (iProperties tracing)

**Part 040963c3** (WebPlate 253×10, outer, length<1000):
- Bottom LE tại Y=23355.6 nhận SE của `ba8b34e8` (vertical web) tại Y=23356 (diff 0.4mm < 1mm tol) ✓
- Match Step 6 pattern nhưng ban đầu bị skip P2 (IsEdgeElement=true)
- Sau fix (bỏ P2 skip): Step 6 sẽ trigger → base=bottom LE, throw=(0,+1) toward centroid

**Part 51613194** (WebPlate 354×6 slanted @-65°):
- V[0] chạm `c05a8869` bottom face tại diff 0.4mm ✓
- V[1] cách 2.9mm — **ngoài 1mm tolerance** → Step 7 không match
- Root cause: tolerance 1mm quá chặt cho slanted connections
- **Pending Q2**: tăng tolerance (5mm hoặc thickness/2) hay giữ 1mm

### Trạng thái Phase

```
Step 6 bug fix (AND → OR, bended, GirderEnd)   : ██████████ 100% implemented
Priority P2-only-for-long-plates                : ██████████ 100% implemented
Step 7 tolerance (slanted V[1] 2.9mm issue)    : ░░░░░░░░░░ 0% — pending Q2
Full panel test với bugs fixed                  : ░░░░░░░░░░ 0% — chờ NETLOAD
```

### Bước tiếp theo

```
1. Test panel thực tế với Step 6 bugs fixed:
   - 040963c3 → throw (0,+1) (thay vì -1)
   - Log [ConnLE] applied=N > 0 cho các short plate match
2. Confirm Q2 tolerance cho Step 7:
   - Option (A): thickness/2
   - Option (B): cố định 5mm
   - Option (C): keep 1mm, chấp nhận miss
3. Có thể cần review priority lại nếu thấy có case edge khác
```

### Ghi chú kỹ thuật

- Step 6 skip: CHỈ P1 (collinear group) + length filter + multi-edge case
- P2 (outer/inner via `IsEdgeElement`) áp dụng song song ở Pass 2, KHÔNG block Step 6/7 vì length ranges rời nhau
- `GetShortEdgesOfRect` dùng ObbWidth 0.7-1.3x range → catch SE cả ở bended shapes
- `FindConnectedWeb` vẫn dùng `TOLERANCE_CONTACT = 1mm` — gây miss case 51613194

---

## [MODULE:DetailDesign] Session 2026-04-21 cont.7 — Priority Rules + GE Strict + Classification Cleanup

### Đã làm

**Classification fixes:**
- `Services/DetailDesign/Classification/PrimaryClassifier.cs` — whitelist layers (0, AM_0, AM_3, AM_5, AM_11). Ngoài whitelist → return null (skip, không tạo ghost AM0). Layer "0" ngoài TOP_PLATE context cũng skip.
- `Utilities/DetailDesign/DetailDesignConstants.cs` — Thêm `COLOR_STIFFENER_ALT = 30`. AM_3 color 30 hoặc 40 đều treat là Stiffener.

**iProperties enhancement:**
- `Commands/DetailDesign/DetailDesignCommand.cs` — Multi-select (`GetSelection`), output thêm `Handle`, `Polyline handle`, `PolyLayer/LType/Color/Class`, `OBB`, `Centroid`, `Vertices` list. Copy clipboard `System.Windows.Clipboard.SetText`. Diagnostic phân biệt failure modes (stale TT, no VM, unknown XData apps).
- `Services/DetailDesign/PanelScanService.cs` — Write `BracketType = elem.BracketSubType` (trước là hardcoded "").

**OB/IB fix (replaces cont.5 dot-product):**
- `Services/DetailDesign/Parameters/BracketAnalyzer.cs` — OB/IB determined bởi `stiffModel.IsEdge` (edge stiff → OB, interior → IB). Bỏ logic dot(throw, toWeb).
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — OB bracket (parent.IsEdgeElement) override throw bằng Panel.Side inner direction. Tránh ambiguity khi parent OUTWARD throw perpendicular với bracket long axis.

**New rules — Step 6 & 7 trong BaseEdgeEngine.cs:**
- **Step 6 `ApplyConnectedLongEdgeRule`**: Web/CBWeb có EXACTLY ONE long edge nhận SE của web khác → CL = edge đó, throw perp về centroid. Helpers: `LongEdgeReceivesShortEdges`, `GetShortEdgesOfRect`, `PointOnSegmentExtent`.
- **Step 7 `ApplySlantedConnectedEdgeRule`**: Slanted Web/CBWeb có EXACTLY ONE edge với 2 vertices chạm 2 web khác nhau → CL = edge đó, throw perp về centroid. Helpers: `IsSlantedOrientation`, `FindConnectedWeb`.
- Constraint: `ObbLength < 1000mm` (constant `CONN_RULE_MAX_LENGTH`).

**GE strict detection:**
- `Models/DetailDesign/Enums/StructuralType.cs` — Thêm `GirderEnd`.
- `Services/DetailDesign/Classification/TopologyEngine.cs` — Thay loose "near stiff" GE detection bằng strict geometric rule: WebPlate 4-vertex có `1 SE touches web + 1 LE touches web + 1 SE touches stiff/BS` → ElemType = GirderEnd. Helpers: `SplitShortLongEdges`, `EdgeTouchesAny`, `PointOnSegment`. Thêm breakdown log `GIRDER END: N`.

### Priority Order — đã xác định + chưa enforce

```
P1 (TOP): Collinear group       — CL group, throw group-based
P2:       Outer (IsEdgeElement) — OUTWARD/INWARD theo type
P3:       Inner (isolated)      — Side-based từ Panel.Side
P4:       Step 6 ConnLE + Step 7 SlantConn (length < 1000)
P5:       Flanges / BF isolated / Bracket fallback
```

**Inconsistency:** Step 6/7 đang chạy sau cùng → ghi đè P1/P2/P3. Đúng priority phải skip P1 (in group) + P2 (outer) trước khi apply P4. CHƯA implement — pending user confirm.

### Bug diagnosis (không code)

- **Polyline 24D99 TT "trôi"**: thực ra là polyline duplicate/overlap (2 plate cách 5mm cùng range Y). `TryGetParallelPair` trả đúng vertex. CAD drafting error, không phải plugin bug.
- **PolyLType `AM_ISO02W050`** = ISO dashed — khi user verify. Phát hiện convention dashed nhưng bỏ qua (dùng rule khác thay thế = Step 7 SlantedConnected).

### Trạng thái Phase

```
PrimaryClassifier whitelist layer           : ██████████ 100% fixed
Color 30 Stiffener                          : ██████████ 100% fixed
iProperties multi + clipboard + polyline    : ██████████ 100% implemented
OB/IB via IsEdge                            : ██████████ 100% implemented
OB bracket Panel.Side override              : ██████████ 100% implemented
Step 6 ConnectedLongEdge                    : ██████████ 100% implemented + length<1000
Step 7 SlantedConnected                     : ██████████ 100% implemented + length<1000
GE strict detection                         : ██████████ 100% implemented
Priority enforcement (skip P1/P2 in Step 6/7): ░░░░░░░░░░  0% — pending
```

### Bước tiếp theo

```
1. Confirm priority enforcement fix → add skip check in Step 6/7:
   if (!cl.IsIsolated) continue;   // P1 wins
   if (p.IsEdgeElement) continue;  // P2 wins
2. Test với panel thực tế → verify GE detection strict + Step 6/7 triggers đúng
3. Review xem có cần tích hợp GirderEnd vào web-like checks ở Step 6/7 không
```

### Ghi chú kỹ thuật

- `PointOnSegmentExtent`: perp dist < tol AND projection t ∈ [-0.001, 1.001]
- `TryGetParallelPair` trả vertex indices thật → base edge luôn từ polyline vertices (không nội suy)
- Dashed linetype `AM_ISO02W050` tồn tại trên AM_0 nhưng không filter — rule khác xử lý (Step 7)
- GE previously was `AnnotationType = "GE"` only; nay thành `ElemType = GirderEnd` (distinct type)
- `CONN_RULE_MAX_LENGTH = 1000mm` — cap cho Step 6/7

---

## [MODULE:DetailDesign] Session 2026-04-20 cont.6 — Box Context + BF Long-Face Contact

### Đã làm

**UPDATE 1 — Box keywords + source context:**
- `Utilities/DetailDesign/DetailDesignConstants.cs` — Thêm `BOX_BLOCK_KEYWORDS[] = {BX, BOX, CB, CBOX}` + `IsBoxBlock(name)`
- `Services/DetailDesign/Classification/SubBlockClassifier.cs` — Thêm `BOX` category; `Classify` check IsBoxBlock trước STRUCTURE/CORNER
- `Services/DetailDesign/Collection/RawEntitySet.cs` — Thêm `BoxEntities` list; `EntityRef.SourceBlock`
- `Services/DetailDesign/Collection/BlockEntityCollector.cs` — `CollectFromBlock` tag sourceBlock; `IsLikelyBoxBlock` auto-detect (sub-block chỉ có AM_0); `LogBlockSummary` for verification
- `Services/DetailDesign/Classification/PrimaryClassifier.cs` — Propagate `SourceBlock` từ EntityRef
- `Services/DetailDesign/PanelScanService.cs` — Classify `rawSet.BoxEntities` với sourceContext="BOX"

**UPDATE 2 — Classify theo source_context + long-face contact:**
- `Services/DetailDesign/Classification/TopologyEngine.cs` — Bước 3 rewrite:
  - Priority 1: SourceContext=="BOX" → ClosingBoxWeb
  - Priority 2: SourceContext=="CORNER" → WebPlate
  - Priority 3 (STRUCTURE): long-face BS contact (≥ obb_width*2) → Bracket BF; SE stiffener contact → Bracket OB/IB later; else WebPlate
  - Thêm `FindLongestContactLength` + `ComputeParallelOverlap` (parallel tol 5°, perp < 2mm)
- `Services/DetailDesign/Parameters/BracketAnalyzer.cs`:
  - Nếu TopologyEngine đã mark BF → giữ nguyên, skip OB/IB analysis
  - Bỏ nhánh cont.5 `shortEdgeTouchesBS → BF` (BF giờ chỉ đến từ TopologyEngine long-face)
  - OB/IB chỉ từ SE contact Stiffener (không BS)

**UPDATE 3 — Debug log:**
- Tích hợp trong `BlockEntityCollector.LogBlockSummary` — group by source_block + layer để verify BOX keywords coverage

### Rule thay đổi so với cont.5

- **BF definition:** REPLACED. Cũ: SE của elem contacts BS. Mới: LONG edge face contact với BS (contact.length ≥ obb_width × 2).
- **SE contact BS (ngắn):** Không còn là Bracket. Giờ classify như WebPlate (end-to-end contact).
- **OB/IB:** Chỉ từ SE contact Stiffener (không BS).
- **BOX context entities:** Bypass hoàn toàn bracket detection → ClosingBoxWeb.

### Trạng thái Phase

```
UPDATE 1 (Box keywords + source context)       : ██████████ 100% build OK
UPDATE 2 (TopologyEngine long-face + BA)       : ██████████ 100% build OK
UPDATE 3 (Block summary log)                   : ██████████ 100% tích hợp trong U1
Test thực tế                                   : ░░░░░░░░░░   0% — chờ NETLOAD scan
```

### Bước tiếp theo

```
Test: NETLOAD DLL mới → scan panel có block BX hoặc BF bracket
Expected log:
  [BlockEntityCollector] BOX: CIDO.L_BX25 → 4 entities
  [BoxDetect] Auto-detected box block: ... (nếu có unknown)
  [BlockSummary] CIDO.L_BX25|Mechanical-AM_0=4
  [TopologyEngine] [BOX] {handle} → ClosingBoxWeb (source=...)
  [TopologyEngine] {handle} long-edge contact BS (Xmm ≥ Ymm) → BF
  [BracketAnalyzer] Bracket {handle}: BF (pre-classified by TopologyEngine)
```

### Ghi chú kỹ thuật

- `ComputeParallelOverlap`: parallel tol = sin(5°) ≈ 0.087, perp dist < 2mm, projection overlap
- `IsLikelyBoxBlock`: heuristic ≥2 AM_0 + 0 stiff/flange → auto BOX
- ClosingBoxDetector vẫn chạy sau, có thể detect thêm từ WebPlate share-edge

---

## [MODULE:DetailDesign] Session 2026-04-20 cont.5 — BracketAnalyzer Short-Edge Classification

### Đã làm

- `Services/DetailDesign/Parameters/BracketAnalyzer.cs` — Thêm `GetShortEdgeVertexPairs` + `FindNearestAtShortEdge` + `PointToPolyDist`
- `Services/DetailDesign/Parameters/BracketAnalyzer.cs` — Thay toàn bộ classify logic: dùng SE vertex (TOLERANCE_CONTACT 1mm) thay `FindNearest` toàn shape (BRACKET_END_GAP_MAX 35mm)
- Priority mới: (1) SE tiếp xúc Stiffener → OB/IB; (2) SE tiếp xúc BS → BF; (3) không có → B
- Fallback bended shape (SE null): dùng `FindNearest` cũ 35mm — không regression với knuckle stiffener
- Rule confirmed: BS ở cạnh DÀI không ảnh hưởng classify. Stiffener ở SE > BS ở long edge.

### Rule đã confirm và lưu vào memory

- **Bracket strict rule:** short edge 1 = WebPlate, short edge 2 = Stiffener/BS. Long edge contact không tính.
- **OB/IB vs BF:** short edge contacts Stiffener → OB/IB; short edge contacts BS → BF; BS ở long edge bị bỏ qua.
- **Type B:** no short edge stiff contact → B, không insert ThrowThickness (đã implement trước).
- **ClosingBoxWeb:** cả 2 short edges tiếp xúc WebPlate → ClosingBoxWeb (fix đã có từ cont.4).

### Trạng thái Phase

```
TopologyEngine bracket detection (short-edge)  : ██████████ 100% implemented
BracketAnalyzer classification (short-edge)    : ██████████ 100% implemented
Fix B (BF isolated → Side-based)               : ██████████ 100% implemented
BracketSubType sync bug                        : ██████████ 100% FIXED
Fix A (OB/IB/BF contact edge SHORT/LONG)       : ██████████ 100% — covered bởi short-edge detect
```

### Bước tiếp theo

```
Test: NETLOAD DLL mới → scan L.BCK201P_Structure
Expected log changes:
  - Part có Stiffener ở SE + BS ở long edge → OB/IB (không phải BF)
  - BF isolated log: "BF isolated → Side-based: ..." 
  - ClosingBoxDetector: > 0 closing boxes
  - Type B: không xuất hiện trong DebugSymbolService TT insert list
```

### Ghi chú kỹ thuật

- `GetShortEdgeVertexPairs`: sort edges by |len - ObbWidth|, sanity check len < 0.6 * ObbLength
- `FindNearestAtShortEdge`: iterate SE1+SE2 vertices → PointToPolyDist cho mỗi candidate
- Fallback condition: `se1 == null` (bended/irregular shape)
- Khi fallback: stiffTol = BRACKET_END_GAP_MAX (35mm); khi short-edge: stiffTol = TOLERANCE_CONTACT (1mm)

---

## [MODULE:DetailDesign] Session 2026-04-20 cont.4 — BF Logic + ClosingBoxWeb Fix

### Đã làm

- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — Fix B: thêm early-return trong `ApplyBracketThrowInherit`: BF isolated → Side-based throw (thay vì kế thừa BS OUTWARD)
- `Services/DetailDesign/PanelScanService.cs` — Bug fix: sync `BracketSubType` từ `bracketModels` → `allElements` TRƯỚC `BaseEdgeEngine.ComputeAll` (thiếu sync khiến Fix B không hoạt động)
- `Services/DetailDesign/Classification/TopologyEngine.cs` — Fix Bracket detection: thay `MinDistance(am0, stiff) < BRACKET_END_GAP_MAX` bằng `TouchesStiffAtShortEdge` — chỉ classify Bracket khi cạnh NGẮN tiếp xúc stiff/BS
- `Services/DetailDesign/Classification/TopologyEngine.cs` — Thêm helper `TouchesStiffAtShortEdge` + `GetShortEdgeVertexPairs` (lấy 2 cạnh ngắn từ ObbWidth)

### Bối cảnh phát hiện

- Panel L.BCK201P_Structure: 4 brackets type B (d2104838, 52e69d4a, b6a84d61, 64460b59) bị classify sai
- Phân tích `b6a84d61`: cạnh ngắn 2 đầu đều tiếp xúc WebPlate → đây là ClosingBoxWeb, không phải Bracket
- Root cause: `MinDistance` check 35mm không phân biệt tiếp xúc cạnh ngắn vs cạnh dài
- Fix: dùng short-edge vertices → nếu không chạm stiff/BS → stays WebPlate → ClosingBoxDetector xử lý → ClosingBoxWeb

### Trạng thái Phase

```
Fix B (BF isolated → Side-based)  : ██████████ 100% implemented, cần test thực tế
BracketSubType sync bug            : ██████████ 100% FIXED
TopologyEngine bracket detection   : ██████████ 100% implemented, cần test thực tế
Fix A (OB/IB/BF theo contact edge SHORT) : ░░░░░░░░░░ 0% — planned (chưa implement)
```

### Bước tiếp theo

```
Test: NETLOAD DLL mới → scan L.BCK201P_Structure
Expected:
  - BF isolated: log "BF isolated → Side-based: efb8b8db throw=..." thay vì "Bracket inherit"
  - ClosingBoxWeb: bracket count giảm, ClosingBoxDetector detect > 0 closing boxes
  - Nếu ClosingBoxDetector vẫn 0: kiểm tra SharesEdge tolerance / group size
Sau test OK: implement Fix A — contact edge SHORT/LONG detection trong BracketAnalyzer
```

### Ghi chú kỹ thuật

- `GetShortEdgeVertexPairs`: sort edges by |len - ObbWidth|, lấy 2 ngắn nhất. Sanity check: loại nếu len > ObbLength * 0.6
- Fallback cho bended shape (vertices > 4): dùng MinDistance toàn bộ shape (cũ)
- Fix B early-return: chỉ trigger khi `BracketSubType == "BF" && cl.IsIsolated`; BF trong web group → Priority 1 vẫn áp dụng bình thường
- BracketSubType sync: dùng `Dictionary<string, string>` ElemGuid → SubType, map vào allElements TRƯỚC khi BaseEdgeEngine chạy

---

## [MODULE:DetailDesign] Session 2026-04-20 — FIX 1-4: BaseEdgeEngine Complete

### Đã làm

- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — FIX 1: split `HUG_DIST_MM=750` thành 3 hằng số: `HUG_DIST_WEB=750`, `HUG_DIST_STIFF=200`, `HUG_DIST_BRACKET=750`
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — FIX 2: `HugCheckWithDistance` đổi từ midpoint-only sang all-vertices check (`p.VerticesWCS` → `checkVerts`)
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — FIX 3: `MergeLinkedBracketCLSpans` — extend stiffener CLSpan bằng cách project tất cả bracket vertices, bỏ perp tolerance (36 merged / 0 skipped trong test)
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — FIX group member face: group member dùng `PickBase(tx,ty)` thay `MemberEdge` từ Pass 1 → [FaceSwap] 7 swaps Δ=6.0mm (= stiffener thickness, đúng)
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` — FIX 4: thêm `ExtractStructuralSegments()` + branch trong Step 1 cho stiffener/BS có >4 vertices mà fail TryGetParallelPair

### Trạng thái Phase

```
Pass 1 (Web CL)            : ██████████ 100% STABLE
Pass 1.5 (Bracket Link)    : ██████████ 100% STABLE
Pass 2 (Throw assignment)  : ██████████ 100% STABLE
CL Merge (stiff+bracket)   : ██████████ 100% STABLE
FIX 4 (knuckle stiff)      : ████████░░  80% — implemented, cần test thực tế
DataState + UI             : ░░░░░░░░░░   0% — CẦN IMPLEMENT
Save Flow                  : ░░░░░░░░░░   0% — CẦN IMPLEMENT
```

### Bước tiếp theo

```
Bước    : Chờ user test FIX 4 với DWG có knuckle/bent stiffener
DLL     : MCGCadPlugin_20260420_133630.dll
Log cần : [FIX4] bent Stiffener/BS → N seg(s), best=Xmm
Sau test: CL-2 — Bracket inherit throw từ linked stiffener (BaseEdge = StiffenerContactEdge)
```

### Ghi chú kỹ thuật

- FIX 4 dùng furthest-pair làm trục chính, chia vertices thành Side A/B bằng perpendicular projection trung vị
- Knuckle detect: góc lệch > 10° giữa 2 edge liên tiếp trong Side A
- Fallback: nếu TryGetParallelPair đã pass (nhiều trường hợp) → FIX 4 không kích hoạt (đúng)
- Chỉ kích hoạt khi: `ElemType ∈ {Stiffener, BucklingStiffener}` AND `VerticesWCS.Length > 4` AND TryGetParallelPair trả false

---
## [MODULE:DetailDesign] Session 2026-04-20 — CL + Throw: Algorithm Finalized

### Đã thảo luận (Claude AI Chat — cần implement)

**Vấn đề phát hiện từ DWG:**
- Edge stiffener (throw OUTWARD) và OB Bracket kề cận (throw INWARD) → ngược chiều → SAI
- 2 CL lines khác nhau dù 2 elements collinear → SAI
- Root cause: Pass 1 không detect Stiffener↔Bracket relationship

**Algorithm decisions đã finalize:**

| Quyết định | Nội dung |
|---|---|
| OB/IB definition | OB = bracket cùng phía với throw stiffener. Throw bracket = CÙNG CHIỀU stiffener (không ngược) |
| Pass 1.5 (MỚI) | Detect bracket↔stiffener contact edge bằng geometry. contact_tol=2mm |
| Pass 2 bracket | Inherit throw từ stiffener. BaseEdge = StiffenerContactEdge. CL dùng stiffener orientation |
| OB/IB classification | dot(vec_stiff→bracket, throw_stiff) > 0 → OB, < 0 → IB |
| DataState | AutoDetected / UserModified / Confirmed / Warning / HashChanged |
| UI Override | Palette-driven [Flip Throw] button, không click trực tiếp DWG |
| Warning | ? mark + red row trong palette tree cho bracket không linked |
| Hash changed | ⚠ mark + red row, block save cho đến khi resolve |
| Save flow | SQLite + XData đồng thời. Block nếu còn ? hoặc ⚠ |
| Multi-drawing | PanelSession per document. Auto-restore khi switch back. Non-modal unsaved banner |

### Files tạo mới
- `.claude/modules/DetailDesign/DD_CONSTRUCTION_LINE.md` ← THÊM VÀO PROJECT

### Trạng thái Phase

```
Pass 1 (Web CL)          : ██████████ 100% STABLE — không đổi
Pass 1.5 (Bracket Link)  : ░░░░░░░░░░   0% — CẦN IMPLEMENT
Pass 2 (Bracket update)  : ░░░░░░░░░░   0% — CẦN IMPLEMENT
DataState + UI           : ░░░░░░░░░░   0% — CẦN IMPLEMENT
Save Flow                : ░░░░░░░░░░   0% — CẦN IMPLEMENT
Multi-Drawing Session    : ░░░░░░░░░░   0% — CẦN IMPLEMENT
```

### Bước tiếp theo
```
Step    : CL-1 — Implement Pass 1.5
File    : Services/DetailDesign/BaseEdgeEngine.cs
Method  : Pass1_5_LinkBracketToStiffener()
Đọc    : .claude/modules/DetailDesign/DD_CONSTRUCTION_LINE.md mục 3
```

### Ghi chú kỹ thuật
- Bracket 1-1 relationship với stiffener (GG3 confirmed)
- BS (BucklingStiffener) cũng là stiffener partner hợp lệ
- Mọi bracket phải có stiffener — nếu không tìm thấy = data issue
- Pass 1.5 data chưa lưu SQLite ngay, chỉ lưu khi user Save
- Multi-drawing: switch A→B→A: palette auto-restore state A ✓

---


## [MODULE:DetailDesign] Session 2026-04-20 — Construction Line + Throw Thickness: Thuật toán tổng hợp & Fix

### Đã làm

**Fix 3 bugs trong `BaseEdgeEngine.cs`:**
- `CLAssignment` thêm `MemberEdgeStart / MemberEdgeEnd` — lưu collinear edge cụ thể của từng member từ Pass 1
- Group member: dùng `MemberEdge` làm BaseStart/BaseEnd (bỏ PickBase) → CL và symbol trên cùng 1 đường
- Isolated element: sau PickBase → `CLSpanStart = BaseStart, CLSpanEnd = BaseEnd` → xóa lệch CL vs insertion point
- Bracket: thêm HugCheck → outer bracket = INWARD (trước đây luôn Side-based)

**DLL deployed:** `MCGCadPlugin_20260420_084620.dll`

---

### THUẬT TOÁN ĐẦY ĐỦ — Construction Line + Throw Thickness

#### PASS 1 — Construction Line Detection

**Input:** tất cả `clParts` = Web / ClosingBoxWeb / Stiff / BS / Bracket (không có Flange)

**Geometry map (V3):**
- Mỗi element → `TryGetParallelPair()` → 2 cạnh dài E1, E2
- `OrientationClass` = LONG (|dx| ≥ |dy|) hoặc TRANS

**Seed loop (seed = WebPlate / ClosingBoxWeb chưa assign, dài nhất):**
```
while (còn web unassigned):
  seed = web dài nhất

  Try edge E1: đếm collinear partners trong corridor seed
  Try edge E2: đếm collinear partners trong corridor seed

  if (partners_E1 == 0 AND partners_E2 == 0):
    → ISOLATED (inner web — không có phần tử nào cạnh nó)
    MemberEdge = E1 (tạm, bị override sau PickBase ở Pass 2)

  else:
    chọn edge có nhiều partners hơn → CL reference edge = (clA, clB)
    ComputeGroupCLSpan(): project TẤT CẢ endpoints lên CL direction → min/max → 1 span thống nhất
    Seed: assign GroupId, GroupCLSpan, MemberEdge = (clA, clB)
    Partners: assign GroupId, GroupCLSpan, MemberEdge = matched collinear edge của từng partner

ISOLATED fallback: Stiff/BS/Bracket không trong group web nào
  MemberEdge = E1 (tạm)
```

**Collinear check (`IsCollinear`):**
- Chỉ check 2 cạnh DÀI V3 của candidate (E1, E2) — KHÔNG check cạnh thickness
- Angle ≤ 0.1° (dot product với seed edge direction)
- Offset ≤ 1mm (perpendicular distance từ candidate edge đến seed edge line)

**Corridor:**
- OBB seed phình 3mm theo short-axis mỗi bên
- Extend 100,000mm theo long-axis (phủ toàn panel)
- Candidate cần có ≥ 1 vertex trong corridor

---

#### PASS 2 — Throw Direction + Base Edge

**Throw direction — theo type + HugCheck:**

| Element type | Logic | Kết quả |
|---|---|---|
| WebPlate / ClosingBoxWeb | HugCheck (≤750mm từ top plate boundary, long edge parallel ≤5°) | Outer → **INWARD** (về tâm panel) / Inner → Side-based |
| Stiffener / BucklingStiffener | HugCheck | Outer → **OUTWARD** (xa tâm panel) / Inner → Side-based |
| Bracket | HugCheck | Outer → **INWARD** (như web, không như stiff) / Inner → Side-based |
| Flange | — | Side-based |

**Side-based rule:**
```
LONG + Port    → (0, -1)
LONG + Stbd   → (0, +1)
LONG + Center → (+1, 0)
TRANS (mọi)   → (+1, 0)
```

**Inward throw:** perpendicular(CL orient), signed toward panel centroid
**Outward throw:** perpendicular(CL orient), signed away from panel centroid

**Base edge + CL span:**

```
Group member (IsIsolated = false, MemberEdge != null):
  BaseStart/BaseEnd = MemberEdge  ← collinear edge từ Pass 1 (KHÔNG dùng PickBase)
  CLSpanStart/End   = GroupCLSpan ← unified span cả group
  → Symbol nằm trên MemberEdge, MemberEdge nằm trên GroupCL line ✓

Isolated element (IsIsolated = true):
  PickBase(E1, E2, throwVec) → edge có midpoint projection THẤP HƠN theo throw direction
  BaseStart/BaseEnd = PickBase result
  CLSpanStart/End   = BaseStart/BaseEnd  ← FIX: CL = base edge, không phải E1 cố định
  → Symbol và CL trên cùng 1 edge ✓
```

**PickBase logic:**
- Tính projection của midpoint E1 và E2 lên throw vector
- Pick edge có projection THẤP HƠN = edge "phía dưới" theo throw direction = face đứng trước khi đo thickness

**SnapThrow:**
- Sau khi xác định base edge → snap throw vector vuông góc với base edge (atan2 theo base direction)
- Đảm bảo arrow vuông góc với CL, tránh lệch do element xiên

**Flange:** xử lý riêng ngoài flow trên, luôn Side-based + PickBase + CLSpan = BaseStart/BaseEnd

---

### CÁC TRƯỜNG HỢP CỤ THỂ VÀ CÁCH XỬ LÝ

#### TH1 — Web plate dài, có stiff/BS/bracket collinear
```
Ví dụ: G0 (seed e5fbdb60, len=2352) — 3 members
Ví dụ: G3 (seed 6f823379, len=2120) — 3 members

Pass 1: Seed chọn edge có nhiều partners → unified CL span phủ cả 3 members
Pass 2: Mỗi member dùng MemberEdge riêng (đoạn của chính nó trên CL line)
        Throw: tất cả qua HugCheck riêng — outer → INWARD/OUTWARD/INWARD theo type
Kết quả: 1 CL line dài + symbol trên từng member tại đúng vị trí của nó ✓
```

#### TH2 — Web plate 4 vertices, không có partner (inner web)
```
Ví dụ: 857bede9, 0e56ca89, 5d0416fa

Pass 1: 0 partners cả 2 edges → ISOLATED
Pass 2: HugCheck → thường false (inner web xa boundary) → Side-based throw
        PickBase → base edge = face gần throw direction hơn
        CLSpan = base edge
```

#### TH3 — Web plate 5+ vertices (bended/irregular)
```
Ví dụ: 395bf172 (5 vertices), 4a73a80e (5 vertices)

V3 TryGetParallelPair: vẫn tìm được cặp cạnh dài song song → E1/E2 hợp lệ
Không làm seed (IsWebSeedEligible chưa filter 5-vertex → thực ra vẫn có thể làm seed)
Pass 2: HugCheck → có thể outer → INWARD ✓
```

#### TH4 — Bracket biên ngoài (outer isolated bracket)
```
Ví dụ: 0ae895d0 (Y≈-42135, đáy panel) — trước: Side-based (0,-1) OUTWARD sai
        25a8048d (X≈46082, cạnh phải)  — trước: Side-based (1,0) OUTWARD sai

Pass 2: HugCheck → true (≤750mm từ boundary, long edge parallel) → INWARD ✓
        Bracket outer nhìn về phía panel interior, cùng chiều với outer web kề ✓
```

#### TH5 — Bracket inner (giữa panel)
```
Ví dụ: 440e136d, 5f53375c (Y≈-36000, X≈45120)

HugCheck → false (xa boundary) → Side-based
Throw đúng (TRANS → (+1,0)) nhưng CL sai trước fix
Sau fix: CLSpan = PickBase edge → CL trùng với symbol ✓
```

#### TH6 — Stiff/BS isolated outer
```
Ví dụ: fea79e7e (Stiff outer phải), d8d834ac (Stiff outer đáy)

HugCheck → true → OUTWARD (xa tâm panel)
Stiff là phần tử "cứng hóa" cạnh web → throw ra ngoài = đúng vật lý ✓
```

---

### TRẠNG THÁI VẤN ĐỀ

#### Đã giải quyết (session này)
- [x] CL không trùng insertion point → CLSpan = base edge sau PickBase cho isolated
- [x] Bracket outer throw sai chiều → HugCheck cho bracket → INWARD
- [x] Group member PickBase pick sai edge → dùng MemberEdge từ Pass 1

#### Còn tồn đọng — chưa rõ / có thể sai

**1. HugCheck dùng `midpoint của long edge` so sánh với top plate segment**
- Hiện tại: dùng midpoint của part's long edge, tính PointToSegmentDist đến từng segment của top plate boundary
- Vấn đề: với element dài (9552mm) midpoint xa các góc panel → có thể miss outer bracket ở góc
- Nên thử: dùng tất cả vertices của element (không chỉ midpoint) → outer nếu BẤT KỲ vertex nào ≤ HUG_DIST

**2. HUG_DIST_MM = 750mm — có thể quá rộng**
- Bracket inner gần stiffener (cách boundary 600-700mm) có thể bị trigger HugCheck → INWARD sai
- Cần observe trên DWG để tinh chỉnh

**3. Bracket trong group (collinear với web) — throw direction**
- Group bracket (collinear với outer web face): HugCheck → INWARD ✓ (logic đúng)
- Group bracket (collinear với inner web): HugCheck → false → Side-based
- Chưa test case bracket group với inner web — có thể cần centroid rule thay Side-based

**4. Bended web (5+ vertices) làm seed**
- `IsWebSeedEligible` không filter theo số vertices → bended web có thể làm seed
- V3 vẫn tìm được E1/E2 → corridor dựa trên E1/E2 đúng
- Nhưng partners collinear với bended web edge (không phải rectangular) có thể miss
- Quan sát: G5 seed = 395bf172 (5-vertex bended) → 3 members → có vẻ hoạt động

**5. Inner web với partners = 0 bị mark ISOLATED**
- Inner web không có partners → side-based throw → có thể đúng/sai tùy context
- Một số inner web thực tế có stiff song song nhưng offset > 1mm → không vào group → isolated
- Khi đó throw của web và throw của stiff cạnh nó có thể nhìn không nhất quán

**6. SnapThrow với element xiên (111d8114: rot=-64.2°, 5d0416fa: rot=-115.8°)**
- Bended web 5-vertex → V3 tìm cặp cạnh dài → base edge xiên
- SnapThrow: snap throw vuông góc base edge → throw cũng xiên
- Cần verify trên DWG: arrow có vuông góc với base edge không, có hợp lý không

**7. CL span cho inner web isolated**
- Hiện tại: CLSpan = E1 (tạm) → sau PickBase: CLSpan = base edge
- Với inner web isolation, CL = chính nó, không gộp với web nào → 1 CL per web
- Nếu 2 inner web trên cùng 1 plane (offset 0mm) → vẫn vẽ 2 CL riêng (chưa merge)

---

### Trạng thái Phase

```
Phase A — Core scanning + debug symbols:
  Pass 1 CL detection    : ██████████ 100% (stable)
  Pass 2 throw direction : ████████░░  80% (HugCheck OK, edge cases còn verify)
  Debug symbol rendering : ████████░░  80% (CL + TT block, DASHDOTX2 pending)
  Bracket classification : ██████░░░░  60% (OB/IB vẫn sai)
```

### Bước tiếp theo

- NETLOAD `MCGCadPlugin_20260420_084620.dll` → test trên DWG
- Verify: `0ae895d0`, `25a8048d` → arrow INWARD
- Verify: `faa28d38`, `440e136d`, `5f53375c` → CL trùng insertion point
- Verify: group members (G0/G3/G4) → symbol nằm trên CL line đúng vị trí
- Nếu HUG_DIST quá rộng → điều chỉnh từ 750mm → 500mm
- Sau khi CL/throw stable → tiếp BS_symbol, CS_symbol, text labels

---

---

## [MODULE:DetailDesign] Session 2026-04-20 (cont.3) — BF Bracket Logic Analysis (planned, not implemented)

### Đã làm
- **Investigate**: đã research BF bracket flow qua Explore agent (BracketAnalyzer + Pass 1.5 + Pass 2 ApplyBracketThrowInherit + Step 4.5 CL-Merge)
- **Xác định rule mới theo user**:
  - BF connect **từ web plate → BS** qua cạnh nhỏ
  - BF collinear với stiff/web → throw theo **group** web-stiff-bracket
  - BF độc lập → throw theo **inner stiffener/web rule = Side-based**
  - Classification OB/IB/BF dựa vào **cạnh nhỏ** của bracket kết nối với gì (Stiffener → OB/IB; BS → BF)

### Vấn đề đã xác định — chưa fix

**Vấn đề A (Classification không check cạnh nhỏ):**
- Current: `BracketAnalyzer` dùng min distance bracket↔stiff/BS, không check contact edge là long hay short của bracket
- Hậu quả: bracket có cạnh DÀI chạy parallel với stiff/BS có thể bị classify thành OB/IB/BF, đáng lẽ là partner của group
- Fix plan: Pass 1.5 ghi nhận contact edge length class (LONG/SHORT); BracketAnalyzer chỉ class OB/IB/BF khi contact edge là SHORT edge của bracket
- File impact: Services/DetailDesign/Parameters/BracketAnalyzer.cs + BaseEdgeEngine.cs (Pass 1.5)
- Risk: cao — touch logic classification core

**Vấn đề B (BF độc lập inherit BS thay vì Side-based):**
- Current: BF isolated (không trong web group) → Priority 2 LinkedStiffenerGuid → inherit BS throw
- Issue: BS outer → BS throw = OUTWARD/INWARD (per config) → BF cũng sai direction
- Fix plan: trong `ApplyBracketThrowInherit`, thêm check đầu method:
  ```csharp
  if (bracket.BracketSubType == "BF" && cl.IsIsolated)
  {
      ComputeSideBasedThrow(g.Orient, panel, out tx, out ty);
      PickBase + SnapThrow + assign BaseStart/End + CLSpan = bs/be
      return;
  }
  ```
- File impact: Services/DetailDesign/Parameters/BaseEdgeEngine.cs — `ApplyBracketThrowInherit` method (~line 725)
- Risk: thấp — 1 block condition, không đụng logic khác

**Vấn đề C (BF collinear với isolated stiff):**
- Stiff isolated → Side-based; BF collinear với nó cũng Side-based → throw trùng
- Không có bug thực sự, skip

### Trạng thái
- Build OK, commit `54d7592` đã push local
- BF logic: phân tích xong, chờ implement Fix B trước (nhanh), Fix A làm session sau

### Bước tiếp theo (theo thứ tự ưu tiên)

**Ưu tiên 1 — Fix B (quick win):**
- File: `Services/DetailDesign/Parameters/BaseEdgeEngine.cs`
- Method: `ApplyBracketThrowInherit` (~line 725)
- Thêm early-return cho BF isolated → Side-based
- Verify: BF trong panel có BS outer → throw phải Side-based, không OUTWARD
- Verify: BF trong web group → vẫn inherit web seed (Priority 1 không đổi)

**Ưu tiên 2 — Fix A (classification):**
- File: `Services/DetailDesign/Parameters/BracketAnalyzer.cs` + `BaseEdgeEngine.cs` (Pass1_5_LinkBracketToStiffener)
- Lưu `ContactEdgeIsShort` bool trên `StructuralElementModel` (hoặc via map riêng)
- BracketAnalyzer chỉ classify OB/IB/BF nếu ContactEdgeIsShort=true
- Bracket có long-edge contact → partner của Pass 1 group, không thuộc OB/IB/BF/B
- Risk: touch Pass 1.5 + BracketAnalyzer — cần test nhiều panels

### Ghi chú
- Current BS throw inheritance: BS inner → Side-based (may mắn khớp Fix B cho nhiều case); nhưng BS outer + OuterStiffOutward=true → OUTWARD, sai rule
- Pass 1 hiện chỉ seed từ Web/CBWeb, không seed stiff → BF collinear với stiff độc lập không tự vào group; nhưng throw tự đúng vì cả hai đều Side-based

---

## [MODULE:DetailDesign] Session 2026-04-20 (cont.2) — Panel Identity Fix + User/Revision Tracking

### Đã làm — 4 updates theo thứ tự

**U1: Schema migration (SchemaInitializer.cs)**
- Thêm 4 columns vào `panels` qua idempotent ALTER TABLE: `drawing_filepath`, `created_by`, `updated_by`, `revision`
- Method `ColumnExists()` dùng `pragma_table_info` check trước khi ADD
- Unique index `idx_panels_handle` trên `root_block_handle WHERE NOT NULL`

**U2: UpsertPanel duplicate detection (DetailDesignRepository.cs + Interface)**
- Đổi `void → string UpsertPanel(...)` — return guid final + mutate panel.Guid in-place
- Duplicate lookup: `root_block_handle` primary → fallback `name+side+drawing_filepath`
- Found → UPDATE (giữ guid, created_at, created_by), set updated_by = LOGINNAME
- Not found → INSERT với created_by + updated_by
- `GetLoginName()`: AutoCAD LOGINNAME sysvar, fallback Environment.UserName
- `GetActiveDrawingFilepath()`: `MdiActiveDocument.Name`
- Callers updated: `PanelScanService.ScanPanel` + `VM.ExecuteSaveToDb` → re-sync `elem.PanelGuid = finalGuid` trước UpsertElement

**U3: Revision từ CAS_HEAD (SubBlockClassifier.cs + PanelContext.cs + PanelScanService.cs)**
- `PanelContext.Revision` field mới
- `SubBlockClassifier.ReadRevisionFromAssy(assyId, tr)` — traverse nested blocks, match `Normalize(name)=="cashead"`, đọc attribute `ARAS_DOCREVISION`
- `SelectPanel` gọi sau `ClassifySubBlocks`, set `panel.Revision`
- Log: `CAS_HEAD found, revision=RevX` / `CAS_HEAD not found in Assy`

**U4: Cleanup duplicates (SchemaInitializer.cs)**
- Bảng mới `schema_migrations (migration_name PK, applied_at)`
- `CleanupDuplicatePanelsIfNeeded(conn)`: check đã chạy chưa → DELETE elements của dup panels → DELETE dup panels (giữ MIN(guid) = oldest) → INSERT flag
- Chạy trong Transaction để atomic

### Verify
- Build 0 errors all 4 steps
- NETLOAD DLL mới → scan cùng 1 panel 2 lần → DB chỉ 1 row
- DebugView: log `[SubBlockClassifier] CAS_HEAD found, revision=...` hoặc `not found`
- Query: `SELECT name, COUNT(*) FROM panels GROUP BY root_block_handle HAVING COUNT(*)>1;` → rỗng

### Bước tiếp theo
- Test end-to-end: open clean DB, scan panel, re-scan → verify duplicate handling
- Check `schema_migrations` row sau lần init đầu

---

## [MODULE:DetailDesign] Session 2026-04-20 — Uniform CL Align + Outer Dir Option + Bracket Inherit

### Đã làm
- **BaseEdgeEngine.cs**:
  - Thêm 2 config static `OuterStiffOutward` (default true), `OuterWebOutward` (default false)
  - Pass 2 chia 2 sub-pass: (A) non-bracket xử lý trước; (B) bracket inherit từ parent:
    - Priority 1: web group-mate → inherit throw từ web seed
    - Priority 2: `LinkedStiffenerGuid` (Pass 1.5) → inherit từ stiffener
    - Fallback: HugCheck (nghe OuterWebOutward)
  - Outer web/stiff compute direction theo config statics (OUTWARD hoặc INWARD)
  - **Step 4.3 mới** `AlignUniformThicknessGroupCLs`: với nhóm collinear có thickness đồng nhất (Δt<0.1mm) → recompute CLSpan bằng cách project `BaseStart/End` lên CL direction. Nhóm mixed thickness giữ logic cũ (CL trên MemberEdge face).

- **DetailDesignViewModel.cs**:
  - 4 property mới: `OuterStiffOutward/Inward`, `OuterWebOutward/Inward` (auto-sync cặp)
  - Setter propagate vào `BaseEdgeEngine` statics + trigger `RecomputeThrowAndRefresh()`
  - Method `RecomputeThrowAndRefresh()`: gọi lại `BaseEdgeEngine.ComputeAll` trên `_classifiedElements` (không re-scan DWG); nếu `DebugSymbolsShown` → `DebugSymbolService.Refresh()`

- **DetailDesignView.xaml**: thêm Row 3 trong Parameters Border với 2 cặp RadioButton `OuterStf: Out/In` + `OutWb: Out/In` — GroupName exclusive, bind TwoWay vào VM

### Hiệu ứng
- User toggle radio → plugin tự recompute throw + redraw debug symbols ngay
- Nhóm same-thickness: CL và throw symbol trên cùng face (hết FaceSwap visual mismatch)
- Bracket luôn nghe theo web/stiff parent — không còn trường hợp bracket ngược chiều với stiffener của nó

### Bước tiếp theo
- Test UI: toggle radio, verify throw symbol flip đúng hướng
- Verify [UniformCL] log để check groups nào được align

---

## [MODULE:DetailDesign] Session 2026-04-17 (cont.4) — CL Span Fix + PickBase Always

### Đã làm
- `StructuralElementModel`: thêm `CLSpanStart`/`CLSpanEnd` (unified group CL span, tách khỏi BaseStart/BaseEnd)
- `BaseEdgeEngine.CLAssignment`: thay `CLStart/CLEnd` → `GroupCLStart/GroupCLEnd`
- `BaseEdgeEngine.BuildConstructionLineGroups`: sau khi form group → gọi `ComputeGroupCLSpan()` project tất cả member endpoints lên CL direction → unified span; isolated/inner-web → E1 edge
- `BaseEdgeEngine`: thêm helper `ComputeGroupCLSpan`
- `ApplyThrowByTypeAndBoundary`: bỏ `usePickBase` param — luôn gọi `PickBase` cho BaseStart/BaseEnd; set `CLSpanStart/CLSpanEnd` từ CLAssignment
- `DebugSymbolService.InsertConstructionLines`: vẽ từ `CLSpanStart→CLSpanEnd` thay vì `BaseStart→BaseEnd`; thêm deduplication by span key (group members chỉ vẽ 1 đường CL chứ không vẽ chồng N lần)

### Kết quả fix
- Gap 1 (MAIN): CL không còn phân mảnh — mỗi group có 1 đoạn span thống nhất
- Gap 2: BaseStart/BaseEnd = throw-side face (PickBase) riêng, CLSpanStart/CLSpanEnd = CL visualization riêng
- Gap 3 & 4: Tất cả elements (group và isolated) đều dùng PickBase → throw symbol luôn đúng face

### Bước tiếp theo
- Test trong AutoCAD: verify G3 (web + 2 brackets) vẽ 1 CL duy nhất đúng span
- File: Services/DetailDesign/Parameters/BaseEdgeEngine.cs | Kiểm tra log CLSpan

---

## [MODULE:DetailDesign] Session 2026-04-17 (cont.3) — Simplified Algo + Outer Web Detection

### Simplified Algorithm (bản cuối)

**Pass 1 — Construction Line (seed = Web only)**:
```
Seed      : longest WebPlate/ClosingBoxWeb với EXACTLY 4 vertices rectangular
Candidates: clParts (web/stiff/BS/bracket) trong corridor seed
Collinear : chỉ check 2 cạnh DÀI V3 của candidate (bỏ qua cạnh thickness)
            angle ≤ 0.1°, offset ≤ 1mm
Pick edge : seed's long edge có nhiều collinear partners hơn → CL chung cho group
Group     : seed + matched partners → remove từ unassigned

Inner web : web seed với 0 partners cả 2 edges → mark ISOLATED
Isolated  : parts còn lại (stiff/BS/bracket không gần web NÀO, web 5+ vertices)
```

**Pass 2 — Throw Thickness**:
```
Group member : throw = perpendicular(CL) signed toward PART's OWN centroid
Isolated     : Side-based per PanelSide (LONG: Port→-Y/Stbd→+Y/Center→+X; TRANS: +X)
Flange       : Side-based (không vào CL detection)
```

### Đã bỏ
- MIXED/SAME thickness classification
- Outer/inner detection via hug distance cho group members
- Representative picking rule
- Dynamic stiffener spacing
- Stiff/BS/Bracket không làm seed nữa

### Bug fix session này
- `FindCollinearSegment` duyệt mọi segment → gán sai bracket 290×6mm vào nhóm vuông góc
  qua cạnh thickness 6mm. Fix: chỉ check 2 cạnh dài V3 (E1/E2).

### Test result trên N.BCK608P (Port, 60 elements)
```
Pass 1: 43 groups, 40 isolated
Groups: G0 (4303d426, 2352mm horizontal), G3 (1f2255ce, 2120mm), G4 (bff1fab0, 1837mm)
Inner webs (no partners): f2d69af8, 5dbb88c6, 32a6b56a, 6da97b19, 35cba274
Isolated (non-web or 5-vertex): stiff/BS/bracket + webs bended (dbf16358, c455226a)
```

### 🔴 Vấn đề phát hiện: outer web throw sai hướng

Cả 3 parts user chất vấn:
- `405b1b31` Bracket (9324×6, vertical tại X=46246)
- `c455226a` WebPlate 5-vertex bended (X=46242)
- `dbf16358` WebPlate 5-vertex bended (X=46242)

Cả 3 nằm biên PHẢI panel (X≈46xxx, panel centroid X=40184). Physical:
- Body web vertical nằm phía LEFT (về tâm panel)
- Outer web construction line = RIGHT edge
- Outer web throw kỳ vọng = **INWARD = -X** (về tâm panel)

Current algo: Side-based TRANS → throw **+X** → NGƯỢC kỳ vọng.

**Root cause**: Simplify đã bỏ hoàn toàn outer/inner detection. Mọi isolated web
dùng Side-based → sai cho outer web (which should throw INWARD).

### Fix plan: outer web detection cho ISOLATED webs

```
IsolatedWeb:
  Hug check: vertex gần top plate boundary (≤ HUG_DIST) AND cạnh dài parallel
  if outer  → throw INWARD (toward panel centroid)
  if inner  → throw Side-based (như hiện tại)

Params:
  HUG_DIST_WEB = 2× dominant stiffener spacing (dynamic) hoặc 600mm fallback
  HUG_ANGLE_TOL = 5°

Isolated Stiff/BS/Bracket: KHÔNG đổi — giữ Side-based
```

### Files touched
- [BaseEdgeEngine.cs](Services/DetailDesign/Parameters/BaseEdgeEngine.cs) — Pass 1 + Pass 2 rewrite
- [DebugSymbolService.cs](Services/DetailDesign/DebugSymbols/DebugSymbolService.cs) — exclude Flange từ CL drawing

### Deploy sequence
- `MCGCadPlugin_20260417_143325.dll` — 2-pass initial
- `MCGCadPlugin_20260417_144830.dll` — fix collinear long-edge only
- `MCGCadPlugin_20260417_151954.dll` — simplified (seed=web, centroid rule)
- `MCGCadPlugin_20260417_154124.dll` — inner web fallback
- Pending — outer web INWARD fix

---

## [MODULE:DetailDesign] Session 2026-04-17 — Construction Line + Throw Thickness FINAL SPEC

### Quyết định kiến trúc
Tách 2 pass độc lập, thay thế cơ chế "diff-thickness detect + throw" đơn khối cũ (root cause override bug session trước).

```
Pass 1 — ConstructionLineEngine: xác định (BaseStart, BaseEnd) per part + group_id
Pass 2 — ThrowEngine           : compute throw vector từ CL + context (centroid/outer/side)
```

### Construction Line — Algorithm unified cho mọi part type

Scope part: Web, corner closing-box, Stiffener, BS, Bracket (KHÔNG áp cho Flange).

```
PARAMS:
  ANGLE_TOL        = 0.1°
  OFFSET_TOL       = 1mm
  CORRIDOR_GAP     = 3mm (short axis mỗi bên)

LOOP while H contains 4-seg rectangular polyline:
  SEED  = argmax(length) trong 4-seg rectangular subset
  corridor = OBB(SEED) phình 3mm short-axis + extend long-axis vượt panel bounds
            (polygon bám theo point list, KHÔNG phải AABB xmin/ymin/xmax/ymax)
  candidates = {obj ∈ H : OBB(obj) ∩ corridor ≠ ∅, obj ≠ SEED}
  filtered   = {c : ∃ seg of c, angle(seg.dir, seed_long_dir) ≤ 0.1°}
  group      = {SEED} ∪ {c ∈ filtered : ∃ edge of c collinear with seed_long_edge}
  CL         = line spanning union of collinear edges
  H −= group

ISOLATED (phần còn lại):
  CL = OBB.mid_axis(obj) ; flag isolated = true

COLLINEAR CHECK:
  parallel = |n1·n2| ≥ cos(0.1°)  (normalized, accept 0° & 180°)
  offset   = |d1 − d2| ≤ 1mm       (signed distance to origin)
  ép dấu normal: n.x > 0 (hoặc n.y > 0 nếu n.x ≈ 0) tránh miss match do hướng ngược
```

### Throw Thickness — Per group classification

```
classify_thickness(group):
  max(t) − min(t) ≤ 0.5mm → SAME
  else → MIXED

CASE MIXED (per-part centroid rule):
  for part in group:
    throw[part] = perpendicular(CL) signed toward centroid(part)
  # Flush edge vật lý tự xác định throw, không override.

CASE SAME (per-group via representative):
  rep = priority {Web → Stiffener → first_part}
  
  is_outer = check_outer(rep):
    hug = (rep.type == Web) ? 2 × dominant_stiff_spacing : 200mm
    return ∃ vertex(rep) within hug of top_plate_boundary
           AND parallel(rep.long_edge, boundary_seg, ≤ 5°)
  
  for part in group:
    if is_outer:
      part.type ∈ {Stiff, BS} → OUTWARD (xa tâm panel)
      part.type == Web        → INWARD  (về tâm panel)
      part.type == Bracket    → follow stiff trong group (fallback: rep)
    else:  # inner
      throw = Side-based per PanelSide
        LONG (∥X) : Port→-Y, Stbd→+Y, Center→+X
        TRANS(∥Y) : +X mọi panel

ISOLATED:
  throw = Side-based per PanelSide
```

### Physical invariants (chốt với user)
1. Stiffener KHÔNG hàn ốp vào web; web KHÔNG hàn ốp vào web.
2. Doubling plate, lug plate — KHÔNG xét.
3. Mỗi part = 1 construction line duy nhất (không có 2 edges cùng collinear với 2 nhóm khác nhau).
4. Conflict diff-thick vs flanged: diff-thick THẮNG (nhưng với spec mới, không còn conflict vì đã tách pass).

### Rule E1-E4 chốt
- E1 (SAME group không có stiff): representative fallback theo `web → stiff → single`.
- E2 (multiple stiff conflict): bất kỳ 1 stiff edge → group = outer.
- E3 (OUTER group): throw direction theo representative rule (web inward / stiff outward).
- E4 (MIXED): luôn centroid rule, không override.

### Debug toggle behavior (mới)
```
Button "Show Debug" (ON)  → insert construction line (AM_18, DASHDOTX2, RED)
                          + throw thickness block (AM_2)
Button "Hide Debug" (OFF) → erase TẤT CẢ entity có XData app = "MCG_DEBUG_SYM"
                          (cả construction line VÀ throw thickness)
```

### TODO để sau
1. **Manual override throw per part**: XData/SQLite field, UI "Flip Throw" button, preserve qua rescan.
2. **DASHDOTX2 linetype**: load từ `acad.lin`.

### Bước tiếp theo
1. Implement `ConstructionLineEngine` trong `Services/DetailDesign/Geometry/`
2. Rewrite `BaseEdgeEngine` → tách `ThrowEngine` thuần hình học + context adapter
3. Update `DebugSymbolService` để vẽ construction line + block trong cùng 1 toggle
4. NETLOAD test trên `CAS-0051566.dwg`

---

## [MODULE:DetailDesign] Session 2026-04-16 (cont.2) — Throw Direction Debug + Root Cause Found

### Vấn đề chính phát hiện

**Diff-thickness rule (Priority 1) OVERRIDE throw direction sai cho outer beams.**

Case cụ thể:
```
dc68c306 WebPlate t=10mm — outer beam ở bottom panel
097362b5 WebPlate t=6mm  — collinear, diff thickness

Diff-thickness: flush edge → construction line ✓, throw INTO thickness = DOWN ✗
Flange-based:   linked to flange → throw = UP (toward panel center) ✓

Kết quả: isDiffThick checked TRƯỚC → override flange throw → DOWN (sai!)
```

### Root cause

`DetectAdjacentDiffThicknessWebs` set CẢ BaseStart/BaseEnd (construction line) VÀ ThrowX/ThrowY (direction). Khi element vừa là diff-thickness VÀ là flanged outer beam, diff-thickness throw override flanged throw.

### Fix plan (chưa implement)

**Tách 2 concerns**:
```
1. Diff-thickness → CHỈ xác định construction line (BaseStart/BaseEnd)
   KHÔNG set throw direction
   
2. Throw direction determined by SEPARATE logic:
   - Nếu flanged outer beam → perpendicular from base, AWAY from flange
   - Nếu outer web (dynamic hug) → INWARD toward panel center
   - Nếu inner web → Side-based
   - CHIỀU throw = perpendicular from construction line, side determined by outer/inner rule
```

### 3 issues tồn đọng

| # | Issue | Status | Fix |
|---|---|---|---|
| 1 | Diff-thickness override throw | **ROOT CAUSE FOUND** | Tách: diff-thick → base only; flange/outer → throw direction |
| 2 | DASHDOTX2 linetype not found | Log warning | Load từ acad.lin hoặc tạo linetype |
| 3 | Collinear web cùng thickness | Partially works | Chain propagation cần mở rộng cho cùng thickness |

### Bug fixes đã làm (trong session này)
1. Construction line RED: `ColorIndex = 1` ✓
2. Resizable sections: DataGrid `1*` MinHeight=60 ✓
3. Arbitrary web grouping: angle threshold 20°/70° thay 45° ✓

### Kết quả test
- 25 plannar groups computed ✓
- 10/12 webs detected as outer (via flange) ✓ (nhưng throw direction sai cho 1 số)
- 4 webs diff-thickness resolved ✓ (nhưng throw sai do override)
- Construction lines drawn 58 lines ✓ (nhưng DASHDOTX2 not found → Continuous fallback)
- DataGrid hiện trên palette ✓

### Code deployed: `MCGCadPlugin_20260416_151438.dll`

### Bước tiếp theo khi mở lại

1. **Fix Priority 1**: Tách `DetectAdjacentDiffThicknessWebs` → chỉ return BaseStart/BaseEnd (construction line), KHÔNG return throw. Throw computed separately by flange/outer/inner rule.

2. **Fix DASHDOTX2**: Ensure linetype loaded. AutoCAD có thể cần `db.LoadLineTypeFile(LINETYPE, "acad.lin")`.

3. **Fix collinear cùng thickness**: Extend chain propagation cho webs cùng thickness (endpoint-near + collinear edge → same construction line + inherit throw).

4. **Verify full throw matrix** trên DWG:
   - Outer beam (flanged): throw away from flange ✓
   - Outer beam (diff-thickness + flanged): construction line from diff-thick, throw from flange ✓
   - Inner web: Side-based ✓
   - Edge stiff/BS: outward ✓
   - Bracket: Side-based ✓

5. Continue pending phases:
   - BS_symbol, CS_symbol, text labels
   - Bended plate split (Phase B)
   - Flip sync

---

## [MODULE:DetailDesign] Session 2026-04-16 (cont.1) — Full Batch Implementation + 3 Bug Fixes

### Đã implement (full batch)

**Phase 1 — Adjacent diff-thickness web detection**
- `DetectAdjacentDiffThicknessWebs()` — collinear edge giữa 2 web khác thickness = construction line
- `MakeThrowFromConstructionLine()` — throw perpendicular from flush edge vào thickness
- Priority 1 trong ComputeAll flow

**Phase 2 — Dynamic outer web detection**
- `ComputeDynamicHugDistance()` — 2× max spacing of dominant stiffener group (auto)
- `IsNearTopPlateBoundaryWithHug()` — vertex + parallel + dynamic distance
- Fallback cho webs không resolved by diff-thickness hoặc flange

**Phase 3 — Plannar groups + DataGrid**
- `Models/DetailDesign/PlannarGroup.cs` — model
- `Services/DetailDesign/Parameters/PlannarGroupService.cs` — cluster by position (5mm tolerance)
- DataGrid section dưới tree trong DetailDesignView.xaml (dark theme, columns: ID|Pos|Members|Throw)

**Phase 4 — Construction line AM_18**
- `InsertConstructionLines()` trong DebugSymbolService — Line on AM_18, DASHDOTX2, RED (ColorIndex=1)

### 3 Bug fixes (cuối session)
1. **Construction line color**: thêm `ColorIndex = 1` (RED) cho line entity
2. **Resize sections**: DataGrid row `1*` MinHeight=60 thay vì Auto MaxHeight=150 → resizable bằng GridSplitter
3. **Arbitrary web grouping**: dùng angle thực tế (< 20° = LONG, > 70° = TRANS, else = A) thay vì OrientationClass 45° threshold; position dùng perpendicular distance from origin cho arbitrary

### Vấn đề CÒN TỒN TẠI (chưa fix)
1. **Collinear web detection vẫn chưa hoạt động đúng** — `DetectAdjacentDiffThicknessWebs` trả 0 resolved vì webs đều có thickness gần giống nhau (6mm). Cần thêm collinear detection cho webs CÙNG thickness.
2. **Flange-web link**: 2 webs cùng 1 flange → throw ngược nhau (box beam case). Fix cần: per-flange rank webs by distance, link exclusive top 1-2.
3. **Construction line visualization**: đã thêm nhưng vì collinear detection sai → construction line positions chưa chính xác.
4. **Outer web throw vẫn sai** ở một số beam → phụ thuộc vào collinear fix.

### Code deployed: `MCGCadPlugin_20260416_151438.dll`

### Bước tiếp theo
1. Fix collinear web detection cho cùng thickness (Ưu tiên 1 mở rộng hoặc mechanism khác)
2. Fix flange-web exclusive link (box beam)
3. Test toàn bộ throw trên DWG
4. Bended plate Phase B (split) vẫn pending
5. BS_symbol, CS_symbol, text labels (CG/OB/IB/BF/B) vẫn pending

---

## [MODULE:DetailDesign] Session 2026-04-16 — Throw Rule Final Spec + Flange-based Outer Beam + Plannar Groups

### Đã làm

**Flange-based outer beam detection — IMPLEMENTED (có bug)**
- `DetectOuterBeamsViaFlange()` trong BaseEdgeEngine — link Flange ↔ Web via collinear edge, offset ≤ flange.ObbWidth
- Bug found: 2 webs link cùng 1 flange → throw ngược nhau (box beam case chưa xử lý đúng)
- Chain propagation (collinear flush edge): implemented nhưng 0 chain do link sai

**Bended plate detection Phase A — DONE**
- `BendedPlateAnalyzer.DetectAndLog()` — 11 bended plates detected trên DWG test
- Chưa split (Phase B pending)

**Construction line visualization — SPEC (chưa implement)**
- Layer: Mechanical-AM_18, linetype DASHDOTX2
- Draw BaseStart → BaseEnd cho mỗi element

### SPEC CUỐI CÙNG — Throw Thickness cho Web Plate

**Priority order**:
```
Ưu tiên 1: Web liền kề (endpoint-near) có chiều dày KHÁC nhau
  → Cạnh collinear giữa 2 web = construction line
  → Throw = hướng chiều dày (perpendicular từ construction line)
  → Áp dụng CẢ inner + outer, CẢ corner + structure block
  → Detection: collinear edge check

Ưu tiên 2: Nếu không có web liền kề khác thickness
  → Inner web: Side-based (Port→-Y, Stbd→+Y, Center→+X cho LONG; +X cho TRANS)
  → Outer web (edge web): HUG_DISTANCE = 2× max dominant stiffener spacing
    (dominant = orientation group có SỐ LƯỢNG stiffener NHIỀU NHẤT)
    (auto-calculated, KHÔNG hardcode)

Ưu tiên 3: Box beam
  → Check web liền kề khác thickness TRƯỚC (Ưu tiên 1)
  → Nếu không → check outer (Ưu tiên 2)
  → Nếu không → inner rule
```

**Flange-Web link rule update**:
- 1 flange : 1 web (T-beam) hoặc 1 flange : 2 webs (box beam)
- Per flange: rank candidate webs by distance, top 1-2 = linked
- Box beam: 2 webs throw NGƯỢC nhau (each away from flange)

### SPEC — Plannar Groups

**Concept**: Gộp elements có construction line COLLINEAR → 1 plannar group (mặt phẳng ⊥ plan view xOy).

**Groups**:
```
T  = Transversal web (phương Y, mặt y=b)     → sort by Y, đánh số T-1, T-2...
L  = Longitudinal web (phương X, mặt x=a)    → sort by X, đánh số L-1, L-2...
A  = Arbitrary web (xiên)                      → sort by angle+offset, A-1, A-2...
ST = Transversal stiffener                     → sort by Y
SL = Longitudinal stiffener                    → sort by X
BS = Buckling stiffener                        → sort by position
```

**Tolerance**: 2 construction lines "cùng plane" khi offset ≤ **5mm**.

**UI**: DataGrid section mới trong DetailDesign tab, **dưới tree**.

### Parameters confirmed

```
Adjacent web detection: collinear edge (không cần endpoint-near)
Dominant stiffener: orientation group có SỐ LƯỢNG nhiều nhất
Plannar group tolerance: 5mm
DataGrid: section mới trong DetailDesign tab dưới tree
```

### Files hiện tại (code state)

DLL deployed: `MCGCadPlugin_20260416_140558.dll`
- BaseEdgeEngine: có DetectOuterBeamsViaFlange (buggy — flangeWidth fix OK, nhưng 2-web-1-flange vẫn sai)
- BendedPlateAnalyzer: detection only (no split)
- DebugSymbolService: ThrowThickness insertion (scale=1, rotation fix, toggle button)

### Bước triển khai khi mở lại

**Phase 1 — Adjacent diff-thickness web detection**
1. Method `DetectAdjacentDiffThicknessWebs(webs, infoMap)`
2. Cho mỗi web pair collinear edge → tìm flush edge → construction line
3. Throw = perpendicular from flush edge vào thickness
4. Override throw cho các web pair này (Ưu tiên 1)

**Phase 2 — Dynamic outer web detection**
5. Compute dominant stiffener spacing (count per orientation → max group → compute max spacing)
6. HUG_DISTANCE = 2× max spacing
7. Detect outer webs via chain + dynamic hug
8. Outer web throw: INWARD toward panel center

**Phase 3 — Plannar groups + DataGrid**
9. Group elements by construction line position (tolerance 5mm)
10. Assign T/L/A/ST/SL/BS labels + sort + number
11. DataGrid XAML section dưới tree
12. Bind plannar groups → DataGrid

**Phase 4 — Construction line visualization**
13. Draw AM_18 DASHDOTX2 line per element trong ShowDebugSymbols

**Phase 5 — Fix remaining throw bugs**
14. Box beam: 2 webs per flange, opposite throws
15. Collinear chain propagation fix
16. Verify on DWG

### Blocker / Concerns
- BendedPlateAnalyzer Phase B (split) vẫn pending
- OB/IB classification counts vẫn chưa đúng (39 brackets, counts vary)
- Stiff/BS edge detection chain+hug works nhưng chưa verify hoàn chỉnh

---

## [MODULE:DetailDesign] Session 2026-04-15 — Debug Symbols Phase 3 (ThrowThickness rendering) + Throw Rule redesign

### Đã làm

**Thickness V3 refinement** (earlier in session)
- V2 → V3: search all edge pairs với score = min(len_i, len_j); parallel tolerance 1°; candidate length ratio 0.5.
- `ThicknessCalculator.TryGetParallelPair()` expose 2 cạnh parallel cho caller.

**Phase 3 — ThrowThickness block insertion DONE**
- `Services/DetailDesign/DebugSymbols/DebugSymbolService.cs` — load block từ `C:\CustomTools\Symbol.dwg`, insert BlockReference với XData app `MCG_DEBUG_SYM`.
- `Commands/DetailDesign/DetailDesignCommand.cs` — thêm `MCG_ShowDebugSymbols` (chạy trên document context).
- `Views/DetailDesign/DetailDesignView.xaml` — thêm button "Show Debug" cạnh iProperties.
- Namespace folder đổi `Debug` → `DebugSymbols` để tránh xung đột với `System.Diagnostics.Debug`.
- Scale = 1 (block tự size trong Symbol.dwg: horizontal 25mm, vertical arrow 50mm).

**Bug fix CRITICAL — Position lệch**
- Root cause: `PanelScanService` trước đây truyền cùng `rootTransform` cho MỌI entity, nhưng entities nằm nested trong sub-blocks cần transform tích lũy.
- Fix: `RawEntitySet` lưu `List<EntityRef { ObjectId, Matrix3d }>`. `BlockEntityCollector` lưu accumulated transform per entity. `PrimaryClassifier.ClassifyBatch` dùng transform riêng cho từng entity.

**Rotation formula fix**
- Block arrow default direction = +Y (block local). Công thức cũ `atan2(throwY, throwX)` sai — arrow không chỉ đúng chiều.
- Fix: `rotation = Math.Atan2(-ThrowVecX, ThrowVecY)`.

**Slanted elements — horizontal arm align theo base edge**
- Sau PickBase, snap throw vector vuông góc base edge (match hint throw qua dot product).
- Axis-aligned element: throw không đổi. Slanted: throw perpendicular to base, horizontal arm dọc base ✓.

**UX improvements**
- Toggle button Show Debug ↔ Hide Debug với DataTrigger binding `DebugSymbolsShown` (OFF: gray #555, ON: accent #0078D4).
- Total part count ở root tree node: `"[Panel] [Side] — Total: N"`.

**Throw Rule — đã thay đổi nhiều lần, cần ghi nhớ state cuối**:

Rule đã confirm (chưa implement full):
| Element | Edge | Non-edge |
|---|---|---|
| Stiffener / BS | OUTWARD (xa panel center) | Side-based Port→-Y, Stbd→+Y, Center→+X |
| Web plate | INWARD (về panel center, snap orient axis) | Side-based |
| CB-Web | Nếu có cạnh collinear với cạnh Web plate (tol 5mm) → inherit throw từ Web đó; else → theo Web rule | same |
| Bracket | — (không check edge) | **Always Side-based** |
| Flange | — | Side-based |

**Edge detection (chưa implement — dang chờ confirm params)**:
- Stiff/BS: **Chain-based** với hug check
  - Seed = stiff có min vertex distance đến top plate boundary
  - Grow chain: stiff B được add nếu (a) endpoint ≤ ENDPOINT_GAP với member; (b) B có long edge parallel (≤ 5°) với boundary segment AND perpendicular distance ≤ HUG_DISTANCE
  - Reinforcement: stiff parallel với primary outer, perpendicular distance ≤ REINFORCEMENT_GAP
  - Isolated fallback: min vertex distance ≤ ISOLATED_THRESHOLD
- Web / CB-Web: **Bottom 20% per orientation group** (min 2, max 8)
- Bracket / Flange: không check edge

**Parameters confirmed**:
- ENDPOINT_GAP = **500mm**
- HUG_ANGLE_TOL = **5°**
- HUG_DISTANCE = **200mm** (update từ 100mm)
- REINFORCEMENT_GAP = **500mm**
- ISOLATED_THRESHOLD = **20mm**
- Web bottom K = **20%** per orientation group (min 2, max 8)

### Đang làm dở
- `BaseEdgeEngine` hiện implement: edge detection đơn giản 150mm vertex-based + rule theo element type (outward/inward/side-based/always-inward).
- **CHƯA implement**: chain-based với hug check cho stiff/BS; bottom K% cho web; CB-Web collinear check.

### Code hiện tại đang deploy (DLL `MCGCadPlugin_20260415_070049.dll`)
- Edge detection: 150mm vertex-based (ALL stiff/BS/web)
- Stiff/BS edge → OUTWARD; non-edge → Side-based
- Web edge → INWARD; non-edge → Side-based
- Bracket + CB-Web → always INWARD (ý user đã thay đổi nhiều lần, cần reconsider)
- Flange → Side-based

### Bước tiếp theo khi mở lại
1. Confirm/re-confirm params cuối (user đã nói HUG = 200mm).
2. Implement chain-based edge detection cho stiff/BS:
   - Method `IsEdgeStiffenerViaChain(elem, allStiffs, topPlates)` trong BaseEdgeEngine
   - Seed + grow chain + hug check + reinforcement + isolated fallback
3. Implement bottom K% cho web/CB-Web (separate method `IsEdgeWebViaBottomK`).
4. Implement CB-Web collinear detection: inherit throw từ web có cạnh collinear (tolerance 5mm offset, 1° angle).
5. Update Bracket rule: confirm user xác nhận "always Side-based" ở turn cuối cùng? → code hiện vẫn INWARD cho Bracket, cần sửa sang Side-based.
6. Test trên DWG và verify 4 case: outer web inward, inner web side, outer stiff outward, bracket side.

### Pending phases
- Phase 2: Segment-segment intersection helper + Continuous detection
- Phase 3 (tiếp): BS_symbol × 2 endpoints
- Phase 4: Text labels CG / OB / IB / BF / B
- Phase 5: Flip sync cho ThrowThickness (dynamic block Flip state1)

### Blocker / Concerns
- Chain-based edge detection cần test trên DWG với nhiều shape khác nhau (rectangular, L-shape, U-shape, bậc) trước khi finalize params.
- Inner stiff meeting outer ở corner với endpoint gap 500mm có thể gộp sai → hug check (angle ≤ 5° parallel với boundary segment, distance ≤ 200mm) để khắc phục.

---

## [MODULE:DetailDesign] Session 2026-04-14 (cont.2) — Debug Symbols System spec + Phase 1 core

### Đã làm

**Batch 1 — Logic fixes đầu session**
- Thickness rounding đã đúng sẵn (`Math.Round AwayFromZero`).
- ExecuteSelectPanel tự chain ExecuteScan — bỏ button SCAN/RESCAN khỏi XAML + code-behind.
- Xóa lệnh `MCG_Scan`.
- Apply defaults: TopPlate thickness fallback `DefaultTopPlateThk`, Flange thickness = `DefaultFlangeThk` (không đo từ polyline), width computed riêng cho Properties Panel.

**Fix UX + theme**
- `DisplayUnit` setter KHÔNG rebuild tree nữa → đổi unit không collapse.
- Theme đổi từ #1E1E1E (đen tuyền) → #3C3C3C (xám AutoCAD Ctrl+1): các surface, border, panel sub-background, text primary/secondary đồng bộ.

**Thickness Algorithm V2 → V3**
- V2 fail: top-2 edges đôi khi không phải cặp song song đúng (vì taper dài rank cao).
- V3 = search all edge pairs:
  1. Filter candidate edges length >= 50% max
  2. Với mỗi cặp: check parallel (angle ≤ 1°)
  3. Score = min(len_i, len_j) → cặp score max thắng
  4. Perpendicular distance
- Tolerance: `PARALLEL_ANGLE_TOLERANCE_DEG = 1.0`, `CANDIDATE_LENGTH_RATIO = 0.5`.
- Dùng chung cho cả `Calculate` (thickness) và `CalculateFlangeWidth` (width).

**Debug Symbols System — thu thập spec đầy đủ từ user**

Axis convention: **X = dọc tàu, Y = ngang tàu**.

Throw rules:
- Edge stiff/BS (≤150mm từ top plate boundary) → ra ngoài panel center
- Non-edge LONG (length dọc X): Port(Y>0) → −Y, Stbd(Y<0) → +Y, Center → +X
- Non-edge TRANS (length dọc Y): +X mọi panel
- Slanted 45° sharp: abs(dx) ≥ abs(dy) → LONG, else TRANS
- Bracket/CB-Web: classify độc lập theo OBB chính nó
- Girder = Web plate (không tách type)

Symbols (từ `C:\CustomTools\Symbol.dwg`, layer AM_2 cho block, AM_6 cho text, height 150mm):

| # | Name | Type | Apply | Insert | Rotation | Notes |
|---|---|---|---|---|---|---|
| 1 | ThrowThickness | Dynamic (flip) | Web/Bracket/CB-Web | mid base segment | throw dir | flip user có thể đổi → sync DB |
| 2 | CS_symbol | Static | Stiff continuous qua web | stiff_base ∩ web_base (in segment) | stiff base dir | |
| 3 | BS_symbol | Static | BS only × 2 endpoints | endpoint BS base | BS base dir | |
| 4 | CG (text) | Text | girder continuous qua web | girder_base ∩ web_base | 0° bottom-left | |
| 5 | OB (text) | Text | bracket type OB | stiff_base ∩ bracket_base | 0° bottom-left | |
| 6 | IB (text) | Text | bracket type IB | stiff_base ∩ bracket_base | 0° bottom-left | |
| 7 | BF (text) | Text | bracket type BF | bracket_base ∩ BS_base | 0° bottom-left | |
| 8 | B (text) | Text | bracket type B | stiff endpoint (cách web gap) | 0° bottom-left | |

Trigger: `MCG_ShowDebugSymbols` command + button "Show Debug" cạnh iProperties trên palette. Xóa chỉ entities do plugin tạo (check XData `MCG_PANEL_TOOL`).

**Phase 1 — Core logic (ĐÃ IMPLEMENT)**
- `Models/DetailDesign/StructuralElementModel.cs`: thêm 6 field (BaseStart, BaseEnd, ThrowVecX/Y, IsEdgeElement, OrientationClass).
- `Services/DetailDesign/Parameters/ThicknessCalculator.cs`: thêm `TryGetParallelPair(...)` expose 2 cạnh parallel đã pick; refactor helpers.
- `Services/DetailDesign/Parameters/BaseEdgeEngine.cs` (MỚI): `ComputeAll(elements, panel)` — tính orientation + edge detection + throw vector + base edge cho mọi rectangular element.
- `Services/DetailDesign/PanelScanService.cs`: gọi `BaseEdgeEngine.ComputeAll` sau khi `Classify + Thickness`.

Build OK — DLL `MCGCadPlugin_20260414_162055.dll`.

### Phase còn lại (chờ làm)
- **Phase 2**: Segment-segment intersection helper + Continuous stiff/girder detection
- **Phase 3**: `SymbolInserter` service — load Symbol.dwg, insert ThrowThickness / CS_symbol / BS_symbol với XData link `elem_guid`
- **Phase 4**: Text labels CG / OB / IB / BF / B (layer AM_6, height 150, bottom-left, 0°)
- **Phase 5**: `MCG_ShowDebugSymbols` command + button "Show Debug" trên palette; xóa symbols cũ (theo XData app) trước khi chèn mới
- **Phase 6**: Flip sync — đọc dynamic block `Flip state1` của ThrowThickness, update DB + palette nếu user đã flip

### Verify Phase 1 (chưa test trên DWG thật)
Cần NETLOAD + SELECT PANEL, mở DebugView, check:
1. Log `[BaseEdgeEngine]` ra đủ cho Web/Bracket/CB-Web/Flange/Stiff/BS
2. Orientation LONG/TRANS đúng
3. Edge stiff/BS detect đúng (≤150mm boundary)
4. Throw vector đúng chiều theo rule

### Blocker vẫn còn
- Các bug classification cũ chưa fix: OB=11 vs 14, IB=14 vs 0, B=8 "no stiff contact", Bracket total 39 vs 46 → sẽ verify bằng debug symbols sau Phase 3–5.
- Axis convention vẫn cần verify trên DWG thật: port throw "từ trái qua phải" đã giả định hiểu theo port→stbd của tàu, chưa test.

### Bước tiếp theo khi mở lại
1. NETLOAD DLL mới, verify log Phase 1.
2. Nếu OK → vào Phase 2: tạo `LineSegmentIntersector` utility + `ContinuityDetector` (stiff-qua-web, girder-qua-web).
3. Nếu log sai → tune rule trong `BaseEdgeEngine.ComputeNonEdgeThrow` hoặc `IsNearTopPlateBoundary`.

---

## [MODULE:ALL] Session 2026-04-14 (cont.1) — Gộp lại 1 palette MCG Plugin đa-tab (vẫn có nút X)

### Bối cảnh
- Session trước tách 5 palette riêng → nút X hoạt động nhưng UX không tốt (5 GUID, 5 vị trí dock riêng).
- Muốn gộp lại 1 palette "MCG Plugin" 5 tabs mà vẫn giữ nút X default.

### Phân tích — combo chưa test
Bảng cấu hình đã thử trước đây:
| Constructor | Style | KeepFocus | Tabs | Nút X |
|---|---|---|---|---|
| 2-arg | explicit 5 flags | false | multi | ❌ |
| 2-arg | **không set** | **true** | **single** | ✅ |
| 2-arg | **không set** | **true** | **multi** | ⚠️ **chưa test** |

→ Combo cuối cùng chính là thứ cần thử.

### Giải pháp
- Tạo `Commands/MCGPluginPaletteCommand.cs` — 1 PaletteSet "MCG Plugin", GUID `d42d9d08-37d7-4cbe-8428-50e046571667`.
- Pattern: 2-arg constructor + KHÔNG set Style + `KeepFocus=true` + 5 `AddVisual()` + thứ tự `Visible→Dock→Activate`.
- Xóa 5 file `*PaletteCommand.cs` per-module.
- Giữ các lệnh cũ `MCG_DetailDesign`, `MCG_FittingManagement`, ... dưới dạng shortcut → activate tab tương ứng.

### Lệnh mới
| Command | Tab |
|---|---|
| `MCG_Plugin` | 0 (default) |
| `MCG_DetailDesign` | 0 |
| `MCG_FittingManagement` | 1 |
| `MCG_PanelData` | 2 |
| `MCG_TableOfContent` | 3 |
| `MCG_Weight` | 4 |

### Bài học (đã lưu vào `.claude/PALETTE_GUIDE.md`)
1. **Style tường minh = kẻ giết nút X**: set `ps.Style = A | B | C` sẽ ĐÈ default flags → mất `ShowCloseButton`. AutoCAD 2023 default style đã có đủ close + autohide.
2. **Debug nhiều biến = phải isolate từng biến**: session trước gộp 3 sai lầm (Style + KeepFocus=false + multi-tab) → kết luận SAI rằng multi-tab là thủ phạm. Thực tế chỉ Style + KeepFocus là vấn đề.
3. **Copy reference nguyên si trước khi scale**: CSSPlugin pattern single-tab đang work → giữ nguyên mọi thứ, CHỈ thay số tab → vẫn work.
4. **Default AutoCAD thường đã tốt** — tránh override trừ khi có lý do cụ thể.

### Rule bất biến (vi phạm = mất nút X)
- KHÔNG set `_ps.Style = ...` tường minh
- KHÔNG `KeepFocus = false`
- KHÔNG dùng 3-arg constructor
- BẮT BUỘC thứ tự: `Visible=true` → `Dock` → `Activate(index)`

### Verify
- Build OK — DLL `MCGCadPlugin_20260414_091412.dll`.
- NETLOAD + `MCG_Plugin` → palette 5 tabs, dock right, có nút X ✅ (user confirm).

---

## [MODULE:ALL] Session 2026-04-14 — Tách PaletteSet riêng từng module (fix nút X)

### Bối cảnh
- Palette gom chung "MCG Plugins" (5 tabs) không hiển thị nút X / AutoHide khi dock.
- Đã thử: đổi constructor 2↔3 arg, set/bỏ Style flags, `KeepFocus` true/false, đổi thứ tự Visible/Dock/Activate → KHÔNG khả dụng với multi-tab palette trong AutoCAD 2023.
- Reference CSSPlugin / CFS_VehicleScripts (1 tab, 2-arg, không set Style, `KeepFocus=true`) hiện đầy đủ nút X.

### Giải pháp đã áp dụng
- **Kiến trúc mới**: mỗi module = 1 PaletteSet riêng, 1 GUID riêng, 1 lệnh mở riêng.
- Xóa `Commands/PaletteManager.cs` + `Views/MainPaletteContainer.xaml` (singleton gom chung).
- Xóa 3 stub view class placeholder (PanelDataView.cs, TableOfContentView.cs, WeightView.cs).

### Files mới
- `Views/PanelData/PanelDataView.xaml(.cs)` — empty UserControl
- `Views/TableOfContent/TableOfContentView.xaml(.cs)` — empty UserControl
- `Views/Weight/WeightView.xaml(.cs)` — empty UserControl
- `Commands/DetailDesign/DetailDesignPaletteCommand.cs` — `MCG_DetailDesign`
- `Commands/FittingManagement/FittingManagementPaletteCommand.cs` — `MCG_FittingManagement`
- `Commands/PanelData/PanelDataPaletteCommand.cs` — `MCG_PanelData`
- `Commands/TableOfContent/TableOfContentPaletteCommand.cs` — `MCG_TableOfContent`
- `Commands/Weight/WeightPaletteCommand.cs` — `MCG_Weight`

### GUID cố định cho từng palette (KHÔNG đổi sau deploy)
| Module | Command | GUID |
|---|---|---|
| DetailDesign | `MCG_DetailDesign` | `6459a212-b8c8-4adb-98da-fffb71e4fea9` |
| FittingManagement | `MCG_FittingManagement` | `f03d8b0a-4660-4f7e-a8b4-42e87fb7957a` |
| PanelData | `MCG_PanelData` | `36a1590b-f901-4362-9063-16df588221ab` |
| TableOfContent | `MCG_TableOfContent` | `e7a62500-126a-4f08-a52e-89783a4d482a` |
| Weight | `MCG_Weight` | `02e1b6e9-4cff-4608-bf6f-cb9cbe45508e` |

### Pattern chuẩn PaletteCommand (áp dụng cho tất cả module)
```csharp
private static PaletteSet _ps = null;
private static readonly Guid PaletteGuid = new Guid("...");

[CommandMethod("MCG_Xxx", CommandFlags.NoHistory)]
public void ShowPalette()
{
    if (_ps == null)
    {
        _ps = new PaletteSet("MCG — Xxx", PaletteGuid);  // 2-arg
        _ps.AddVisual("Xxx", new XxxView());
        _ps.DockEnabled = DockSides.Right | DockSides.Left;
        _ps.Size = new System.Drawing.Size(400, 600);
        _ps.KeepFocus = true;     // KHÔNG set Style → dùng default flags → có nút X
    }
    _ps.Visible = true;
    _ps.Dock = DockSides.Right;
    if (_ps.Count > 0) _ps.Activate(0);
}
```

### Rule bất biến (ghi nhớ)
- **KHÔNG** gán `Style` tường minh — sẽ đè default flags → mất nút X.
- **KHÔNG** dùng PaletteSet gom nhiều tab — AutoCAD suppress close controls với multi-tab.
- Thứ tự: `Visible=true` → `Dock` → `Activate(index)`.
- Constructor 2-arg `PaletteSet(name, guid)` là đủ.

### Verify
- Build OK — DLL `MCGCadPlugin_20260414_075933.dll`.
- NETLOAD + `MCG_DetailDesign` → palette có nút X ✅ (user confirm).

### Bước tiếp theo
- Áp lại các task DetailDesign tồn đọng từ session 2026-04-13 cont.3 (thickness rounding, defaults apply, OB/IB logic, B=8, bracket total).
- Tree columns dynamic by type: Part | Thickness | Length | Height/Width | GUID.
- SELECT PANEL all-in-one (bỏ SCAN/RESCAN).
- Naming: W-01 (not WEB-01), TPL-01 (not Region).
- Apply / Apply All Same / Apply to Selected — implement logic.

---

## [MODULE:DetailDesign] Session 2026-04-13 (cont.3) — UI redesign + Default Params + iProperties

### Đã làm
- UI redesign: SpaceClaim-style tree (text-only) + Properties Panel (split layout)
- Multi-select tree (Ctrl+Click)
- Default Parameters panel: TP t:6mm, FL t:20mm, Stiff:HP120x6, BS:FB80x6
- ThicknessCalculator (midpoint-to-opposite-edge method)
- iProperties command (pick entity → show XData/GUID)
- Bỏ handle → dùng GUID làm primary identifier
- Seed thêm profiles: HP80x6, HP120x6, FB60x6, FB75x6, FB80x6
- Layout tree mới: Part | Thickness/Section | GUID (8 chars)

### Vấn đề tồn đọng (session sau)
1. **iProperties thiếu thông tin** — TopPlate/Flange cần area, Profile cần weight/length
2. **Default values chưa apply** — TP thickness, Stiff/BS profile chưa lấy từ defaults
3. **Flange properties sai** — thickness ≠ flange width. Thickness = default, width = computed
4. **Web plate thickness algorithm** — cần review midpoint method
5. **Thickness làm tròn** — 9.999→10, 10.0001→10 (Math.Round)
6. **OB/IB** — vẫn chưa đúng (OB=11 IB=14 vs expected OB=14 IB=0)
7. **B=8** — "no stiff contact"
8. **Bracket total** — 39 vs expected 46

### Bước tiếp theo
```
1. Fix thickness rounding (Math.Round)
2. Apply defaults: TP thickness, Stiff profile, BS profile từ palette defaults
3. Flange: thickness = default (20mm), width = computed (midpoint)
4. iProperties: thêm area cho plates, weight/length cho profiles
5. Review web plate thickness algorithm
6. Fix OB/IB logic
```

---

## [MODULE:DetailDesign] Session 2026-04-13 (cont.2) — TopPlate fix + UI inline + SpaceClaim discussion

### Đã làm
- Fix TopPlate: layer "0" chỉ từ TopPlate sub-block mới là TopPlateRegion → **1 part** (đúng)
- Fix OB/IB: throw vs web plate direction (thay vì panel center) — vẫn chưa hoàn toàn đúng
- Fix BF: chỉ chạm trực tiếp BS < 1mm → **6 BF** (đúng)
- Thêm inline controls trên tree: handle + thickness TextBox + profile ComboBox + throw text
- Thảo luận SpaceClaim-style tree → confirmed: display trên tree, edit trên Properties Panel

### Kết quả gần nhất
```
TopPlateRegion: 1 (đúng!)
Flange: 27 outer + 12 holes
Stiffener: 15 | BS: 27
Bracket: 39 (OB=11, IB=14, BF=6, B=8) ← OB/IB vẫn sai
Girder: 8 | ClosingBox: 2
WebPlate: 77
```

### Vấn đề tồn đọng
1. **Handle mismatch** — block definition handle vs block instance handle
2. **OB/IB** — OB=11, IB=14, expected OB=14, IB=0. Logic throw vs web cần review
3. **B=8** — "no stiff contact" brackets cần investigate
4. **UI redesign** — SpaceClaim-style: text-only tree + Properties Panel bên dưới

### Quyết định thiết kế
- SpaceClaim-style tree: compact text-only, values inline nhưng read-only
- Properties Panel riêng bên dưới tree: editable thickness/profile/apply
- Girders nhóm Web + Flange trong tree hierarchy

### Bước tiếp theo
```
1. Redesign tree → text-only (bỏ inline TextBox/ComboBox)
2. Thêm Properties Panel bên dưới tree
3. Fix handle mismatch (investigate block definition vs instance)
4. Fix OB/IB logic
5. Fix B "no stiff contact"
```

---

## [MODULE:DetailDesign] Session 2026-04-13 (cont.) — Bracket/Layer/TopPlate fixes

### Đã làm
- Fix Flange layer normalize: `"Mechanical -AM_0"` → `"Mechanical-AM_0"`, `"AM-3"` → `"AM_3"`
- Fix bracket detection: quay lại paired-first + touchesAnyStiff approach (35mm tolerance)
- Fix BracketAnalyzer: OB/IB dựa trên throw vs panel center (vẫn chưa đúng — cần throw vs web)
- Fix BRACKET_END_GAP_MAX = 35mm
- Fix palette ShowCloseButton
- Fix palette inactive selection dark background
- Bracket sub-types: OB/IB/BF/B trong tree hierarchy

### Kết quả gần nhất
```
TopPlateRegion: 8 outer + 7 holes     ← SAI: nên là 1 main + equipment pads
Flange: 27 outer + 12 holes           ← OK
Stiffener: 15 | BS: 27
Bracket: 39 (OB=21, IB=3, BF=15, B=0) ← SAI: mong đợi B=26, BF=6, OB=14, IB=0
Girder: 8 | ClosingBox: 2
WebPlate: 67
```

### Vấn đề tồn đọng (session sau)
1. **OB/IB logic** — phải dùng throw vs web plate direction, KHÔNG phải panel center
2. **BF quá nhiều (15 vs 6)** — tolerance 35mm khiến AM0 gần BS classify sai
3. **Top plate = 8 vs 1** — chỉ polyline lớn nhất là top plate, nhỏ = equipment pads
4. **Close button** — chưa verify
5. **IB = 3 vs 0** — do OB/IB logic sai

### Quyết định thiết kế confirmed
- OB = throw hướng RA XA web plate (không phụ thuộc panel side)
- IB = throw hướng VỀ PHÍA web plate
- B = bracket chưa resolve OB/IB
- BF = bracket tại BS position
- Top plate = 1 polyline lớn nhất, các polyline nhỏ = equipment/pad
- Layer normalize: bỏ space, hyphen → underscore

### Bước tiếp theo
```
1. Fix OB/IB: dot(throwVec, stiffCenter→webCenter) > 0 → IB, < 0 → OB
2. Fix BF: logic chặt hơn (chỉ AM0 trực tiếp chạm BS, không phải gần)
3. Fix TopPlate: 1 main + equipment classification
4. Verify close button
```

---

## [MODULE:DetailDesign] Session 2026-04-13 — STEP 5-9 + Major Refactors

### Đã làm
- STEP 5: WCSTransformer + BlockEntityCollector + DirectEntityCollector
- STEP 6: ConvexHullHelper + OBBCalculator + PrimaryClassifier
- STEP 7: GeometryHasher + XDataManager (XData write + SQLite save)
- STEP 8: TopologyEngine + ClosingBoxDetector + StructureTreeView (MILESTONE 2)
- STEP 9: ThrowVectorEngine + BracketAnalyzer

**Major Refactors (từ DWG test thực tế):**
- Fix Flange layer: AM_11 thay vì AM_5 (LAYER_FLANGE_ALT)
- Fix MinDistance: vertex-to-vertex → point-to-segment
- Fix bracket logic: BS cũng tạo bracket (touchesAnyStiff = Stiff OR BS)
- Fix bracket detection: paired-first (girder pairing TRƯỚC bracket)
- Fix bracket endpoint: stiffener endpoint analysis thay vì AM0 scan
- Fix cutout detection: PointInPolygonHelper cho TopPlate/Flange holes
- Fix palette inactive selection: dark background khi mất focus
- Thêm: IsHole, ParentGuid, AnnotationType, NetArea vào StructuralElementModel
- Thêm: SubType (OB/IB/BF/B) vào BracketModel
- Thêm: ShowCloseButton cho PaletteSet
- Thêm: GE annotation cho girder end web plates

### Kết quả NETLOAD gần nhất
```
TopPlateRegion: 8 (outer) + 7 holes = 15 total
Flange: 27 (outer) + 12 holes = 39 total
Stiffener: 15 | BucklingStiffener: 25
Girders: 8 | ClosingBoxes: 2-4
Bracket: 8 (chưa đúng — mong đợi: B=26, BF=6, OB=14, IB=0)
```

### Vấn đề tồn đọng (ưu tiên cao)
1. **Bracket count sai** — OBB endpoint tính sai vị trí stiffener end. Cần dùng actual polyline vertices
2. **BracketAnalyzer "no stiff contact"** — tolerance quá chặt cho bracket cách stiffener 15-30mm
3. **Close button** — ShowCloseButton đã thêm nhưng chưa verify
4. **Tree highlight** — eInvalidInput cho nested block entities
5. **Web plate count** — cần review sau khi bracket fix

### Quyết định thiết kế quan trọng
- Bracket "B" = stiffener end cách web ≤ 30mm (kể cả chạm)
- Web Plate = AM0 có flange paired (girder). Bracket = AM0 không paired + gần stiffener end
- GE annotation = web plate gần stiff nhưng không phải bracket
- Cutout = polyline con nằm trong polyline cha (TopPlate/Flange)
- Annotation types (GE/OB/IB/BF/CG/BS_sym) = attributes, không phải StructuralTypes

### Bước tiếp theo
```
1. Fix stiffener endpoint — dùng actual polyline first/last vertices
2. Fix BracketAnalyzer — tăng tolerance cho bracket-stiff matching
3. Verify bracket counts: B=26, BF=6, OB=14, IB=0
4. Sau khi bracket OK → tiếp STEP 10 (Thickness + Properties UI)
```

### Ghi chú API
- PaletteSetStyles.ShowCloseButton — cần verify trên AutoCAD 2023
- AutoCAD Mechanical layers: AM_11 = flange (khác với spec AM_5)
- Layer variants: "Mechanical -AM_0" (space), "Mechanical-AM-3" (hyphen)
- AM_3 color 30 = stiffener variant (không phải 40/6 chuẩn)

---

## [MODULE:DetailDesign] Session 2026-04-13 — STEP 4 DONE + NETLOAD Verified

### Đã làm
- STEP 4: 8 files (6 mới + 2 sửa)
  - `Services/DetailDesign/Collection/RawEntitySet.cs` — container entities phân nhóm
  - `Services/DetailDesign/Collection/IEntityCollector.cs` — interface
  - `Services/DetailDesign/Classification/SubBlockClassifier.cs` — phân loại sub-blocks
  - `Services/DetailDesign/Parameters/PanelNameParser.cs` — parse tên + auto-detect side
  - `Services/DetailDesign/IPanelScanService.cs` + `PanelScanService.cs` — entry point
  - `Commands/DetailDesign/DetailDesignCommand.cs` — MCG_SelectPanel command
  - Sửa `DetailDesignViewModel.cs` — thêm ExecuteSelectPanel()
  - Sửa `DetailDesignView.xaml.cs` — wire up button → SendStringToExecute

### NETLOAD Test Result (DWG: CAS-0051566.dwg)
```
✅ Filter BlockReference only — không click được line/polyline
✅ Panel name: N.BCK608P — đúng
✅ Side: Port (auto-detect từ hậu tố P) — đúng
✅ SubBlockClassifier: ASSY_ROOT, TOP_PLATE, STRUCTURE, SKIP đúng
✅ FLAG_UNKNOWN: Deck lifter, LH, TH, panel stopper, Small cover — đúng behavior
✅ UI cập nhật: Panel name + Side hiện trên palette
```

### Trạng thái Step
```
PRE-STEP 0: ✅    STEP 1: ✅    STEP 2: ✅    STEP 3: ✅
STEP 4:     ✅    STEP 5-16: ░░ NOT STARTED
```

### Bước tiếp theo
```
Step    : STEP 5 — WCS Transform + Entity collection
Files   : WCSTransformer.cs, BlockEntityCollector.cs, DirectEntityCollector.cs
Test    : SCAN panel → đếm entities per layer, so sánh với DWG
```

### Ghi chú
- DWG test panel: N.BCK608P (Port side), có sub-blocks: TopPlate, Structure, + 6 FLAG_UNKNOWN
- SendStringToExecute("MCG_SelectPanel ") cần khoảng trắng cuối để AutoCAD execute
- Static ViewModel reference pattern: View set → Command đọc (workaround cho PaletteSet context)

---

## [MODULE:DetailDesign] Session 2026-04-13 — STEP 1-3 DONE + NETLOAD Verified

### Đã làm
- STEP 1: 3 Utilities files (Constants, LogHelper, DrawingUnitsValidator)
- STEP 2: 20 Model files (6 enums + 14 models) — grep verified no Autodesk.*
- STEP 3: SQLite + UI skeleton + PaletteManager commands
  - SchemaInitializer (18 tables + 4 indexes)
  - DetailDesignRepository + Interface
  - ProfileCatalogSeeder (15 profiles EN10067)
  - DetailDesignView.xaml dark theme + code-behind
  - DetailDesignViewModel MVVM
  - PaletteManager: uncomment DetailDesign, thêm MCG_Show/Hide/DetailDesign commands
- Fix .csproj: `CopyLocalLockFileAssemblies=true`, thêm SQLite + Costura.Fody
- Fix SQLite native: static constructor set PATH tới x64/ folder
- Fix PaletteManager: Initialize() retry-safe, log English, Dock=Right sau Visible
- Cleanup: xóa MongoDB.Driver, xóa/tạo lại FodyWeavers

### NETLOAD Test Result
```
✅ Palette "MCG Plugins" hiện ra, dock right
✅ Tab "Detail Design" active, UI skeleton đúng layout
✅ SELECT PANEL → focus về bản vẽ
✅ DB created: C:\CustomTools\MCGPanelTool.db (18 tables, 15 profiles)
✅ DebugView: tất cả log đúng sequence, không crash
```

### Trạng thái Step
```
PRE-STEP 0: ✅ DONE
STEP 1:     ✅ DONE
STEP 2:     ✅ DONE
STEP 3:     ✅ DONE (NETLOAD verified)
STEP 4:     ░░ NOT STARTED (next)
```

### Bước tiếp theo
```
Step    : STEP 4 — Block traversal + Panel name parser
Files   : 6 files trong Services/DetailDesign/
Test DWG: C:\CustomTools\Test Detail Design\CAS-0051566.dwg
Verify  : Click Assy block → tên panel + side hiện trên status
```

### Ghi chú API
- SQLite.Interop.dll native: KHÔNG dùng Costura Unmanaged64Assemblies (đổi entry points)
- Dùng static constructor + Environment.SetEnvironmentVariable("PATH") thay thế
- PaletteSet.Dock = DockSides.Right phải gọi SAU Visible = true
- CopyLocalLockFileAssemblies phải = true để Costura embed managed DLLs

---

## [MODULE:DetailDesign] Session 2026-04-13 — PRE-STEP 0 + STEP 1 Done

### Đã làm
- PRE-STEP 0: Xóa 5 placeholder DetailDesign files, fix PaletteManager (comment out Views, fix `System.Exception` ambiguous, remove `RecalculateSize`)
- STEP 1: Tạo 3 files trong `Utilities/DetailDesign/`:
  - `DetailDesignConstants.cs` — 15 constants (layers, colors, tolerances, paths, directions)
  - `LogHelper.cs` — file logger 4 levels, auto-flush, sanitize filename
  - `DrawingUnitsValidator.cs` — check INSUNITS, alert tiếng Việt
- Cleanup `.csproj`: xóa `Costura.Fody` + `MongoDB.Driver`, xóa `FodyWeavers.xml/.xsd`

### Trạng thái Step
```
PRE-STEP 0: ✅ DONE
STEP 1:     ✅ DONE (build PASS, 3 files đúng namespace)
STEP 2:     ░░ NOT STARTED (next)
```

### Bước tiếp theo
```
Step    : STEP 2 — Models (13 files, build-only)
Files   : Models/DetailDesign/ — 13 model classes
Rule    : KHÔNG import Autodesk.* — verify bằng grep
Verify  : Build PASS + grep rỗng
```

### Ghi chú API
- `RecalculateSize` không tồn tại trong AutoCAD 2023 PaletteSet API — đã xóa
- `Exception` ambiguous giữa `Autodesk.AutoCAD.Runtime.Exception` và `System.Exception` — dùng `System.Exception` tường minh
- ROADMAP Step 1 verify criteria (Palette hiện ra, Tab active) thực tế thuộc Step 3 — đã cập nhật

---

## [MODULE:DetailDesign] Session 2026-04-13 — Roadmap Finalized

### Đã làm
- Thống nhất lộ trình 16 steps + Pre-Step 0
- Tạo `.claude/modules/DetailDesign/ROADMAP.md` — 16 steps chi tiết với expected output
- Cập nhật `.claude/modules/DetailDesign.md` — thêm Test Config, Profile Catalog, Roadmap ref, xóa Phase Checklist cũ
- Cập nhật `CONTEXT.md` — trỏ đến Pre-Step 0
- Xác nhận: tách Step 2 (Models) và Step 3 (SQLite+UI)
- Xác nhận: Profile catalog seed khi init DB

### Quyết định kỹ thuật bổ sung

| Vấn đề | Quyết định |
|---|---|
| Step 2 vs Step 3 | Tách riêng — Step 2 build-only, Step 3 NETLOAD |
| Pre-Step 0 | Cleanup placeholders trước Step 1 |
| Roadmap location | `.claude/modules/DetailDesign/ROADMAP.md` (file riêng) |
| Profile catalog | Seed khi init DB (ProfileCatalogSeeder.cs) |
| Test DWG | `C:\CustomTools\Test Detail Design\CAS-0051566.dwg` |

### Trạng thái Step
```
PRE-STEP 0: ░░ NOT STARTED (next)
STEP 1-16:  ░░ ALL PENDING
```

### Bước tiếp theo
```
Step    : PRE-STEP 0 — Cleanup
File    : Chạy audit-project skill để liệt kê placeholder files
Mục tiêu: Xóa 5 placeholder files sai structure, verify build PASS
Sau đó  : Bắt đầu STEP 1 ngay trong cùng session
```

### Ghi chú
- Mỗi step trong ROADMAP.md có: files cần tạo + expected output + verify criteria
- Sau mỗi step PASS → đánh ✅ trong ROADMAP.md + cập nhật CONTEXT.md

---

## [MODULE:DetailDesign] Session 2026-04-12 — Knowledge Files Created

### Đã làm
- Trao đổi + thống nhất toàn bộ thiết kế module DetailDesign
- Tạo `.claude/modules/DetailDesign.md` (phiên bản cũ — đã update ở session trên)
- Tạo 4 placeholder module files: FittingManagement, Weight, PanelData, TableOfContent
- Tạo 5 skill files trong `.claude/skills/`
- Tạo `Models/DetailDesign/Enums/` — 6 enum files ✅
- Tạo `Services/DetailDesign/Data/SchemaInitializer.cs` ✅

### Trạng thái Phase
```
Phase A: ██░░░░░░░░ 20% (Enums + SchemaInitializer done)
```

### Ghi chú API
- `(UnitsValue)(int)db.Insunits` — cách check INSUNITS đúng
- XData `ResultBuffer`: group 1001=AppName, 1000=String, 1040=Real
- Mirror: `M_total.Inverse().Transpose().GetDeterminant() < 0`

---

## [MODULE:ALL] Session 2026-04-08 — Scaffold & Setup

### Đã làm
- `CLAUDE.md` — Fix PaletteSet patterns, GUID thật, SetFocusToDwgView
- `Commands/PaletteManager.cs` — Fix 4 lỗi audit
- 25 placeholder files cho 5 modules (Commands/Models/Services/Views/Utilities)

### Trạng thái
```
Phase 0 (Scaffold): ████████░░ 80%
Còn lại: 5 View XAML UserControl, CommandMethod MCG_Show/Hide/Toggle
```

### Ghi chú API
- PaletteSet order: `new()` → `AddVisual()` → properties → `Visible=true`
- GUID: `2b80cfe9-c560-49d6-8a09-9d636260fcf2` — KHÔNG thay đổi
