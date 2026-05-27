# Requirements — Purchase / AP Phase 1 (UX Parity Sprint)

> Drafted: 2026-05-23 by Sana (planning) · Ham approved scope
> Status: **REQUIREMENTS LOCKED** — pending Claude Code spec (Answer-Sana-Backend{N})
> Gold Standard source: `accounting-system-plan.md` §7-8, `Design(UI).md` §8, `openapi.yaml`, `CLAUDE.md` §12.1

---

## 0. Locked decisions (Ham, 2026-05-23)

1. **Scope = Option A — Polish UX parity + AP Aging.** Backend stable; gap is FE consistency with Sales chain shipped Sprint 13j-FE.
2. **NO Goods Receipt (any form).** Reason quoted: *"เราไม่ทำอะไรกับสินค้าเลย มันคนละระบบกัน ไม่ทำ Inventory คือรับเข้า มีเอกสารแล้วจบกันเลย"* — Vendor Invoice (`vendor_invoices`) **is** the receipt-confirmation document. No separate `goods_receipts` table, no FE for it, no migration reservation. Reinforces `accounting-system-plan.md` §8 (Inventory Out of Scope).
3. **AP Aging report included in this sprint.**
4. (Inherited from Sprint 13j-FE clarification, 2026-05-22) Purchase **menu items stay as-is**, Settings page stays at existing route.

---

## 1. Purpose

Bring Purchase / AP module up to **same visual + UX standard as Sales** without expanding feature scope. Backend is ~85% shipped, FE pages exist but pre-date the PaperDocument / DocumentChain / PrintMenu refactor that landed for Sales in Sprint 13j-FE. Users currently see two visually different experiences for symmetric workflows — confusing.

This is **Chapter 4** in the CLAUDE.md §16 chapter-by-chapter validation workflow (Chapter 3 = Sales, validated). Manual chapter cannot be authored until this UX work + the bug pass complete.

---

## 2. Current state (verified 2026-05-23)

| Layer | Status | Notes |
|---|---|---|
| Domain + EF migrations | ~85% | `purchase.purchase_orders`, `vendor_invoices`, `payment_vouchers`, `Tax.WhtCertificate`, `sys.expense_categories` (19 categories seeded) |
| Backend endpoints | ~90% | PO Draft→Approve→Sent→Close/Cancel · VI Draft→ClaimPeriod→Post · PV Draft→Approve→Post+PDF · WHT cert generation · `/reports/outstanding-po` + `/reports/missing-wht-cert` |
| Frontend pages | ~70% | `/vendors` `/purchase-orders` `/vendor-invoices` `/payment-vouchers` `/wht-certificates` — all have list+new+detail; ภ.ง.ด.3/53/54 + ภ.พ.36 + WHT Receivable + Outstanding PO complete |
| Cross-doc links | ◐ partial | PV←VI link exists (`?fromVendorInvoiceId=`); VI←PO link exists (pull lines); but no visual chain panel |
| UX parity vs Sales | ☐ ขาด | No PaperDocument, no DocumentChain, no PrintMenu, no print tracking, no ต้นฉบับ/สำเนา watermark, no mascot on list empty state |
| AP Aging | ☐ ขาด | Listed in §7.2 but not built |
| PO `/new` form quality | ◐ minimal | Single-line, hardcoded `VAT7`, no ProductPicker — needs lift to VI-level quality |
| Tests | ~partial | BE has PV/VI integration tests; no AP Aging coverage; no E2E PO→VI→PV chain |

---

## 3. Workflow (post-Phase 1)

```
[Vendor master]
       ↓
[Purchase Order]   (optional pre-step, ออกให้ vendor — "Approved" → "Sent" → "Closed")
       ↓
[Vendor Invoice]   (ใบกำกับภาษีซื้อ — บันทึกใบที่ vendor ส่งมา + ยืนยันรับสินค้า/บริการ;
       │            VI = receipt-of-record, ไม่มี GR แยก)
       ↓                   ↓
       │           [WHT Cert 50 ทวิ]  (auto-generated on PV post ถ้ามี WHT > 0)
       ↓
[Payment Voucher]  (จ่ายเงิน — SoD enforced, GL posts on approve)
```

