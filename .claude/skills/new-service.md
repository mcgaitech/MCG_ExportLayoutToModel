---
name: new-service
description: Scaffold a new Service + Interface for the active module. Usage: /new-service ServiceName "description"
disable-model-invocation: true
argument-hint: <ServiceName> "<short description>"
---

Tạo cặp Interface + Service class mới theo chuẩn project.
Tự động xác định namespace và sub-folder từ module đang active.

## Arguments
- `$0` — Tên Service (không có chữ "Service"). Ví dụ: `OBBCalculator`, `ThrowVector`
- `$1` — Mô tả ngắn (optional). Ví dụ: `"Computes OBB from polyline vertices"`

---

## Bước 1 — Đọc Active Module Context

Trước khi tạo file, đọc `CONTEXT.md`:
```
Module: {ActiveModule}   ← ví dụ: DetailDesign
```

Đọc `.claude/modules/{ActiveModule}.md` mục **File Architecture**:
- Xác định sub-folder phù hợp cho service mới
- Ví dụ: `$0 = OBBCalculator` → thuộc `Services/DetailDesign/Geometry/`

Kết quả cần biết trước khi tạo file:
```
ActiveModule  : DetailDesign
SubFolder     : Services/DetailDesign/Geometry/   ← user xác nhận nếu không rõ
Namespace     : MCGCadPlugin.Services.DetailDesign
InterfaceFile : Services/DetailDesign/Geometry/I$0Service.cs
ServiceFile   : Services/DetailDesign/Geometry/$0Service.cs
```

---

## Bước 2 — Tạo Interface file

**Path:** `Services/{ActiveModule}/{SubFolder}/I$0Service.cs`

```csharp
using System;
using System.Collections.Generic;

namespace MCGCadPlugin.Services.{ActiveModule}
{
    /// <summary>
    /// Interface cho $0Service.
    /// $1
    /// </summary>
    public interface I$0Service
    {
        // TODO: Điền method signatures
    }
}
```

---

## Bước 3 — Tạo Service class file

**Path:** `Services/{ActiveModule}/{SubFolder}/$0Service.cs`

```csharp
using System;
using System.Diagnostics;

namespace MCGCadPlugin.Services.{ActiveModule}
{
    /// <summary>
    /// $1
    /// </summary>
    public class $0Service : I$0Service
    {
        #region Fields
        private const string LOG_PREFIX = "[$0Service]";
        #endregion

        #region Constructor
        /// <summary>Khởi tạo $0Service</summary>
        public $0Service()
        {
            Debug.WriteLine($"{LOG_PREFIX} Initialized.");
        }
        #endregion

        #region Public Methods
        // TODO: Implement methods từ I$0Service
        #endregion

        #region Private Helpers
        #endregion
    }
}
```

---

## Bước 4 — Thông báo và hướng dẫn DI

Sau khi tạo file, thông báo:

```
✅ Đã tạo:
   Services/{ActiveModule}/{SubFolder}/I$0Service.cs
   Services/{ActiveModule}/{SubFolder}/$0Service.cs

Đăng ký DI tại entry point (PanelScanService hoặc Command):
   I$0Service {camelCase}Service = new $0Service();
   // Inject vào class cần dùng qua constructor
```

---

## Bước 5 — Cập nhật module file và session log

**Cập nhật `.claude/modules/{ActiveModule}.md`:**
- Thêm 2 file mới vào mục **File Architecture** (đánh dấu `[ ] NEW`)

**Cập nhật `SESSION_LOG.md`** (thêm lên đầu):
```markdown
## [MODULE:{ActiveModule}] New Service YYYY-MM-DD HH:mm
### Đã tạo
- Services/{ActiveModule}/{SubFolder}/I$0Service.cs
- Services/{ActiveModule}/{SubFolder}/$0Service.cs
### Bước tiếp theo
- Implement methods trong $0Service
```

---

## Quy tắc namespace — Tóm tắt

| Active Module | Namespace Services | Namespace Models |
|---|---|---|
| DetailDesign | `MCGCadPlugin.Services.DetailDesign` | `MCGCadPlugin.Models.DetailDesign` |
| FittingManagement | `MCGCadPlugin.Services.FittingManagement` | `MCGCadPlugin.Models.FittingManagement` |
| Weight | `MCGCadPlugin.Services.Weight` | `MCGCadPlugin.Models.Weight` |
| PanelData | `MCGCadPlugin.Services.PanelData` | `MCGCadPlugin.Models.PanelData` |
| TableOfContent | `MCGCadPlugin.Services.TableOfContent` | `MCGCadPlugin.Models.TableOfContent` |

> Pattern: `MCGCadPlugin.{Layer}.{ActiveModule}`
> Layer = Services / Models / Views / Commands / Utilities
