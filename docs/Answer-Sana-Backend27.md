# Answer-Sana-Backend27 — Sprint 13h: Chapter 3 acceptance fix (large)

**Owner:** Claude Code
**Spec author:** Sana (after Ham + Sana joint validate of Sprint 13e, 2026-05-20)
**Sequencing:** AFTER Sprint 13e ship (P2-P5 + P3) — this sprint addresses 25 issues that Sprint 13e did NOT cover. Sprint 13i (Print/PDF + Font + Logo embed) chains AFTER 13h ship + Sana re-validate.
**ROI:** 3-5 days. Unblocks chapter 3 manual production + Phase 1 launch acceptance.
**Workflow gate:** CLAUDE.md §16 — chapter-3 stays in FIX phase until 13h+13i ship + Sana RE-VALIDATE deep mode green.

---

## Background — what went wrong with 13e acceptance

Sprint 13e shipped Q/SO/DO forms + ProductPicker + TaxInvoicePicker + StatusBadge wiring. FE tsc 0, BE build 0/0, Domain 89/89. Passed those gates.

But on first joint validate (Sana Chrome MCP + Ham personal check):
- Sana drove only the happy path as `demo-admin` (super-admin) → missed RBAC seed gap that fails all non-admin chapter-3 access.
- Sana didn't open PDF/XML downloads → missed 0-byte XML + HTML-rasterized "print" instead of PDF.
- Sana didn't try the "แก้ไข" button → it's view-only; Q lifecycle (Edit Draft / Delete Draft / Cancel Finalized / Download PDF) all missing.
- Sana didn't audit every form field → didn't notice TI/RC allow user-entered VAT rate (should be enum-locked from product tax_code).
- Sana didn't try non-product flows → missed that Receipt/TI flow doesn't carry Product master SERVICE/GOOD type through, so WHT base auto-calc is impossible.
- Sana didn't audit sibling docs → Billing Note (ใบแจ้งหนี้) entirely missing from FE despite Plan §6 listing it as a sub-module.
- Sana didn't test SO/DO list filtering, didn't notice `<select>` widget clipped half across the app, didn't notice Logo upload absent from doc headers.

**Honest assessment: Sprint 13e shipped a working FE shell. Sprint 13h is the substantive Phase-1 completion.**

Process rule for Sana for Sprint 13h re-validate (after 13h ship): **deep mode** — every button (incl. "แก้ไข" / lifecycle / cancel) clicked; every PDF and XML opened and inspected; every form field audited (enum vs free entry); every list filter exercised; every state transition tested in both directions; non-admin role at least once (demo-accountant) before declaring acceptance.

---

## Scope — 13 phases

Sprint 13h delivers a single integrated patch across these phases. Recommended sequencing inside the sprint: P1 (RBAC seed gap) FIRST so chapter 3 unblocks for the role matrix Sana will eventually use; then everything else in roughly the order below. Phases that touch shared components (ProductPicker, StatusBadge, etc.) consolidate early so later phases consume the fixed version.

---

### P1 — RBAC seed gap + group-auth refactor (compliance/blocker, P0)

**Bug:** `demo-accountant` (ACCOUNTANT + AR_CLERK + AP_CLERK) gets 403 on `GET /api/proxy/customers` and `GET /api/proxy/tax-invoices`. Same as KI-01 (Purchase RBAC gap, §23.1 of plan.md) but for sales-side.

**Root cause (codebase survey, Sana 2026-05-20):**
1. `backend/src/Accounting.Api/Endpoints/CustomerEndpoints.cs:14` — entire `/customers` group requires `master.customer.manage`. GET should require lighter `master.customer.read`.
2. No `master.customer.read` permission row in seed `110_seed_roles_and_permissions.sql`.
3. ACCOUNTANT/AR_CLERK/CHIEF_ACCOUNTANT never granted `sales.tax_invoice.read` (only SUPER_ADMIN gets it via the all-permissions cross-join).

**Fix:**
1. Refactor `CustomerEndpoints.cs`: GROUP level removes `RequireAuthorization(...customer.manage)`. Per-endpoint:
   - GET `/customers` + GET `/customers/{id}` → `master.customer.read`
   - POST/PUT → `master.customer.manage`
