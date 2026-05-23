# Receipt itemization + multi-category WHT — Design Spec

> **STATUS: IMPLEMENTED (cont. 66, 2026-05-22).** Full stack shipped + build-green
> (allocator 8/8, Domain 89/89, BE 0/0, FE tsc 0 / next build 0/0, migration applied
> live). See `progress.md` cont. 66. Integration tests (multi-cert post, GL balance)
> pending Postgres — verify live per project pattern.

> Decided by Ham 2026-05-22 (cont. 65). Approved approach **B** (derive line items
> on read; persist only the WHT breakdown). Supersedes the deferred note in
> `docs/sprint-line-product-wht-plan.md` ("receipt stays receipt-level WHT — no
> per-line change needed"): a bill mixing goods + multiple service categories
> cannot withhold one flat rate, so the AR receipt now carries a **per-income-type
> WHT breakdown**, mirroring the PV (AP) side.

## Problem

A Receipt today (`Receipt` + `ReceiptApplication`) stores only `{TaxInvoiceId,
AppliedAmount}` and a **single** header-level WHT (`WhtAmount`, `WhtTypeId`, one
`CustomerWhtCertNo`). Two defects:

1. **No line items** — the receipt PDF synthesizes one row per applied Tax Invoice
   (`"ใบกำกับภาษี <docNo>" → amount`); it does not show the goods/service lines the
   customer actually paid for.
2. **Single WHT rate for the whole bill is wrong** — one invoice can mix goods (no
   WHT), service 3% (SVC), rent 5% (RENT), ads 2% (ADS). Withholding differs per
   income category; a flat 3% over the whole bill is non-compliant.

## Decisions (locked with Ham)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Receipt line items source | **Derive from the applied Tax Invoice(s)** on read (TIs are immutable per CLAUDE.md §4.2 → safe). No `ReceiptLine` table. |
| 2 | WHT base per category under partial payment | **Pro-rata** by applied fraction across that TI's lines, grouped by income category. |
| 3 | 50ทวิ shape | **One customer cert number → multiple income-type rows.** `CustomerWhtCertNo` stays on the Receipt header (shared); per-category breakdown persists as `ReceiptWhtLine` rows; POST creates one `WhtCertificate` Direction='R' per income type, all sharing that cert no. |
| 4 | WHT on the receipt PDF | **Not shown.** Receipt face lists line items + TI no(s) in notes only. WHT recorded in DB + 50ทวิ (keeps cont. 64 behavior). |

## Approach B (chosen) — persist WHT breakdown, derive lines on read

- **Persist** the compliance-critical WHT breakdown (`ReceiptWhtLine`): the customer's
  actual withholding by income type at the moment of payment is a recorded fact and
  must survive even though it is computed by pro-rata.
- **Derive** display line items on read from the applied TIs. TIs are immutable, so
  the derivation is stable and reproducible. Avoids a second snapshot table + its
  migration/RLS/duplication.

Rejected **Approach A** (persist a `ReceiptLine` snapshot too): more schema (2 tables),
line duplication from immutable TIs, no compliance benefit here.

## Data model

### NEW entity `ReceiptWhtLine` (`Domain/Entities/Sales/ReceiptWhtLine.cs`)

```
ReceiptWhtLineId  long   (PK)
ReceiptId         long   (FK → Receipt, cascade)
WhtTypeId         int    (FK → WhtType)
IncomeTypeCode    string (snapshot of WhtType.IncomeTypeCode)
WhtTypeCode       string (snapshot of WhtType.Code, e.g. "SVC")
WhtRate           decimal(snapshot, e.g. 0.03)
BaseAmount        decimal(ex-VAT service base for this category)
WhtAmount         decimal(= round(BaseAmount * WhtRate, 2, AwayFromZero))
```

- `Receipt.WhtLines : ICollection<ReceiptWhtLine>` added.
- `Receipt.WhtAmount` (header) = `Σ WhtLines.WhtAmount` (kept; GL + list still read it).
- `Receipt.WhtTypeId` (header, legacy single) → **kept nullable for back-compat**, set
  to the single line's type when exactly one category, else `null`. Read side stops
  depending on it for the breakdown.
- `CustomerWhtCertNo` / `CustomerWhtCertDate` stay on the header (one shared cert no).

### EF + DB
- `ReceiptWhtLineConfiguration` — table `sales.receipt_wht_lines`, decimals `numeric(18,4)`,
  FK to receipt (cascade) + WhtType (restrict). Index `ix_receipt_wht_lines_receipt_id`.
- `DbSet<ReceiptWhtLine> ReceiptWhtLines` on `AccountingDbContext`.
- Migration `AddReceiptWhtLines` (EF). RLS: raw SQL in `Migrations/SqlScripts/` enabling
  RLS + `company_id` policy — **but `receipt_wht_lines` has no `company_id`**; it is
  tenant-scoped transitively via `receipt_id`. Follow the existing child-table pattern
  (`receipt_applications`): check whether `receipt_applications` has RLS/`company_id`;
  mirror exactly. (Implementation step verifies the sibling pattern before writing.)

## WHT computation — pro-rata allocation (the core, pure & unit-tested)

A pure function (no DB), in `Application/Sales` or `Domain`, unit-tested without Postgres:

```
Input per applied TI:
  appliedAmount         (incl-VAT portion paid toward this TI)
  tiTotalAmount         (TI.TotalAmount, incl VAT)
  lines: [{ lineAmount(ex-VAT), productType, whtTypeId? }]   // whtTypeId from Product.DefaultWhtTypeId
Output:
  list of { whtTypeId, baseAmount } grouped across all applied TIs
```

Algorithm:
1. For each applied TI: `f = appliedAmount / tiTotalAmount` (clamp; if tiTotal=0 → f=0).
2. For each line that is a **service** (`productType ∈ {SERVICE, EXEMPT_SERVICE}`):
   resolve its `whtTypeId` (below); if resolved, `categoryBase += lineAmount * f`,
   keyed by `whtTypeId`.
   - Goods / exempt-goods → excluded (no WHT).
   - Service line with no resolvable category (no product WHT type **and** no customer
     fallback — e.g. a B2C individual) → excluded from auto-suggest; the user may add a
     category manually. Documented; not silently bucketed under a wrong rate.
3. Round each category base to 2dp (AwayFromZero). `whtAmount = round(base * rate, 2)`.

**Resolving `whtTypeId` per service line (priority order):**
`TaxInvoiceLine` snapshots only `ProductType` (string), not the WHT type, so:
1. `TaxInvoiceLine.ProductId → Product.DefaultWhtTypeId` (the per-line category — primary).
2. **Fallback** (line has no product, or the product has no `DefaultWhtTypeId`): the
   customer's `DefaultWhtTypeId`, else the SVC-3% heuristic for `Corporate` customers
   (preserves the pre-existing single-category behavior for ad-hoc service lines so this
   change never regresses the common case).
3. No fallback resolvable (individual / no default) → excluded (no auto WHT).
Only active, in-force `WhtType`s qualify.

`whtOn` master toggle OFF (individuals / under-threshold) → zero WHT lines, no cert.

## Service layer changes (`Accounting.Infrastructure/Sales/ReceiptService*.cs`)

### `SuggestWhtBaseAsync` → returns a per-category breakdown
- New DTO `WhtBaseSuggestion` (or a new `WhtCategorySuggestion` list field) carrying
  `categories: [{ whtTypeId, code, nameTh, rate, base, amount }]` plus the existing
  `serviceSubtotal/goodsSubtotal` totals (keep for the summary line).
- Now **pro-rata aware**: takes the applied amounts (not just TI ids) so the suggested
  base reflects partial payment. Signature gains the applied amounts (or accepts
  `IReadOnlyList<ReceiptApplicationInput>`).
- Old single-suggestion fields retained additively where cheap, or the FE migrates to
  the list (FE is the only consumer).

### `CreateReceiptRequest` / `CreateDraftAsync`
- Replace single `WhtAmount`/`WhtTypeId` inputs with
  `IReadOnlyList<ReceiptWhtLineInput> WhtLines` (each `{WhtTypeId, BaseAmount}`; rate +
  amount computed server-side from the in-force `WhtType`). Keep `CustomerWhtCertNo/Date`.
  - Back-compat: keep the old scalar fields as optional; if `WhtLines` empty but legacy
    `WhtAmount>0` provided, synthesize a single line (so existing API callers/tests don't
    break). Validator updated accordingly.
- Build `Receipt.WhtLines`; set header `WhtAmount = Σ`, `WhtTypeId = single? : null`.
- Validation: each `WhtTypeId` active & in-force; `Σ WhtAmount ≤ Amount + 0.01`.

### `PostAsync`
- Loop `rc.WhtLines` → one `WhtCertificate` Direction='R' per line, all sharing
  `CustomerWhtCertNo` (only when the cert no is present; else deferred — same as today).
- `CashReceived = Amount - Σ WhtAmount` (unchanged formula, sum now).

### `SetWhtCertAsync` (deferred cert entry)
- When the cert no arrives later: create the Direction='R' certs for **all** `WhtLines`
  (not just one), sharing the cert no. Idempotent (skip if any R cert already exists).

### `GetDetailAsync` / `BuildPdfAsync` (read)
- `ReceiptDetail` gains: `WhtLines: [{code, nameTh, rate, base, amount}]` **and**
  `Lines: [{descriptionTh, productType, quantity, unitPrice, lineAmount, tiDocNo}]`
  derived from the applied TIs' `TaxInvoiceLine`s (with the TI docNo per line for the note).
- PDF: render the derived **line items** (goods/service) as `PaperLine`s; put the applied
  TI number(s) into `Notes`; **WHT not rendered** (decision #4). Summary = `Amount` (no VAT
  row — receipt settles already-VAT'd TIs).

### GL posting (`IGlPostingService.PostReceiptAsync`) — VERIFY
- Today posts WHT using header `WhtAmount` (+ maybe a single WHT payable account). With
  multiple categories the credit may need to split by each `WhtType.DefaultPayableAccountId`.
  **Implementation step reads `PostReceiptAsync` first**; if it keys off a single
  `WhtTypeId`/account, refactor to post one WHT-receivable GL line per `ReceiptWhtLine`
  (grouped by account). This is compliance-affecting — keep the GL balanced
  (Dr Cash + Dr WHT-receivable(s) = Cr AR). Covered by the existing GL balance assertions.

## Frontend (`frontend`)

- `lib/types.ts`: `ReceiptDetail` gains `whtLines` + `lines`; `WhtBaseSuggestion` gains
  `categories`.
- `lib/queries.ts`: `useWhtBaseSuggest` passes applied amounts; create payload sends
  `whtLines`.
- `app/(dashboard)/receipts/new`: replace the single WHT type/base/rate block with a
  **per-category WHT table** (auto-filled from the suggest call, each row base editable;
  rate read-only from type; amount computed). Keep the "ลูกค้าหัก ภาษี ณ ที่จ่าย" toggle
  (off → no rows). Show the derived line items (read-only preview) so the user sees
  goods vs service.
- Receipt detail page: show the WHT breakdown table (was single line).
- i18n th/en for the new labels.

## Compliance / testing

- **Pure unit tests** (no DB, runnable in this env) for the pro-rata allocator:
  - mixed goods + service single category, full payment
  - mixed multiple service categories (RENT 5% + SVC 3%) + goods, full payment
  - **partial** payment → bases scale by fraction; goods still excluded
  - tiTotal = 0 guard; service line w/o whtType excluded; whtOn off → empty
- **Integration tests** (written, `Skip` in this env — no Postgres, per existing pattern):
  - post receipt with 2 WHT categories + cert no → 2 `WhtCertificate` R rows share the no
  - deferred cert: post w/o cert → 0 R rows; `SetWhtCertAsync` → N rows
  - GL balanced with split WHT-receivable
- **Verify gate:** BE build 0/0 · `dotnet test` Domain ≥89 (+ new allocator tests) ·
  FE `tsc` 0 · `next build` 0/0 · EF migration generated + reviewed (RLS sibling pattern) ·
  missing-cert report (`/reports/wht-receivable-missing-cert`) still works (reads
  `CustomerWhtCertNo`).

## Out of scope (YAGNI)
- A `ReceiptLine` snapshot table (Approach A).
- Per-line WHT override on the *document* lines (WHT stays a receipt-side, category-level
  concept; the customer issues one 50ทวิ).
- Changing the TI/Quotation faces (WHT is not printed there — only a static note).

## Ordered implementation steps
1. Pure pro-rata allocator + unit tests (TDD, red→green). No DB.
2. Domain `ReceiptWhtLine` + `Receipt.WhtLines`.
3. EF config + DbSet + migration `AddReceiptWhtLines` + RLS (mirror `receipt_applications`).
4. Application DTOs (`ReceiptWhtLineInput`, suggestion `categories`, `ReceiptDetail` lines+whtLines) + validator.
5. `ReceiptService`: SuggestWhtBase (pro-rata categories), CreateDraft (build lines),
   Post (loop certs), SetWhtCert (all lines), Read (derive lines + whtLines + PDF).
6. GL `PostReceiptAsync` review/refactor for split WHT (keep balanced).
7. Endpoints request/response mapping.
8. FE types/queries/receipts-new table/detail/i18n.
9. Build gates (BE 0/0, Domain tests, FE tsc 0, next build 0/0) + progress.md/plan.md.
```
