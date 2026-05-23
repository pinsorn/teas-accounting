# Answer-Sana-Backend28 — Sprint 13i: Bug fix + UX cleanup + deferred FE tails

**Owner:** Claude Code
**Spec author:** Sana (post Sprint 13h RE-VALIDATE truly-deep batch 1, 2026-05-21)
**Sequencing:** AFTER Sprint 13h ☑. Sprint 13i is the **first of 4** sub-sprints (per Sana's scope assessment + Ham's "a" decision):
- **Sprint 13i — Bug fix + UX cleanup (this spec). 3-4 days.**
- Sprint 13j — Print/PDF revamp + Font + Logo embed. (Answer-29, lands after 13i ship + Sana RE-VALIDATE)
- Sprint 13k — Security + RBAC full Cartesian + Performance + Accessibility audit. (Answer-30, after 13j)
- Sprint 13L — DevOps: Migration rollback + Build pipeline + Test skip audit. (Answer-31, after 13k)

**ROI:** 3-4 days. Clears 9 known bugs + 10 carry-over FE tails. Unblocks deeper category coverage in 13j/k/L.
**Workflow gate:** CLAUDE.md §16 — chapter 3 manual stays deferred until ALL 4 sub-sprints ship + Sana RE-VALIDATE deep mode green.

---

## Background — what Sana found

Sana ran truly-deep batch 1 (Chrome MCP, 2026-05-21) after Ham flagged the first-pass as shallow. Findings:

**P0:**
- **SR2** RBAC seed gap — Sprint 13h P1 seed 320 omitted `sales.receipt.*` + `sales.credit_note.*` + `sales.debit_note.*` grants for AR_CLERK/ACCOUNTANT. demo-accountant 403 on /receipts + /tax-adjustment-notes (CN/DN).

**P1:**
- **SR4** — `QueryState` swallows 403 → "ไม่มีข้อมูล" (empty state) instead of "ไม่มีสิทธิ์เข้าถึง". Hides RBAC gaps from UI users.
- **SR5** — `CustomerSelector` missing lookup-on-mount. TI from Q prefill shows `"#5"` db id, not "บริษัท แอคมี จำกัด" name.
- **SR6** — Receipt form Post button silently aborts on empty validation (no toast, no field highlight). Systemic likely across 7 forms.
- **SR7** — "แก้ไข" link on Q/SO/RC list rows opens read-only detail page, not Edit form. Mislabel. Confused user expectation.
- **SR8** — TI "พิมพ์" button = native `window.print()` printing HTML detail screen. NOT the PDF endpoint. Should print the legal Tax Invoice PDF. Systemic likely across all detail pages.
- **SR9** — BN form submit without BU = generic "เกิดข้อผิดพลาด" toast. No field highlight. Same family as SR6 but at least surfaces a toast.

**Process:**
- **SR-OWN-1** — Sana's spec authoring lesson. Answer-Sana-Backend27 P1 RBAC matrix omitted Receipt + CN/DN. Future RBAC specs must enumerate ALL 8 sales surfaces + master surfaces in role × surface table.

