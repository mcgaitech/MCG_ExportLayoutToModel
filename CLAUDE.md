# CLAUDE.md — MCGCadPlugin
# AutoCAD 2023 | C# | .NET 4.8 | WPF | VS Code

> Rules bất biến toàn project. Áp dụng cho mọi module, mọi session.
> KHÔNG chứa rules riêng của bất kỳ module nào.

---

## 1. Startup Sequence — Đọc theo thứ tự

```
Bước 1 → CLAUDE.md          (file này)
Bước 2 → BEHAVIOR.md        ← THÊM MỚI
Bước 3 → CONTEXT.md         biết module đang active
Bước 4 → .claude/modules/{ActiveModule}.md   module brain
Bước 5 → .claude/SESSION_LOG.md             trạng thái gần nhất
Bước 6 → Skill files được chỉ định trong module file (theo phase)
```

Sau khi đọc xong, **báo cáo ngắn**:
```
Module  : [tên]
Phase   : [hiện tại]
Tiếp theo: [file + method + mục tiêu cụ thể]
Tồn đọng: [nếu có, nếu không ghi "None"]
```
---

## 2. Thông tin dự án

```
Tên           : MCGCadPlugin
Mục tiêu      : Plugin AutoCAD .NET — bóc tách dữ liệu hình học (MTO)
Thư mục       : C:\CustomTools\Autocad\MCGCadPlugin
Runtime       : .NET Framework 4.8, x64
UI            : WPF + AutoCAD PaletteSet (Singleton, 5 tabs)
AutoCAD DLLs  : C:\Program Files\Autodesk\AutoCAD 2023\
                  acmgd.dll | acdbmgd.dll | accoremgd.dll
Output        : .dll (debug: timestamp) + PackageContents.xml (bundle)
Build         : dotnet build -c Debug   (quyền Administrator)
Test          : NETLOAD trong AutoCAD
```

> ⚠️ KHÔNG sửa `MCGCadPlugin.csproj` — trừ ngoại lệ đã ghi trong module file.

---

## 3. Module Index

| Module | File | Trạng thái |
|---|---|---|
| DetailDesign | `.claude/modules/DetailDesign.md` | 🔄 Active — Phase A |
| FittingManagement | `.claude/modules/FittingManagement.md` | ⏳ Pending |
| Weight | `.claude/modules/Weight.md` | ⏳ Pending |
| PanelData | `.claude/modules/PanelData.md` | ⏳ Pending |
| TableOfContent | `.claude/modules/TableOfContent.md` | ⏳ Pending |

> Cập nhật cột "Trạng thái" khi switch module hoặc hoàn thành phase.

---

## 4. Namespace — Toàn project

```
MCGCadPlugin.Commands.{Module}    ← Đăng ký lệnh, gọi Service. KHÔNG có logic.
MCGCadPlugin.Models.{Module}      ← Data objects thuần. KHÔNG import AutoCAD.
MCGCadPlugin.Services.{Module}    ← Business logic. Luôn có Interface.
MCGCadPlugin.Views.{Module}       ← WPF XAML + ViewModel. Code-behind tối thiểu.
MCGCadPlugin.Utilities.{Module}   ← Helpers, validators, constants của module.
```

**Ví dụ:**
```
Services/DetailDesign/OBBCalculator.cs  → namespace MCGCadPlugin.Services.DetailDesign
Models/Weight/WeightResult.cs           → namespace MCGCadPlugin.Models.Weight
Utilities/DetailDesign/LogHelper.cs     → namespace MCGCadPlugin.Utilities.DetailDesign
```

---

## 5. Naming Conventions

| Loại | Rule | Ví dụ |
|---|---|---|
| Class | PascalCase | `OBBCalculator` |
| Interface | `I` + PascalCase | `IOBBCalculator` |
| Method | PascalCase | `ComputeOBB()` |
| Property | PascalCase | `AspectRatio` |
| Variable | camelCase | `vertexList` |
| Constant | UPPER_SNAKE | `TOLERANCE_CONTACT` |
| CAD Command | `MCG_` + PascalCase | `MCG_DetailDesign` |
| Module folder | PascalCase | `DetailDesign` |

---

## 6. Language Rules

| Nội dung | Ngôn ngữ |
|---|---|
| Class, method, variable, property | **English** |
| UI text (button, label, tooltip) | **English** |
| XML doc comment `/// <summary>` | **Tiếng Việt** |
| Inline comment `//` | **Tiếng Việt** |
| Log message (`Debug.WriteLine`) | **English** |
| Error message hiển thị cho user | **Tiếng Việt** |

---

## 7. Architecture — MVC + SOLID

```
Commands  → Gọi Service + PaletteManager. KHÔNG chứa logic nghiệp vụ.
Models    → Data objects. KHÔNG import Autodesk.* namespace.
Services  → Logic nghiệp vụ. Luôn có Interface (IXxxService).
Views     → XAML + ViewModel (MVVM). Code-behind tối thiểu.
Utilities → Constants, validators, helpers dùng chung trong module.
```