2. Add `master.customer.read` permission to `Accounting.Api.Authorization.Permissions` static class.
3. New seed `320_seed_chapter3_rbac.sql` (additive + idempotent — mirrors `180_seed_pv_purchase_perms.sql`):
   - INSERT `master.customer.read` permission row.
   - Grant `master.customer.read` to: COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, SALES_STAFF, AP_CLERK, AUDITOR.
   - Grant `master.customer.manage` to: COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT.
   - Grant `sales.tax_invoice.read` to: COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, SALES_STAFF, AUDITOR.
   - Grant `sales.quotation.manage`, `sales.sales_order.manage`, `sales.delivery_order.manage` to: ACCOUNTANT (Sprint 10 seed 270 covered AR_CLERK/SALES_STAFF/admins but NOT ACCOUNTANT — gap).
4. Audit other sales-side endpoints for the same group-level over-restriction pattern:
   - SalesOrderEndpoints, DeliveryOrderEndpoints, QuotationEndpoints, ReceiptEndpoints, AdjustmentNoteEndpoints — confirm GET requires read perm, POST/PUT/transitions require manage. Fix any with the same anti-pattern.

**Acceptance:** demo-accountant logs in → all of `/customers`, `/tax-invoices`, `/quotations`, `/sales-orders`, `/delivery-orders`, `/receipts`, `/credit-notes`, `/debit-notes` lists + details return 200. demo-accountant can create Q→SO→DO→TI→RC→CN/DN end-to-end. demo-admin still has full manage.

---

### P2 — Picker / dropdown UX hardening (P1.5, foundation)

