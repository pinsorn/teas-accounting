# Sprint 13j-PURCH — Purchase / AP Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: dispatch one subagent per Phase (see `purchase-subagents/subAgent{1..7}Task.md`). Steps use checkbox (`- [ ]`) syntax. Main agent verifies the gate between phases and runs the final consolidated gate. **NO `git commit` — Ham commits.**

**Goal:** Bring Purchase / AP to UX + compliance parity with Sales — adopt FE PaperDocument/DocumentChain/PrintMenu on PO/VI/PV/WHT, ship AP Aging report, wire `IActivityRecorder` into all Purchase services, consolidate PO + PV PDF onto `PaperDocumentPdf.Render`.

**Architecture:** Backend = Clean Architecture (Domain→Application→Infrastructure→Api), .NET 10, EF Core 10, PostgreSQL. Frontend = Next.js 15 App Router, React Query, RHF+Zod, next-intl. Mirror the **already-shipped Sales chain** patterns rather than inventing new ones.

**Tech Stack:** C#/.NET 10, EF Core, QuestPDF (`PaperDocumentPdf`), Npgsql · Next.js 15, TypeScript, Tailwind/daisyUI, TanStack Query · xUnit + FluentAssertions (`teas_test` via `TEAS_TEST_PG`), Playwright.

**Spec source of truth (Gold Standard wins on conflict):** `docs/Requirements-Purchase-Phase1.md` (WHAT) + `docs/Answer-Sana-Backend30.md` (HOW). On conflict with `CLAUDE.md` / `accounting-system-plan.md` / `Design(UI).md` / `openapi.yaml` / `runtime-gotchas.md` → the existing doc wins; flag the conflict in `docs/Report-Backend35.md`.

---

## ⚠️ Deviations from Answer-Sana-Backend30 (verified against live code 2026-05-27)

These were resolved by reading the actual repo; the spec is derivative, the code wins. **Each must be re-stated in `docs/Report-Backend35.md` §scope deviation.**

