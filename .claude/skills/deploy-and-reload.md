---
name: deploy-and-reload
description: Build plugin, show DLL path for NETLOAD in AutoCAD. Use after finishing new code to test.
disable-model-invocation: true
allowed-tools: Bash
---

Build plugin và hướng dẫn NETLOAD vào AutoCAD đang mở.
Không cần đóng/mở lại AutoCAD nhờ cơ chế timestamp DLL.

## Bước 1 — Build Debug

```bash
dotnet build -c Debug
```

## Bước 2 — Xác định DLL mới

```bash
# Tìm DLL timestamp mới nhất
ls -t bin/Debug/MCGCadPlugin_*.dll | head -1
```

Kết quả dạng:
```
bin\Debug\MCGCadPlugin_20240115_103045.dll
```

## Bước 3 — Phân tích kết quả build

### Build THẤT BẠI → Fix lỗi
Đọc output, tìm `error CS`:
- `CS0246` → thiếu reference, kiểm tra PackageReference trong csproj
- `CS1061` → method không tồn tại, sai AutoCAD API
- `CS0103` → thiếu `using` hoặc sai namespace

Fix → chạy lại `/deploy-and-reload`.

### Build THÀNH CÔNG → Hướng dẫn NETLOAD

In rõ hướng dẫn cho user:

```
╔══════════════════════════════════════════════════════════╗
║  ✅ BUILD OK — Sẵn sàng NETLOAD                          ║
║                                                          ║
║  Trong AutoCAD, chạy theo thứ tự:                        ║
║                                                          ║
║  1. Gõ lệnh: NETLOAD                                     ║
║  2. Browse đến file:                                     ║
║     bin\Debug\MCGCadPlugin_{timestamp}.dll               ║
║  3. Gõ lệnh: MCG_DetailDesign                            ║
║                                                          ║
║  Nếu báo lỗi "already loaded" → vẫn NETLOAD lại         ║
║  DLL mới (timestamp khác) sẽ load song song OK           ║
╚══════════════════════════════════════════════════════════╝
```

## Bước 4 — Sau khi user test trong AutoCAD

Hỏi kết quả:
```
Test trong AutoCAD kết quả thế nào?
[ ] Palette hiện đúng
[ ] Tab đúng module
[ ] Chức năng đang test hoạt động
[ ] Không có lỗi trong DebugView
```

### Nếu PASS:
- Đánh dấu task/phase hoàn thành trong `.claude/modules/{ActiveModule}.md`
- Cập nhật `CONTEXT.md` → bước tiếp theo
- Cập nhật `SESSION_LOG.md`

### Nếu FAIL:
- Hỏi: "Log lỗi trong DebugView là gì?"
- Phân tích log → đề xuất fix
- Build lại và NETLOAD lại

## Lưu ý quan trọng

```
✅ Timestamp DLL = không cần restart AutoCAD
✅ Mỗi build tạo DLL tên khác → NETLOAD trực tiếp
⚠️ Nhiều DLL cũ trong bin/Debug → không cần xóa, không ảnh hưởng
⚠️ Release build (không có timestamp) chỉ khi user confirm "OK all"
```
