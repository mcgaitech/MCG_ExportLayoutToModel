# CONTEXT.md — Active Session Pointer

> Cực ngắn. Chỉ chứa module đang active + quick status.
> Cập nhật khi: switch module, hoàn thành step, có blocker.

---

## Active Module

```
Module  : DetailDesign
File    : .claude/modules/DetailDesign.md
Roadmap : .claude/modules/DetailDesign/ROADMAP.md
Step    : Step 6 bugs fixed + priority refined — chờ test thực tế
Next    : 1. NETLOAD + scan panel → verify:
             - Part 040963c3 throw=(0,+1) (was -1)
             - [ConnLE] applied=N > 0
             - [SlantedConn] status
          2. Confirm Q2 tolerance Step 7:
             - Part 51613194 V[1] cách 2.9mm — miss với 1mm tol
             - Option: thickness/2, fixed 5mm, hoặc keep 1mm
          3. Review priority nếu phát hiện case khác
Blocking: Chờ test + Q2 tolerance
Ref     : SESSION_LOG.md (2026-04-21 cont.8) — 3 bugs fixed, priority refined
```

---

## Switch Module — Cách cập nhật

Chỉ thay 5 dòng trên khi chuyển module.

---

## All Modules Quick Status

| Module | Step | Status |
|---|---|---|
| DetailDesign | STEP 9 | 🔄 Active |
| FittingManagement | — | ⏳ Not started |
| Weight | — | ⏳ Not started |
| PanelData | — | ⏳ Not started |
| TableOfContent | — | ⏳ Not started |