| # | Spec said | Reality | Plan decision |
|---|---|---|---|
| D1 | AP Aging endpoint in `ReportEndpoints.cs` | No `ReportEndpoints.cs`; `/reports/outstanding-po` is mapped in **`Accounting.Api/Endpoints/PurchaseOrderEndpoints.cs:67`**, served by **`PurchaseOrderService.OutstandingAsync` (`Infrastructure/Purchase/PurchaseOrderService.cs:202`)**, DTOs in **`Application/Purchase/PurchaseOrderDtos.cs:52-70`** | Mirror the **neighbor**: map `/reports/ap-aging` in `PurchaseOrderEndpoints.cs`. New `IApAgingService` + `ApAgingService` (spec's `Application/Reports` + `Infrastructure/Reports` layout is fine). |
| D2 | Outstanding = `TotalAmount − Σ(PV applications)` | `VendorInvoice` already carries **`SettledAmount`** + **`SettlementStatus` ("UNPAID\|PARTIAL\|PAID")** | **Phase B step 1 = verify `SettledAmount` is updated on PV post.** If yes → `Outstanding = TotalAmount − SettledAmount` (where `SettlementStatus != "PAID"`). If no → fall back to `Σ(PaymentVoucherApplication.Amount)` joined on `VendorInvoiceId`. |
| D3 | WHT "Generated" hook in `WhtCertificateService` | `WhtCertificateService` is **read-only** (`ListAsync`/`GetDetailAsync`/`BuildPdfAsync` only) | The `_activity.Record("WhtCertificate", …)` call site lives **inside `PaymentVoucherService.PostAsync`**, at the point the cert is auto-generated (WHT row > 0). Do **not** inject into `WhtCertificateService`. |
| D4 | Print tracking "reuse existing infra" | Sales has `OriginalPrintedAt`/`PrintCount` on `TaxInvoice` + `Sales/PrintTrackingService.cs`. **Purchase entities have neither.** | Phase C includes a real migration **`AddPrintTrackingToPurchaseChain`** adding `OriginalPrintedAt`/`PrintCount` to `PurchaseOrder` + `PaymentVoucher` (skip `VendorInvoice` — no `/pdf`). |

---

## File Map (created / modified)

### Backend
- `Application/Reports/ApAgingDtos.cs` — **create** (`ApAgingRow`, `ApAgingReport`)
- `Application/Reports/IApAgingService.cs` — **create**
- `Infrastructure/Reports/ApAgingService.cs` — **create**
- `Infrastructure/Purchase/PurchaseOrderService.cs` — **modify** (audit hooks A; PDF→PaperDocModel C; `copy` param)
- `Infrastructure/Purchase/VendorInvoiceService.cs` (+`.Read.cs`) — **modify** (audit hooks A)
- `Infrastructure/Purchase/PaymentVoucherService.cs` (+`.Read.cs`) — **modify** (audit hooks A incl. WHT-cert hook; PDF→PaperDocModel C; `copy` param)
- `Infrastructure/Pdf/PaperDocModel.cs` — **modify only if** a field must be added (extend, never fork)
- `Infrastructure/DependencyInjection.cs` — **modify** (register `IApAgingService`)
- `Api/Endpoints/PurchaseOrderEndpoints.cs` — **modify** (`/reports/ap-aging`; `?copy` on PO/PV `/pdf`)
- `Domain/Entities/Purchase/PurchaseOrder.cs`, `PaymentVoucher.cs` — **modify** (print-tracking columns, Phase C)
- `Infrastructure/Migrations/*` — **create** (`AddPrintTrackingToPurchaseChain`)
- Tests: `tests/Accounting.Api.Tests/Purchase/PurchaseAuditTests.cs`, `Reports/ApAgingTests.cs`, `Purchase/PurchasePdfTests.cs` — **create**

### Frontend
- `app/(dashboard)/purchase-orders/[id]/page.tsx`, `vendor-invoices/[id]/page.tsx`, `payment-vouchers/[id]/page.tsx`, `wht-certificates/[id]/page.tsx` — **modify** (PaperDocument + DocumentChain + PrintMenu)
- `app/(dashboard)/{purchase-orders,vendor-invoices,payment-vouchers,wht-certificates}/page.tsx` — **modify** (Mascot, FilterBar, StatusBadge, Thai headers)
- `app/(dashboard)/purchase-orders/new/page.tsx` — **modify** (lift to VI quality)
- `app/(dashboard)/reports/ap-aging/page.tsx` — **create**
- `app/(dashboard)/settings/expense-categories/page.tsx` — **create** (read-only)
- `components/doc/DocumentChain.tsx`, `components/ui/PrintMenu.tsx`, `components/doc/ChainRowPrint.tsx` — **modify** (extend doctype enum: add `PO|VI|PV|WHT`)
- `lib/queries.ts` — **modify** (`useApAgingReport`, Purchase chain hooks)
- `lib/types.ts` — **modify** (AP Aging types; print-tracking fields on Purchase detail types)
- `messages/th.json` + `messages/en.json` — **modify** (TH primary, both edited)
- `components/app-shell/SidebarNav.tsx` — **modify** (ap-aging nav entry under reports; do NOT touch Purchase menu items or Settings route)
- `e2e/purchase-chain.spec.ts` — **create** (Phase G)

---

## Phase A — BE: Purchase audit hooks  → subAgent1

**Files:** `Infrastructure/Purchase/{PurchaseOrderService,VendorInvoiceService,PaymentVoucherService}.cs`; test `tests/Accounting.Api.Tests/Purchase/PurchaseAuditTests.cs`.

**Pattern (verified):** `IActivityRecorder` at `Application/Audit/IActivityRecorder.cs`:
```csharp
void Record(string entityType, long entityId, string? docNo, int companyId,
    string action, string? fromStatus = null, string? toStatus = null,
    string? note = null, string module = "sales");
```
Call-site template — `Sales/TaxInvoiceService.cs:223`:
```csharp
_activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "Created", toStatus: "Draft");
```
Call **before** `SaveChangesAsync` (same transaction). `module: "purchase"` for every call.

- [ ] **A1. Inject `IActivityRecorder` into the 3 service ctors.** Add `private readonly IActivityRecorder _activity;` + ctor param `IActivityRecorder activity` to `PurchaseOrderService`, `VendorInvoiceService`, `PaymentVoucherService`. (DI auto-resolves — recorder already registered for Sales.)
- [ ] **A2. PurchaseOrderService call sites:**
  - `CreateDraftAsync` → `Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId, "Created", toStatus: "Draft", module: "purchase")`
  - `ApproveAsync` (`:91`) → `"Approved"`, from `"Draft"`, to `"Approved"`
  - `MarkSentAsync` (`:107`) → `"MarkedSent"`, from `"Approved"`, to `"Sent"`
  - `CloseAsync` (`:117`) → `"Closed"`, from `"Sent"`, to `"Closed"`
  - `CancelAsync` (`:125`) → `"Cancelled"`, to `"Cancelled"`, `note: <reason>`
- [ ] **A3. VendorInvoiceService call sites:**
  - `CreateDraftAsync` → `"Created"`, to `"Draft"`
  - `SetClaimPeriodAsync` → `"ClaimedPeriod"`, from `"Draft"`, to `"Draft"`, `note: $"period:{yyyymm}"`
  - `PostAsync` → `"Posted"`, from `"Draft"`, to `"Posted"`
- [ ] **A4. PaymentVoucherService call sites:**
  - `CreateDraftAsync` → `"Created"`, to `"Draft"`
  - `ApproveAsync` (`:141`) → `"Approved"`, from `"Draft"`, to `"Approved"`
  - `PostAsync` (`:159`) → `"Posted"`, from `"Approved"`, to `"Posted"`
  - **WHT-cert hook (D3):** inside `PostAsync`, at the branch that auto-generates the WHT cert (WHT row > 0), add `Record("WhtCertificate", cert.WhtCertificateId, cert.DocNo, cert.CompanyId, "Generated", toStatus: "Issued", note: $"pv:{pv.DocNo}", module: "purchase")`.
- [ ] **A5. Write `PurchaseAuditTests.cs`** — one test per transition above (≈12 tests). Each: arrange via existing service calls using `TestIds.VendorCode()`/`TestIds.ProductCode()` etc.; act the transition; assert `audit.activity_log` has exactly **+1** row with correct `EntityType`/`Action`/`FromStatus`/`ToStatus`/`Module="purchase"`. Mirror the existing Sales activity-log test (grep `activity_log` under `tests/`).
- [ ] **A6. Build:** kill :5080 listener → `dotnet build W:\Accounting.sln` → expect **0/0**.
- [ ] **A7. Test 2×:** from `W:\tests\Accounting.Api.Tests` with `TEAS_TEST_PG` set → run `PurchaseAuditTests` **twice consecutive**, both green; existing suite ≥ baseline (112+).

**Gate A:** build 0/0 · `PurchaseAuditTests` pass 2× on `teas_test` · no existing-test regression.

---

## Phase B — BE: AP Aging report  → subAgent2

**Files:** create `Application/Reports/ApAgingDtos.cs`, `Application/Reports/IApAgingService.cs`, `Infrastructure/Reports/ApAgingService.cs`; modify `Infrastructure/DependencyInjection.cs`, `Api/Endpoints/PurchaseOrderEndpoints.cs`, `docs/api/openapi.yaml`; test `tests/Accounting.Api.Tests/Reports/ApAgingTests.cs`.

- [ ] **B1. (D2) Verify settlement source.** Read `PaymentVoucherService.PostAsync` + `VendorInvoice.cs`. Confirm `SettledAmount`/`SettlementStatus` are updated when a PV posts. Record the answer in the Report. Choose: outstanding = `TotalAmount − SettledAmount` (preferred) OR `TotalAmount − Σ(PaymentVoucherApplication.Amount where VendorInvoiceId=vi.Id)` (fallback).
- [ ] **B2. DTOs** (`ApAgingDtos.cs`):
```csharp
public sealed record ApAgingRow(int VendorId, string VendorName, string VendorTaxId,
    decimal Current, decimal Bucket31To60, decimal Bucket61To90, decimal BucketOver90, decimal Total);
public sealed record ApAgingReport(DateOnly AsOf, IReadOnlyList<ApAgingRow> Rows, ApAgingRow Totals);
```
- [ ] **B3. Interface** (`IApAgingService.cs`):
```csharp
public interface IApAgingService
{
    Task<ApAgingReport> GetAsync(DateOnly asOf, long? vendorId, CancellationToken ct);
}
```
- [ ] **B4. Service** (`ApAgingService.cs`) — inject `AccountingDbContext db`, `ITenantContext tenant`. Query `vendor_invoices` where `Status == "Posted"` and outstanding `> 0` and **`CompanyId == tenant.CompanyId`** (mandatory §4.7). Bucket by `(asOf.DayNumber - vi.DocDate.DayNumber)`: `0–30→Current`, `31–60→Bucket31To60`, `61–90→Bucket61To90`, `≥91→BucketOver90`. Group by vendor; build per-vendor rows + a `Totals` row summing all buckets. Optional `vendorId` filter.
- [ ] **B5. Register** in `DependencyInjection.cs` (mirror how outstanding-po's service is registered — grep `OutstandingAsync`/`IPurchaseOrderService` registration; likely `services.AddScoped<IApAgingService, ApAgingService>();`).
- [ ] **B6. Endpoint** in `PurchaseOrderEndpoints.cs` next to `/reports/outstanding-po` (`:67`). `app.MapGet("/reports/ap-aging", …)`, params `asOf` (default `DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))` = Bangkok today) + `long? vendorId`; same auth policy as outstanding-po (`purchase.view` / `.RequireAuthorization(...)` — copy the neighbor's exact policy).
- [ ] **B7. OpenAPI** — add `/reports/ap-aging` path under `paths:` in `docs/api/openapi.yaml`, mirroring `/reports/outstanding-po` request/response.
- [ ] **B8. Tests** (`ApAgingTests.cs`) — `TestIds.VendorCode()` for unique seed:
  - bucket boundaries exactly 30 / 31 / 60 / 61 / 90 / 91 days
  - multi-tenant: Company A VI absent from Company B report
  - partial payment: total 1000, paid 300 → outstanding 700 in correct bucket
  - empty: no posted VIs → empty rows, zero totals
- [ ] **B9. Build 0/0 · tests 2× on `teas_test`** (kill :5080 first).

**Gate B:** build 0/0 · `ApAgingTests` pass 2× · OpenAPI valid · multi-tenant test present.

---

## Phase C — BE: PaperDocumentPdf consolidation (PO + PV) + print-tracking migration  → subAgent3

**Files:** modify `Infrastructure/Purchase/PurchaseOrderService.cs` (PDF builder, `:173 BuildPdfAsync`), `Infrastructure/Purchase/PaymentVoucherService.Read.cs` (PDF builder), `Domain/Entities/Purchase/{PurchaseOrder,PaymentVoucher}.cs`, `Api/Endpoints/PurchaseOrderEndpoints.cs` (`?copy`); create migration; test `tests/Accounting.Api.Tests/Purchase/PurchasePdfTests.cs`. **DO NOT touch `WhtCertificateService.cs` PDF (50 ทวิ stays bespoke).**

**Reference (canonical):** how `Sales/TaxInvoiceService(.Read).cs` builds `PaperDocModel` and calls `Pdf.PaperDocumentPdf.Render(m)`. `PaperDocModel` shape (`Infrastructure/Pdf/PaperDocModel.cs:53-67`):
```csharp
record PaperDocModel(string DocType, string DocTypeEn, string DocNo, DateOnly IssueDate,
    PaperSeller Seller, PaperCustomer Customer, IReadOnlyList<PaperLine> Items, PaperSummary Summary,
    PaperSignRoles SignRoles, DateOnly? ValidUntil, string? ValidUntilLabel,
    string? AmountWords, string? Notes, PaperWatermark? Watermark);
```

- [ ] **C0. Read** `PaperDocModel.cs` + the Sales TI PDF builder fully before editing. If PV needs a section the model lacks (e.g. "ใบกำกับภาษีอ้างอิง" list / WHT-deducted "จ่ายสุทธิ" foot row), **extend `PaperDocModel`/`PaperSummary`/`PaperSignRoles`** rather than forking.
- [ ] **C1. Print-tracking migration.** Add `public DateTimeOffset? OriginalPrintedAt { get; set; }` + `public int PrintCount { get; set; }` to `PurchaseOrder` and `PaymentVoucher` (mirror `TaxInvoice.cs:109-110`). Build solution FIRST, then from `W:`: `dotnet ef migrations add AddPrintTrackingToPurchaseChain --project src\Accounting.Infrastructure --startup-project src\Accounting.Api` (WITH build — never `--no-build`). Review the generated `Up`/`Down` (additive columns only, no data loss). Apply: `dotnet ef database update …`. Commit migration files together with the entity edits (Ham commits).
- [ ] **C2. PO PDF → `PaperDocModel`.** In `PurchaseOrderService.BuildPdfAsync`, build a `PaperDocModel` from the PO entity: `DocType="ใบสั่งซื้อ"`, `DocTypeEn="Purchase Order"`, lines from PO lines, two-box `SignRoles` (ผู้ขออนุมัติ / ผู้อนุมัติ), `Watermark = copy ? "สำเนา" : "ต้นฉบับ"`. Replace the bespoke QuestPDF body with `return Pdf.PaperDocumentPdf.Render(m);`.
- [ ] **C3. PV PDF → `PaperDocModel`.** Same in `PaymentVoucherService.Read.cs` PDF builder: `DocType="ใบสำคัญจ่าย"`, `DocTypeEn="Payment Voucher"`, three-box `SignRoles` (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน), foot includes WHT-deducted "จ่ายสุทธิ".
- [ ] **C4. `?copy` param + tracking.** In `PurchaseOrderEndpoints.cs`, add `bool? copy` to the PO `/pdf` and PV `/pdf` handlers (bind as `bool?` — FE sends `?copy=true`, per cont.70 the string `1` 400s). On first original print, set `OriginalPrintedAt`/increment `PrintCount` (reuse/mirror `Sales/PrintTrackingService.cs` logic; extend it to Purchase entities or add a small Purchase equivalent — pick the smaller diff).
- [ ] **C5. Tests** (`PurchasePdfTests.cs`) — PO + PV render without exception for Draft/Approved/Posted × single-line/multi-line × with-WHT/without-WHT (PV). Assert returned bytes length > 1024 and content `application/pdf` at the endpoint. Existing TI/CN/DN PDF tests must still pass.
- [ ] **C6. Build 0/0 · all PDF tests 2× · eyeball one PO + one PV PDF** (size > 1KB, `application/pdf`).

**Gate C:** build 0/0 · migration generated-with-build + reviewed + applied · PDF tests pass · no TI/CN/DN regression.

---

## Phase D — FE: PaperDocument + DocumentChain + PrintMenu adoption  → subAgent4

**Files:** `app/(dashboard)/{purchase-orders,vendor-invoices,payment-vouchers,wht-certificates}/[id]/page.tsx` + the four list `page.tsx`; `components/doc/DocumentChain.tsx`, `components/ui/PrintMenu.tsx`, `components/doc/ChainRowPrint.tsx`; `components/ui/StatusBadge.tsx`; `lib/types.ts`, `lib/queries.ts`, `messages/{th,en}.json`.

- [ ] **D0. Read first (do NOT pre-assume the diff):** `components/doc/DocumentChain.tsx` (its doctype union), `components/ui/PrintMenu.tsx` + `ChainRowPrint.tsx` (doctype prop), `components/ui/StatusBadge.tsx` (status→Thai map), and ONE Sales detail page that already uses all three (e.g. `tax-invoices/[id]/page.tsx`) as the adoption template.
- [ ] **D1. Extend doctype enums** in `DocumentChain.tsx` + `PrintMenu.tsx` + `ChainRowPrint.tsx` to add `PO | VI | PV | WHT` to the existing `Q|SO|DO|INV|TI|RC|CN|DN` union, with Thai labels.
- [ ] **D2. Backend chain coverage.** Verify `Api/Endpoints/DocumentCrossRefEndpoints.cs` (+ its service) returns Purchase doctypes for `GET /documents/chain?docType=PO&id=…`. If Sales-only, extend the cross-ref service to resolve PO→VI→PV→WHT (mirror the Sales chain resolver). *(If this turns out non-trivial, flag to main agent — may split to a sub-task.)*
- [ ] **D3. PO/VI/PV/WHT detail pages** — wrap body in `<PaperDocument>`, add `<DocumentChain>` panel (vertical, like Sales), wire `<PrintMenu>` ("พิมพ์ต้นฉบับ"/"พิมพ์สำเนา" → `/{doc}/{id}/pdf?copy=true|false`). **Never `window.print()` the HTML (BUG #SR8).** WHT detail: chain + PrintMenu uses the existing bespoke 50ทวิ PDF endpoint (no PaperDocument body change required beyond chain/print).
- [ ] **D4. Posted = read-only** — confirm no edit fields enabled when `status==="Posted"` (§4.2).
- [ ] **D5. List pages** — `<MascotGreeting>` empty state, `<FilterBar>` (status chips + vendor + date range), `<StatusBadge>` Thai labels (ร่าง/อนุมัติแล้ว/ส่งแล้ว/ปิดแล้ว/ยกเลิก/จ่ายแล้ว), column headers via `useTranslations()` — no hardcoded EN.
- [ ] **D6. Gate D:** `cd frontend; node node_modules\next\dist\bin\next ...` — run `tsc --noEmit` → **0**; `next build` → **0/0** from the **NATIVE frontend path, NOT `U:\frontend`** (subst breaks webpack — Sprint 13j-FE gotcha §39). Stop `next dev` before building.

**Gate D:** tsc 0 · next build 0/0 (native path) · all four detail pages render PaperDocument + chain + PrintMenu.

---

## Phase E — FE: AP Aging report page  → subAgent5

**Files:** create `app/(dashboard)/reports/ap-aging/page.tsx`; modify `lib/queries.ts`, `lib/types.ts`, `messages/{th,en}.json`, `components/app-shell/SidebarNav.tsx`.

- [ ] **E0. Read** `app/(dashboard)/reports/outstanding-po/page.tsx` + its hook `useOutstandingPo` (`lib/queries.ts:985`) as the mirror template.
- [ ] **E1. Types** (`lib/types.ts`) — `ApAgingRow`, `ApAgingReport` matching the BE DTOs (Phase B2).
- [ ] **E2. Hook** (`lib/queries.ts`) — `useApAgingReport(asOf, vendorId?)` calling `apiGet<ApAgingReport>('reports/ap-aging?…')` (mirror `useOutstandingPo`).
- [ ] **E3. Page** — table: vendor name + tax ID + 4 buckets + Total column + Totals row; filters as-of date (default today Asia/Bangkok) + `<VendorSelector>` (optional); CSV export (copy outstanding-po pattern); `<MascotGreeting>` empty state.
- [ ] **E4. Nav** — add ap-aging entry under the reports section of `SidebarNav.tsx` (Thai "รายงานยอดเจ้าหนี้ค้างชำระ"). **Do NOT touch Purchase menu items or Settings route (Ham locked).**
- [ ] **E5. i18n** — labels in `messages/th.json` + `en.json` (TH primary).
- [ ] **E6. Gate E:** tsc 0 · next build 0/0 (native path) · page loads on demo data.

---

## Phase F — FE: Bug pass + PO form lift  → subAgent6

**Files:** `app/(dashboard)/purchase-orders/new/page.tsx`; create `app/(dashboard)/settings/expense-categories/page.tsx`; audit `vendor-invoices/new`, `payment-vouchers/new`, `purchase-orders/new` + list pages.

- [ ] **F0. Read** `vendor-invoices/new/page.tsx` as the "VI quality" target (LineItemsTable usage).
- [ ] **F1. PO `/new` lift.** Replace the 1-line form (hardcoded `taxCodeId:1, taxCode:'VAT7', taxRate:0.07` at `:36`) with: `<LineItemsTable>` multi-line, `<ProductPicker>` per line (free-text fallback OK per Plan §7), real VAT-code selector from `tax.tax_codes`, discount-percent per line, real toast errors (no generic "เกิดข้อผิดพลาด" — BUG #SR9). Keep submit→detail redirect (already correct).
- [ ] **F2. Expense Category list** — read-only `settings/expense-categories/page.tsx` showing the 19 seeded `sys.expense_categories` (mirror `settings/wht-types`). No CRUD.
- [ ] **F3. Toast Thai-only audit** — grep the three `/new` pages for English fallback error strings → replace with `useTranslations('common').error` / specific keys.
- [ ] **F4. Column-header audit** — replace any hardcoded EN in list pages with i18n.
- [ ] **F5. Gate F:** tsc 0 · next build 0/0 (native) · PO/VI/PV/WHT/AP-Aging reachable, no console errors.

---

## Phase G — E2E + final consolidated gate  → subAgent7 + main agent

- [ ] **G0. Verify** `frontend/e2e/` exists and the Sales spec runs locally. If only manual Chrome MCP exists, downgrade the "Sales regression" item to a manual revalidate note (don't promise a CI gate that isn't there).
- [ ] **G1. `e2e/purchase-chain.spec.ts`** — demo-admin login → create PO multi-line → Approve → MarkSent → create VI from PO (lines pull) → ClaimPeriod → Post → create PV from VI → Approve → Post → assert WHT cert generated (WHT > 0) → `/reports/ap-aging` shows zero outstanding for that vendor → each detail page shows PaperDocument + DocumentChain + PrintMenu. Use `e2e/helpers/test-ids.ts`.
- [ ] **G2. Existing Sales E2E still passes** (or manual revalidate per G0).
- [ ] **G3. Final consolidated gate (main agent runs):**
  - `dotnet build W:\Accounting.sln` 0/0
  - all BE tests pass **2× consecutive** on `teas_test` (`TEAS_TEST_PG`)
  - `tsc --noEmit` 0 · `next build` 0/0 (native path)
  - OpenAPI updated (`/reports/ap-aging` + `?copy`)
  - no `inventory.*` artifact · no edit/delete of posted docs · no `audit.activity_log` delete
  - **NO git commit**
- [ ] **G4. Write `docs/Report-Backend35.md`** (per Answer-Sana-Backend30 §6): Phase A–G status, gate evidence (paste counts/build output), migration list (expect 1: `AddPrintTrackingToPurchaseChain`), the 4 deviations (D1–D4), new gotchas, files touched grouped by phase, draft `progress.md` cont.71 text, `Question-Backend{N+1}` open items.
- [ ] **G5. Prepend `progress.md` cont.71** + tick **Sprint 13j-PURCH ☑** in `plan.md`.

---

## Self-review coverage check (spec → task)

| Requirement (Req §/Answer §) | Covered by |
|---|---|
| Purchase audit hooks (Req §4.5 / Ans Phase A) | Phase A (incl. D3 WHT hook) |
| AP Aging report + endpoint (Req §4.2 / Ans Phase B) | Phase B (+ D1, D2) |
| PO+PV PaperDocumentPdf consolidation (Req §4.6 / Ans Phase C) | Phase C |
| Print tracking on Purchase (Req §4.3) | Phase C C1 (D4) |
| FE PaperDocument/Chain/PrintMenu (Req §4.1, §4.3 / Ans Phase D) | Phase D |
| AP Aging FE page (Req §4.2 / Ans Phase E) | Phase E |
| PO form lift + Expense-cat list + Thai audit (Req §4.4 / Ans Phase F) | Phase F |
| E2E + parity + no regression (Req §4.7 / Ans Phase G) | Phase G |
| WHT 50ทวิ PDF stays bespoke (Req §4.6 / Ans DO-NOT) | excluded everywhere ✓ |
| No Goods Receipt / inventory (Req §0,§6) | no inventory schema in any phase ✓ |
| Multi-tenant on AP Aging (Req §5 / Ans rail 7) | Phase B B4 + B8 test |
| Posted immutable / read-only UI (Ans rail 1,11) | Phase D D4 |

---

## Operating model (CLAUDE.md §7)

- One **subagent per phase**, **sequential** (A→B→C share Purchase services + DI; D→E→F share frontend `queries.ts`/`messages`/components → never parallel).
- Each subagent: COLD start → its `purchase-subagents/subAgent{n}Task.md` carries the full §6 env briefing + spec refs + verification gate + skill/MCP allocation. **Subagents self-gate, do NOT commit.**
- Main agent verifies each phase gate before dispatching the next, runs the final consolidated gate, then hands to Ham for commit.
- Tracking: `progressPurchase.md` (phase status), `bugPurchase.md` (bugs found mid-sprint), `progressValidation.md` (gate evidence per phase).
