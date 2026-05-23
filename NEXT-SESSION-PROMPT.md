Sprint 13h CHECKPOINT 2 — resume. CHECKPOINT 1 (P1+P12+P2) shipped + verified
last session. 10 phases remain + E2E + Report-Backend31. Honest goal: ship as
many as cleanly fit; do NOT cram half-finished migrations.

## Read in this exact order before any code

1. `docs/Session-Resume.md` (≤200 lines — phase-status table is the
   authoritative resume-from-where map; checkpoint-2 phase order is
   prescribed there).
2. `Report-Backend30.md` (checkpoint-1 report — what shipped, decisions
   taken, EF format-placeholder gotcha §35 caught live, → Sana proposed
   deltas not yet applied).
3. `docs/Answer-Sana-Backend27.md` (full 13-phase spec — your
   active sprint; checkpoint 1 covered P1/P12/P2).
4. `progress.md` cont. 53 (top entry — what landed last session).
5. `CLAUDE.md` §4 compliance + §10 do-not list + §15 TestIds + §16
   chapter-sequential workflow.

## Hard environment facts (do not re-discover)

- **Build through `subst U:` short-path** (`subst | findstr "U:"` to verify;
  remap to absolute code root if absent). MSIX long path breaks MSBuild
  node launch — runtime-gotchas §29.
- **`dotnet` via the PowerShell tool, NOT Bash.** Bash for git only.
- **Frontend tsc**: `node node_modules\typescript\bin\tsc --noEmit` from
  `U:\frontend`. (`pnpm` is NOT on PATH in the PowerShell tool.)
- **EF migrations**: real build always (`dotnet ef migrations add <Name>
  --project src/Accounting.Infrastructure --startup-project src/Accounting.Api`).
  **NEVER `--no-build`** (runtime-gotchas §25). Never `remove` on a
  desynced snapshot.
- **Seeds**: idempotent, `ON CONFLICT DO NOTHING`. **NEVER literal `{...}`
  in seed comments** (DbInitializer calls `ExecuteSqlRawAsync(sql, ct)`
  which resolves to `(string, params object[])` → `RawSqlCommandBuilder`
  runs the SQL through `string.Format` → braces explode with
  `System.FormatException`). Caught last session — gotcha §35.
- **Tenant**: every new entity needs EF global query filter **AND**
  explicit `Where(x => x.CompanyId == tenant.CompanyId)` in the service
  (runtime-gotchas §26, CLAUDE.md §4.7).

## Live state at handoff

- Backend :5080 — `dotnet run` (api10.log/err); Swagger 200. Seed 320
  applied this DB run.
- Frontend :3000 — `next dev` (long-running across sessions).
- Postgres 18 at `S:\Program Files\PostgreSQL\18\bin\psql.exe`, pwd
  `egoist`, DB `accounting_dev`. Role `accounting` is BYPASSRLS for dev.
- Login: `admin` / `Admin@1234` (super-admin). Also `demo-accountant` /
  `Demo@1234` — now unblocked for chapter 3 (P1).
- Existing data in dev DB from Sana's joint-validate: TI #1, RC #1, Q #1,
  SO #1, DO #1 (status=Posted on DO — migrations in P9 need to backfill
  these to Delivered).

## Phase order (Session-Resume's prescription — follow strictly)

1. **P9** — smallest breaking migration (DO 3→4 states, backfill
   Posted→Delivered, split `/post` into `/issue` + `/mark-delivered` —
   the latter triggers TI creation). Foundational for P11.
2. **P6.1** — TI ← Q FK (single-column nullable, non-breaking
   migration `AddTaxInvoiceQuotationReference`).
3. **P7** — product_type snapshot cascade across 6 line tables
   (Q/SO/DO/TI/RC line + CN/DN line). Migration
   `AddLineItemProductTypeSnapshot` with pre-flight COUNT + GOOD
   default. Locks tax_code_id/tax_rate readOnly when a product is
   picked. RC WHT auto-base = Σ(SERVICE lines ex-VAT). Big cascade.
