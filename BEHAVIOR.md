# BEHAVIOR.md — Coding Behavior Guidelines

> Đọc file này ngay sau CLAUDE.md, mỗi session.
> Adapted từ Andrej Karpathy's CLAUDE.md cho MCGCadPlugin context.
> Những rule này giảm lỗi phổ biến khi AI coding.

---

## 1. Think Before Coding — Hỏi Trước, Code Sau

```
TRƯỚC KHI VIẾT BẤT KỲ DÒNG CODE NÀO:

✅ Nêu rõ assumptions:
   "Tôi hiểu yêu cầu là X. Đúng không?"
   Không tự assume rồi code sai hướng.

✅ Nhiều cách hiểu → liệt kê, không tự chọn:
   "Có 2 cách hiểu:
    A) ... → implement thế này
    B) ... → implement thế kia
    Bạn muốn cách nào?"

✅ Thấy cách đơn giản hơn → nói ra:
   "Có thể dùng X thay Y — đơn giản hơn, đủ không?"

✅ Không rõ → DỪNG và hỏi:
   Đừng đoán. Đừng code rồi hỏi sau.
   Chi phí hỏi < chi phí rewrite.
```

---

## 2. Surgical Changes — Chỉ Touch Những Gì Cần

```
KHI EDIT FILE ĐÃ CÓ SẴN:

✅ Chỉ thay đổi lines liên quan trực tiếp đến task.
   Test: "Mỗi dòng thay đổi có trace về yêu cầu không?"
   Nếu không → đừng thay.

❌ KHÔNG "cải thiện" code lân cận không liên quan.
❌ KHÔNG refactor nếu không được yêu cầu.
❌ KHÔNG đổi formatting/style của code hiện có.
❌ KHÔNG xóa dead code trừ khi được yêu cầu rõ ràng.
   → Nếu thấy dead code: nhắc user, không tự xóa.

✅ Match style của file đang edit:
   File dùng var → dùng var.
   File dùng explicit type → dùng explicit type.
   File có region → giữ region.

✅ Cleanup ONLY your own mess:
   Imports/variables mà CHÍNH BẠN tạo ra và không dùng → xóa.
   Imports/variables cũ không dùng → mention, không xóa.
```

### Tại sao quan trọng với MCGCadPlugin

```
BaseEdgeEngine.cs, TopologyEngine.cs là files phức tạp
Pass 1 đã STABLE → tuyệt đối không đụng vào Pass 1
khi đang fix Pass 2 hoặc Pass 1.5

Nếu cần đụng vào Pass 1: nói trước, giải thích lý do.
```

---

## 3. Simplicity — Minimum Code Giải Quyết Vấn đề

```
TRƯỚC KHI TẠO CLASS/METHOD MỚI — hỏi:

✅ "Cái này đã có trong Services/ chưa?"
✅ "Extend class hiện tại được không?"
✅ "200 lines hay 50 lines cho cùng kết quả?"
   Nếu có thể 50 → viết 50.

❌ Không thêm features ngoài yêu cầu.
❌ Không thêm "flexibility" không được yêu cầu.
❌ Không thêm parameters "phòng khi cần sau này".

Test câu hỏi: "Senior engineer có nói cái này
overcomplicated không?" → Nếu có → simplify.
```

### Exceptions cho MCGCadPlugin

```
Những thứ KHÔNG áp dụng simplicity rule:

1. Error handling AutoCAD API:
   AutoCAD có nhiều edge cases thực tế
   (null document, locked entity, transaction abort)
   → Error handling là BẮT BUỘC, không phải optional
   → Không cắt bớt error handling vì "simplicity"

2. Interface convention:
   Services luôn có Interface (IXxxService)
   → Đây là team standard, không phải over-engineering
   → Giữ nguyên kể cả khi chỉ có 1 implementation

3. Logging (LOG_PREFIX):
   Mọi class phải có LOG_PREFIX và log đầu/cuối method
   → Bắt buộc theo CLAUDE.md, không cắt vì "simplicity"
```

---

## 4. Goal-Driven — Verify Criteria Rõ ràng

```
TRANSFORM TASK THÀNH VERIFIABLE GOALS:

❌ Yếu: "Fix bug CL không đúng"
✅ Mạnh: "Sau fix: stiffener + bracket kề cận
          có cùng CL line và cùng throw direction.
          Verify bằng NETLOAD + zoom vào vị trí 4 trong DWG."

Multi-step task → state plan trước:
  1. [Action] → verify: [check cụ thể]
  2. [Action] → verify: [check cụ thể]
  3. [Action] → verify: [check cụ thể]

→ Plan rõ = có thể loop độc lập = ít hỏi lại
→ Plan mờ = phải hỏi liên tục = chậm
```

### Verify Criteria đã có trong ROADMAP.md

```
Mỗi step trong ROADMAP có verify criteria cụ thể
→ Đây là implementation của rule này
→ Trước khi code step X: đọc verify criteria của X
→ Sau khi code: tự check từng criteria trước khi báo done
```

---

## 5. AutoCAD-Specific Rules (thêm mới)

```
Những rule riêng do đặc thù AutoCAD .NET:

TRANSACTION:
  Mọi read/write AutoCAD DB → trong Transaction
  Không "optimize" bằng cách bỏ transaction

NULL CHECKS:
  Application.DocumentManager.MdiActiveDocument
  có thể null khi không có drawing mở
  → Luôn null check trước khi dùng

ENTITY MODIFICATION:
  GetObject với ForWrite chỉ khi thực sự cần write
  ForRead khi chỉ đọc → tránh lock không cần thiết

STABLE CODE:
  Pass 1 của BaseEdgeEngine = STABLE
  → Không đụng vào Pass 1 khi fix Pass 2/1.5
  → Nếu cần: tạo method mới thay vì modify method cũ
```

---

## 6. Quick Reference Card

```
TRƯỚC KHI CODE:         KHI EDIT:              VERIFY:
─────────────────       ────────────────────   ──────────────────
State assumptions    →  Touch only needed   →  Check each criteria
List interpretations →  Match existing style→  Build passes
Simpler way? Say so  →  Don't refactor      →  NETLOAD test
Unclear? ASK         →  Mention dead code   →  Report result
```