**Bugs:** ProductPicker dropdown clipped by `LineItemsTable` parent overflow (BUG #3). TaxInvoicePicker dropdown rendered but invisible inside `/receipts/new` table cell (BUG #6 — `getBoundingClientRect` returned `{}`). Selected TI in RC shows `#1` (db id) instead of docNo (BUG #7). `<select>` widget across the whole site renders only top half (Ham's screenshot).

**Fix:**
1. Refactor `ProductPicker` + `TaxInvoicePicker` + (any other async combobox) to render the listbox via React **portal** (mount on `document.body` with absolute positioning anchored to input via `getBoundingClientRect`). This eliminates parent-overflow clipping permanently.
2. Use the existing shadcn/ui Popover primitive or DaisyUI dropdown via Radix Popper — pick whichever the project already standardizes on (shadcn/ui is listed in CLAUDE.md §2 tech stack).
3. After picker select, the input's displayed value is the **document number** (or product code+name), never the db id. Fix the RC TaxInvoicePicker render path that shows `#{taxInvoiceId}`.
4. `<select>` half-render: inspect `frontend/styles/globals.css` (or wherever DaisyUI overrides live) — `select` overflow + line-height likely conflicts with a global `* { max-height: ... }` rule or Tailwind `leading` reset. Fix with explicit `min-h-fit` or DaisyUI `select-bordered` proper class. Verify across BU dropdown, hot-fix any element using bare `<select>` without DaisyUI class.

**Acceptance:** Open ProductPicker in any sales form line — dropdown fully visible, no clip. Open TaxInvoicePicker in `/receipts/new` AND `/credit-notes/new` AND `/debit-notes/new` — fully visible. Pick a TI in RC → input shows `05-2026-TI-ECOM-0001` not `#1`. Open any `<select>` in the app — options fully visible.

---

### P3 — i18n sweep + date format consistency (P1.5)

**Bugs:**
- Toast EN ("Posted", "Draft saved") vs TH ("บันทึก") inconsistent across forms (BUG #9).
- List column headers EN ("No., Date, Customer, Amount, WHT, Status") (BUG #11).
- RC form "Date" label EN (BUG #5).
- Date inputs render US `mm/dd/yyyy` CE; lists render Thai `dd/mm/พ.ศ.` BE (BUG #4).

**Fix:**
1. Audit `frontend/messages/th.json` + `en.json` for ALL keys referenced under list pages + form labels + toasts + dialogs. Add missing TH translations.
2. Replace every literal `<th>No.</th>` / `<th>Date</th>` etc. with `t('list.header.docNo')` etc., keyed in messages.
3. Toast call sites — ensure they call `t('toast.posted')` not raw English. Sweep `useTranslations('toast')` or similar pattern.
4. Date display convention — **single rule everywhere**:
   - Internal storage: CE ISO `YYYY-MM-DD` (no change — CLAUDE.md §10).
   - User-facing display + input: Thai locale via `Intl.DateTimeFormat('th-TH', { dateStyle: 'medium' })` → renders `20 พ.ค. 2569` or similar. Buddhist Era. Same in inputs (`<input type="date">` cannot localize natively — use a Thai date picker component or accept `dd/mm/yyyy` user-typed and convert).
   - Settle the picker question now and document in `frontend/lib/format/date.ts` as the single source.

**Acceptance:** Every list header in chapter 3 is Thai. Every toast in chapter 3 is Thai. Every date displayed to user is Thai locale Buddhist Era. Form date inputs accept Thai `dd/mm/พ.ศ.` (or a clearly-labeled CE alternative — document the choice).

---

### P4 — Quotation lifecycle (P1, large)

**Bugs:** "แก้ไข" button is View-only. No Edit mode. No Delete (Draft). No Cancel (Finalized). No PDF download.

**Fix:**
1. **Route split** `/quotations/[id]`:
   - Default = View mode (read-only display, current behavior).
   - Add `?mode=edit` (or `/quotations/[id]/edit` sub-route) that renders the same `QuotationForm` pre-populated. **Only allowed when `status = Draft`**. BE PUT endpoint required.
2. **Edit endpoint** `PUT /quotations/{id}` (BE). Idempotent. Update fields only when `Status = Draft`; reject 409 otherwise with `urn:teas:error:quotation.cannot_edit_after_send`.
3. **Delete Draft** — DELETE `/quotations/{id}` (BE). Only `Status = Draft`. Hard delete acceptable (Draft has no document number allocated yet — no audit concern). FE: trash icon on Draft rows in list, with AlertDialog "ยืนยันการลบใบเสนอราคาร่าง" confirm.
4. **Cancel Finalized** (soft) — `POST /quotations/{id}/cancel`. Allowed when `Status ∈ {Sent, Accepted}`. Transitions to `Cancelled` (terminal). FE: "ยกเลิกใบเสนอราคา" button with AlertDialog confirm. **Soft only — row stays, doc number stays (gap rule honored — voided number not reused per Plan §17.6).**
5. **PDF download** — `GET /quotations/{id}/pdf` BE endpoint (QuestPDF, mirrors TI PDF skeleton). Only allowed when `Status ∈ {Sent, Accepted, Converted, Rejected, Cancelled}` (any finalized state — Draft does NOT generate PDF, since Q is not legally "issued" yet). FE: button "ดาวน์โหลด PDF" on detail page header. Layout: simplify Sprint 13j will revamp — for Sprint 13h, produce a clean working PDF using the existing TI PDF code as a template (then Sprint 13i will redesign both).
6. **Status-to-action map updated** in `docs/accounting-system-plan.md §6.4` to match:
   - Draft → Edit, Delete, Issue
   - Sent → Edit-disabled, Accept, Reject, Cancel, Resend PDF, Download PDF
   - Accepted → Convert to SO, Cancel, Download PDF
   - Converted → Read-only, Download PDF
   - Rejected / Cancelled → Read-only, Download PDF
7. **Tests** (BE Domain + Api): every state-machine transition, every reject-on-wrong-state path, soft-cancel preserving doc_no.

**Acceptance:** Sana can create Draft Q, click "แก้ไข", change line items, save, re-open and see change. Sana can delete a Draft Q (gone from list). Sana can cancel a Finalized Q (still in list, status=Cancelled, no further actions). Sana can download PDF for every non-Draft Q.

---

### P5 — SO + DO list filtering (P1)

**Bug:** SO + DO list pages have no filter controls. Receipt list already has BU filter — extend the same pattern.

**Fix:**
1. `app/(dashboard)/sales-orders/page.tsx` — add filter row: BU dropdown, status enum dropdown, customer combobox, date range. Backend `GET /sales-orders` already accepts these (verify); FE just wire it via URLSearchParams.
2. `app/(dashboard)/delivery-orders/page.tsx` — same pattern, plus filter by linked SO doc_no.
3. Persist filter state in URL (`?bu=ECOM&status=Posted&customerId=5&dateFrom=2026-05-01&dateTo=2026-05-31`) — refresh-safe + shareable.

**Acceptance:** Sana applies "Status = Posted" filter on SO list → only Posted rows. URL contains the filter. Refresh → filter persists.

---

### P6 — TI direct from Q (2 paths) + Billing Note (ใบแจ้งหนี้) CRUD (P1, large)

#### P6.1 — TI direct creation paths

**Current ship:** TI created only via SO → DO auto-chain (the "convert" path).

**Fix:** Two explicit entry paths for TI:
1. **Path A — no Q reference**: `/tax-invoices/new` standalone form (already exists since pre-Sprint 13e). Keep as-is; Sana confirms it works in chapter 3 validate.
2. **Path B — with Q reference**: From Q detail page in `Accepted` state, button "สร้างใบกำกับภาษีจากใบเสนอราคา" → opens `/tax-invoices/new?fromQuotationId={id}`. TI form pre-fills customer + line items + BU + notes from Q. Stores `quotation_id` reference on the new TI.
3. **Path C — auto from SO/DO chain**: existing — unchanged, but now TI carries `sales_order_id` AND optionally `quotation_id` (cascade from SO if SO has it).
4. **DB**: `sales.tax_invoices` add nullable `quotation_id BIGINT REFERENCES sales.quotations(quotation_id)`. EF migration `AddTaxInvoiceQuotationReference`. Index for the FK.
5. **UI cross-reference**: TI detail page shows linked Q + SO + DO + Billing Note + RC + CN/DN as chips with clickable navigation (the cross-ref panel — same as DO already shows "ใบกำกับภาษีที่เชื่อม").

#### P6.2 — Billing Note (ใบแจ้งหนี้) — missing entirely from FE

Per Plan §6 sub-modules: "Billing Note — ใบวางบิล (สำหรับธุรกิจที่ตั้งหนี้ก่อนรับเงิน — common ในไทย)". Currently no FE route at all. Need full CRUD.

**Backend** (assess if entity exists; likely not):
- New entity `sales.billing_notes` (table) — header + lines.
- Document number `MM-YYYY-BL-{BU}-NNNN` (prefix `BL`).
- Status enum: Draft / Issued / Settled / Cancelled.
- Cross-references: nullable `tax_invoice_ids[]` (one BN can group multiple TIs for one customer), nullable `quotation_id`.
- Endpoints: GET list, GET detail, POST create, PUT edit (Draft only), POST `/billing-notes/{id}/issue` (Draft→Issued, allocate doc_no), POST `/billing-notes/{id}/cancel` (soft), GET PDF.
- Permission keys: `sales.billing_note.read`, `sales.billing_note.manage`. Add to seed `320` from P1.
- Migration `AddBillingNotes` (table + FKs + RLS).

**Frontend**:
- Sidebar: add "ใบแจ้งหนี้" under sales section.
- `app/(dashboard)/billing-notes/page.tsx` — list with filter (status / customer / date range).
- `app/(dashboard)/billing-notes/new/page.tsx` — form: customer picker, BU, date, line items (or pick TIs to roll up), notes.
- `app/(dashboard)/billing-notes/[id]/page.tsx` — detail with Issue / Cancel / Download PDF.
- StatusBadge MAP extend: `Issued` already there from P5 of Sprint 13e; add `Settled` (badge-success, `CheckCheck` icon).
- i18n keys: `billingNote.*` namespace, status keys.

**E2E:** Authored spec `billing-note-flow.spec.ts`.

**Acceptance:** Sana creates BN linking 2 TIs for the same customer → Issue → download PDF → BN appears on TI detail cross-ref panel → cancel BN → status = Cancelled.

---

### P7 — Product master SERVICE/GOOD type wiring + kill VAT/tax override on TI/RC/CN/DN (P1, structural)

**Bugs:**
- TI form lets user type any `tax_rate` value (BUG #11).
- RC form has free-entry tax field (BUG #12).
- WHT base hint on RC says "ระบบยังไม่มี Product master แยกสินค้า/บริการ — กรุณาปรับให้เหลือเฉพาะส่วนบริการเอง" (manual trim).

**Background:** Sprint 8.6 R-B1a degraded WHT base to manual because Product master didn't carry SERVICE/GOOD discriminator. Sprint 10 added Product master — but ProductType enum (EXEMPT_GOOD / GOOD / SERVICE) is already there (Sana confirmed via `/api/proxy/products` GET — every product has a `productType` field). Just isn't wired through the document flow.

**Fix:**
1. **Line item snapshot**: every sales document line (Q/SO/DO/TI/RC line, CN/DN line) carries `product_type` (enum) snapshotted at the moment the product is picked. Add column to each line table. Migration `AddLineItemProductTypeSnapshot`.
2. **Tax code enum lock**: ProductPicker on select fills `tax_code_id` AND `tax_rate` (computed from tax_code) — both **disabled/readOnly** in the line row. Only ad-hoc free-text lines (no product picked) allow tax_code dropdown (still enum, not free entry).
3. **TI/RC/CN/DN forms**: remove the `อัตราภาษี` editable input. Replace with: read-only display when product is picked; tax_code_id dropdown (enum, only Active codes) when ad-hoc.
4. **Receipt WHT auto-base**: with `product_type` per line, automatically compute WHT base = SUM(SERVICE line amounts ex-VAT). Remove the manual hint. Show calculated base + let user override only with explicit "แก้ไขด้วยมือ" toggle (rare case).
5. **Q→TI conversion**: line items carry `product_type` snapshot from Q forward. No re-entry needed.
6. **DB**: `master.products.product_type` already enum — confirm `Quotation`/`SalesOrder`/`DeliveryOrder`/`TaxInvoice`/`Receipt`/`AdjustmentNote` line tables get the column. Snapshot on insert (NOT a live FK — product master can later be edited; document line is immutable per legal compliance).
7. **Backwards compat**: existing line rows (Q#1, TI#1 from Sana validate) — `product_type` defaults to `GOOD` for backfill (or NULL with NOT NULL deferred via `Sql()` block pattern from Answer-26 — pre-flight count check, set default per row by querying product master). Document in migration.

**Acceptance:** TI/RC/CN/DN line items show locked tax field after product pick. RC with one SERVICE + one GOOD line → WHT base auto = SERVICE-only ex-VAT. No more manual trim hint. Q→TI conversion preserves product_type per line.

---

### P8 — Receipt cleanup + cross-reference (P1)

**Bugs (joint):**
- RC PostConfirmDialog title "ยืนยันการบันทึกใบกำกับภาษี" — wrong (BUG #8).
- RC Post doesn't navigate (BUG #10).
- RC tax field manual entry (covered by P7).
- Receipt relationship to TI unclear in UI.

**Fix:**
1. PostConfirmDialog: dispatch correct title per doc type — make the title a prop instead of hardcoded TI label. Possible solution: rename component to `DocumentPostConfirmDialog` with `docType: 'tax_invoice' | 'receipt' | 'credit_note' | 'debit_note' | 'quotation' | 'billing_note'` and resolve title via i18n key per type.
2. Receipt Post → navigate to `/receipts/{id}` detail.
3. Receipt detail page cross-reference panel: linked TI(s) shown as chips with doc_no + clickable to TI detail.
4. TI detail cross-reference panel: linked RC(s) shown with applied amount per RC.
5. Document cross-reference helper service (BE): `IDocumentCrossRefService.GetReferencesForTaxInvoice(id)` → returns linked Q, SO, DO, BN, RC[], CN[], DN[]. FE `useCrossReferences(docType, docId)` hook to consume.

**Acceptance:** RC Post → detail page → see linked TI chip. TI detail → see linked RC chip. CN against TI → TI detail shows CN. Etc.

---

### P9 — DO Delivered stage extension (P1, Plan §6.4 alignment)

**Decision (Ham, 2026-05-20):** DO needs explicit Delivered stage per Plan §6.4. Current ship has DO Draft → Posted (= ส่งของแล้ว). Need: Draft → Issued (printed/sent to recipient) → Delivered (recipient confirmed receipt) → linked TI fires on Delivered, not Issued.

**Fix:**
1. **DB**: extend `DeliveryOrderStatus` enum from `{Draft, Posted, Cancelled}` → `{Draft, Issued, Delivered, Cancelled}`. Migration `AddDeliveryOrderDeliveredStage`. Backfill: existing `Posted` rows (Sana's #1 + any others) → migrate to `Delivered` (since linked TI already exists). `Sql()` block per Answer-26 pattern: pre-flight count, default rule (`Posted` → `Delivered`), then AlterColumn.
2. **DO Status enum file** (`Accounting.Domain.Sales.DeliveryOrderStatus`) updated. Status-to-action map:
   - Draft → Edit, Issue, Delete
   - Issued → Mark Delivered, Cancel, Print, Download PDF
   - Delivered → Read-only, Download PDF (linked TI created automatically)
   - Cancelled → Read-only
3. **Endpoints**: split current `POST /delivery-orders/{id}/post` into two:
   - `POST /delivery-orders/{id}/issue` (Draft → Issued, allocate doc_no, do NOT create TI yet)
   - `POST /delivery-orders/{id}/mark-delivered` (Issued → Delivered, **NOW create linked TI**)
4. **FE**: DO detail action buttons reflect status. "ยืนยัน (Post)" button removed; replaced with "ออกใบส่งของ" (Issue) on Draft and "ยืนยันส่งมอบ" (Mark Delivered) on Issued.
5. **StatusBadge**: `Delivered` already proposed for the MAP in Answer-26 P5 (kept). `Issued` already shared with Quotation status.
6. **Plan §6.4 alignment**: explicit table for DO state-machine matches the new enum.

**Acceptance:** Sana creates DO from SO → Draft → Issue → status Issued, doc_no allocated, no TI yet → Mark Delivered → status Delivered + linked TI fires + visible in cross-ref panel.

---

### P10 — Company Logo upload + display (P1)

**Bug:** Logo absent from every document header. Ham requires every doc to carry company logo.

**Fix:**
1. Reuse existing CompanyProfile soft fields (Sprint 13d-P6): `logo_url` field already in `master.company_profile` (per accounting-system-plan.md §6.7). Add `logo_blob` storage OR keep URL field but supplement with an upload route.
2. Upload endpoint: `POST /api/v1/company-profile/logo` (multipart/form-data, image/png|jpg|svg, max 1 MB). Stores in `attachments` table (existing from Sprint 11) under parent_type=`COMPANY_PROFILE`, returns URL.
3. CompanyProfile UI (`/settings/company`): "อัปโหลดโลโก้" button next to logo URL field. Preview after upload. Replace = delete old attachment + new upload.
4. **Document headers consume logo**: every doc detail page (Q/SO/DO/TI/RC/CN/DN/BN) header renders the logo from `CompanyProfile.logo_url`. If absent, fall back to text-only header (no broken image icon).
5. **PDF embed** (defers final styling to Sprint 13i — for 13h just render the existing logo URL into all current PDF generators via QuestPDF `Image()` call).

**Acceptance:** Sana uploads logo PNG → preview shows → opens any document detail → logo appears in header → downloads PDF → logo embedded.

---

### P11 — XML download 0-byte fix (P0)

**Bug:** TI detail `ดาวน์โหลด XML` returns 0-byte file (Ham observed).

**Hypothesis (Sana, root cause TBD by Claude Code):** Either (a) the XAdES-BES signing pipeline isn't writing the signed XML back into `etax.submissions` correctly, or (b) the download endpoint reads from wrong source, or (c) the TI #1 (auto-created from DO) never triggered the signing pipeline at all because Tier 1 mock mode wasn't enabled at that runtime.

**Fix:**
1. Verify Tier 1 config: `ETax:Enabled=true`, `ETax:AutoSendOnTaxInvoicePost=true`, `ETax:Signing:PfxPath=secrets/dev-cert.pfx`, `ETax:Signing:PfxPassword=dev123` (per CLAUDE.md §14).
2. Verify the DO→TI auto-create path triggers the e-Tax pipeline (might bypass it — common gotcha when "Post" is invoked from a sibling document not directly).
3. Verify the download endpoint reads from `etax.submissions.signed_xml_blob` (or wherever the canonical signed payload lives) AND falls back gracefully if absent (currently returns 0 bytes silently — should return 404 with `urn:teas:error:etax.not_yet_signed` if the TI hasn't been signed).
4. Re-test: post a new TI manually → e-Tax pipeline fires → MailHog shows the email → download XML → file is > 0 bytes, XAdES-BES structure valid (validate with `xmllint --schema` against ETDA XSDs if available, otherwise structural eyeball).
5. Note `/etax/submissions?taxInvoiceId={id}` endpoint returns 400 (Sana noted). Either fix query signature OR document the correct query params in OpenAPI.

**Acceptance:** Sana opens any Posted TI → downloads XML → file size > 0, opens in text editor, sees signed XAdES structure with `<ds:Signature>` block.

---

### P12 — `<select>` widget systemic CSS fix (P1.5)

**Bug:** Half-height clipped `<select>` rendering across the entire app (Ham's screenshot of BU dropdown).

**Fix:**
1. Sweep `frontend/styles/globals.css` + every `*.tsx` for raw `<select>` elements vs DaisyUI-classed ones.
2. Apply `class="select select-bordered w-full"` (or appropriate DaisyUI class) wherever `<select>` is used; verify Tailwind/DaisyUI base styles aren't overridden by a global rule.
3. Most-likely culprit: a global `line-height` or `max-height` reset; or a flex container that compresses the select. Inspect parent CSS.
4. Smoke test: open every page in the app, click every dropdown, verify full height visible.

**Acceptance:** Every `<select>` in the app renders with all options fully visible.

---

### P13 — Product list display as table (P1.5) + sundry

- `/settings/products` (or wherever Product master lives) renders as proper data table (not card grid). Reuse `DataTable` component (`design/component-patterns.md §10`).
- Sundry: Q draft saved toast in Thai ("บันทึกร่างแล้ว"), Post toast "บันทึก (Post) สำเร็จ".

---

## E2E (chapter 3 — author after FE/BE lands)

Specs to author (or update from Sprint 13e versions):
- `quotation-lifecycle.spec.ts` — Draft → edit → save → Issue → Accept → Convert; Draft → delete; Sent → Cancel; download PDF
- `sales-order-flow.spec.ts` — list + filter + create from Q + Post
- `delivery-order-flow.spec.ts` — list + filter + 3-state machine (Draft → Issued → Delivered)
- `tax-invoice-from-quotation.spec.ts` — Q Accepted → create TI direct with `quotation_id` reference
- `billing-note-flow.spec.ts` — new entirely; create BN linking 2 TIs → issue → cancel
- `receipt-cross-ref.spec.ts` — RC post → see in TI detail
- `rbac-chapter3.spec.ts` — demo-accountant traverses entire chapter 3 (existing should now pass; will fail before P1 fix)
- `product-type-wht.spec.ts` — RC with SERVICE + GOOD lines → WHT base auto = SERVICE-only

All specs use `TestIds.*` random suffix per CLAUDE.md §15.

**Run = Sana chapter-3 RE-VALIDATE in deep mode + CI.** Specs authored even if not runnable in-session.

---

## DoD per phase

Each phase has FE `tsc --noEmit` → 0; BE build 0/0; BE Domain tests pass; ON the same `accounting_dev` DB used in dev. The integration suite SHOULD run for P1, P4 (lifecycle endpoints), P6.1 (Q→TI link), P6.2 (BN entity), P7 (product_type snapshot tests), P9 (DO Delivered transitions), P11 (XML signing path). Skipping a test = explicit deferral note in Report-Backend30, never silent.

**Sprint-level DoD (Report-Backend30):**
- 13 phases above all shipped
- BE `dotnet build Accounting.sln` 0/0 + `dotnet test Accounting.Domain.Tests` ≥ 89/89 (no regression; new tests added per phase)
- `Accounting.Api.Tests` (Testcontainers) — run if Docker available; otherwise defer with command-list per Answer-26 §"Deferred verification"
- FE `pnpm tsc --noEmit` 0; `pnpm next build` 0
- Playwright specs authored + tsc-clean; live run = Sana RE-VALIDATE channel
- Migration `AddDeliveryOrderDeliveredStage`, `AddTaxInvoiceQuotationReference`, `AddBillingNotes`, `AddLineItemProductTypeSnapshot` — generated via `subst U:` short-path workflow, applied to clean accounting_dev, no drift
- Seed `320_seed_chapter3_rbac.sql` idempotent + verified (run twice → no error, role grants present)
- Mirror Y:\AccountApp

---

## Out of scope for Sprint 13h (deferred to Sprint 13i)

Sprint 13i (chained after 13h ship + Sana RE-VALIDATE deep mode green):
- **TI PDF revamp**: layout 3 sections (Header / Items / Summary+Remark always bottom), fixed section sizes, long-name wrap → font-shrink fallback, ต้นฉบับ + สำเนา print sets
- **Font global**: Noto Sans Thai + TH Sarabun New across the entire FE app (replace whatever current font stack is)
- **PDF font embed**: same fonts in every PDF document
- **Logo embed in PDF**: refine Sprint 13h's basic Image() placement into proper sized + positioned design

These could go into 13h but: (a) print/PDF is a deep design task that benefits from settling all data-model issues first (which is Sprint 13h scope), (b) splitting it lets Sana RE-VALIDATE 13h before piling 13i on top.

---

## → Sana (binding ownership rule — apply after Report-Backend30)

- `plan.md` — append Sprint 13h ☑ entry under Sprint 14.5 (mirror cont.50 Sprint 13e pattern). Tick the 13 phases as they land. Add Sprint 13i to forward queue.
- `docs/accounting-system-plan.md` —
  - §6 sub-modules: add Billing Note details (status enum, cross-ref).
  - §6.4: update DO state machine to 4-state (Draft / Issued / Delivered / Cancelled). Update Q state map to match P4 (Edit/Delete/Cancel/PDF actions per state).
  - §6.4: explicit "1 Q → 1 SO Phase 1, partial = Phase 2 backlog" text (carry over from Sana old session's commit).
- `docs/api/openapi.yaml` —
  - `POST /quotations/{id}/cancel`, `DELETE /quotations/{id}`, `PUT /quotations/{id}`, `GET /quotations/{id}/pdf`
  - `POST /delivery-orders/{id}/issue`, `POST /delivery-orders/{id}/mark-delivered` (rename + split from current `/post`)
  - `POST /tax-invoices/new?fromQuotationId={id}` — or add `quotation_id` param to existing TI create body
  - Full new section `/billing-notes` (list/get/post/put/issue/cancel/pdf)
  - `POST /api/v1/company-profile/logo` (multipart upload)
- `docs/runtime-gotchas.md` —
  - §30 NEW: "Picker dropdowns must render via portal — parent overflow will clip them otherwise" (with the Sprint 13h fix pattern as reference)
  - §31 NEW: "Endpoint group-level `RequireAuthorization(...manage)` is wrong by default; split GET (read) vs write (manage)" — the KI-01 / Sprint 13h pattern
- `docs/manual/chapters/03-การขาย.md` + `frontend/manual/walkthroughs/03.*` — author **after** Sprint 13h+13i both ship + Sana RE-VALIDATE deep mode green. Strict per CLAUDE.md §16.

---

## Ask back (only if blocked)

If a phase surfaces a design decision not in this spec (e.g. Billing Note can have line items independent of TIs, vs only TI rollup — current spec says rollup; if implementation hits an ambiguity, file `Question-Backend15.md`).

Otherwise: proceed phase-by-phase. Report-Backend30 = comprehensive sprint completion report including the **honest DoD per phase**. Sana RE-VALIDATE deep mode after merge — every button, every PDF, every XML, every field, every role.
