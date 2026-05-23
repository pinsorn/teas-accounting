# Question-Sana-Handoff-Answers

**From:** ซานะ session ก่อน (Sprint 13b → 13e P1 + housekeeping)
**To:** ซานะ session ใหม่ที่รับช่วงต่อหลัง Sprint 13e P2-P5
**Date:** 2026-05-19/20 handoff
**Reference:** Question-Sana-Handoff.md (5 หลัก + 1 optional)

---

## 1. Chapter 3 test plan — negative cases coverage

ไม่มี draft ไฟล์ — แนะนำซานะใหม่เขียน `Test-Plan-Chapter-3.md` (one-pager) ก่อน start validate.

**Priority 1 (must):**
- Happy path full cycle: Q → Issue → Accept → Convert → SO → Confirm → DO → Issue → Delivered, parallel SO → TI → Post → Receipt → Post
- Reissue flow: Post TI → create CN against → see CN posted → optionally create new TI as reissue (compliance — ห้าม void)
- Immutability after Post: try edit Post-ed TI → expect 409 (legal); try delete Post-ed → expect block

**Priority 2 (should):**
- RBAC: accountant + AP_clerk + AR_clerk can create TI; specific gates per role
- Validation 400: missing customer, BU not selected when enforce-BU on, empty line items, negative qty
- Double-conversion: try Convert Q twice → expect 409 or button hidden
- Q→SO partial — Phase 1 = 1:1 only, partial = Phase 2 backlog (see Plan §6.4 final lock)

**Priority 3 (nice):**
- e-Tax mock failure paths (Sprint 13c Tier 1 mock — toggleable failure injection)
- Idempotency: same Idempotency-Key replay → expect cached response

**Patterns reuse จาก chapter 2:**
- Error envelope v1 assertion (`urn:teas:error:*` + `fieldErrors[]`)
- AlertDialog assertion (`[role="alertdialog"]` + Thai title + ปุ่ม destructive variant)
- PermissionGate assertion (button hidden for missing scope, not just disabled)
- DOM-assert > waitForResponse (Sprint 13g lesson — see 02.01/02.02 for example)

---

## 2. DocumentStatusBadge — existing convention

ยังไม่ได้ inspect codebase ลึกพอจะตอบแน่นอน — ซานะใหม่ grep ก่อน:

```bash
grep -r "Badge\|badge\|StatusChip" frontend/components frontend/app | grep -v node_modules
```

**ที่เห็นจริง** จาก Sprint 13f Chrome MCP inspect: WHT-types modal ใช้
`<span class="badge badge-ghost badge-xs">` (**DaisyUI**). Stack project = Next.js + Tailwind + DaisyUI + shadcn (ทั้งคู่). DaisyUI มี semantic classes built-in:
- `.badge-success` (green) — สำหรับ Posted / Active
- `.badge-warning` (amber) — สำหรับ Draft
- `.badge-error` (red) — สำหรับ Cancelled / Rejected
- `.badge-info` (blue) — สำหรับ Confirmed / In Progress
- `.badge-neutral` (gray) — สำหรับ Converted / Read-only

**แนะนำ spec ให้ Claude Code:** `<DocumentStatusBadge status={status} />` ห่อ DaisyUI `<span class="badge badge-{variant}">` + Thai tooltip via `title` attr. **อย่าให้ Claude Code สร้าง custom Badge component จาก scratch** — DaisyUI พร้อมใช้แล้ว.

Color mapping ที่ซานะคนเก่าเขียนใน Answer-26 (Draft=gray, Posted=teal ฯลฯ) ปรับให้ใช้ DaisyUI semantic colors แทน hardcoded hex.

---

## 3. Q→SO conversion semantics ที่ Answer-22 lock

Answer-Sana-Backend22.md + Plan §6.4 (เดิม) **ไม่ได้ explicit lock 1:1 vs partial**.

**ผมเพิ่ง update Plan §6.4** ใส่ section "Q → SO conversion semantics (Phase 1 locked)" ที่ระบุ:

- **1 Q → 1 SO** (one-to-one mapping). Single FK column `Quotation.ConvertedToSalesOrderId` (nullable, set on convert).
- After convert: **source Q status = `Converted`, read-only**. No further edit, no second convert (button hidden via state-machine).
- **No join table** in Phase 1 — single FK is sufficient.
- Convert action copies: customer, BU, line items, notes, discount. Date fields reset to today (SO is a fresh commit).
- After convert, user can adjust qty/price/lines on the SO before Confirm.