**Out of scope (explicit per §6):** Purchase Request, Goods Receipt, 3-way match, approval matrix, stock tracking.

---

## 4. Deliverables — Phase 1

### 4.1 UX parity with Sales (the bulk)

For each of **Purchase Order, Vendor Invoice, Payment Voucher** detail pages:
- Adopt `<PaperDocument>` wrapper (same Phase C component that landed in 13j-FE for Sales)
- Apply ต้นฉบับ/สำเนา watermark per the §C7 matrix that Sprint 13j-FE locked
  - PO posted = ต้นฉบับ on first print, สำเนา on reprints
  - VI posted = ต้นฉบับ once, สำเนา reprints (this is the customer-facing ใบกำกับภาษีซื้อ they keep on file)
  - PV posted = ต้นฉบับ once, สำเนา reprints
- Render `<DocumentChain>` panel on detail (vertical, like Sales) showing the PO → VI → PV nodes with status badges
- `<PrintMenu>` + `<ChainRowPrint>` — same component reused from Sales, just doctype labels extended

For each **list page** (PO / VI / PV / WHT Cert):
- `<MascotGreeting>` on empty state (no rows)
- `<FilterBar>` polish (status chips + vendor filter + date range)
- Status badges via `<DocumentStatusBadge>` (Thai labels — ร่าง / อนุมัติแล้ว / จ่ายแล้ว / ยกเลิก ฯลฯ)
- Column headers — Thai (audit existing code; some may already be EN per Sprint 13i bug class)

### 4.2 AP Aging report (NEW)

**Backend** (`Accounting.Application/Reports` + endpoint):
- `GET /reports/ap-aging?asOf=<yyyy-MM-dd>&vendorId=<int?>`
- Returns:
  ```json
  {
    "asOf": "2026-05-31",
    "rows": [
      {
        "vendorId": 1,
        "vendorName": "...",
        "vendorTaxId": "0105...",
        "current": 0,        // 0-30 days
        "bucket31to60": 0,
        "bucket61to90": 0,
        "bucketOver90": 0,
        "total": 0
      }
    ],
    "totals": { "current": 0, "bucket31to60": 0, "bucket61to90": 0, "bucketOver90": 0, "total": 0 }
  }
  ```
- Source: `vendor_invoices` where `status = Posted` and `outstanding_amount > 0`; bucket by `(asOf - vi.doc_date)` days
- `outstanding_amount = vi.total_amount − Σ(applied PV amounts)`
- Multi-tenant: `company_id` filter MANDATORY (`CLAUDE.md` §4.7)

**Frontend** (`/reports/ap-aging`):
- Mirror layout of `/reports/outstanding-po`
- Table: Vendor name + tax_id + 4 buckets + Total row
- Filters: as-of date (default = today Asia/Bangkok), vendor (optional)
- CSV export (same pattern as outstanding-po)
- Mascot empty state when no outstanding

**Tests:**
- Unit: bucket math edge cases (exactly 30 / 31 / 60 / 61 days)
- Integration: seed VIs with varying dates → assert bucket assignment + totals
- Multi-tenant: company A's VI must not appear in company B's report

### 4.3 Cross-doc links + chain

- `<DocumentChain>` Purchase variant — reuse Sales chain component; doctype keys = `PO | VI | PV | WHT`
- Backend `GET /documents/chain?docType=PO&id=...` etc. already exists for Sales — extend to Purchase doctypes (mirror `Sales.GetChainAsync` pattern)
- "Mark printed" tracking on PO / VI / PV — same migration pattern as `AddPrintTrackingToSalesChain` but for Purchase tables
- "Sent to vendor" status on PO already exists; reuse for printed-ต้นฉบับ semantics

### 4.4 Bug pass + polish

- **PO `/new` form lift** — current state is 1-line scaffolded form with hardcoded `VAT7` + `taxCodeId: 1`. Bring to **VI `/new` quality**:
  - Multi-line items via `<LineItemsTable>` (same component as Sales)
  - `<ProductPicker>` for line description (optional — PO may be free-text per Plan §7)
  - Real VAT code selector (`taxCodeId` from `tax.tax_codes`)
  - Discount percent per line
  - Real validation messages (no generic "เกิดข้อผิดพลาด" toast — `BUG #SR9` class)
  - Submit returns to detail page (current code already correct)
