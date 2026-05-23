# Report-Backend31 — Sprint 13h **CHECKPOINT 3** (P6.2 BillingNote BE+FE+E2E shipped; P8/P10/P11/E2E other deferred to ckpt4)

**Date:** 2026-05-21 · **Spec:** docs/Answer-Sana-Backend27.md §P6.2
**Sprint status:** ◐ in-progress — **10 of 13 phase-pieces shipped** (3 ckpt1 + 6 ckpt2 + 1 ckpt3).
**This is checkpoint 3, NOT the final completion report.** Sprint 13h continues in checkpoint 4 per Session-Resume.md prescribed phase order. User chose "P6.2 full (BE+FE+E2E) แล้วจบ ckpt3" at session start to focus context budget on a single, complete, biggest phase.

---

## Phase delivered this checkpoint

### P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) full BE + FE + E2E ✅

**Domain (new entity):**
- `Accounting.Domain.Entities.Sales.BillingNote` + `BillingNoteLine`. Implements `ITenantOwned + IAuditable + IConcurrencyVersioned` — global tenant query filter applies automatically (CLAUDE.md §4.7).
- `Accounting.Domain.Enums.BillingNoteStatus = { Draft, Issued, Settled, Cancelled }`. Non-fiscal — does NOT trigger e-Tax.
- Header carries: nullable `quotation_id` (FK Restrict to `sales.quotations`), nullable `long[]? TaxInvoiceIds` (PG `bigint[]` array for grouping N TIs from same customer).
- Line carries: nullable `tax_invoice_id` FK (per-line source TI for rolled-up reads), `product_type` snapshot (Sprint 13h P7 consistency).

**EF config + persistence:**
- `Persistence/Configurations/Sales/SalesChainConfigurations.cs` extended with `BillingNoteConfiguration` + `BillingNoteLineConfiguration`. Status enum stored UPPER string (same convention as Q/SO/DO/TI). `TaxInvoiceIds` mapped to `bigint[]` PG array.
- `AccountingDbContext` += `DbSet<BillingNote> BillingNotes` + `DbSet<BillingNoteLine> BillingNoteLines`.
- Migration `20260520165849_AddBillingNotes` — auto-scaffolded, applied to dev DB cleanly. Tables verified via psql: `sales.billing_notes` + `sales.billing_note_lines`.

**RLS + seed:**
- `Migrations/SqlScripts/322_billing_notes_rls.sql` — `ALTER TABLE ... ENABLE/FORCE ROW LEVEL SECURITY` + `company_isolation` policy (mirrors `010_rls_policies.sql` shape). Applied manually + recorded in `sys.applied_sql_scripts`.
- `Migrations/SqlScripts/321_seed_billing_note_perms.sql` — 2 new permission codes (`sales.billing_note.read`/`.manage`) + grants. Idempotent `ON CONFLICT DO NOTHING`. Comments avoid literal `{...}` per gotcha §35. Applied manually + recorded. **13 role-permission grants verified** in dev DB: SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/AR_CLERK/SALES_STAFF get manage+read; AUDITOR gets read only.

**Application layer:**
- `Application/Sales/BillingNoteDtos.cs` — `BillingLineInput`, `CreateBillingNoteRequest`, `BillingNoteListItem`, `BillingNoteDetail`, `IBillingNoteService`, `CreateBillingNoteValidator` (FluentValidation: customerId>0, currency NotEmpty.Length(3), exchangeRate>0, lines NotEmpty, dueDate≥docDate).

**Service:**
- `Infrastructure/Sales/BillingNoteService.cs` (`IBillingNoteService`). Methods: `CreateDraftAsync`, `UpdateDraftAsync` (Draft-only, 409 `billing_note.cannot_edit_after_issue`), `DeleteDraftAsync` (Draft-only, 409 `billing_note.cannot_delete_after_issue`), `IssueAsync` (Draft→Issued + `BL-NNNN` doc_no via `INumberSequenceService` with BU sub-prefix), `CancelAsync` (soft cancel, NOT permitted from Settled/Cancelled), `MarkSettledAsync` (Issued→Settled, sets `SettledAt`). All methods include explicit `Where(b => b.CompanyId == tenant.CompanyId)` in addition to the EF global filter (gotcha §26 belt-and-braces).
- DI: `Accounting.Infrastructure.DependencyInjection` += `services.AddScoped<IBillingNoteService, BillingNoteService>()`.

**Endpoints:**
- `Api/Endpoints/BillingNoteEndpoints.cs` — `POST /billing-notes` (Created 201), `PUT /billing-notes/{id}`, `DELETE /billing-notes/{id}`, `POST /billing-notes/{id}/issue`, `POST /billing-notes/{id}/cancel` (reuses `SalesChainEndpoints.ReasonBody`), `POST /billing-notes/{id}/mark-settled`, `GET /billing-notes?status=…`, `GET /billing-notes/{id}`. Read endpoints require `sales.billing_note.read`; manage endpoints require `sales.billing_note.manage`. Wired in `Program.cs` (`app.MapBillingNoteEndpoints();`).
- `Authorization/Permissions.cs.Sales` += `BillingNoteRead` + `BillingNoteManage` + added to `Permissions.All` static list.

