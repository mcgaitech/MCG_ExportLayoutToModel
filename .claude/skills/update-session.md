---
name: update-session
description: Save current session progress to SESSION_LOG.md and sync module file. Use before ending a session or when approaching context limit.
disable-model-invocation: true
---

Lưu tiến độ session hiện tại. Đồng bộ SESSION_LOG + module file + CONTEXT.md.

## Bước 1 — Thu thập thông tin

Trả lời các câu hỏi sau trước khi ghi:

```
Active module  : [từ CONTEXT.md]
Phase hiện tại : [Phase X — tên]
% hoàn thành   : [ước tính dựa trên checklist]
Files đã làm   : [list file tạo/sửa trong session này]
Quirk API mới  : [AutoCAD API quirk phát hiện, hoặc "None"]
Quyết định mới : [thay đổi technical decision, hoặc "None"]
Bước tiếp theo : [CỤ THỂ: tên file + tên method + lý do]
```

---

## Bước 2 — Cập nhật SESSION_LOG.md

**Thêm lên ĐẦU FILE** (không xóa session cũ):

```markdown
## [MODULE:{ActiveModule}] Session YYYY-MM-DD HH:mm

### Đã làm
- `path/to/file.cs` — mô tả ngắn đã làm gì
- `path/to/file2.cs` — ...

### Trạng thái Phase
- Phase {X} — {Tên}: {████░░░░░░} {%}
  ✅ {A.1} | ✅ {A.2} | 🔄 {A.3} | ⏳ {A.4}

### Bước tiếp theo
- File   : `path/to/next-file.cs`
- Method : `MethodName()`
- Mục tiêu: [mô tả rõ phải làm gì]

### Ghi chú API
- [AutoCAD API quirk nếu có — để tham chiếu sau]
- [Hoặc "None"]
```

---

## Bước 3 — Cập nhật Phase Checklist trong module file

Mở `.claude/modules/{ActiveModule}.md` → mục **Phase Checklist**:
- Đánh dấu `✅` cho tasks đã hoàn thành
- Cập nhật `🔄` cho task đang làm
- Thêm ghi chú nếu task bị block

---

## Bước 4 — Cập nhật CONTEXT.md

Nếu phase hoặc bước tiếp theo thay đổi, cập nhật 3 dòng:
```
Phase   : [phase mới]
Next    : [bước tiếp theo mới]
Blocking: [None hoặc mô tả blocker]
```

---

## Bước 5 — Nếu có Quirk API mới

Thêm vào `.claude/modules/{ActiveModule}.md` cuối file:

```markdown
## API Quirks (phát hiện trong quá trình implement)

| API / Method | Vấn đề | Workaround |
|---|---|---|
| `db.Insunits` | Trả về `UnitsValue` enum cần cast `(int)` trước | `(UnitsValue)(int)db.Insunits` |
```

---

## Bước 6 — Xác nhận

In tóm tắt những gì đã ghi:

```
✅ SESSION_LOG.md — thêm session [MODULE:{ActiveModule}] {timestamp}
✅ .claude/modules/{ActiveModule}.md — cập nhật Phase Checklist
✅ CONTEXT.md — cập nhật Next + Phase
[✅ / ⏭️] API Quirks — [có cập nhật / không có quirk mới]
```

---

## Lưu ý quan trọng

```
Bước tiếp theo PHẢI cụ thể:
  ✅ "File: Services/DetailDesign/XData/XDataManager.cs
      Method: Write(ObjectId, Transaction, XDataPayload)
      Mục tiêu: implement ghi XData lên entity"

  ❌ "Tiếp tục Phase A"
  ❌ "Làm XDataManager"

Ghi đủ context để session sau KHÔNG cần đọc lại conversation.
```
