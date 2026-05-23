# Report-Backend31 — Sprint 13h **CHECKPOINT 2** (P9 + P6.1 + P7 BE + P5 + P3 + P4 BE shipped; P6.2/P8/P10/P11/E2E deferred to checkpoint 3)

**Date:** 2026-05-20 · **Spec:** docs/Answer-Sana-Backend27.md
**Sprint status:** ◐ in-progress — **9 of 13 phase-pieces shipped** (3 from ckpt1 + 6 from this session).
**This is checkpoint 2, NOT the final completion report.** Sprint 13h continues in checkpoint 3 per Session-Resume.md prescribed phase order.

---

## Phases delivered this checkpoint

### P9 — DO Delivered stage (3 → 4 states) ✅
- `Accounting.Domain.Enums.DeliveryOrderStatus` = `{ Draft, Issued, Delivered, Cancelled }` (was `{ Draft, Posted, Cancelled }`).
- `IDeliveryOrderService.PostAsync` split into `IssueAsync` (Draft → Issued, allocate doc_no, no TI) + `MarkDeliveredAsync` (Issued → Delivered, **THIS is where Pattern X fires the linked TI**). `CreateTaxInvoiceAsync` now requires Delivered (was Posted).
- Endpoints `/delivery-orders/{id}/post` → split into `/issue` + `/mark-delivered`.
- Migration `20260520132712_AddDeliveryOrderDeliveredStage` — backfills `POSTED → DELIVERED` via `migrationBuilder.Sql(...)`. Status stored as UPPER string (no column shape change). Applied; dev DB DO #1 verified backfilled.
- `Sprint10ChainTests` updated: chain test now calls `IssueAsync` + `MarkDeliveredAsync`; Pattern Y test asserts `do.ti_exists` after Delivered + manual TI.
- FE: `DeliveryOrderForm` action `'post'` → `'issue'`; detail page `Draft → Issue button` + `Issued → Mark Delivered button` + `Delivered → Create TI (Pattern Y)`. `StatusBadge` MAP += `Issued`, `Delivered`. i18n `deliveryOrder.markDelivered`/`delivered` + `status.Issued`/`Delivered` in TH + EN.

### P6.1 — TI ← Q FK ✅
- `TaxInvoice` entity += `long? QuotationId`. EF Config: `HasOne<Quotation>().WithMany().HasForeignKey(t => t.QuotationId).OnDelete(Restrict)` + filtered index `ix_tax_invoices_quotation_id WHERE quotation_id IS NOT NULL`.
- `CreateTaxInvoiceRequest` += `long? QuotationId = null`; `TaxInvoiceDetail` += `long? QuotationId`. `TaxInvoiceService.CreateDraftAsync` persists it. `GetDetailAsync` surfaces it.
- Migration `20260520133244_AddTaxInvoiceQuotationReference` — nullable column + FK + index. Clean Up()/Down().
- FE: `lib/types.ts` `TaxInvoiceDetail` + `CreateTaxInvoiceRequest` += `quotationId`. Q detail page Accepted state → "สร้างใบกำกับภาษีจาก Q" button → `/tax-invoices/new?fromQuotationId={id}`. TI new page reads URL param, fetches `useQuotation(id)`, prefills customer + lines + BU via `reset()`. TI detail page chip linking back to Q.
- i18n: `quotation.createTaxInvoice`; `ti.detail.fromQuotation`.