**Frontend:**
- `lib/types.ts` += `BillingNoteListItem`, `BillingNoteDetail`, `BillingLineInput`, `CreateBillingNoteRequest`.
- `lib/queries.ts` += `useBillingNotes(status?)`, `useBillingNote(id)`, `useCreateBillingNote`, `useUpdateBillingNote`, `useDeleteBillingNote`, `useBillingNoteAction` (issue/cancel/mark-settled string-routed).
- `components/forms/BillingNoteForm.tsx` — full Draft create form (mirrors `QuotationForm` shape): customer/BU/dates/lines/notes + Save Draft / Issue.
- `app/(dashboard)/billing-notes/page.tsx` — list with status `<select>` filter + URL `?status=` persistence (refresh + share-link safe; mirrors P5 DO pattern).
- `app/(dashboard)/billing-notes/new/page.tsx` — thin wrapper for `<BillingNoteForm />`.
- `app/(dashboard)/billing-notes/[id]/page.tsx` — detail with status-driven action buttons (Issue/Delete for Draft; Mark Settled / Cancel-with-reason for Issued), Q cross-ref chip, multi-TI cross-ref chip array, AttachmentsSection (parent=`BILLING_NOTE`).
- `components/ui/StatusBadge.tsx` += `Settled` mapping (`badge-success`, `CheckCheck`).
- `components/app-shell/SidebarNav.tsx` += `/billing-notes` link in Sales section with `ReceiptText` icon, between TI and RC.
- `messages/th.json` + `en.json` — new `billingNote.*` namespace, `nav.billingNotes`, `status.Settled`, `common.delete` + `common.confirmDelete`.

**E2E:**
- `frontend/e2e/billing-note-flow.spec.ts` — two scenarios:
  1. `create → issue → mark settled` (full lifecycle happy path, status transitions verified).
  2. `create draft → delete` (Draft hard-delete contract verified, confirm dialog auto-accept).

---

## Verification gates (this checkpoint)

| Gate | Result |
|---|---|
| Frontend `tsc --noEmit` | **0** |
| `dotnet build Accounting.sln` | **0 err / 0 warn** |
| `dotnet test Accounting.Domain.Tests` | **89 / 89** (no regression — BillingNote added without touching existing domain rules) |
| Migration apply | clean — `20260520165849_AddBillingNotes` applied via `dotnet ef database update --no-build` after Api project rebuild (EF tooling caveat §36 documented this checkpoint follows ckpt2 pattern) |
| Permission seed | **13 role-permission grants** verified via psql across 7 roles (SUPER_ADMIN, COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, SALES_STAFF, AUDITOR) |
| RLS | `company_isolation` policy created + `FORCE ROW LEVEL SECURITY` on `sales.billing_notes` |
| Live UI verification | NOT done — Sana Chrome-MCP channel per CLAUDE.md §16 |

---

## Phases NOT shipped this checkpoint (deferred to checkpoint 4)

| # | Phase | Effort | Notes |
|---|---|---|---|
| P8 | Receipt cleanup + cross-ref | 2-3 hr | PostConfirmDialog docType prop + i18n; RC post nav; `IDocumentCrossRefService.GetReferencesForTaxInvoice`; `useCrossReferences` hook; cross-ref chips on TI/RC/CN/DN detail. **BN P6.2 already pre-wires the chip pattern via Q/TI links on detail page** — extending to CN/DN is mostly UI. |
| P10 | Logo upload + display | 3 hr | Multipart endpoint via attachments table parent=`COMPANY_PROFILE`; `/settings/company` UI; every doc header renders logo with text fallback; PDF embed via QuestPDF `Image()`. |
| P11 | XML 0-byte fix | 2 hr | Live-debug Tier 1 pipeline. P9 reshape (TI auto-fires on `MarkDeliveredAsync`) prerequisite — done ckpt2. |
| E2E (others) | 7 specs | 2-3 hr | quotation-lifecycle, sales-order-flow, delivery-order-flow, tax-invoice-from-quotation, receipt-cross-ref, rbac-chapter3, product-type-wht. (billing-note-flow shipped this ckpt.) |
| P4 FE tail | Q edit page | 1-2 hr | See ckpt2 deferred list — unchanged. |
| P7 FE tail | LineItemsTable readOnly + WHT auto-base | 2 hr | See ckpt2 deferred list — unchanged. |
| P3 tail | AdjustmentNoteForm date label EN + toast sweep | 1 hr | See ckpt2 deferred list — unchanged. |

**Total deferred effort estimate:** ~13-16 hr = 2 working days. Realistic for checkpoint 4.

---

## Decisions taken this checkpoint