Phase 2 backlog: partial conversion (1 Q → multi SO) จะแทน single FK ด้วย join table.

→ Sana ใหม่ใช้ Plan §6.4 (updated) เป็น source of truth. Answer-26 ที่เขียนไว้ assume 1:1 + lock + single FK = ตรงสเปก.

---

## 4. Migration backfill values สำหรับ stub Q rows

**ไม่ได้ inspect accounting_dev count วันนี้** — แต่จาก manual-demo seed survey ตอน Sprint 13b: Quotations list หน้าแรกขึ้น "ไม่มีข้อมูล" = น่าจะ **0 stub Q rows**. ถ้าจริง backfill ไม่กระทบ.

**Verify ก่อน gen migration** (Sana ใหม่ ask Claude Code session ใหม่ให้ run):

```sql
SELECT COUNT(*) FROM sales.quotations;
```

- 0 rows → migration backfill ไม่ matter, ค่าอะไรก็ได้
- >0 rows → ตกลง defaults ตามนี้:

| Column | Default | Note |
|---|---|---|
| `Status` | `'Draft'` | NOT NULL DEFAULT 'Draft' |
| `IssuedAt` | NULL | nullable — set on Issue transition |
| `AcceptedAt` | NULL | nullable |
| `ConvertedAt` | NULL | nullable |
| `ConvertedToSalesOrderId` | NULL | nullable FK |
| `ValidUntil` | `doc_date + INTERVAL '30 days'` | NOT NULL — backfill via `Sql()` block in migration |
| `Discount` | `0` | NOT NULL DEFAULT 0 |
| `Notes` | NULL | nullable TEXT |

⚠️ Migration MUST include `Sql()` block for ValidUntil backfill, otherwise NOT NULL constraint จะ reject existing rows.

⚠️ Migration-safety carry-over (Sprint 13d lesson, runtime-gotchas §25): **NEVER** `dotnet ef migrations add --no-build`. **NEVER** `migrations remove` on a desynced snapshot. Verify via `git status Migrations/` clean before any operation.

---

## 5. #59 tenant isolation audit — priority + timing

**"FUTURE — post chapter manual"** = หลัง chapter manual 1-10 เสร็จทั้งหมด (ถ้า launch timeline ไม่กระชั้น).

**เหตุผล:**
- Manual = forward-progress, end-user-visible value
- Audit = security hardening, infrastructure (prod RLS layer ปัจจุบัน mitigates — Sprint 13f Report: "prod role ≠ BYPASSRLS")

**แต่ถ้า production launch timeline ใกล้** — ควรเลื่อนขึ้นคู่กับ chapter 4-5 (สมมติ chapter 4-5 scope เล็กกว่า chapter 3). กระจาย risk.

**Draft list ของ tenant entities ที่ควรตรวจ** (Sana ใหม่ทำ `Test-Plan-Tenant-Audit.md` แยกได้):

- `master.business_units` (BU)
- `master.products`
- `master.vendors`
- `master.customers`
- `master.expense_categories`
- `master.api_keys`
- `master.company_profile` (Sprint 13d — ตรวจด้วย)
- `tax.wht_types` ✅ fixed Sprint 13f
- `tax.wht_certificates`
- `sales.*` (Q/SO/DO/TI/RC/CN/DN ทั้งหมด)
- `purchase.*` (PR/PO/GR/VI/PV/WHT-cert)
- `gl.*` (journal entries, periods)

**Audit checklist per entity:**
- [ ] Entity registered ใน EF global query filter (`OnModelCreating` filter on `CompanyId`)?
- [ ] Every service read/mutation has explicit `Where(x => x.CompanyId == tenant.CompanyId)`?
- Both required (defense-in-depth per CLAUDE.md §4.7).

---

## 6. Pattern ที่นายท่าน push back (lessons)

**คำเตือนหลัก:**

### (a) "ลักไก่ไม่รู้เรื่อง" — ทำไปเช็คไป
ตอน chapter 2 ผมเขียน walkthroughs โดยแค่เปิด modal + cancel (ไม่ test submit จริง). นายท่านดุ. **ห้ามทำ.** Exercise full flow + verify acceptance criteria จริง. Live test ทุก mutation.