### P7 BE — product_type line snapshot (4 line tables) ✅
- 4 line entities += `string? ProductType`: `QuotationLine`, `SalesOrderLine`, `DeliveryOrderLine`, `TaxInvoiceLine`. (Receipt has no product lines — has ReceiptApplication against TIs. TaxAdjustmentNote is header-only. Spec's "RC/CN/DN line" was overstated; verified by entity inspection.)
- EF configs += `b.Property(l => l.ProductType).HasMaxLength(20)` on each.
- DTOs: `ChainLineInput`, `DeliveryLineInput`, `TaxInvoiceLineInput` += `string? ProductType = null`.
- Services snapshot the input value on insert: `QuotationChainServices.CreateDraftAsync` + `ConvertToSalesOrderAsync` (Q→SO cascade); `SalesOrderService.CreateDraftAsync` + `DeliveryOrderService.CreateDraftAsync` (SO/DO inserts) + `CreateDeliveryOrderAsync` (SO→DO cascade) + `GenerateTiAsync` (DO→TI cascade); `TaxInvoiceService.BuildLine`.
- Migration `20260520144906_AddLineItemProductTypeSnapshot` — 4 nullable columns + `DO $migrate_p7$ ... $migrate_p7$` block (pre-flight COUNT + backfill GOOD per Answer-26 pattern). Applied; psql verified all 4 line tables have 100% product_type populated.

### P5 — SO/DO list filters ✅
- `sales-orders/page.tsx` + `delivery-orders/page.tsx` rewritten: filter row with status `<select>` + URL `?status=` persistence (`useSearchParams` + `router.replace`). DO uses 4-state enum (Draft/Issued/Delivered/Cancelled per P9). Refresh + share-link safe.
- BU/customer/date filters deferred — BE list endpoints don't accept them yet (only `status`). Documented in Session-Resume for Sprint 13i.
- `common.all` already exists in TH + EN — no i18n delta.

### P3 — i18n + Thai date (partial) ◐
- `frontend/lib/format/date.ts` NEW: single-source `formatDateTH` + `formatDateTHLong` with `dateStyle: 'medium'` + `calendar: 'buddhist'` + `timeZone: 'Asia/Bangkok'`. Output `"20 พ.ค. 2569"`.
- `frontend/lib/utils.ts` `formatDate` updated to same `dateStyle: 'medium' + calendar: 'buddhist'` — all existing callers get Buddhist Era automatically with no API change.
- i18n: `common.posted` + `common.draftSaved` added (TH + EN). TI new page hard-coded `'Posted'` + `'Draft saved'` toasts replaced with `tc('posted')` + `tc('draftSaved')`.
- Sweep tail deferred to ckpt3: AdjustmentNoteForm RC date label EN (BUG #5); audit every chapter-3 raw toast string.

### P4 BE — Q lifecycle (Edit + Delete; FE edit page deferred) ✅
- `IQuotationService` += `UpdateDraftAsync(long id, CreateQuotationRequest req, ct)` + `DeleteDraftAsync(long id, ct)`. Both Draft-only; 409 with codes `quotation.cannot_edit_after_send` / `quotation.cannot_delete_after_send` otherwise. UpdateDraftAsync recomputes header aggregates from the new line items.
- Endpoints `PUT /quotations/{id}` (FluentValidation + UpdateDraftAsync), `DELETE /quotations/{id}` (DeleteDraftAsync).
- `/cancel` + `/pdf` endpoints already exist from Sprint 10/13e. Verify in checkpoint 3 if `QuotationPdfAsync` body is real vs. stub.
- FE edit page + Draft trash icon + AlertDialog confirm + `useUpdateQuotation`/`useDeleteQuotation` hooks: **deferred to checkpoint 3** (see Session-Resume Deferred section).

---

## Verification (this checkpoint, honest)

| Gate | Result |
|---|---|
| Frontend `tsc --noEmit` | **0** — re-verified after every phase |
| `dotnet build Accounting.sln` | **0 err / 0 warn** (real in-harness via `subst U:`) |
| `dotnet test Accounting.Domain.Tests` | **89 / 89** (no regression from P9 enum / P6.1 FK / P7 line snapshot / P4 service additions) |
| All 3 migrations applied to dev DB | **clean** — verified with `dotnet ef migrations list` showing all as applied |
| Backfill verification (psql) | **DO #1 = `DELIVERED`** (was Posted); **q=1/so=1/do=1/ti=1 line tables 100% product_type populated** |
| New seed | none this checkpoint; existing seed 320 from ckpt1 still applies for RBAC |
| Live UI verification | NOT done — Sana Chrome-MCP channel per CLAUDE.md §16 |

---

## Phases NOT shipped this checkpoint (deferred to checkpoint 3)

| # | Phase | Why deferred | Estimated effort |
|---|---|---|---|
| P6.2 | Billing Note CRUD (new entity) | Entire new entity — Domain + EF config + RLS + migration `AddBillingNotes` + service + endpoints + permissions + FE list/new/detail/form + i18n + StatusBadge `Settled` + E2E spec. **Biggest single phase.** | 5-6 hr |
| P8 | Receipt cleanup + cross-ref | PostConfirmDialog docType prop + i18n; RC post nav; `IDocumentCrossRefService` + `useCrossReferences` hook; chips on TI/RC detail | 2-3 hr |
| P10 | Logo upload + display | Multipart endpoint + attachments parent + FE upload + every doc header + PDF embed via QuestPDF | 3 hr |
| P11 | XML 0-byte fix | Live debug (Tier 1 config + DO→TI pipeline + download endpoint). May surface deeper signing pipeline issue. P9 reshape (TI now fires on Delivered) is a prerequisite — done. | 2 hr |
| E2E | 8 new specs | Authored after BE/FE lands. | 2-3 hr |
| **P4 FE tail** | Q edit page + Delete/PDF buttons + hooks | Deferred from P4 BE — see Session-Resume Deferred section. | 1-2 hr |
| **P7 FE tail** | LineItemsTable readOnly tax_rate; RC WHT auto-base SERVICE-only; AdjustmentNoteForm lock | Deferred from P7 BE — see Session-Resume Deferred section. | 2 hr |
| **P3 tail** | AdjustmentNoteForm date label EN; raw toast sweep | Deferred from P3 — see Session-Resume Deferred section. | 1 hr |

**Total deferred effort estimate:** ~17-20 hr = 2-3 working days. Realistic for checkpoint 3 if context budget holds.

---

## Decisions taken this checkpoint

- **DO Delivered semantic**: Pattern X auto-TI fires on `MarkDeliveredAsync`, not `IssueAsync`. Aligns with Plan §6.4 (recipient confirmation triggers tax point).
- **P7 = string column, not enum**: lowest-risk migration shape; FE/BE both stringly-typed already (`ProductTypeStr` in FE types). Backfill GOOD on all 4 tables.
- **P7 stays nullable**: hardening to NOT NULL after observable backfill is a Sprint 13i candidate.
- **P5 = status-only filter**: BU/customer/date were BE-gated; deferred rather than ship FE-only mismatch.
- **P3 single-source date util**: `dateStyle: 'medium' + calendar: 'buddhist'` everywhere via `formatDate` in `lib/utils.ts` (shadows old `dateFmt`) + new `lib/format/date.ts` for opt-in callers.
- **P4 hard-delete acceptable for Draft Q**: per Plan §17.6 — no doc_no allocated yet, gap rule not violated.
- **EF tooling caveat surfaced**: `--no-build` operations may use stale Api `bin\` copy of Infrastructure DLL → migrations appear missing. Fix is to build Api project explicitly. Documented in Session-Resume + NEXT-SESSION-PROMPT2 + runtime-gotchas candidate §36.

---

## → Sana (proposed deltas; Sana applies after checkpoint 3 final ship)

`plan.md` — Sprint 13h ◐ in-progress: ckpt2 phases ticked per the status table above. P6.2/P8/P10/P11/E2E + small FE tails remain.

`docs/runtime-gotchas.md` —
- §36 NEW (candidate): **EF migration tooling stale-DLL caveat.** After `dotnet ef migrations add ...` succeeds, `migrations list` / `database update` invoked with `--no-build` may report the prior migration set. Cause: Api startup project's `bin\Debug\net10.0\Accounting.Infrastructure.dll` is still the pre-generation copy. Fix: build Api project explicitly between the `add` and the `--no-build` operation. Documented for the workflow.

`docs/accounting-system-plan.md` —
- §6.4: update DO state machine table to 4-state (Draft → Issued → Delivered → Cancelled). Note that Pattern X TI fires on Delivered, not Issued.
- §6: optional addition: TI may reference Q via `quotation_id` (P6.1). Cross-ref UI shows the chip.

`docs/api/openapi.yaml` —
- DO: replace `POST /delivery-orders/{id}/post` with `/issue` + `/mark-delivered`. Status enum 4-state.
- TI: `POST /tax-invoices` body += optional `quotationId` (nullable); `GET /tax-invoices/{id}` response += `quotationId`.
- Q: NEW `PUT /quotations/{id}` (Draft-only edit); NEW `DELETE /quotations/{id}` (Draft-only hard-delete).
- SO/DO list endpoints: clarify `status` query param (status-only filter shipped; BU/customer/date deferred to Sprint 13i).
- All sales line items: response shape += optional `productType` field (snapshot).

`docs/manual/chapters/03-การขาย.md` — unchanged; per CLAUDE.md §16 chapter authoring waits for full sprint ship + Sana RE-VALIDATE deep mode green. Sprint 13h not acceptance-ready until checkpoint 3 ships.

---

## Next session resume

Pick up at: **P6.2 first** (biggest single phase — new entity scaffolding) → **P8** (RC cleanup + cross-ref service) → **P10** (logo upload + doc headers + PDF embed) → **P11** (XML 0-byte live debug). Then **FE deferred tails** (P4 / P7 / P3) interleaved as the same files are touched. Then **E2E specs** (8 chapter-3 scenarios). Final = **Report-Backend31.md** as sprint completion (not another checkpoint).

Session-Resume.md updated this checkpoint with the phase-by-phase status table so the next session sees exactly what's done and what's left.

---

## DoD (checkpoint 2)

✅ P9 + P6.1 + P7 BE + P5 + P3 (partial) + P4 BE shipped + FE tsc 0 + BE build 0/0 + Domain 89/89.
✅ All 3 migrations applied to dev DB + backfill verified via psql.
✅ Session-Resume.md overwritten with ckpt3 phase order + deferred items.
✅ NEXT-SESSION-PROMPT2.md written (= ckpt3 brief).
◐ P6.2 + P8 + P10 + P11 + E2E remain + small FE tails — deferred, documented above.
☐ progress.md cont. 55 — to be prepended next.
☐ Mirror Y:\AccountApp — at session end after progress entry.

**Honest:** Sprint is **not complete** — 9 of 13 phase-pieces landed (3 ckpt1 + 6 ckpt2). Sana cannot yet RE-VALIDATE in deep mode against the full Sprint 13h scope. What she CAN do this checkpoint: re-test the DO 4-state machine (P9), TI ← Q cross-ref (P6.1), SO/DO status filter persistence (P5), Q→TI prefill from Accepted Q (P6.1). All other ckpt1 items (RBAC, picker, `<select>`) still hold.