4. **P4** — Q lifecycle: PUT/DELETE/cancel/PDF endpoints + service
   methods + Domain tests + QuestPDF generator (mirror TI skeleton) +
   FE edit page + AlertDialog confirms.
5. **P6.2** — BillingNote new entity. Biggest. Domain + EF config + RLS
   + migration `AddBillingNotes` + service + endpoints + perms (extend
   seed 320 or new seed 321) + FE list/new/detail/form + i18n +
   StatusBadge += Settled + E2E spec.
6. **P8** — Receipt cleanup: PostConfirmDialog → docType prop +
   i18n; RC post nav; `IDocumentCrossRefService.GetReferencesForTaxInvoice`
   + `useCrossReferences` hook; cross-ref chips on TI/RC/CN/DN detail.
7. **P10** — Company logo: multipart endpoint via attachments table
   parent=COMPANY_PROFILE; /settings/company UI; every doc header
   renders logo with text fallback; PDF embed via QuestPDF `Image()`.
8. **P11** — XML 0-byte: live-debug Tier 1 config; verify DO→TI
   auto-create triggers signing pipeline; download endpoint reads
   `etax.submissions.signed_xml_blob` with 404 fallback
   (`urn:teas:error:etax.not_yet_signed`). Repost a TI → MailHog +
   XML > 0 bytes + valid XAdES-BES `<ds:Signature>`.
9. **P5** — SO + DO list filters: BU + status + customer combobox +
   date range + URLSearchParams persistence. BE filters likely already
   accepted; just wire FE.
10. **P3** — i18n + Thai date format sweep across chapter-3 pages +
    `lib/format/date.ts` single-source (`Intl.DateTimeFormat('th-TH',
    { dateStyle: 'medium' })` → Buddhist Era).
11. **P13** — `/settings/products` → DataTable; 2 Thai toasts.
12. **E2E specs** (after BE/FE lands per phase): quotation-lifecycle,
    sales-order-flow, delivery-order-flow,
    tax-invoice-from-quotation, billing-note-flow, receipt-cross-ref,
    rbac-chapter3, product-type-wht. All use `TestIds.*` random suffix
    (CLAUDE.md §15). Run = Sana ch.3 deep-mode / CI.
13. **Report-Backend31** (= sprint completion, NOT a checkpoint this
    time — final ship), progress.md cont. 54, plan.md tick proposed
    in §→ Sana, mirror `Y:\AccountApp`, notify Dispatch → Sana
    RE-VALIDATE **deep mode**.

## What is NOT a verification

- "BUILD-PENDING" handoff is a fallback. Toolchain works. Run real
  build + tests for every phase. FE `tsc` 0 + BE `dotnet build` 0/0 +
  Domain tests no regression per phase.
- UI verification is **Sana Chrome-MCP** (CLAUDE.md §16) — don't claim
  it for yourself.

## Honest-status culture

- Never claim shipped what you didn't run. If a phase hits a design
  ambiguity not covered in Answer-27, file `Question-Backend15.md`
  and pause that phase — do NOT improvise on compliance-adjacent
  design.
- If context starts thinning before all 10 phases land, write a
  checkpoint 3 the same way: ship clean, defer rest, update
  Session-Resume.md's phase table + write Report-Backend31 with the
  honest table. Sprint 13h finally ships when checkpoint N hits "all
  phases ☑".

## Things to watch out for

- Existing dev rows from Sana's validate run: TI #1, RC #1, Q #1, SO #1,
  DO #1 (Posted). P9 migration backfill must handle the DO row. P7
  migration backfill defaults to GOOD. Pre-flight `SELECT COUNT(*)` per
  table per Answer-26 P2 pattern.
- Posted TI/RC/CN/DN are immutable (CLAUDE.md §4.2). Q is pre-fiscal
  → edit allowed only in Draft.
- DO ship-to/recipient currently fold into Notes (R-P4a from
  Sprint 13e). Phase-2 backlog for dedicated fields. Don't add the
  fields in this sprint.
- Receipt module owns `useCreateReceipt`/`usePostReceipt`. AdjustmentNote
  module owns CN+DN (shared `AdjustmentNoteForm`).

## When you're done

1. `Report-Backend31.md` written — comprehensive completion report
   (no longer a "checkpoint"). Honest per-phase status table.