- **Expense Category list view** — add read-only `/settings/expense-categories` showing the 19 seeded categories (matches `/settings/wht-types`, `/settings/products` etc.). No CRUD — system-seeded.
- **Toast messages Thai only** — audit `vendor-invoices/new`, `payment-vouchers/new`, `purchase-orders/new` for any English fallback in error toasts.
- **List column headers** — same audit; replace any English label with `useTranslations()` calls.

### 4.5 Purchase audit hooks — **NEW (verified gap, 2026-05-23)**

Grep of `backend/src/Accounting.Infrastructure/Purchase/*` confirms **zero** `IActivityRecorder` injections — Purchase doctypes do not write `audit.activity_log` rows on state transitions. Sales got this via Sprint 13k §4.8; Purchase was missed. Closing the gap now (not deferred to a separate sprint) because the same service files will be open for §4.7 anyway.

**Pattern to mirror** (verified in Sales code):
```csharp
// Constructor inject
private readonly IActivityRecorder _activity;
public PaymentVoucherService(... IActivityRecorder activity) { _activity = activity; ... }

// At each state transition (BEFORE SaveChangesAsync — same transaction)
_activity.Record("PaymentVoucher", pv.PaymentVoucherId, pv.DocNo, pv.CompanyId,
    "Posted", fromStatus: "Approved", toStatus: "Posted", module: "purchase");
```

**Required call sites:**

| Service | Entity | Actions to record |
|---|---|---|
| `PurchaseOrderService` | `PurchaseOrder` | Created (→Draft) · Updated (Draft→Draft, optional) · Approved (Draft→Approved) · MarkedSent (Approved→Sent) · Closed (Sent→Closed) · Cancelled (any→Cancelled, with reason note) |
| `VendorInvoiceService` | `VendorInvoice` | Created (→Draft) · Updated (Draft→Draft, optional) · ClaimedPeriod (Draft→Draft, note=period) · Posted (Draft→Posted) |
| `PaymentVoucherService` | `PaymentVoucher` | Created (→Draft) · Approved (Draft→Approved) · Posted (Approved→Posted) |
| `WhtCertificateService` | `WhtCertificate` | Generated (auto on PV post — note PV docNo) · Resent (optional, mirror TaxInvoice.Resent) |

`module` parameter = `"purchase"` for all (Sales uses `"sales"` default).

**Tests:** integration tests asserting `audit.activity_log` row count + payload on each transition. Mirror existing Sales activity-log tests pattern.

### 4.6 PaperDocumentPdf consolidation (Purchase) — **NEW**

**Current state:** Purchase services use bespoke `.GeneratePdf()` calls (verified):
- `PurchaseOrderService.cs:199` — bespoke QuestPDF
- `PaymentVoucherService.Read.cs:82` — bespoke QuestPDF
- `WhtCertificateService.cs:143` — bespoke QuestPDF (50 ทวิ — RD-mandated layout)

Sales TI / CN / DN already use shared `Pdf.PaperDocumentPdf.Render(PaperDocModel)`. Refactor PO and PV to use the same → paper layout uniform with Sales (per Ham 2026-05-23: "Layout ของเอกสารก็ให้มันรูปแบบใกล้เคียงกับของ Sales").

**Scope:**
- **PO** → migrate to `PaperDocumentPdf.Render(PaperDocModel)`. Build PaperDocModel from PO entity (header / lines / footer / signatures). ต้นฉบับ/สำเนา watermark per `copy` query param (mirror TI endpoint pattern — currently PO `/pdf` endpoint has no `copy` param; ADD it).
- **PV** → migrate to `PaperDocumentPdf.Render(PaperDocModel)`. WHT cert remains separately embedded (see exclusion below).
- **WHT certificate (50 ทวิ)** → **DO NOT REFACTOR** in this sprint. RD has mandated 50 ทวิ form layout (`docs/accounting-system-plan.md` §12.2.2); generic PaperDocument cannot replicate. Leave bespoke. Flag for future `PaperDocWht` variant if needed.
- **VI** → has no `/pdf` endpoint and does not need one (VI records the vendor's TI; the vendor's original PDF is the artifact). Confirm with Ham if VI PDF requested.

