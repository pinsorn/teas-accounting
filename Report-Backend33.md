# Report-Backend33 — Sprint 13i FINAL completion (16/16)

**Owner:** Claude Code
**Date:** 2026-05-21 (cont. 60)
**Supersedes:** Report-Backend32 (partial, 13/16). Sprint 13i is now **fully shipped + verified-live**.
**Spec:** `docs/Answer-Sana-Backend28.md` (C3/C5/C7 sections).

---

## What landed this tail

The three handed-off phases — done in the mandated order **C7 → C5 → C3**.

### C7 — BN ↔ TI dedicated join table + multi-TI picker
- **Migration** `20260521120000_AddBillingNoteTaxInvoiceJoinTable`: creates
  `sales.billing_note_tax_invoices(billing_note_id, tax_invoice_id, company_id, applied_amount DECIMAL(19,4))`
  — composite PK `(billing_note_id, tax_invoice_id)`, FK cascade→billing_notes / restrict→tax_invoices,
  index on tax_invoice_id. **Drops** `billing_notes.tax_invoice_ids bigint[]`.
- **RLS** via new SqlScript `323_billing_note_tax_invoices_rls.sql` (mirrors 322; `company_isolation`
  policy on `company_id`). Entity is `ITenantOwned` → EF global query filter (belt-and-braces, CLAUDE.md §4.7).
- **Entity**: `BillingNote.TaxInvoiceIds` removed; `ICollection<BillingNoteTaxInvoice> TaxInvoiceLinks` added.
- **Service rewrites**:
  - `BillingNoteService.{CreateDraft,UpdateDraft,Get}` — links built via `BuildTaxInvoiceLinksAsync`
    (resolves requested TI ids scoped to tenant; `applied_amount` defaults to the TaxInvoice total at
    link time; TIs outside the tenant silently skipped). `Get` projects `taxInvoices` from the join
    (incl. TI `docNo` for chips).
  - `DocumentCrossRefService.GetForTaxInvoiceAsync` — `b.TaxInvoiceIds.Contains(id)` →
    `b.TaxInvoiceLinks.Any(j => j.TaxInvoiceId == id)`.
  - `ReceiptService.PostAsync` C6 BN auto-settle — rewired to the join (`TaxInvoiceLinks.Any` + per-BN
    `BillingNoteTaxInvoices.Where(...).Select(TaxInvoiceId)`).
- **DTO**: `BillingNoteDetail.TaxInvoiceIds (long[])` → `TaxInvoices (IReadOnlyList<BillingNoteTaxInvoiceRef>)`
  where `BillingNoteTaxInvoiceRef = {TaxInvoiceId, DocNo, AppliedAmount}`. `CreateBillingNoteRequest`
  still takes `long[]? TaxInvoiceIds` (FE sends ids; applied_amount derived server-side).
- **FE**: `BillingNoteForm` multi-TI picker — reuses `TaxInvoicePicker` via composition (each pick appends
  a chip; customer-scoped, `status="Posted"`, disabled until a customer is chosen; chips with × remove).
  BN detail renders chips from `d.taxInvoices`. `lib/types.ts` updated. i18n `billingNote.taxInvoices` +
  `pickCustomerFirst` (th/en).
- **E2E**: `billing-note-flow.spec.ts` extended — group ≤2 posted TIs → chip count → detail chips
  (tolerant of seed depth; skips if customer has no posted TI).