2. `progress.md` cont. 54 prepended with gates table + decisions +
   → Sana.
3. `docs/Session-Resume.md` overwritten — Sprint 13h ☑ COMPLETE +
   forward queue (Sprint 13i print/PDF revamp queued, only after Sana
   RE-VALIDATE deep mode green).
4. Mirror `Y:\AccountApp`.
5. Notify Dispatch → Sana RE-VALIDATE deep mode (every button, every
   PDF, every XML, every field, every role — non-negotiable per
   Session-Resume's "what Sana found that you must not repeat" list).

Caveman + pordee modes are auto-active globally (terse responses, Thai
acceptable; code / commits / security / reports in normal English).
Don't drop them unless the user says `stop caveman` / `หยุดพอดี`.

Start by reading `docs/Session-Resume.md`.

---

## Files per phase (read before editing; verify shape with the spec)

### P9 — DO Delivered stage (3→4)
- `backend/src/Accounting.Domain/Enums/SalesChainStatus.cs` — `DeliveryOrderStatus` += `Issued`, `Delivered` (replace `Posted` semantics)
- `backend/src/Accounting.Domain/Entities/Sales/DeliveryOrder.cs` — likely no shape change; verify
- `backend/src/Accounting.Application/Sales/SalesChainDtos.cs` — `IDeliveryOrderService` += `IssueAsync`, `MarkDeliveredAsync`
- `backend/src/Accounting.Infrastructure/Sales/SalesOrderDeliveryServices.cs` — split `PostAsync` → `IssueAsync` (Draft→Issued, allocate doc_no, no TI) + `MarkDeliveredAsync` (Issued→Delivered, **now** trigger linked TI)
- `backend/src/Accounting.Api/Endpoints/SalesChainEndpoints.cs` — DO group: replace `/post` with `/issue` + `/mark-delivered`
- `backend/src/Accounting.Infrastructure/Migrations/{ts}_AddDeliveryOrderDeliveredStage.cs` — NEW; pre-flight `SELECT COUNT(*)`, backfill `Posted → Delivered`, `AlterColumn` enum
- `frontend/lib/queries.ts` — `useDeliveryOrderAction` already accepts arbitrary action
- `frontend/components/forms/DeliveryOrderForm.tsx` — action button labels (ออกใบส่งของ / ยืนยันส่งมอบ)
- `frontend/app/(dashboard)/delivery-orders/[id]/page.tsx` — status-dependent action buttons
- `frontend/components/ui/StatusBadge.tsx` — `Delivered` already mapped Sprint 13e P5; verify
- `frontend/messages/th.json` + `en.json` — `deliveryOrder.issue`/`markDelivered`/`delivered`

### P6.1 — TI ← Q FK
- `backend/src/Accounting.Domain/Entities/Sales/TaxInvoice.cs` — `+ public long? QuotationId { get; set; }`
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/Sales/TaxInvoiceConfiguration.cs` — FK + index
- `backend/src/Accounting.Application/Sales/TaxInvoiceDtos.cs` — `CreateTaxInvoiceRequest` += `QuotationId`; `TaxInvoiceDetail` += `QuotationId`
- `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.cs` — `CreateDraftAsync`: if `QuotationId` set, load Q + snapshot customer/BU/lines
- `backend/src/Accounting.Infrastructure/Migrations/{ts}_AddTaxInvoiceQuotationReference.cs` — NEW; nullable column + FK + index
- `frontend/lib/types.ts` — `TaxInvoiceDetail` += `quotationId`
- `frontend/app/(dashboard)/quotations/[id]/page.tsx` — button "สร้างใบกำกับภาษีจากใบเสนอราคา" when `status==='Accepted'` → `/tax-invoices/new?fromQuotationId={id}`
- `frontend/app/(dashboard)/tax-invoices/new/page.tsx` — read `fromQuotationId` from URL, pre-fill from `useQuotation`
- `frontend/app/(dashboard)/tax-invoices/[id]/page.tsx` — cross-ref chip to linked Q

### P7 — product_type line snapshot + lock tax_rate
- `backend/src/Accounting.Domain/Entities/Sales/Quotation.cs` — `QuotationLine` += `ProductType` (enum or string snapshot)
- `backend/src/Accounting.Domain/Entities/Sales/SalesOrder.cs` — `SalesOrderLine` += same
- `backend/src/Accounting.Domain/Entities/Sales/DeliveryOrder.cs` — `DeliveryOrderLine` += same
- `backend/src/Accounting.Domain/Entities/Sales/TaxInvoiceLine.cs` — += same
- Receipt entity + line — find via `Glob backend/src/Accounting.Domain/Entities/Sales/Receipt*.cs`
- AdjustmentNote entity + line (CN/DN shared) — find via `Glob backend/src/Accounting.Domain/Entities/Sales/AdjustmentNote*.cs`
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/Sales/*.cs` — line configurations (column + length if string)
- `backend/src/Accounting.Application/Sales/SalesChainDtos.cs` — `ChainLineInput` += `ProductType`
- `backend/src/Accounting.Application/Sales/TaxInvoiceDtos.cs` — `TaxInvoiceLineInput` += `ProductType`
- All sales services — snapshot the value on insert (do **not** lookup live; document immutability)
- `backend/src/Accounting.Infrastructure/Migrations/{ts}_AddLineItemProductTypeSnapshot.cs` — NEW; pre-flight COUNT per table, backfill via product master lookup with default `'GOOD'`, set NOT NULL after backfill
- `frontend/components/ui/LineItemsTable.tsx` — when `l.productId != null`, render `taxRate` cell as readOnly (greyed); same for `tax_code` if surfaced
- `frontend/components/forms/ProductPicker.tsx` — already returns `productType` in `onSelectProduct` ✓
- `frontend/app/(dashboard)/tax-invoices/new/page.tsx` — TI form: when picker picks product, lock taxRate cell
- `frontend/app/(dashboard)/receipts/new/page.tsx` — RC: replace per-line free taxRate with locked; WHT auto-base = Σ(SERVICE line ex-VAT); remove the manual trim hint
- `frontend/components/forms/AdjustmentNoteForm.tsx` — CN/DN form: lock taxRate
- `backend/tests/Accounting.Domain.Tests/Sales/*` — new tests: product_type snapshot preserves across Q→SO→DO→TI; WHT base = SERVICE-only

### P4 — Quotation lifecycle (Edit/Delete/Cancel/PDF)
- `backend/src/Accounting.Application/Sales/SalesChainDtos.cs` — `IQuotationService` += `UpdateDraftAsync`, `DeleteDraftAsync`; existing `CancelAsync` already covers Sent/Accepted→Cancelled
- `backend/src/Accounting.Infrastructure/Sales/QuotationChainServices.cs` — `UpdateDraftAsync` (Draft-only, 409 otherwise with `urn:teas:error:quotation.cannot_edit_after_send`), `DeleteDraftAsync` (Draft hard-delete acceptable; no doc_no yet)
- `backend/src/Accounting.Infrastructure/Sales/SalesChainPdfService.cs` (or `QuotationChainServices.cs` adjacent) — `QuotationPdfAsync` via QuestPDF; mirror `TaxInvoiceService.BuildPdfAsync` skeleton in `TaxInvoiceService.Read.cs`
- `backend/src/Accounting.Api/Endpoints/SalesChainEndpoints.cs` — Q group: `PUT /{id}`, `DELETE /{id}`, `GET /{id}/pdf`; cancel already there
- `backend/tests/Accounting.Domain.Tests/Sales/*` — Q state-machine tests: every transition + 409 on wrong-state + cancel preserves doc_no
- `frontend/app/(dashboard)/quotations/[id]/edit/page.tsx` — NEW; thin wrapper hydrating `QuotationForm` with `useQuotation(id).data` + edit-mode prop
- `frontend/app/(dashboard)/quotations/[id]/page.tsx` — buttons by status: Draft={Edit, Delete, Issue}; Sent={Accept, Reject, Cancel, Download PDF}; Accepted={Convert, Cancel, Download PDF}; terminal={Download PDF}
- `frontend/app/(dashboard)/quotations/page.tsx` — trash icon on Draft rows + AlertDialog
- `frontend/components/forms/QuotationForm.tsx` — accept `initial`/`mode` props for edit; if edit + non-Draft, redirect+toast
- `frontend/lib/queries.ts` — `useUpdateQuotation`, `useDeleteQuotation`; PDF download via existing `downloadFile` helper
- `frontend/messages/th.json` + `en.json` — `quotation.edit`/`delete`/`cancelConfirm`/`pdfDownload`/`cannotEditAfterSend`

### P6.2 — Billing Note (new entity — biggest)
- `backend/src/Accounting.Domain/Enums/SalesChainStatus.cs` — `+ public enum BillingNoteStatus { Draft, Issued, Settled, Cancelled }`
- `backend/src/Accounting.Domain/Entities/Sales/BillingNote.cs` — NEW: header + lines; `ITenantOwned`, `IAuditable`, `IConcurrencyVersioned`
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/Sales/BillingNoteConfiguration.cs` — NEW: table `sales.billing_notes` + lines + indexes
- `backend/src/Accounting.Infrastructure/Persistence/AccountingDbContext.cs` — `DbSet<BillingNote> BillingNotes` + global query filter `b => b.CompanyId == _tenant.CompanyId`
- `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs` — NEW: `CreateBillingNoteRequest`, `BillingNoteListItem`, `BillingNoteDetail`, `IBillingNoteService`
- `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs` — NEW; `Create/Update/Issue/Cancel/List/Get/Pdf`; explicit `Where(b => b.CompanyId == _tenant.CompanyId)` even with global filter (CLAUDE.md §4.7)
- `backend/src/Accounting.Api/Endpoints/BillingNoteEndpoints.cs` — NEW; per-endpoint auth
- `backend/src/Accounting.Api/Program.cs` — `app.MapBillingNoteEndpoints();`
- `backend/src/Accounting.Api/Authorization/Permissions.cs` — `Sales` += `BillingNoteRead`, `BillingNoteManage`; add to `All` list
- `backend/src/Accounting.Infrastructure/Migrations/{ts}_AddBillingNotes.cs` — NEW; table + FKs; RLS policies via raw SQL `migrationBuilder.Sql(File.ReadAllText(...))` mirroring TI pattern
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/322_seed_billing_note_perms.sql` — NEW; grants for read tier + manage tier (NO `{}` in comments per gotcha §35)
- `frontend/components/ui/Sidebar.tsx` (or wherever nav lives — `Glob frontend/**/Sidebar*.tsx`) — add "ใบแจ้งหนี้" under sales
- `frontend/app/(dashboard)/billing-notes/page.tsx` — NEW list + filter (status/customer/date)
- `frontend/app/(dashboard)/billing-notes/new/page.tsx` — NEW thin wrapper
- `frontend/app/(dashboard)/billing-notes/[id]/page.tsx` — NEW detail (Issue/Cancel/Download PDF actions)
- `frontend/components/forms/BillingNoteForm.tsx` — NEW; customer picker + BU + date + TI roll-up picker or line items + notes
- `frontend/lib/queries.ts` — `useBillingNotes`, `useBillingNote`, `useCreateBillingNote`, `useBillingNoteAction`
- `frontend/lib/types.ts` — `BillingNoteListItem`, `BillingNoteDetail`
- `frontend/components/ui/StatusBadge.tsx` — MAP += `Settled` (badge-success, `CheckCheck` icon)
- `frontend/messages/th.json` + `en.json` — `billingNote.*` namespace; `status.Settled`
- `frontend/e2e/billing-note-flow.spec.ts` — NEW

### P8 — Receipt cleanup + cross-ref
- `frontend/components/ui/PostConfirmDialog.tsx` — `+ docType?: 'tax_invoice' | 'receipt' | 'credit_note' | 'debit_note' | 'quotation' | 'billing_note'`; resolve title via `t('postConfirm.${docType}.title')`
- `frontend/app/(dashboard)/tax-invoices/new/page.tsx` — pass `docType='tax_invoice'`
- `frontend/app/(dashboard)/receipts/new/page.tsx` — pass `docType='receipt'`; on Post success → `router.push(\`/receipts/${id}\`)`
- `frontend/components/forms/AdjustmentNoteForm.tsx` — pass `docType` per `noteType`
- `backend/src/Accounting.Application/Sales/IDocumentCrossRefService.cs` — NEW
- `backend/src/Accounting.Infrastructure/Sales/DocumentCrossRefService.cs` — NEW; `GetReferencesForTaxInvoice(id)` → linked Q, SO, DO, BN, RC[], CN[], DN[]
- `backend/src/Accounting.Api/Endpoints/CrossRefEndpoints.cs` — NEW or fold into doc-specific endpoints
- `frontend/lib/queries.ts` — `useCrossReferences(docType, docId)`
- `frontend/components/ui/CrossRefPanel.tsx` — NEW shared chip strip
- All doc detail pages (TI/RC/CN/DN/BN) — render `<CrossRefPanel>`
- `frontend/messages/th.json` + `en.json` — `postConfirm.{tax_invoice/receipt/credit_note/debit_note/quotation/billing_note}.title`

### P10 — Logo upload + display
- `backend/src/Accounting.Api/Endpoints/CompanyProfileEndpoints.cs` — `POST /api/v1/company-profile/logo` multipart; reuse Sprint 11 attachment with `parent_type='COMPANY_PROFILE'`; max 1 MB
- `backend/src/Accounting.Infrastructure/Master/CompanyProfileService.cs` — `UpdateLogoAttachmentAsync`
- `frontend/app/(dashboard)/settings/company/page.tsx` — upload button + preview + replace
- `frontend/components/ui/DocHeader.tsx` — NEW shared; consumed by every doc detail
- All doc detail pages (Q/SO/DO/TI/RC/CN/DN/BN) — replace ad-hoc header with `<DocHeader>`
- `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.Read.cs` (TI PDF) — `QuestPDF Image()` for logo
- `backend/src/Accounting.Infrastructure/Sales/SalesChainPdfService.cs` (Q/SO/DO PDFs — find or share) — same Image() call
- `frontend/messages/th.json` + `en.json` — `company.logo.{upload/preview/replace}`

### P11 — XML 0-byte fix (live debug)
- `appsettings.Development.json` — verify Tier 1 keys: `ETax:Enabled=true`, `ETax:AutoSendOnTaxInvoicePost=true`, `ETax:Signing:PfxPath=secrets/dev-cert.pfx`, `ETax:Signing:PfxPassword=dev123`
- `backend/src/Accounting.Infrastructure/ETax/*.cs` — find pipeline; verify the DO→TI auto-create path triggers `TryAutoSendETaxAsync`
- `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.Read.cs` `BuildXmlAsync` — must read `etax.submissions.signed_xml_blob` (or wherever signed payload is stored)
- `backend/src/Accounting.Api/Endpoints/TaxInvoiceEndpoints.cs` — `/xml`: if no signed row, return `Results.NotFound()` with `urn:teas:error:etax.not_yet_signed` (not 0 bytes)
- `backend/src/Accounting.Api/Endpoints/ETaxEndpoints.cs` (find) — fix `/etax/submissions?taxInvoiceId={id}` 400 (query signature)
- Live smoke: post a new TI manually → MailHog `:8025` shows the email → download XML → file >0 bytes, contains `<ds:Signature>`

### P5 — SO + DO list filters + URL persistence
- `frontend/app/(dashboard)/sales-orders/page.tsx` — BU + status + customer + date range filter row; URLSearchParams persist
- `frontend/app/(dashboard)/delivery-orders/page.tsx` — same + filter by linked SO doc_no
- `frontend/lib/queries.ts` — extend `useSalesOrders(params)`, `useDeliveryOrders(params)` to accept the filter object
- `backend/src/Accounting.Api/Endpoints/SalesChainEndpoints.cs` — verify SO/DO `GET /` accepts these (cursor-style like TI list); add if missing
- `backend/src/Accounting.Application/Sales/SalesChainDtos.cs` — list query params shape if added BE-side
- `backend/src/Accounting.Infrastructure/Sales/SalesOrderDeliveryServices.cs` + `QuotationChainServices.cs` — `ListAsync` filter pipeline

### P3 — i18n + Thai date format
- `frontend/lib/format/date.ts` — NEW or update; single source via `Intl.DateTimeFormat('th-TH', { dateStyle: 'medium' })` for Buddhist Era; helper `formatDateTH(iso)` + `parseTH(s)`
- `frontend/lib/utils.ts` — `formatDate` calls the new util
- `frontend/messages/th.json` + `en.json` — sweep every chapter-3 list header / toast / dialog; add missing TH translations; toast keys `toast.posted`/`toast.draftSaved`/etc.
- All chapter-3 pages — replace `<th>No.</th>` / `<th>Date</th>` / hardcoded English with `t('list.header.docNo')` etc.; replace `toast.success('Posted')` with `toast.success(t('toast.posted'))`
- Form date inputs — decision: accept dd/mm/YYYY CE (with label clarifying) OR ship a Thai date picker component; document the choice in `lib/format/date.ts` header comment

### P13 — Product list table + sundry
- `frontend/app/(dashboard)/settings/products/page.tsx` — replace card grid with `<DataTable>` (verify `frontend/components/ui/DataTable.tsx` exists)
- `frontend/components/forms/QuotationForm.tsx` + `SalesOrderForm.tsx` + `DeliveryOrderForm.tsx` — toasts via i18n: `t('toast.draftSaved')` / `t('toast.posted')` (depends on P3 keys)

### E2E (after BE/FE per phase lands)
- `frontend/e2e/quotation-lifecycle.spec.ts` — NEW (Draft → edit → save → Issue → Accept → Convert; Draft → delete; Sent → Cancel; PDF download)
- `frontend/e2e/sales-order-flow.spec.ts` — NEW (list + filter + create from Q + Post)
- `frontend/e2e/delivery-order-flow.spec.ts` — NEW (3-state machine Draft → Issued → Delivered; linked TI fires on Delivered)
- `frontend/e2e/tax-invoice-from-quotation.spec.ts` — NEW (Q Accepted → create TI with `quotation_id` ref → cross-ref panel chip)
- `frontend/e2e/billing-note-flow.spec.ts` — NEW (P6.2)
- `frontend/e2e/receipt-cross-ref.spec.ts` — NEW (RC post → TI detail chip)
- `frontend/e2e/rbac-chapter3.spec.ts` — NEW or update (demo-accountant traverses all of chapter 3 — passes only after P1 + seed 320 applied)
- `frontend/e2e/product-type-wht.spec.ts` — NEW (RC with SERVICE + GOOD lines → WHT base auto = SERVICE-only ex-VAT)
- All specs use `TestIds.*` random suffix per CLAUDE.md §15; helper at `frontend/e2e/helpers/test-ids.ts`

### Reporting
- `Report-Backend31.md` — NEW; sprint COMPLETION report (no longer a "checkpoint"); per-phase honest status table; → Sana proposed deltas
- `progress.md` — prepend cont. 54
- `docs/Session-Resume.md` — overwrite; flip Sprint 13h to ☑ COMPLETE; queue Sprint 13i print/PDF revamp
- `Y:\AccountApp` — mirror every changed file at session end

---

## Reference touchstones (consult, don't re-derive)

- Migration pattern (with raw SQL block for RLS): `backend/src/Accounting.Infrastructure/Migrations/20260517180740_AddQuotationChain.cs`
- Seed pattern (idempotent, no braces): `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/270_seed_quotation_chain_perms.sql` + this sprint's `320_seed_chapter3_rbac.sql`
- TI PDF (template for Q/BN PDF): `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.Read.cs` `BuildPdfAsync`
- Sprint 13e form pattern: `frontend/components/forms/QuotationForm.tsx`, `SalesOrderForm.tsx`, `DeliveryOrderForm.tsx`
- Async combobox pattern (post-portal): `frontend/components/ui/FloatingListbox.tsx` + `frontend/components/forms/{TaxInvoicePicker,ProductPicker}.tsx`
- State-machine domain test pattern: existing `backend/tests/Accounting.Domain.Tests/Sales/*` files