**SOLID bắt buộc:**
- SRP: mỗi class 1 trách nhiệm
- DIP: phụ thuộc interface, không phụ thuộc implementation
- DI: Dependency Injection thủ công tại entry point

---

## 8. Logging — Bắt buộc mọi class

```csharp
private const string LOG_PREFIX = "[ClassName]";

public void DoWork()
{
    Debug.WriteLine($"{LOG_PREFIX} Starting DoWork...");
    try
    {
        // logic
        Debug.WriteLine($"{LOG_PREFIX} DoWork SUCCESS.");
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"{LOG_PREFIX} ERROR: {ex.Message}");
        Debug.WriteLine($"{LOG_PREFIX} Stack:\n{ex.StackTrace}");
        throw; // KHÔNG swallow
    }
}
```

---

## 9. Error Handling

```
Service layer    → Log + throw (không swallow)
Command layer    → Log + ed.WriteMessage() tiếng Việt
View layer       → Hiển thị thông báo, không crash
```

---

## 10. AutoCAD API Patterns

### Command registration
```csharp
[CommandMethod("MCG_TenLenh", CommandFlags.Modal)]
public void MyCommand()
{
    var doc = Application.DocumentManager.MdiActiveDocument;
    var db  = doc.Database;
    var ed  = doc.Editor;
    Debug.WriteLine($"{LOG_PREFIX} Starting MCG_TenLenh...");
    using (var tr = db.TransactionManager.StartTransaction())
    {
        try { /* logic */ tr.Commit(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"{LOG_PREFIX} ERROR: {ex.Message}");
            ed.WriteMessage($"\nLỗi: {ex.Message}");
        }
    }
}
```

### Transaction — bắt buộc khi đọc/ghi DB
```csharp
using (var tr = db.TransactionManager.StartTransaction())
{
    try { /* thao tác */ tr.Commit(); }
    catch (Exception ex) { Debug.WriteLine($"{LOG_PREFIX} ABORT: {ex.Message}"); throw; }
}
```

### Focus về bản vẽ sau button click — bắt buộc
```csharp
// Gọi trong handler của button cần tương tác bản vẽ (pick, select, v.v.)
Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
```

---

## 11. PaletteSet — Singleton (đọc PALETTE_GUIDE.md)

> Chi tiết đầy đủ: `.claude/PALETTE_GUIDE.md`

- **1 PaletteSet duy nhất** — GUID: `2b80cfe9-c560-49d6-8a09-9d636260fcf2`
- `PaletteManager.cs` là Singleton trong `Commands/`
- 5 tabs = 5 modules
- **Cấm** tạo PaletteSet thứ 2

**Thứ tự khởi tạo bắt buộc:**
```
1. new PaletteSet(name, guid)
2. AddVisual(...)          ← TRƯỚC khi set properties
3. DockEnabled / Size / KeepFocus
4. Visible = true          ← CUỐI CÙNG
```

---

## 12. WPF Patterns

```csharp
// ViewModel MVVM
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
    }
}

// Thread-safe UI update
Application.Current.Dispatcher.Invoke(() => { Status = "Processing..."; });
```

**Dark theme colors:**
```xml
BackgroundColor  #FF1E1E1E  |  SurfaceColor   #FF2D2D2D
AccentColor      #FF0078D4  |  BorderColor    #FF404040
TextPrimary      #FFEEEEEE  |  TextSecondary  #FF999999
WarningColor     #FFFFC107  |  ErrorColor     #FFFF5252
AmbiguousColor   #FFFF9800
```

---

## 13. Pre-code Checklist

```
[ ] Đọc SESSION_LOG.md — phase và bước tiếp theo?
[ ] Đọc module file và skill files tương ứng
[ ] File đã tồn tại chưa? Không tạo lại nếu có.
[ ] Namespace đúng theo vị trí file?
[ ] Interface cho Service mới?
[ ] LOG_PREFIX trong class?
[ ] Log đầu/cuối method quan trọng?
[ ] Transaction khi đọc/ghi AutoCAD DB?
[ ] Models: không import Autodesk.*?
[ ] Commands: không chứa logic?
```

---

## 14. Session End — Bắt buộc

Sau khi hoàn thành bất kỳ task nào → cập nhật `SESSION_LOG.md`:
- THÊM session mới lên **ĐẦU FILE**
- Tag module: `[MODULE:TenModule]`
- KHÔNG xóa session cũ

```markdown
## [MODULE:DetailDesign] Session YYYY-MM-DD HH:mm
### Đã làm
- file/path/name.cs — mô tả ngắn
### Trạng thái Phase
- Phase A: ██████░░░░ 60%
### Bước tiếp theo
- File: path/to/file.cs | Method: MethodName | Mục tiêu: ...
### Ghi chú API
- [Quirk AutoCAD API nếu có]
```

---

## 15. Quick Save

Khi user nhắn: `"lưu session"`, `"save session"`, `"hết token"`, `"tạm dừng"`
→ Lập tức cập nhật SESSION_LOG.md theo format mục 14.
→ Không hỏi lại. Không chờ xác nhận.
