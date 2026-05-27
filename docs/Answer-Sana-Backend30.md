# Answer-Sana-Backend30 — Sprint 13j-PURCH spec

> **Purpose:** implementation brief for Claude Code, Sprint 13j-PURCH (Purchase / AP Phase 1).
> Replaces both the prior 13j-PDF plan and the 13j-PURCH plan — they collapsed into one sprint after Sana audited backend state (2026-05-23) and found QuestPDF + Sales audit hooks already shipped (Sprint 13k §4.8). The remaining gap is **Purchase-side parity** plus the FE polish that Sprint 13j-FE applied to Sales.
> **Source of truth:** `docs/Requirements-Purchase-Phase1.md` (locked 2026-05-23). This file is the HOW; that file is the WHAT.

---

## §0. GOLD STANDARD rule (non-negotiable)

When this spec contradicts the existing project docs below, **the existing docs win**. This file is derivative.

| Doc | Wins on |
|---|---|
| `CLAUDE.md` | env, gotchas, compliance §4, autonomy boundaries, naming, test discipline |
| `docs/accounting-system-plan.md` | §7 Purchase flow, §8 Inventory out-of-scope, §12 tax, §17.3 expense categories, §18 compliance |
| `docs/Design(UI).md` | §8 Purchase screens |
| `docs/api/openapi.yaml` | exact request/response shape (extend, don't break) |
| `docs/runtime-gotchas.md` | every gotcha there is load-bearing |
| `docs/Requirements-Purchase-Phase1.md` | scope + acceptance |
| Prior `Answer-Sana-Backend{20-29}.md` | conventions for similar work |

**Mockup / inspirations (Claude Design `design/claude-design/`) — Sprint 13j-FE already extracted what was usable. Do not re-import.**

---

## §0a. Ham's locked decisions (2026-05-23)

1. **Scope = Option A (Polish UX parity) + AP Aging + Purchase audit hooks + Purchase PaperDocumentPdf consolidation.** Audit + PDF folded in because they touch the same Service files and Sales already proved the pattern.
2. **No Goods Receipt — ever.** VI = receipt-of-record. No `goods_receipts` table, no migration reservation. *"เราไม่ทำอะไรกับสินค้าเลย มันคนละระบบกัน ไม่ทำ Inventory คือรับเข้า มีเอกสารแล้วจบกันเลย"* — Ham, verbatim.
3. **AP Aging report included.**
4. **Purchase menu items + Settings route stay as-is** (inherited from 13j-FE clarification).
5. **WHT certificate (50 ทวิ) PDF stays bespoke** — RD-mandated layout; not migrated to PaperDocumentPdf.
6. **Sales-side PaperDocumentPdf consolidation (Q/SO/DO/BN/RC still bespoke) deferred to Sprint 13L.** Not blocker.
7. **Sequencing:** Sprint 13j-PURCH → 13k (Security/RBAC/Perf/A11y) → 13L (DevOps + remaining Sales PDF consolidation).

---

## §1. Sprint goal (one sentence)

Bring Purchase / AP to UX + compliance parity with Sales: adopt FE PaperDocument/DocumentChain/PrintMenu, ship AP Aging report, wire `IActivityRecorder` into all Purchase services, and consolidate PO + PV PDF generation onto `PaperDocumentPdf.Render` so the printed paper layout matches Sales.

---

## §2. Phases (sequential — do not parallelize unless §2.x explicitly says so)

### Phase A — BE: Purchase audit hooks (small, low-risk start) — Day 1

Inject `IActivityRecorder` into and record state transitions in:

- `Accounting.Infrastructure/Purchase/PurchaseOrderService.cs`
  - Created → toStatus "Draft"
  - Approved → "Draft" → "Approved"
  - MarkedSent → "Approved" → "Sent"
  - Closed → "Sent" → "Closed"
  - Cancelled (with reason `note`) → any → "Cancelled"
- `Accounting.Infrastructure/Purchase/VendorInvoiceService.cs`
  - Created → "Draft"
  - ClaimedPeriod → "Draft" → "Draft", `note = $"period:{yyyymm}"`
  - Posted → "Draft" → "Posted"
- `Accounting.Infrastructure/Purchase/PaymentVoucherService.cs`
  - Created → "Draft"
  - Approved → "Draft" → "Approved"
  - Posted → "Approved" → "Posted"
- `Accounting.Infrastructure/Purchase/WhtCertificateService.cs`
  - Generated (called from `PaymentVoucherService.PostAsync` if any WHT row > 0) → toStatus "Issued", `note = $"pv:{pvDocNo}"`

`module = "purchase"` for every call. EntityType = `"PurchaseOrder"`, `"VendorInvoice"`, `"PaymentVoucher"`, `"WhtCertificate"`.

**Pattern reference:** `Accounting.Infrastructure/Sales/PaymentVoucherService.cs` — wait, that's Purchase. Use **`Accounting.Infrastructure/Sales/TaxInvoiceService.cs:223` + `:269`** and **`Accounting.Infrastructure/Sales/QuotationChainServices.cs:86`** as the call-site template (these are the canonical patterns).

**Tests (`backend/tests/Accounting.Api.Tests/Purchase/PurchaseAuditTests.cs`, new):**
- One test per transition asserting `audit.activity_log` has exactly +1 row with correct payload.
- Use `Accounting.TestKit.TestIds.*` for all unique data (§8 test discipline).
- Run 2× consecutive on `teas_test` per CLAUDE.md §8.

**Gate A:** `dotnet build W:\Accounting.sln` 0/0 · all new tests pass 2× on shared `teas_test` · existing 112+ still green.

---

### Phase B — BE: AP Aging report (independent, BE-only) — Day 1-2

**Entity / DTO:** none new. Read-only projection from `purchase.vendor_invoices`.

**Files to create:**

- `backend/src/Accounting.Application/Reports/ApAgingDtos.cs`
  - `ApAgingRow(int VendorId, string VendorName, string VendorTaxId, decimal Current, decimal Bucket31To60, decimal Bucket61To90, decimal BucketOver90, decimal Total)`
  - `ApAgingReport(DateOnly AsOf, IReadOnlyList<ApAgingRow> Rows, ApAgingRow Totals)`
- `backend/src/Accounting.Application/Reports/IApAgingService.cs`
- `backend/src/Accounting.Infrastructure/Reports/ApAgingService.cs`
  - Source: `vi.Status == "Posted" && vi.OutstandingAmount > 0`
  - `OutstandingAmount = vi.TotalAmount − Σ(pv applications applied to this vi)` — confirm via existing computed field if present; otherwise query
  - Bucket by `(asOf − vi.DocDate).Days`:
    - `[0..30]` → Current
    - `[31..60]` → Bucket31To60
    - `[61..90]` → Bucket61To90
    - `[91..∞)` → BucketOver90
  - **Multi-tenant filter mandatory** (`company_id` — and confirm RLS test added)
- `backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs` (extend existing — same file as `/reports/outstanding-po`)
  - `GET /reports/ap-aging?asOf=yyyy-MM-dd&vendorId=int?` → `ApAgingReport`
  - Default `asOf` = `DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))` — Asia/Bangkok today, per CLAUDE.md §5
  - Authorization: `purchase.view` permission (mirror `outstanding-po` policy)
- `backend/src/Accounting.Infrastructure/DependencyInjection.cs` — register `IApAgingService`

**OpenAPI:** add path under `paths:` in `docs/api/openapi.yaml` mirroring `/reports/outstanding-po` structure.

**Tests (`backend/tests/Accounting.Api.Tests/Reports/ApAgingTests.cs`, new):**
- Bucket boundary edge cases — exactly 30, 31, 60, 61, 90, 91 days
- Multi-tenant isolation — Company A VI must not appear in Company B's report
- Partial payment — VI total 1000, PV applied 300, outstanding 700, correct bucket
- Empty result — no VIs → empty rows, zero totals
- Use `TestIds.VendorCode()` for unique seed data

**Gate B:** `dotnet build` 0/0 · new tests pass 2× · OpenAPI valid (`swagger.json` GET 200).

---

### Phase C — BE: PaperDocumentPdf consolidation (PO + PV) — Day 2

Refactor `PurchaseOrderService.cs` PDF builder and `PaymentVoucherService.Read.cs` PDF builder to use `Pdf.PaperDocumentPdf.Render(PaperDocModel)`. **Do not touch `WhtCertificateService.cs` PDF** — 50 ทวิ stays bespoke.

**Reference implementation:** `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.Read.cs:120-137` — this is the canonical pattern (build `PaperDocModel`, call `Pdf.PaperDocumentPdf.Render`). Copy that shape, swap field bindings for PO/PV.

**Endpoint deltas:**
- `GET /purchase-orders/{id}/pdf` → add `?copy=bool` query param (mirror TI endpoint). `copy=false` (default) = "ต้นฉบับ" first time; reprint sets `copy=true` → "สำเนา". Print tracking already exists for Sales chain — extend the same table or use existing infrastructure.
- `GET /payment-vouchers/{id}/pdf` → same `?copy` param addition.

**PaperDocModel field mapping notes:**
- `Title` = "ใบสั่งซื้อ" (PO) / "ใบสำคัญจ่าย" (PV)
- `SubtitleEn` = "Purchase Order" / "Payment Voucher"
- `Watermark` = "ต้นฉบับ" (copy=false) / "สำเนา" (copy=true) — preserved per Sprint 13j-FE §C7
- `ShowVat` = `VatModeOptions.Enabled` (PV doesn't show VAT inputs but PO may carry vat_rate on lines — defer to current PO PDF behavior)
- `Lines` = PO lines / PV lines
- `Foot` = subtotal/VAT/WHT/total split — PV needs the WHT-deducted "จ่ายสุทธิ" row
- `Sign` = สองช่อง (ผู้ขออนุมัติ / ผู้อนุมัติ) for PO, สามช่อง (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน) for PV

If a PaperDocModel field needs extension (e.g. new section for PV's "ใบกำกับภาษีอ้างอิง" list), **extend `PaperDocModel` rather than fork** — keeping one model preserves the consolidation.

**Tests:**
- `backend/tests/Accounting.Api.Tests/Purchase/PurchasePdfTests.cs` (new) — PO + PV render without exception for: Draft / Approved / Posted statuses, single-line, multi-line, with-WHT, without-WHT.
- Existing TI / CN / DN PDF tests must still pass (no regression).

**Gate C:** `dotnet build` 0/0 · all PDF tests pass · open one generated PO + PV PDF and eyeball that layout = TI's layout (manual via Sana RE-VALIDATE in next session — but Claude Code must confirm file size > 1KB and `application/pdf` content type returned).

---

### Phase D — FE: PaperDocument + DocumentChain + PrintMenu adoption — Day 3-4

For each of: **PO detail (`/purchase-orders/[id]/page.tsx`)**, **VI detail (`/vendor-invoices/[id]/page.tsx`)**, **PV detail (`/payment-vouchers/[id]/page.tsx`)**:

1. Wrap the document body in `<PaperDocument>` — reuse the Sprint 13j-FE component (`components/paper/PaperDocument.tsx` or wherever it landed).
2. Add `<DocumentChain>` panel — extend the existing component's doctype enum to include `PO | VI | PV | WHT`. The chain shows: PO → VI → PV (→ WHT if applicable).
3. Wire `<PrintMenu>` + `<ChainRowPrint>` — same component reused from Sales detail pages. PrintMenu options: "พิมพ์ต้นฉบับ" / "พิมพ์สำเนา" — calls `/pdf?copy=true|false`. **Do NOT use `window.print()` of the HTML page** (BUG class #SR8).
4. `<DocumentStatusBadge>` — confirm status labels are Thai (ร่าง / อนุมัติแล้ว / ส่งแล้ว / ปิดแล้ว / ยกเลิก / จ่ายแล้ว / ฯลฯ).
5. Posted detail must be read-only — confirm no edit fields enabled when `status === "Posted"` (compliance §4.2).

For list pages **`/purchase-orders/page.tsx`, `/vendor-invoices/page.tsx`, `/payment-vouchers/page.tsx`, `/wht-certificates/page.tsx`:**
- `<MascotGreeting>` on empty state
- `<FilterBar>` polish (status chips + vendor filter + date range)
- Column headers Thai (`useTranslations()` only — no hardcoded EN)
- Status badges via `<DocumentStatusBadge>`

**Backend chain endpoint:** if `GET /documents/chain?docType=PO&id=...` doesn't already include Purchase doctypes, extend `DocumentCrossRefService` (Sales-side) — or create `PurchaseChainService` mirroring it. Pick whichever fits the existing code shape best.

**Files to touch (probable, verify before edit):**
- `frontend/app/(dashboard)/purchase-orders/[id]/page.tsx`
- `frontend/app/(dashboard)/vendor-invoices/[id]/page.tsx`
- `frontend/app/(dashboard)/payment-vouchers/[id]/page.tsx`
- `frontend/app/(dashboard)/wht-certificates/[id]/page.tsx`
- `frontend/app/(dashboard)/{purchase-orders,vendor-invoices,payment-vouchers,wht-certificates}/page.tsx`
- `frontend/components/paper/PaperDocument.tsx` (extend if needed)
- `frontend/components/chain/DocumentChain.tsx` (extend doctype enum)
- `frontend/messages/{th,en}.json` (add new keys for Purchase status badges if any are missing)

**Gate D:** `cd frontend && tsc --noEmit` 0 · `next build` 0/0 (run from the **native path**, NOT `U:\frontend` — `subst U:` breaks webpack per the new gotcha discovered Sprint 13j-FE).

---

### Phase E — FE: AP Aging report page — Day 4

- `frontend/app/(dashboard)/reports/ap-aging/page.tsx` (new) — mirror `/reports/outstanding-po/page.tsx`
- Table: vendor name + tax ID + 4 buckets + Total column + Totals row
- Filters: as-of date (default today), vendor (optional `VendorSelector`)
- CSV export (same pattern as outstanding-po)
- Mascot empty state when no outstanding
- Sidebar nav entry under reports section — Thai label "AP Aging" or "รายงานยอดเจ้าหนี้ค้างชำระ"
- `frontend/lib/queries.ts` — add `useApAgingReport()` query hook
- `frontend/messages/{th,en}.json` — add report labels

**Gate E:** tsc 0 · next build 0/0 · page loads on demo data.

---

### Phase F — FE: Bug pass + PO form lift — Day 4-5

1. **PO `/new` form lift** — current state at `frontend/app/(dashboard)/purchase-orders/new/page.tsx`:
   - 1-line form, hardcoded `taxCodeId: 1` + `taxRate: 0.07`
   - Lift to **Vendor Invoice `/new` quality**: `<LineItemsTable>` with multi-line, `<ProductPicker>` per line (optional for PO — free-text fallback OK per Plan §7), real VAT code selector, discount-percent per line, proper toast errors (no generic "เกิดข้อผิดพลาด" — BUG #SR9 class)
   - Submit returns to detail page (already correct — preserve)
2. **Expense Category list view** — `frontend/app/(dashboard)/settings/expense-categories/page.tsx` (new, read-only) showing the 19 seeded categories from `sys.expense_categories`. Mirror `/settings/wht-types` structure. No CRUD — system-seeded.
3. **Toast messages Thai-only audit** — grep `vendor-invoices/new`, `payment-vouchers/new`, `purchase-orders/new` for English fallback strings in error toasts. Replace with `useTranslations('common').error` or specific keys.
4. **List column header audit** — same files, replace any hardcoded EN with i18n.

**Gate F:** tsc 0 · next build 0/0 · happy path PO/VI/PV/WHT/AP Aging all reachable without console errors.

---

### Phase G — E2E + final gate — Day 5

- E2E Playwright test (`frontend/e2e/purchase-chain.spec.ts`, new):
  - demo-admin login
  - Create PO multi-line → Approve → MarkedSent
  - Create VI from PO (lines pull) → ClaimPeriod → Post
  - Create PV from VI → Approve → Post
  - Verify WHT cert generated (when WHT > 0)
  - Visit `/reports/ap-aging` → assert that vendor has zero outstanding (after PV post)
  - Visit each detail page → `PaperDocument` rendered + `DocumentChain` shows full chain + PrintMenu present
- Manual Sana RE-VALIDATE via Chrome MCP (post-ship, separate session): visual parity check + this E2E walked-through.
- Existing Sales E2E must still pass.

**Final gate (consolidated):**
- [ ] `dotnet build W:\Accounting.sln` 0/0
- [ ] All BE tests pass 2× consecutive on shared `teas_test` (`TEAS_TEST_PG` env)
- [ ] `cd frontend && pnpm test` or equivalent passes
- [ ] `cd frontend && tsc --noEmit` 0
- [ ] `next build` 0/0 (from native path)
- [ ] No `dotnet ef … --no-build` was ever used (CLAUDE.md §6)
- [ ] No raw SQL files added outside `Migrations/SqlScripts/`
- [ ] OpenAPI updated (`/reports/ap-aging` path + any `?copy` param additions)
- [ ] `progress.md` cont.{N} prepended with status table + verification results
- [ ] `plan.md` tick Sprint 13j-PURCH ☑
- [ ] **No git commit** — Ham commits per CLAUDE.md §10

---

## §3. Compliance rails (must not violate — verify each before claiming done)

| # | Rule | Source | Verification |
|---|---|---|---|
| 1 | Posted PO / VI / PV immutable | CLAUDE.md §4.2 | DB triggers + service guards already in place — test that detail page disables edit on Posted |
| 2 | VI Tax Period ≤ 6 months from vendor TI date | ม.82/4 | Existing `claimOptions()` — keep |
| 3 | Non-recoverable line → debit expense, not Input VAT | ม.82/5 | Already in `VendorInvoice.Line.Recoverable` — keep |
| 4 | PV SoD `created_by ≠ approved_by` | CLAUDE.md §12.1 | DB CHECK `ck_pv_sod` — do not relax |
| 5 | WHT cert auto on PV post if WHT > 0 | §15.10 | Existing — verify audit hook fires |
| 6 | Expense Category required on PV | §17.3 | DB NOT NULL — keep |
| 7 | Multi-tenant `company_id` filter on AP Aging | §4.7 + RLS | **Add explicit RLS test** in `ApAgingTests` |
| 8 | Document numbering monthly sequence | §4.3 | Do not touch number-allocation code |
| 9 | Vendor info snapshot on PV post | §12.1 | Existing — keep |
| 10 | No `inventory.*` schema activation | §8 | **No migration may add inventory-shaped table** |
| 11 | No editing posted docs from UI | §4.2 | PaperDocument adoption must preserve disabled-state for Posted |
| 12 | 5-year retention `audit.activity_log` | พรบ.การบัญชี ม.14 | Append-only — never delete |
| 13 | 50 ทวิ PDF stays RD-mandated layout | §12.2 | DO NOT migrate WhtCertificate to PaperDocumentPdf |

---

## §4. DO NOT list (the easy mistakes)

- ❌ DO NOT migrate WHT certificate PDF to `PaperDocumentPdf` — RD has its own form layout
- ❌ DO NOT add a `goods_receipts` table or any inventory-shaped schema
- ❌ DO NOT touch `WhtCertificateService.cs` PDF builder (audit hook OK, PDF code untouched)
- ❌ DO NOT consolidate Sales-side bespoke PDFs (Q/SO/DO/BN/RC) into PaperDocumentPdf — defer to Sprint 13L
- ❌ DO NOT run `dotnet ef … --no-build` after entity edits — CLAUDE.md §6 footgun
- ❌ DO NOT `next build` from `U:\frontend` — use native path (Sprint 13j-FE discovered this gotcha)
- ❌ DO NOT `git commit` — Ham commits
- ❌ DO NOT touch `Purchase Order /new` heavy-lift if Phase D timing slips — Phase F can defer to a follow-up if needed (flag in Report)
- ❌ DO NOT delete from `audit.activity_log` — append-only
- ❌ DO NOT add new endpoints not in this spec without flagging Ham (CLAUDE.md §11)
- ❌ DO NOT regress Sales chain — existing E2E must still pass at final gate
- ❌ DO NOT use Buddhist calendar internally (CE only)
- ❌ DO NOT trust user input for `doc_date` — always today Asia/Bangkok
- ❌ DO NOT add Stitch UI references (Sprint 13j-FE dropped Stitch — design source = Claude Design integration)
- ❌ DO NOT touch Settings page routes (Ham locked: stay as-is)
- ❌ DO NOT change Purchase menu items in sidebar (Ham locked: stay as-is)

---

## §5. Open items for Sana RE-VALIDATE (post-ship)

After Claude Code ships, Sana runs Chrome MCP RE-VALIDATE in next session:
1. Visual parity — PO/VI/PV detail = TI detail paper look
2. Print: ต้นฉบับ first time, สำเนา subsequent (assert tracking row written)
3. AP Aging — sample data correctness
4. Audit log query (`GET /activity?entityType=PaymentVoucher&entityId=...`) shows all expected transitions for a Posted PV
5. Sales chain regression — no break
6. Purchase menu + Settings — verify untouched

---

## §6. Hand-off — report back to Sana

When done, write `docs/Report-Backend35.md` with:
- Phase A–G status (done/skipped/blocked)
- Final gate evidence (paste test counts, build output, file lists)
- Migration list created (expected: at most 1 — for any PaperDocModel extension if needed; AP Aging is read-only no migration)
- Any **new gotcha** discovered → propose runtime-gotchas §N entry
- Any **scope deviation** + reason
- Files touched (full list, grouped by phase)
- `progress.md` cont.{N} entry text drafted (Sana finalizes)
- Open questions for Sana (label `Question-Backend{N+1}`)

---

**End of Answer-Sana-Backend30.**