- **BN status enum = `{Draft, Issued, Settled, Cancelled}`** — Settled = fully paid (manual `MarkSettledAsync` in ckpt3; Sprint 13i can auto-derive from receipts).
- **Doc number prefix `BL`** with BU sub-prefix per spec → `MM-YYYY-BL-{BU}-NNNN`. Allocated on Issue only (Draft has no doc_no, so hard-delete is gap-rule-safe per Plan §17.6 — same pattern as P4 Q Draft delete from ckpt2).
- **Cancel from Issued is soft** (status change only) because doc_no is allocated — gap rule applies. Settled and Cancelled are terminal.
- **TI grouping = PG `bigint[]` column** (not a join table). Lower-risk for v1; lookup is `WHERE tax_invoice_ids @> ARRAY[…]` if needed. Sprint 13i can promote to join table if needed.
- **RLS via dedicated `322_billing_notes_rls.sql`** instead of editing `010_rls_policies.sql` — keeps the 010 baseline immutable, mirrors the additive-script discipline.
- **Settled cannot be cancelled** (`bad_status` 409) — protects the audit trail. Cancellation reason kept on the row even after status change (no DB-level immutability trigger needed since BN is non-fiscal).
- **FE Draft delete uses `confirm()` browser dialog** for ckpt3 expedience. AlertDialog refactor lives in P4 FE tail.

---

## → Sana (proposed deltas; Sana applies after checkpoint 4 final ship)

`plan.md` — Sprint 13h ◐ in-progress: ckpt3 P6.2 ticked. P8/P10/P11/E2E other + FE tails remain.

`docs/runtime-gotchas.md` — no new candidates this checkpoint. §36 (EF stale-DLL caveat from ckpt2) reaffirmed by today's `dotnet build src/Accounting.Api` between scaffold and apply.

`docs/accounting-system-plan.md` — §6 sub-modules table: tick "Billing Note — ใบวางบิล" as implemented. §17.3 (numbering): add `BL` prefix to the canonical list with the BU sub-prefix shape `MM-YYYY-BL-{BU}-NNNN`. §19 (DB schema): add `sales.billing_notes` + `sales.billing_note_lines`.

`docs/api/openapi.yaml` —
- NEW `POST /billing-notes`, `PUT /billing-notes/{id}`, `DELETE /billing-notes/{id}`, `POST /billing-notes/{id}/issue`, `POST /billing-notes/{id}/cancel`, `POST /billing-notes/{id}/mark-settled`, `GET /billing-notes?status=`, `GET /billing-notes/{id}`. Schemas: `CreateBillingNoteRequest`, `BillingNoteListItem`, `BillingNoteDetail`, `BillingLineInput`.
- Status enum 4-state `[Draft, Issued, Settled, Cancelled]`.

`docs/manual/chapters/03-การขาย.md` — defer per CLAUDE.md §16 chapter authoring wait for full sprint ship + Sana RE-VALIDATE deep mode green.

---

## EF migration tooling caveat (carried from ckpt2 §36)

Same pattern observed this session: after `dotnet ef migrations add AddBillingNotes`, ran `dotnet build src/Accounting.Api/Accounting.Api.csproj` to refresh `bin\Debug\net10.0\Accounting.Infrastructure.dll`, then `dotnet ef database update --no-build` saw the new migration and applied it cleanly. Without the explicit Api build, `--no-build` would have used the pre-scaffold Infrastructure DLL and reported the migration as missing. Documented in Session-Resume.md ckpt4 brief.

---

## Next session resume (ckpt4)

Pick up at: **P8** (RC cleanup + cross-ref service — biggest remaining) → **P10** (logo upload + doc headers + PDF embed) → **P11** (XML 0-byte live debug). Then **FE deferred tails** (P4 / P7 / P3) interleaved when same files are touched. Then **E2E specs** (7 remaining chapter-3 scenarios). Final = **Report-Backend31.md** as sprint completion (not another checkpoint).

`docs/Session-Resume.md` updated this checkpoint with the phase-by-phase status table so the next session sees exactly what's done and what's left.
`NEXT-SESSION-PROMPT3.md` written as ckpt4 brief.

---

## DoD (checkpoint 3)

✅ P6.2 BE + FE + E2E shipped + FE tsc 0 + BE build 0/0 + Domain 89/89.
✅ Migration `AddBillingNotes` applied to dev DB.
✅ Seed 321 + RLS 322 applied to dev DB; 13 grants verified.
✅ Session-Resume.md overwritten with ckpt4 phase order + deferred items.
✅ NEXT-SESSION-PROMPT3.md written (= ckpt4 brief).
◐ P8 + P10 + P11 + E2E (other 7 specs) remain + small FE tails — deferred, documented above.
☐ progress.md cont. 55 — to be prepended next.
☐ Mirror Y:\AccountApp — at session end after progress entry.

**Honest:** Sprint is **not complete** — 10 of 13 phase-pieces landed (3 ckpt1 + 6 ckpt2 + 1 ckpt3). Sana cannot yet RE-VALIDATE in deep mode against the full Sprint 13h scope. What she CAN do this checkpoint: re-test the new BN happy path (create → issue → settled → cancel-from-issued), confirm RBAC (login as `demo-accountant` Demo@1234 → BN visible + manageable), confirm sidebar link present, confirm StatusBadge Settled renders, confirm Q-detail / multi-TI cross-ref chips render on BN detail.