### (b) Strict chapter-sequential
ตอนแรกผมเสนอ parallel work (Sana chapter 4 ระหว่าง Claude Code 13d). นายท่าน OK ชั่วคราว แล้วเปลี่ยนเป็น "VALIDATE → FIX → RE-VALIDATE → CREATE MANUAL per chapter, NO PARALLEL". อยู่ใน CLAUDE.md §16. **อย่าเสนอ parallel chapters อีก.**

### (c) Manual workflow relax (positive)
หลัง Sprint 13g pilot สำเร็จ (framework production-grade), นายท่าน relax §16 ให้ "Manual ค่อยทำทีเดียวก็ได้ — script ไว้, render ทีเดียวตอนจบ". **Per-chapter PDF render = optional ตอนนี้.** Final batched render ตอน ship.

### สิ่งที่นายท่าน OK + ชม
- **Spec ละเอียด** (Backend21-25 ยาวมาก) — OK ทุกครั้ง
- **ROI estimate** ทุก spec — OK
- **Honest assessment** — โปรดมาก. Claude Code self-deferring context limit นายท่านชม. ยอมรับผิดเรื่องลักไก่ — นายท่าน OK ไม่ต่อว่าซ้ำ.
- **"เอาตามที่ซานะเห็นสมควร"** — นายท่าน trust judgment ของซานะในเรื่อง design (Company Profile hybrid lock เป็นตัวอย่าง)
- **Subagent + session discipline** — นายท่านเสนอเองตอน context สะสมเริ่มเยอะ. แสดงว่า context-hygiene ก็เป็น value.

### Persona reminders
- เรียกเจ้านายว่า "เจ้านาย" หรือ "นายท่าน"
- Thai mixed English — English สำหรับ tech terms (code, library, framework), Thai สำหรับ conversation + business domain
- ไม่ใช้ asterisk action descriptions (*ยิ้ม* *วาง* ฯลฯ)
- Cool demeanor + warmth via competence — ไม่ gushing
- Honest > optimistic

---

## Carry-over open tasks (handoff)

| # | Title | Defer note |
|---|---|---|
| 38 | Missing UI features (dark mode + company switcher tracking) | Phase 2 |
| 58 | WHT-types disable lacks AlertDialog confirm | Sprint 13e housekeeping or later |
| 59 | Tenant isolation audit (this doc §5) | Post-chapter-manual or earlier if timeline |
| 69 | A11y: icon buttons missing aria-label (WCAG 2.1) | Sprint 13e P5 housekeeping (alongside DocumentStatusBadge) |

---

## Files Sana ใหม่ ควรอ่านก่อนเริ่ม

1. `CLAUDE.md` — full project context + §16 chapter workflow + §15 test data discipline
2. `progress.md` — last entry cont.48 (Sprint 13e P1 done)
3. `docs/accounting-system-plan.md` — skim § headers; deep-read §6 Sales (incl. updated §6.4 state machines + Q→SO semantics), §20.7 ErrorEnvelopeV1, §6.7 Company Profile
4. `docs/runtime-gotchas.md` — 28 gotchas; §25 ef-migrations + §26 tenant isolation + §27 Next.js routing trap (latest) เป็น must-know
5. `docs/Answer-Sana-Backend22.md` — Sprint 13e full spec (P1-P5)
6. `docs/Report-Backend28.md` — Sprint 13e P1 status + P2-P5 file-level plan (Claude Code's honest hand-off)
7. This file (`Question-Sana-Handoff-Answers.md`)

---

## Sign-off

ขอบคุณ Sana ใหม่ที่อ่านอย่างละเอียดก่อนเริ่ม. งาน chapter 3 จะหนักกว่า chapter 2 (7 walkthroughs vs 5, compliance-heavy domain). ตั้งใจระมัดระวัง — โดยเฉพาะ immutability + reissue + e-Tax mock paths.

ถ้านายท่านสั่งให้ใช้ subagent (Sonnet via Agent tool, `general-purpose`) สำหรับ Chrome MCP testing repetitive — รับเลย. Brief subagent ด้วย step-by-step + acceptance criteria ชัด + รวบรวมผล. อย่า delegate exploratory validate (ต้อง deep judgment).

โครงการนี้คุณภาพดีเพราะนายท่าน Ham push back ตรง — รักษา discipline นั้นไว้.

— ซานะคนเก่า (Sprint 13b → 13e P1)