**Design decision (was the only Answer-28 ambiguity):** `applied_amount` is **persisted on write**
(the column is explicitly in the spec's table def), defaulting to the linked TaxInvoice's total at link
time. The FE picker captures TI selection only (no per-TI amount), so server-derives the default. No
Question-Backend16 filed — the schema fixed the storage question; the default is the defensible v1 choice.

### C5 — product_type NOT NULL hardening
- **Migration** `20260521120500_HardenLineItemProductTypeNotNull`: idempotent backfill
  `UPDATE sales.<t> SET product_type='GOOD' WHERE product_type IS NULL` on all 5 line tables, then
  `AlterColumn NOT NULL ×5` (quotation/sales_order/delivery_order/tax_invoice/billing_note `_lines`).
  No DB default added (snapshot has none) — services always supply a value.
- Entities: `string ProductType = "GOOD"` (non-nullable) ×5; EF `.IsRequired()` ×5.
- Service defaults: `BillingNoteService.ApplyLines` + `TaxInvoiceService` coalesce `?? "GOOD"`; 6
  cascade/create write sites (QuotationChain ×2, SalesOrderDelivery ×3, TaxInvoice ×1) coalesced after
  the nullable-reference compiler (warnings-as-errors) flagged them.

### C3 — status + BU + customer + date filters on 8 list pages
- New shared `components/ui/ListFilters.tsx` (URL-persisted `?status=&bu=&customerId=&dateFrom=&dateTo=`)
  + `lib/list-filter.ts` `applyListFilters` (generic client-side predicate).
- Wiring: **Q/SO/DO/BN** full client-side (non-paginated lists); **TI** server-side paginated filters
  (now URL-driven, incl. customerId); **RC/CN/DN** BU server-side + status/customer/date client-side on
  loaded rows.
- DTO additions: `customerId`+`businessUnitId` added to `TaxInvoiceListItem`, `ReceiptListItem`,
  `AdjustmentNoteListItem`, `BillingNoteListItem` (+ projections); FE Q/SO/DO list types gained the
  fields (BE already sent them).
- **Sprint 13j flag:** paginated lists (TI/RC/CN/DN) client-filter only the loaded page — acceptable for
  v1 small data per Answer-28's "(b)"; revisit (server-side params) if any list exceeds ~1000 rows.

---

## Verification (cont. 60)

| Check | Result |
|---|---|
| `dotnet build Accounting.Api.csproj` | 0 err / 0 warn |
| `dotnet ef migrations add _SnapshotVerify --no-build` | **empty Up()/Down()** → hand-written migrations + snapshot match model exactly (throwaway deleted) |
| `dotnet ef database update --no-build` | both migrations applied clean to `accounting_dev` |
| `dotnet test Accounting.Domain.Tests` | 89 / 89 |
| `tsc --noEmit` (frontend) | 0 |
| API `/health` :5080 | 200 (DbInitializer ran SqlScript 323 on startup) |
| psql `accounting_dev` | join table 4×NOT NULL; RLS `company_isolation`; `tax_invoice_ids` dropped; `product_type` NOT NULL ×5 |

**Toolchain note:** Built/migrated by stopping the running API (user authorized; Sonnet's testing paused),
then `dotnet run --no-build` on :5080. The C7/C5 migrations were hand-written (mirroring `AddBillingNotes`)
and proven byte-correct via the empty-diff `migrations add` check — §29 R-Q1a remains the safe default
when the API can't be stopped.

---

## → Sana doc-routing (binding ownership rule — Answer-28)

- **`docs/api/openapi.yaml`** — BN detail response: `taxInvoiceIds: number[]` → `taxInvoices: array of
  { taxInvoiceId: integer, docNo: string|null, appliedAmount: number }`. `POST /billing-notes` request
  still carries `taxInvoiceIds: number[]` (ids only; applied_amount server-derived).
- **`docs/accounting-system-plan.md` §6** — document BN multi-TI grouping via the
  `sales.billing_note_tax_invoices` join table (replaces the `bigint[]` column); note the
  `applied_amount = TI total at link time` v1 rule + that auto-settle (C6) and cross-ref both read the join.
- **`docs/runtime-gotchas.md`** — optional new entry: "verify hand-written EF migrations + snapshot with a
  throwaway `migrations add` empty-diff check" (technique, not a defect). No compliance gotcha surfaced.
- **Chapter 3 manual** — stays deferred per CLAUDE.md §16 until ALL 4 sub-sprints ship + Sana RE-VALIDATE
  deep-mode green on each.

## → Sana RE-VALIDATE (deep mode)

Sprint 13i is 16/16. Resume the deep-mode pass from batch 1: properly extend categories 1-6 + 9; flag
7/8/10/11/12/13 for Sprint 13k/13L. New/changed surfaces to exercise:
- BN multi-TI picker (customer-scoped, Posted-only) → chips + × remove → save → detail chips → cross-ref
  panel both directions (TI detail shows the BN; BN detail shows the TIs).
- product_type NOT NULL: create Q/SO/DO/TI/BN line with no product picked → expect silent GOOD default
  (no 500, no validation wall).
- 8 list pages: status/BU/customer/date filter bar → URL persistence → refresh + share-link safe.

## Next

Sprint 13j (Print/PDF revamp — Answer-29) lands only AFTER Sana RE-VALIDATE deep-mode green on 13i.
