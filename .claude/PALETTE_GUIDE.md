# PALETTE_GUIDE.md — Kiến trúc PaletteSet

> **Khi nào cần đọc file này:**
> Tạo View mới, sửa palette command, thêm tab, debug palette không hiện/dock/mất nút X.
> KHÔNG cần đọc mỗi session — chỉ đọc khi làm việc liên quan UI/Palette.
> Được reference từ CLAUDE.md mục 11.

---

## Nguyên tắc cốt lõi

```
Toàn plugin có DUY NHẤT 1 PaletteSet "MCG Plugin" — 5 tabs.
Entry point: Commands/MCGPluginPaletteCommand.cs
GUID cố định: d42d9d08-37d7-4cbe-8428-50e046571667
```

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────┐
│            MCG Plugin (PaletteSet)              │
│  ┌────────┬────────┬───────┬───────┬───────┐    │
│  │ Detail │Fitting │ Panel │ Table │Weight │    │
│  │ Design │ Mgmt   │ Data  │  of   │       │    │
│  │        │        │       │Content│       │    │
│  └────────┴────────┴───────┴───────┴───────┘    │
└─────────────────────────────────────────────────┘
         │ Dock: Right
         │ Close X + AutoHide: ✅ (default AutoCAD style)
```

---

## CODE CHUẨN — Pattern bất biến

```csharp
private static PaletteSet _ps = null;
private static readonly Guid PaletteGuid = new Guid("d42d9d08-37d7-4cbe-8428-50e046571667");

private static void EnsureInit()
{
    if (_ps != null) return;

    // 1. Constructor 2-arg — KHÔNG dùng 3-arg (name, toolPaletteName, guid)
    _ps = new PaletteSet("MCG Plugin", PaletteGuid);

    // 2. AddVisual — mỗi tab 1 lần, thứ tự = index
    _ps.AddVisual("Detail Design",      new DetailDesignView());
    _ps.AddVisual("Fitting Management", new FittingManagementView());
    // ... các tab khác

    // 3. Properties tối thiểu — KHÔNG set _ps.Style
    _ps.DockEnabled = DockSides.Right | DockSides.Left;
    _ps.Size        = new System.Drawing.Size(400, 600);
    _ps.KeepFocus   = true;
}

private static void ShowAndActivate(int tabIndex)
{
    EnsureInit();

    // Thứ tự BẮT BUỘC: Visible → Dock → Activate
    _ps.Visible = true;
    _ps.Dock    = DockSides.Right;
    if (_ps.Count > tabIndex) _ps.Activate(tabIndex);
}
```

---

## RULE BẤT BIẾN — Vi phạm sẽ mất nút X

| Rule | Lý do |
|---|---|
| **KHÔNG** gán `_ps.Style = ...` tường minh | Set Style = A \| B \| C sẽ ĐÈ HOÀN TOÀN default flags → mất `ShowCloseButton` mặc dù Style enum không show rõ default là gì. AutoCAD 2023 default Style đã chứa đầy đủ close + autohide + properties menu. |
| **KHÔNG** set `KeepFocus = false` | Làm `KeepFocus = true` match pattern CSSPlugin/CFS_VehicleScripts đang hoạt động. `false` combo với multi-tab từng gây suppress close controls. |
| **KHÔNG** dùng constructor 3-arg `PaletteSet(name, toolPaletteName, guid)` | 2-arg đã đủ. 3-arg từng thử không giải quyết vấn đề. |
| **BẮT BUỘC** thứ tự `Visible=true` → `Dock=Right` → `Activate(index)` | AutoCAD chỉ cho phép `Dock` khi đã `Visible`. Activate cuối cùng tránh event loop. |
| Focus về bản vẽ: gọi `Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView()` trong button handler | Thay vai trò của `KeepFocus=false` mà không hy sinh nút X. |

---

## BÀI HỌC — Tại sao session này xử lý được

### Session trước (thất bại)
- Gộp 3 sai lầm cùng lúc → không isolate được biến nào gây lỗi:
  1. Set `Style` tường minh (5 flags).
  2. `KeepFocus = false`.
  3. Multi-tab.
- Cứ fix 1 biến, 2 biến kia vẫn còn → vẫn mất nút X → kết luận SAI: "multi-tab là nguyên nhân".

### Session này (thành công)
- Bắt đầu từ cấu hình đơn giản nhất match reference CSSPlugin: **single-tab, không Style, `KeepFocus=true`, 2-arg** → ✅ có nút X.
- Giữ nguyên tất cả thuộc tính, **CHỈ** đổi 1 biến (số lượng tab: 1 → 5) → ✅ vẫn có nút X.
- Kết luận đúng: **Style + KeepFocus là thủ phạm**, multi-tab OK.

### Bài học tổng quát
- Khi debug vấn đề nhiều biến → **isolate 1 biến 1 lần**. Đừng fix nhiều thứ cùng lúc.
- Khi có reference đang hoạt động → **copy nguyên si trước**, rồi scale dần. Đừng "cải tiến" trước khi match.
- Default AutoCAD value thường đã tốt — tránh set tường minh trừ khi cần ghi đè có lý do rõ ràng.

---

## Thêm tab mới

1. Tạo `Views/<Module>/<Module>View.xaml` + `.xaml.cs` (UserControl).
2. Trong [Commands/MCGPluginPaletteCommand.cs](../Commands/MCGPluginPaletteCommand.cs):
   - Thêm `using MCGCadPlugin.Views.<Module>;`
   - Thêm hằng `TAB_<MODULE> = N` (N = index tiếp theo).
   - Thêm `_ps.AddVisual("<Name>", new <Module>View());` trong `EnsureInit()`.
   - Thêm method `[CommandMethod("MCG_<Module>")]` gọi `ShowAndActivate(TAB_<MODULE>)`.

---

## Lệnh AutoCAD

| Command | Tác dụng |
|---|---|
| `MCG_Plugin` | Mở palette (default = Detail Design tab) |
| `MCG_DetailDesign` | Mở + focus Detail Design |
| `MCG_FittingManagement` | Mở + focus Fitting Management |
| `MCG_PanelData` | Mở + focus Panel Data |
| `MCG_TableOfContent` | Mở + focus Table of Content |
| `MCG_Weight` | Mở + focus Weight |

---

## Debug checklist — khi palette không có nút X

1. Có dòng `_ps.Style = ...` không? → **XÓA**.
2. `_ps.KeepFocus` đang là `false`? → đổi thành `true`.
3. Constructor 3-arg? → đổi sang 2-arg.
4. Thứ tự `Visible`/`Dock`/`Activate` đúng không? → sửa theo pattern chuẩn ở trên.
5. Vẫn lỗi → so code với [MCGPluginPaletteCommand.cs](../Commands/MCGPluginPaletteCommand.cs) từng dòng.