**Confirmed working:**
- Tenant isolation: cross-tenant id probe (`/customers/99999` other-co) returns 404 — no leak.
- BN create end-to-end (doc# `05-2026-BL-ECOM-0001`, status "ออกแล้ว", Total ฿1,605, Thai toast).
- XML 0-byte fix (P11) — 73 KB valid UBL Invoice.
- ProductPicker + TaxInvoicePicker portal — no clip.
- `<select>` CSS — full height.

**Observed but deferred (Sprint 13j+):**
- TI PDF Layout (Sprint 13e era, Ham flagged "แย่มาก" — full revamp = 13j R1)
- TI XML encoding=utf-16 (compliance review → 13j N1)
- TI cross-ref panel missing Q+SO+DO chain chips (Sana noted) — small enough to fold into 13i R5
- BN Cancel button no UI feedback (likely browser `confirm()` off-screen) — fixed by C9 replace with AlertDialog

---

## Sprint 13i scope — 17 phases (priority-ordered)

### Bug fix block (B1-B7) — ship FIRST, gates re-validate

**B1 — SR2 RBAC complete grants** (~0.5 hr)
New seed `330_seed_receipt_adjnote_rbac.sql` (additive + idempotent, mirrors 320):
```sql
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.receipt.read',   'sales', 'receipt',    'read',   'View Receipts'),
    ('sales.receipt.create', 'sales', 'receipt',    'create', 'Create Receipts'),
    ('sales.receipt.post',   'sales', 'receipt',    'post',   'Post Receipts'),
    ('sales.credit_note.read',   'sales', 'credit_note',   'read',   'View Credit Notes'),
    ('sales.credit_note.create', 'sales', 'credit_note',   'create', 'Create Credit Notes'),
    ('sales.credit_note.post',   'sales', 'credit_note',   'post',   'Post Credit Notes'),
    ('sales.debit_note.read',    'sales', 'debit_note',    'read',   'View Debit Notes'),
    ('sales.debit_note.create',  'sales', 'debit_note',    'create', 'Create Debit Notes'),
    ('sales.debit_note.post',    'sales', 'debit_note',    'post',   'Post Debit Notes')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r JOIN sys.permissions p ON p.permission_code IN (
    'sales.receipt.read', 'sales.credit_note.read', 'sales.debit_note.read')
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AR_CLERK','SALES_STAFF','AUDITOR')
ON CONFLICT DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r JOIN sys.permissions p ON p.permission_code IN (
    'sales.receipt.create','sales.receipt.post',
    'sales.credit_note.create','sales.credit_note.post',
    'sales.debit_note.create','sales.debit_note.post')
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AR_CLERK')
ON CONFLICT DO NOTHING;
```

Plus audit `ReceiptEndpoints.cs` + `TaxAdjustmentNoteEndpoints.cs` group-level vs per-endpoint authorization. Split GET (read) vs POST/PUT (create/manage/post) per the Sprint 13h `CustomerEndpoints.cs` refactor pattern.

**Acceptance:** demo-accountant traverses all 10 sales surfaces (Q/SO/DO/TI/RC/CN/DN/BN + customer + vendor) — 0 × 403.

**B2 — SR4 QueryState 403 surface** (~1 hr)
`components/states/QueryState.tsx`:
- 401 → already handled (redirect /login)
- **403 → render "ไม่มีสิทธิ์เข้าถึง" + lock icon + suggest contact admin** (NEW)
- 404 → "ไม่พบหน้านี้"
- 5xx → "เกิดข้อผิดพลาดในระบบ" + retry button

`lib/api/errors.ts` `parseApiError` already returns typed envelope — wire `status === 403` branch into QueryState.

**Acceptance:** As `demo-accountant` BEFORE B1 lands, `/receipts` should show "ไม่มีสิทธิ์" not "ไม่มีข้อมูล".

**B3 — SR5 CustomerSelector lookup-on-mount** (~1.5 hr)
`components/ui/CustomerSelector.tsx`:
- New `useCustomer(id)` React Query hook in `lib/queries.ts`
- On mount: if `value` prop is set and the picker doesn't have a display label cached → fetch `/api/proxy/customers/{id}` → display name + tax_id
- Same lookup-on-mount pattern as Sprint 13h `TaxInvoicePicker` already does
- Apply same fix to `VendorSelector` if it has the same gap (audit)

**Acceptance:** `/tax-invoices/new?fromQuotationId=1` → customer combobox shows "บริษัท แอคมี จำกัด (0105556123453)" matching Q's customer.

**B4 — SR6/SR9 systemic form validation feedback** (~3 hr — 7 forms)
React Hook Form `required` + error rendering on:
- QuotationForm.tsx — required: customer, BU, dates, ≥1 line item
- SalesOrderForm.tsx — same shape
- DeliveryOrderForm.tsx — same shape
- TaxInvoiceForm (tax-invoices/new) — required: customer, BU, ≥1 line
- ReceiptForm (receipts/new) — required: customer, BU, ≥1 TI application
- AdjustmentNoteForm.tsx — required: originalTaxInvoiceId, reason code, reason text, BU, amount > 0
- BillingNoteForm.tsx — required: customer, BU, ≥1 line item

Common pattern:
- Visible label asterisk on required fields (already mostly there)
- Submit attempt with errors → `toast.error(t('toast.validationFailed'))` + scroll-into-view to first error
- Field highlight via `formState.errors.{fieldName}` red border + inline message (`text-error text-sm`)
- Backend validation 400 (ErrorEnvelopeV1) → `parseApiError` → render `fieldErrors[]` per camelCase field name

Replace BN's current generic "เกิดข้อผิดพลาด" with this pattern (SR9).

**Acceptance:** Empty submit on any of 7 forms → toast + field highlight + scroll to error.

**B5 — SR7 "แก้ไข" link disambiguation** (~1 hr)
List-page row action link text:
- If Q/SO/DO/RC/TI/CN/DN/BN status = `Draft` → "แก้ไข" (and link routes to `/{resource}/[id]/edit` when C1 Q lifecycle ships; otherwise to detail for now)
- If status = non-Draft → "เปิด" or "ดูรายละเอียด"

Q list specifically — coordinate with C1 to route Drafts to `/quotations/[id]/edit`.

For SO/DO/RC/TI/CN/DN/BN — Edit UI not in scope this sprint (deferred to future sprint or never if compliance forbids). So labels should be "เปิด"/"ดูรายละเอียด" on those lists across all rows. Document this intent.

**Acceptance:** All sales lists show contextually correct row action labels. Click → matches expectation.

**B6 — SR8 Print button = PDF endpoint print** (~1.5 hr — systemic)
Each document detail page "พิมพ์" button:
- Replace `window.print()` (current) with:
  ```typescript
  const handlePrint = async () => {
    const res = await fetch(`/api/proxy/{docType}/${id}/pdf`);
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const w = window.open(url, '_blank');
    w?.addEventListener('load', () => w.print());
    // optionally revoke later: setTimeout(() => URL.revokeObjectURL(url), 60_000)
  };
  ```
- Apply to: tax-invoices/[id], receipts/[id], credit-notes/[id], debit-notes/[id], quotations/[id], sales-orders/[id], delivery-orders/[id], billing-notes/[id]

For docs that don't have PDF endpoint yet (Q/SO/DO/RC/BN PDF endpoints — verify), defer Print button visibility OR add a basic PDF generator stub. Coordinate with C1 (Q PDF endpoint).

**Acceptance:** Click "พิมพ์" → new tab opens with the PDF → browser native print dialog fires on the PDF.

**B7 — Replace browser `confirm()` with AlertDialog** (~1 hr — covers C9)
Grep `frontend/` for `confirm(` calls. Each one replaced with `useConfirm` hook + AlertDialog (Sprint 13d-P1):
- Q Draft delete (Sprint 13h ckpt3 expedience)
- BN Draft delete (same)
- BN Cancel from Issued (suspected from OBS-BN-CANCEL — Chrome MCP couldn't see the dialog → probably `confirm()` blocked off-screen)
- Any other destructive action

**Acceptance:** No remaining `confirm(` call in `frontend/` source. All destructive actions use AlertDialog with proper Thai title + warning text.

### Carry-over FE tails (C1-C7) — from Sprint 13h deferred queue

**C1 — P4 FE Quotation lifecycle UI** (~3 hr)
BE endpoints shipped Sprint 13h P4 BE — just FE wiring needed.
- Q detail page action buttons (status-aware):
  - **Draft state**: "แก้ไข" → `/quotations/[id]/edit` route (full QuotationForm prefilled, allow edit, save = PUT `/quotations/{id}`). Plus "ลบใบเสนอราคา" → AlertDialog confirm → DELETE.
  - **Sent state**: "ลูกค้าตอบรับ" + "ลูกค้าปฏิเสธ" + **"ยกเลิกใบเสนอราคา"** + "ดาวน์โหลด PDF"
  - **Accepted state**: existing "แปลงเป็นใบสั่งขาย" + "สร้างใบกำกับภาษีจากใบเสนอราคา" + **"ยกเลิกใบเสนอราคา"** + "ดาวน์โหลด PDF"
  - **Converted / Rejected / Cancelled**: read-only + "ดาวน์โหลด PDF"
- Cancel endpoint: `POST /quotations/{id}/cancel` (verify Sprint 13h P4 BE shipped this; if not, add). Returns 204.
- `/quotations/[id]/edit` route — new page renders QuotationForm with `defaultValues` from GET — saves via PUT `/quotations/{id}`.
- "ดาวน์โหลด PDF" — GET `/api/proxy/quotations/{id}/pdf` → blob download. Q PDF endpoint: if Sprint 13h didn't ship, add basic QuestPDF generator (full revamp in Sprint 13j R1).
- StatusBadge already supports Q states (Sprint 13h P5).

**Acceptance:** Sana can: create Draft → click "แก้ไข" → modify line items → save → reload sees changes; delete Draft Q (gone from list); Cancel Finalized Q (status=Cancelled, no further actions); download PDF from any non-Draft Q.

**C2 — P7 FE readOnly tax_rate + RC WHT auto-base** (~3 hr)
- `components/ui/LineItemsTable.tsx`:
  - When product picked (line has product_id) → `tax_rate` input becomes `disabled + readOnly`, value locked from product's tax_code rate
  - Ad-hoc free-text lines still allow tax_code selection (enum dropdown, not free entry)
- `components/forms/AdjustmentNoteForm.tsx`:
  - `tax_rate` field disabled when referencing a Posted TI — locked to that TI's tax_code rate
- Receipt form (receipts/new):
  - WHT base auto-compute = SUM(SERVICE line amounts ex-VAT) — uses `product_type` snapshot from each line (Sprint 13h P7 BE shipped)
  - Show computed base + optional "แก้ไขด้วยมือ" toggle for edge cases
  - Remove the stale "ระบบยังไม่มี Product master..." hint label (Sprint 8.6 era)

**Acceptance:** TI/RC/CN/DN line items show locked tax field after product pick. RC with one SERVICE + one GOOD line → WHT base auto = SERVICE-only ex-VAT.

**C3 — P5 BU/customer/date filters** (~2 hr)
Extend the Sprint 13h ckpt2 status filter on:
- /quotations, /sales-orders, /delivery-orders, /tax-invoices, /receipts, /credit-notes, /debit-notes, /billing-notes

Each gains 4 filter controls + URL persistence:
- Status: existing `<select>` ✓
- BU: `<select>` of BUs (reuse BusinessUnitSelector)
- Customer: combobox via CustomerSelector (single-select)
- Date range: from/to date inputs

URL: `?status=Posted&bu=ECOM&customerId=5&dateFrom=2026-05-01&dateTo=2026-05-31`

**Acceptance:** Apply filters → URL updates → refresh persists.

**C4 — P3 toast sweep tail + AdjustmentNoteForm date label** (~1.5 hr)
- Grep every `toast.success(` / `toast.error(` / `toast.info(` call site in `frontend/`
- Replace raw EN strings with `t('toast.{key}')` from `messages/{th,en}.json`
- Audit/fix BUG #5 (RC form "Date" label English) — replace with "วันที่"
- Audit list page column headers — confirm Thai everywhere (BUG #11 partial fix verified). Specifically: /receipts list headers "No., Date, Customer, Amount, WHT, Status" → "เลขที่ / วันที่ / ลูกค้า / จำนวนเงิน / ภาษีหัก ณ ที่จ่าย / สถานะ"

**Acceptance:** No EN literals in toast calls (grep clean). All chapter-3 list headers + form labels Thai.

**C5 — P7 NOT NULL hardening on product_type** (~1 hr)
Migration `HardenLineItemProductTypeNotNull` — after Sprint 13h ckpt2 backfill GOOD on 4 line tables (`sales.quotation_lines`, `sales.sales_order_lines`, `sales.delivery_order_lines`, `sales.tax_invoice_lines`, plus BN lines from ckpt3), flip `product_type` column to NOT NULL.

Verify backfill 100% populated on `accounting_dev` first (`SELECT COUNT(*) FROM ... WHERE product_type IS NULL` = 0).

Migration body: `AlterColumn` to NOT NULL on each 5 line tables.

Domain rule: line service validators reject NULL product_type on create.

**Acceptance:** Migration applies clean. New Q/SO/DO/TI/BN create with no product picked → service defaults `product_type = 'GOOD'` or validation rejects (decision: default GOOD = safer + matches backfill).

**C6 — BN settled auto-derive from receipts** (~2 hr)
- BillingNote `Status = Settled` when SUM(linked Receipts' applied_amount on BN-referenced TIs) ≥ BN.total_amount
- Listen on `Receipt.PostAsync` event → re-check linked BN status → flip to Settled if criterion met
- Domain service `BillingNoteSettlementService` — single source for the auto-derive logic
- Keep manual `MarkSettledAsync` endpoint for admin-override only (`require admin` perm)

**Acceptance:** Create BN linking TI#1 (total ฿1,605). Create RC#X applying ฿1,605 to TI#1 → BN auto-flips to Settled.

**C7 — BN ↔ TI dedicated join table + multi-TI picker (covers C7+C8 from previous)** (~3 hr)
- Migration `AddBillingNoteTaxInvoiceJoinTable` — new table `sales.billing_note_tax_invoices(billing_note_id, tax_invoice_id, applied_amount)` replacing `BillingNote.TaxInvoiceIds` array column
- Drop `BillingNote.TaxInvoiceIds` PG `bigint[]` column
- Service query rewrites: `.Contains(id)` → `.Any(j => j.TaxInvoiceId == id)`
- E2E spec: query BN's TIs
- BN form: multi-TI picker UI — uses existing `TaxInvoicePicker` (Sprint 13h P3) in multi-select mode (allow multiple, customer-scoped, Posted-only). Selected TIs display as chips with remove (×) button.

**Acceptance:** Create BN form → multi-TI picker → select 2 TIs from same customer → save → BN detail shows both TI chips → cross-ref panel shows both.

### Cross-ref chain extension (R5)

**R5 — TI detail cross-ref panel adds Q + SO + DO chain chips** (~2 hr)
- `DocumentCrossRefService.GetForTaxInvoiceAsync` extends response DTO:
  - `quotationId` + `quotationDocNo` (if TI links to a Q via `quotation_id` FK from Sprint 13h P6.1)
  - `salesOrderId` + `salesOrderDocNo` (from TI's `sales_order_id` if linked)
  - `deliveryOrderId` + `deliveryOrderDocNo` (from TI's `delivery_order_id` if linked)
- `CrossRefChipRow.tsx` renders Q/SO/DO chips alongside existing RC/CN chips
- Same chain extension on RC/CN/DN detail (each can see their upstream chain too)

**Acceptance:** TI#1 detail "เอกสารอ้างอิง" shows Q#1 + SO#1 + DO#1 chips + RC#1 + CN#1 (5 chips for TI that has full chain).

### Cleanup (L1)

**L1 — Legacy `ti.postConfirm.*` i18n removal** (~15 min)
- Grep `frontend/` for `t('ti.postConfirm.` — if any consumer still uses, replace with `t('postConfirm.title.{docType}')`
- Remove legacy block from `messages/{th,en}.json` once grep clean

**Acceptance:** Grep clean. No regressions.

---

## DoD per phase

Each phase: FE `tsc --noEmit` → 0; BE build 0/0; BE Domain tests pass; migrations via `subst U:` short path (gotcha §29/§36). New seeds idempotent (gotcha §28). Tenant isolation §26 belt-and-braces on any new service. Live UI verify deferred to Sana RE-VALIDATE channel.

**Sprint-level DoD (Report-Backend32):**
- 17 phases (B1-B7, C1-C7, R5, L1) all shipped or honestly flagged
- BE build 0/0, Domain ≥ 89/89, no regression
- FE tsc 0
- Migration `HardenLineItemProductTypeNotNull` + `AddBillingNoteTaxInvoiceJoinTable` applied clean on accounting_dev
- Seed `330_seed_receipt_adjnote_rbac.sql` applied + grants verified via psql
- Mirror Y:\AccountApp
- Sana RE-VALIDATE deep mode (all 13 categories truly covered, not just spot-check) → green before Sprint 13j start

---

## Out of scope — explicitly deferred

| Item | Defer to |
|---|---|
| TI PDF 3-section layout + ต้นฉบับ+สำเนา | Sprint 13j R1 |
| Font global Noto Sans Thai + TH Sarabun New (FE + PDF) | Sprint 13j R4 |
| Doc-header `<CompanyLogoBanner />` rendering | Sprint 13j R2 |
| Logo PDF embed (QuestPDF Image()) | Sprint 13j R3 |
| XML encoding=utf-16 → UTF-8 review | Sprint 13j N1 |
| Reports module print revamp (TB/P&L/Sales Summary/ภ.พ.30) | Sprint 13j |
| Security audit (secrets, CORS, CSRF, XSS surface) | Sprint 13k |
| RBAC full Cartesian (12 roles × 8+ resources × 4 actions) | Sprint 13k |
| Performance (>1000 records, N+1, pagination edge) | Sprint 13k |
| Accessibility audit (keyboard nav, screen reader, focus, contrast) | Sprint 13k |
| Migration rollback testing | Sprint 13L |
| Build pipeline clean-state check | Sprint 13L |
| Test skip/quarantine/expected-fail audit | Sprint 13L |
| e-Tax XAdES-BES signing pipeline production rollout | Phase 2 (CA cert + ETDA UAT lead 4-6 wk) |

---

## → Sana (binding ownership rule — apply after Report-Backend32)

- `plan.md` — append Sprint 13i ☑ entry; tick 17 phases. Add Sprint 13j/k/L placeholders.
- `docs/api/openapi.yaml` —
  - `GET /quotations/{id}/pdf` (C1)
  - `PUT /quotations/{id}` (C1 verify endpoint exists)
  - `DELETE /quotations/{id}` (C1)
  - `POST /quotations/{id}/cancel` (C1)
  - Cross-ref response schema extends with Q+SO+DO chain (R5)
  - Receipt + AdjustmentNote endpoint auth code split (B1)
- `docs/accounting-system-plan.md` —
  - §6 sub-modules: BN settled auto-derive (C6) + join table (C7) + multi-TI picker
  - §6.4: Q state machine — explicit Cancel transition from Sent/Accepted with PDF preserved (C1)
- `docs/runtime-gotchas.md` —
  - **§38 NEW (Sana 2026-05-21 own-flag):** Spec-authoring RBAC matrix must enumerate ALL sales surfaces (Q/SO/DO/TI/RC/CN/DN/BN + master) explicitly. Partial enumeration is the trap. SR-OWN-1 lesson.
- `docs/manual/chapters/03-การขาย.md` + `frontend/manual/walkthroughs/03.01-03.07.ts` — **author ONLY after Sprint 13i + 13j + 13k + 13L all ship + Sana RE-VALIDATE deep mode green on each** (strict CLAUDE.md §16).

---

## Ask back (only if blocked)

If a phase surfaces a design decision not in this spec (e.g. C7 join table column shape — should `applied_amount` be on the join table or computed?), file `Question-Backend16.md` and pause. Don't improvise on compliance-adjacent design.

Otherwise: proceed B1 → B2 → ... → L1. Report-Backend32 = sprint completion. **Sana RE-VALIDATE deep mode** = all 13 categories truly covered before Sprint 13j starts.
