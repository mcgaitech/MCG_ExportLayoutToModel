---
name: build-and-test
description: Build the AutoCAD plugin and report results. Use after writing new code to verify compilation.
disable-model-invocation: true
---

Build plugin và báo cáo kết quả chi tiết.

## Bước thực hiện

### 1. Chạy build
```bash
dotnet build -c Debug
```

### 2. Phân tích kết quả

#### Build THÀNH CÔNG
- Ghi nhận tên DLL có timestamp: `MCGCadPlugin_{yyyyMMdd_HHmmss}.dll`
- Liệt kê warnings nếu có (không bỏ qua warning)
- In path DLL để user NETLOAD:
  ```
  ✅ Build OK → bin\Debug\MCGCadPlugin_{timestamp}.dll
  ```
- Chuyển sang bước 3

#### Build THẤT BẠI
Phân tích từng lỗi `error CS`, đề xuất fix theo thứ tự ưu tiên:

| Error code | Nguyên nhân | Cách fix |
|---|---|---|
| `CS0246` | Thiếu reference DLL | Kiểm tra `<Reference>` hoặc `<PackageReference>` trong csproj |
| `CS0234` | Namespace không tìm thấy | Kiểm tra `using` directive + namespace chính xác |
| `CS1061` | Method không tồn tại | Sai tên API AutoCAD — kiểm tra version 2023 |
| `CS0103` | Biến/class không tìm thấy | Thiếu `using` hoặc sai namespace |
| `CS0117` | Member không có trong type | AutoCAD 2023 API khác version khác |
| `CS0051` | Inconsistent accessibility | Interface public nhưng type return là internal |

Fix lỗi → chạy lại `/build-and-test`.

### 3. Checklist test trong AutoCAD (sau build thành công)

```
[ ] Mở AutoCAD 2023
[ ] Gõ lệnh: NETLOAD
[ ] Browse đến: bin\Debug\MCGCadPlugin_{timestamp}.dll
[ ] Gõ lệnh: MCG_DetailDesign (hoặc lệnh đang test)
[ ] Palette "MCG Tools" hiện ra và dock đúng?
[ ] Tab "Detail Design" active?
[ ] Mở DebugView (Sysinternals) → xem log khởi động
[ ] Kiểm tra log: không có ERROR, có "[PaletteManager] Initialized"
```

### 4. Cập nhật SESSION_LOG.md
Sau khi build + test xong, thêm kết quả vào đầu SESSION_LOG.md:

```markdown
## [MODULE:{ActiveModule}] Build YYYY-MM-DD HH:mm
### Kết quả build
- Status: ✅ OK / ❌ FAIL
- DLL: MCGCadPlugin_{timestamp}.dll
- Warnings: [list hoặc "None"]
- Test AutoCAD: Pass / Fail
### Ghi chú
- [Quirk AutoCAD API nếu phát hiện]
```

## Lưu ý
- Debug build luôn có timestamp trong tên DLL → không cần restart AutoCAD
- Nếu AutoCAD báo "Assembly already loaded" → gõ lại NETLOAD với DLL mới
- Log xem qua DebugView (Sysinternals) hoặc Output window trong VS Code
