# Report-Backend32 — Sprint 13i (Bug fix + UX cleanup) — PARTIAL ship

**Author:** Claude Code · **Date:** 2026-05-21 (cont. 59)
**Spec:** `docs/Answer-Sana-Backend28.md` (17-phase list; 16 discrete tasks B1–B7, C1–C7, R5, L1)
**Outcome:** **13 of 16 phases shipped + verified-live.** 3 handed off (C3, C5, C7).

---

## Verification (gate)

| Check | Command | Result |
|---|---|---|
| BE build | `dotnet build src/Accounting.Api/Accounting.Api.csproj` (subst U:) | **0 err / 0 warn** |
| Domain tests | `dotnet test Accounting.Domain.Tests` | **89 / 89** |
| FE typecheck | `node node_modules\typescript\bin\tsc --noEmit` (U:\frontend) | **0** |
| i18n JSON | `JSON.parse` th.json + en.json | OK |
| API live | `dotnet run --no-build` :5080 → `/health` | **200** |
| Seed 330 | psql `sys.applied_sql_scripts` + grant count | applied; ACCOUNTANT 3/3 read grants |
| product_type backfill | psql NULL count ×5 line tables | **NOT 0** (q_lines=1, bn_lines=2) → blocks C5 |

**§29/§36 note:** in-session .NET build works via `subst U:` short path. The only
blocker was the running API holding `Accounting.Api.exe`; sequence is stop → build
→ migrate → restart. No `--no-build` migration shortcut taken.

---

## Shipped (13)

**Bug block B1–B7 (the priority — ship first):**
- **B1** seed `330_seed_receipt_adjnote_rbac.sql` (idempotent, ON CONFLICT): 3 new read perms `sales.{receipt,credit_note,debit_note}.read` + read tier (6 roles) + create/post tier (4 roles). `Permissions.cs` +3 consts +All. `ReceiptEndpoints` GET→`ReceiptRead`. `TaxAdjustmentNoteEndpoints.CanRead` +read perms. Verified live.
- **B2** `QueryStateRow` (new export in `QueryState.tsx`) — table-row state for loading/403→"ไม่มีสิทธิ์เข้าถึง"/error/empty + 401 redirect. Root cause: 8 sales lists hand-rolled `<tr>` and bypassed the existing `QueryState` 403 branch. Wired into all 8.
- **B3** lookup-on-mount in `CustomerSelector` + `VendorSelector` (resolve prefilled id → label via GET /{resource}/{id}).
- **B4** `lib/forms.ts` (`onInvalidSubmit` + `scrollToFirstError`), `BusinessUnitSelector` `error` prop, `toast.*` i18n namespace. Wired into all 7 forms.
- **B5** contextual row labels (Q Draft→"แก้ไข"→/edit; else/SO/DO/BN→"ดูรายละเอียด").
- **B6** `printPdf()` in `lib/api.ts`; replaced `window.print()` on TI; Print added to RC + CN/DN detail.
- **B7** BN draft delete → `useConfirm`/AlertDialog (last raw `confirm(`; grep clean).

**Carry-overs + enhancement:**
- **C1** Q lifecycle: `useUpdateQuotation`/`useDeleteQuotation`, `QuotationForm` `edit` mode, `/quotations/[id]/edit`, status-aware detail actions (edit/delete/cancel/reject/PDF/print). BE endpoints already existed.
- **C2** `LineItemsTable` + `AdjustmentNoteForm` lock tax_rate; RC auto-applies SERVICE-only WHT base on mount; stale hint copy replaced.
- **C4** EN `'Draft saved'` ×2 → `tc('draftSaved')`; RC "Date"→วันที่; /receipts + CN/DN headers Thai.
- **C6** `ReceiptService.PostAsync` auto-settles Issued BNs once referenced TIs fully paid (uses current `TaxInvoiceIds` array).
- **R5** `DocumentCrossRefService.GetForTaxInvoiceAsync` resolves SO+DO (via DO.TaxInvoiceId→SalesOrderId→SO) + derives Q from SO. FE chip row already supported it.
- **L1** removed dead `ti.postConfirm.*` block from th/en.json.

---

## Handed off (3) — rationale + next steps

| Phase | Why deferred | What it needs |
|---|---|---|
| **C7** BN↔TI join table | Largest: new `sales.billing_note_tax_invoices(billing_note_id, tax_invoice_id, applied_amount)`, drop `BillingNote.TaxInvoiceIds bigint[]`, entity + EF config rewrite, `.Contains`→`.Any` query rewrites (C6 + cross-ref + service), FE multi-TI picker (`TaxInvoicePicker` multi-select + chips). Migration + entity rewrite = fresh-session work per §36. **Do first** — C5/C6 queries depend on its schema choice. |
| **C5** product_type NOT NULL | Backfill not 100% (q_lines=1, bn_lines=2 NULL — BN form sends `productType:null`). Needs: backfill UPDATE NULL→GOOD, `BillingNoteService.ApplyLines` default GOOD, entity non-nullable, `AlterColumn NOT NULL` ×5 migration. |
| **C3** list filters | BU/customer/date on 8 lists. Q/SO/DO/BN list endpoints take `status` only → either BE param extension or client-side filter across pages + URL persistence. ~8-page UI sweep. Low risk; deferred to keep ship clean. |

---

## → Sana (binding ownership rule — apply after this report)

- **`docs/api/openapi.yaml`** — verify/document: `GET /quotations/{id}/pdf`, `PUT /quotations/{id}`, `DELETE /quotations/{id}`, `POST /quotations/{id}/cancel`, `POST /quotations/{id}/reject` (all already in `SalesChainEndpoints`); Receipt GET endpoints now require `sales.receipt.read` (was `sales.receipt.create`); cross-ref response for a TI now populates `salesOrder` + `deliveryOrder`.
- **`docs/accounting-system-plan.md`** §6 — BN settled auto-derive (C6); Q Cancel/Reject transitions with PDF preserved (C1).
- **`docs/runtime-gotchas.md` §38** — written this session (spec-authoring RBAC matrix rule, SR-OWN-1). Sana to confirm wording.
- **Chapter 3 manual** — stays deferred until all 4 sub-sprints ship + RE-VALIDATE green (CLAUDE.md §16).

---

## → Sana RE-VALIDATE (deep mode) — now unblocked

B1 grants demo-accountant Receipt + CN/DN read across all 10 sales surfaces — exercise those. Also re-test: B2 403 surfacing, B3 customer prefill label (TI-from-Q), B4 empty-submit feedback on all 7 forms, B5 row labels, B6 print opens PDF, C1 Q edit/delete/cancel, C2 locked tax rate, R5 5-chip chain on a full Q→SO→DO→TI. Categories partly covered: 1/2/3/4/5/6/9. Uncovered (7/8/10/11/12/13) move to 13k/13L.
