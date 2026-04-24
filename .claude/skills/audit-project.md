---
name: audit-project
description: Audit project files on disk vs module architecture. Use when resuming after a broken session or to check project completeness.
disable-model-invocation: true
---

Kiểm tra toàn bộ trạng thái project thực tế trên disk.
Dùng khi session bị đứt hoặc muốn kiểm tra tình trạng project.

## Bước thực hiện

### 1. Xác định module đang active
Đọc `CONTEXT.md` → lấy giá trị `Module` và `Phase`.
Đọc `.claude/modules/{ActiveModule}.md` → lấy mục **File Architecture**.

### 2. Liệt kê files thực tế trên disk
```bash
find . -name "*.cs" -o -name "*.xaml" -o -name "*.csproj" \
  | grep -v "/bin/" | grep -v "/obj/" | grep -v "/.git/" \
  | sort
```

### 3. So sánh và lập bảng

| File trong module architecture | Tồn tại? | Trạng thái |
|---|---|---|
| `Models/{Module}/Enums/StructuralType.cs` | ✅ / ❌ | Complete / Partial / TODO |

Với mỗi file tồn tại, đánh giá nhanh:
- Class/interface chính đã có chưa?
- Còn `// TODO` chưa implement không?
- Có compile error rõ ràng không (thiếu using, sai namespace)?

### 4. Xác định Phase thực tế
So sánh files có trên disk với Phase Checklist trong module file:
- Phase nào **đã hoàn thành** (tất cả files có + không có TODO)?
- Phase nào **đang dang dở** (một số files có, một số thiếu)?
- Phase nào **chưa bắt đầu**?

### 5. Cập nhật SESSION_LOG.md
Thêm session audit lên ĐẦU FILE theo format:

```markdown
## [MODULE:{ActiveModule}] Audit YYYY-MM-DD HH:mm
### Kết quả audit
- Tổng files cần có: X | Đã có: Y | Thiếu: Z
- Phase hoàn thành: [list]
- Phase dang dở: [tên phase + % ước tính]
### Bước tiếp theo
- File: path/to/file.cs | Mục tiêu: ...
```

### 6. Báo cáo cho user
Tóm tắt:
- `✅ X files complete | ⚠️ Y files partial | ❌ Z files missing`
- Phase hiện tại + % hoàn thành
- Đề xuất bước tiếp theo cụ thể (tên file + method)
- Hỏi: "Tiếp tục từ [bước cụ thể] không?"

## Lưu ý
- KHÔNG viết code trong skill này — chỉ audit và báo cáo
- KHÔNG tự sửa files — chờ user confirm
- Phase Checklist nằm trong `.claude/modules/{ActiveModule}.md` mục 12