**Sales-side PaperDocumentPdf consolidation (Q/SO/DO/BN/RC)** — explicitly OUT OF SCOPE for this sprint. These still work via bespoke QuestPDF; uniformity nice-to-have, not blocker. Defer to Sprint 13L.

### 4.7 Tests

- E2E (Playwright): demo-admin login → Create PO (multi-line) → Approve → Mark Sent → Create VI from PO (lines pulled) → Post → Create PV from VI → Approve → Post → Verify WHT cert generated → Verify AP Aging shows zero outstanding for that vendor → Verify `audit.activity_log` has rows for every transition
- Visual snapshot (manual via Chrome MCP RE-VALIDATE): PO / VI / PV detail page = paper layout matches Sales TI / SO / Q detail (same `PaperDocument` FE component + same `PaperDocumentPdf` BE output)
- Regression: Sales chain happy path still passes (don't break Sprint 13j-FE)
- Integration: each Purchase audit hook fires (assert row in `audit.activity_log` with correct entityType / action / fromStatus / toStatus / module)
- Integration: PaperDocumentPdf renders PO + PV without exceptions for all status combinations
- Integration: AP Aging buckets math edge cases (exactly 30 / 31 / 60 / 61 days)

---

## 5. Compliance rails (must not violate)

| Rule | Source | Notes |
|---|---|---|
| Posted PV / VI immutable | `CLAUDE.md` §4.2 | Existing DB triggers + service guards — verify still active after this sprint |
| VI Tax Period ≤ 6 months from vendor TI date | ม.82/4 | Already enforced in `claimOptions()` — keep |
| Non-recoverable line → debit as expense, not Input VAT | ม.82/5 | Already in VI line model — keep |
| PV SoD: `created_by ≠ approved_by` | `CLAUDE.md` §12.1 | DB CHECK `ck_pv_sod` + app — do not relax |
| WHT cert auto on PV post if any WHT line > 0 | §15.10 | Already in `PaymentVoucherService.PostAsync` — keep |
| Expense Category required on PV | §17.3 | `purchase.payment_vouchers.expense_category_id NOT NULL` — keep |
| Multi-tenant `company_id` filter | §4.7 + RLS | **Verify AP Aging query** includes it (most common bug class) |
| Document numbering — monthly sequence, no gaps | §4.3 | Existing — do not touch number-allocation code |
| Vendor info snapshot on PV post | §12.1 | Already implemented — keep |
| No `Inventory.*` schema activation | §8 | This sprint must not create `goods_receipts` or any inventory-shaped table |
| No editing posted docs from UI | §4.2 | Verify PaperDocument adoption preserves disabled-state for posted detail pages |
| 5-year retention on audit log | พรบ.การบัญชี ม.14 | Cross-sprint concern — flagged in Q15 audit gap |

---

## 6. Out of scope (explicit)

| Item | Reason |
|---|---|
| Goods Receipt (any) | Ham 2026-05-23: VI = receipt; no separate doc |
| 3-way match | Requires GR; out of scope |
| Purchase Request + approval matrix | Plan §7.2 mentions but lacks detail; SME phase-1 doesn't need |
| Stock balance / SKU qty on hand | §8 inventory out of scope |
| Vendor portal | Not in roadmap §22 Phase 1-2 |
| e-Tax inbound (receive XML from vendor) | Not Phase 1 — we only do outbound e-Tax |
| Multi-currency FX revaluation | Phase 2 |
| Recurring PV / scheduled payments | Phase 2 |
| AP automation / bank file generation | Phase 2 |
| New `wht-certificates/new` flow | WHT cert is auto-generated by PV post; manual create not in Phase 1 |
| Sprint 13i pending bugs (#47/49/50/51/59/60/61) | These are Sales bugs — separate fix track |

---

## 7. Acceptance criteria

- [ ] All 4.x deliverables shipped + verified
- [ ] Build gates: `tsc 0`, `next build 0/0`, `dotnet build 0/0`
- [ ] Backend tests: existing 112 still pass + new AP Aging tests pass + new E2E PO→VI→PV passes
- [ ] Chrome MCP RE-VALIDATE: Purchase chain happy path completes end-to-end on demo data
- [ ] Visual parity: PO / VI / PV detail pages use the same PaperDocument primitives that Sales does (no second visual style)
- [ ] No regression on Sales chain (Sprint 13j-FE acceptance still green)
- [ ] No `CLAUDE.md` §4 violation
- [ ] No `inventory.*` schema artifact created
- [ ] AP Aging multi-tenant query verified (RLS test added)
- [ ] All Thai labels — no English bleed-through in user-facing UI (form fields, toasts, status badges, column headers)
- [ ] OpenAPI updated for AP Aging endpoint
- [ ] `progress.md` cont.{N} entry + `plan.md` tick

---

## 8. References (Gold Standard — wins on conflict)

| Topic | File | Section |
|---|---|---|
| AP / Purchase workflow | `docs/accounting-system-plan.md` | §7 (esp §7.4 Input VAT rules) |
| Inventory out of scope | `docs/accounting-system-plan.md` | §8 |
| WHT module | `docs/accounting-system-plan.md` | §12.2 |
| Expense Categories — 19 seeded | `docs/accounting-system-plan.md` | §17.3 |
| Numbering format | `docs/accounting-system-plan.md` | §4.3 + §17.3 (PV `MM-YYYY-PV-{CATEGORY}-NNNN`) |
| Purchase UI screens | `docs/Design(UI).md` | §8 |
| Payment Voucher non-negotiables | `CLAUDE.md` | §12.1 |
| Chapter-by-chapter workflow | `CLAUDE.md` | §16 (Purchase = Chapter 4) |
| PaperDocument props contract | `docs/paper-document-spec.md` | (to be written before this sprint OR as part of Sprint 13j-PDF) |
| Runtime gotchas | `docs/runtime-gotchas.md` | All — read before code |
| OpenAPI | `docs/api/openapi.yaml` | `/vendors`, `/purchase-orders`, `/vendor-invoices`, `/payment-vouchers` |

---

## 9. Open items for Claude Code brief (NEXT step — Answer-Sana-Backend{N})

When Ham approves this requirements doc, Sana will write the implementation spec. Open questions to resolve there (not here):

- Sprint number assignment — current candidates: **Sprint 13j-PURCH** (slots between 13j-FE and 13k) OR **Sprint 13L** (after 13j-PDF + 13k). Ham to pick ordering.
- Ordering vs. **Sprint 13j-PDF** (QuestPDF mirror): does PaperDocument need its spec doc (`docs/paper-document-spec.md`) locked first, or can Purchase ride on the same FE component without the PDF mirror? Likely first — both depend on the same component contract.
- Detail file list (paths) for each deliverable
- AP Aging endpoint OpenAPI delta — exact request/response schema
- DocumentChain Purchase variant — reuse Sales chain component (extend doctype enum) vs. new component
- PrintMenu doctype enum extension — `PO | VI | PV | WHT` added to existing `Q | SO | DO | INV | TI | RC | CN | DN`
- Migration: `AddPrintTrackingToPurchaseChain` mirroring `AddPrintTrackingToSalesChain` — confirm column names + state machine

---

## 10. Sprint sequencing context (informational)

Current backlog (post Sprint 13j-FE ship):
1. **Sprint 13j-PDF** — QuestPDF mirror of PaperDocument (locks paper-document-spec.md contract). Necessary because Sales chain ships paper UI without printable PDF parity.
2. **Sprint 13j-PURCH** (THIS) — Purchase UX parity + AP Aging. Depends on (1) for `paper-document-spec.md`.
3. **Sprint 13k** — Security / RBAC / Perf / A11y hardening (covers Question-Backend15 audit gap as part of audit-trail work).
4. **Sprint 13L** — DevOps (CI/CD, runbooks, monitoring).

Decision needed from Ham: **does 13j-PDF have to ship first**, or can Purchase use the Sales PaperDocument component as-is (lock spec then) and 13j-PDF follow? Sana's lean: **13j-PDF first** — locks the spec, then Purchase implementation can target a stable component contract.
