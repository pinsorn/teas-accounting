Sprint 13h CHECKPOINT 3 — resume. Checkpoint 2 (this session) shipped 6 of
10 remaining phases (P9 + P6.1 + P7 BE + P5 + P3 + P4 BE). 4 phases + E2E
remain. Honest goal: finish P6.2 + P8 + P10 + P11 + E2E + Report-Backend31
= sprint complete, then Sana RE-VALIDATE deep mode.

## Read in this exact order before any code

1. `docs/Session-Resume.md` (≤200 lines — checkpoint-3 phase-status table;
   prescribed phase order is `P6.2 → P8 → P10 → P11 → E2E → Report-31`).
2. `Report-Backend31-checkpoint2.md` (this session's checkpoint — what
   shipped, decisions, deferred FE polish items, → Sana proposed deltas).
3. `Report-Backend30.md` (checkpoint 1 — what shipped earlier this sprint).
4. `docs/Answer-Sana-Backend27.md` (full 13-phase spec — sprint scope).
5. `progress.md` cont. 54 (this session's entry, prepended).
6. `CLAUDE.md` §4 compliance + §10 do-not list + §15 TestIds + §16
   chapter-sequential workflow.

## Hard environment facts (do not re-discover)

- **Build through `subst U:` short-path** (`subst | findstr "U:"` to verify;
  remap to absolute code root if absent). MSIX long path breaks MSBuild.
- **`dotnet` via the PowerShell tool, NOT Bash.** Bash for git only.
- **Frontend tsc**: `node node_modules\typescript\bin\tsc --noEmit` from
  `U:\frontend`. (`pnpm` is NOT on PATH in the PowerShell tool.)
- **EF migrations**: real build always (`dotnet ef migrations add <Name>
  --project src/Accounting.Infrastructure --startup-project src/Accounting.Api`).
  **NEVER `--no-build`** (runtime-gotchas §25). Never `remove` on a
  desynced snapshot.
- **EF migration tooling gotcha (caught this checkpoint):** after a freshly
  generated migration, `dotnet ef migrations list` / `database update`
  with `--no-build` may report old migration set. Cause: the Api startup
  project's `bin\Debug\net10.0\Accounting.Infrastructure.dll` is still the
  pre-generation copy. Fix: `dotnet build src/Accounting.Api/...` to refresh
  the bin copy, then `--no-build` operations see the new migrations.
- **Seeds**: idempotent, `ON CONFLICT DO NOTHING`. **NEVER literal `{...}`
  in seed comments** (gotcha §35).
- **Tenant**: every new entity needs EF global query filter **AND**
  explicit `Where(x => x.CompanyId == tenant.CompanyId)` (gotcha §26,
  CLAUDE.md §4.7).

## Live state at handoff

- Backend was stopped this session to apply 3 migrations. Restart with
  `dotnet run --project src/Accounting.Api` from U:\backend after pulling
  this session's changes. Swagger 200 expected at :5080.
- Frontend :3000 — `next dev` (long-running across sessions).
- Postgres 18 at `S:\Program Files\PostgreSQL\18\bin\psql.exe`. DB
  `accounting_dev`. **Connect as `postgres` (pw `egoist`), NOT `accounting`**
  — the `accounting` role auth failed this session. Verify role config
  before next session if it matters.
- Dev DB now has 4 sprint-13h migrations applied:
  - `20260520132712_AddDeliveryOrderDeliveredStage`  (DO 3→4 states)
  - `20260520133244_AddTaxInvoiceQuotationReference` (TI←Q FK)
  - `20260520144906_AddLineItemProductTypeSnapshot`  (4 line tables += product_type)
- Existing data backfilled correctly:
  - DO #1 = `DELIVERED` (was Posted)
  - Q/SO/DO/TI lines all have `product_type = 'GOOD'`
- Login: `admin` / `Admin@1234` (super-admin). Also `demo-accountant` /
  `Demo@1234` — unblocked for chapter 3 (checkpoint-1 P1 + seed 320 still
  applies).

## Phase order (Session-Resume's prescription — follow strictly)

1. **P6.2** — BillingNote new entity. **BIGGEST single phase**: Domain +
   EF config + RLS + migration `AddBillingNotes` + service + endpoints +
   perms (extend seed 320 or new seed 321) + FE list/new/detail/form +
   i18n + StatusBadge `Settled` (already wired — verify) + E2E spec.
2. **P8** — Receipt cleanup: PostConfirmDialog → docType prop +
   i18n; RC post nav; `IDocumentCrossRefService.GetReferencesForTaxInvoice`
   + `useCrossReferences` hook; cross-ref chips on TI/RC/CN/DN detail.
3. **P10** — Company logo: multipart endpoint via attachments table
   parent=COMPANY_PROFILE; /settings/company UI; every doc header
   renders logo with text fallback; PDF embed via QuestPDF `Image()`.
4. **P11** — XML 0-byte: live-debug Tier 1 config; verify DO→TI
   auto-create triggers signing pipeline; download endpoint reads
   `etax.submissions.signed_xml_blob` with 404 fallback
   (`urn:teas:error:etax.not_yet_signed`). Repost a TI → MailHog +
   XML > 0 bytes + valid XAdES-BES `<ds:Signature>`.
5. **E2E specs** (after BE/FE lands per phase): quotation-lifecycle,
   sales-order-flow, delivery-order-flow,
   tax-invoice-from-quotation, billing-note-flow, receipt-cross-ref,
   rbac-chapter3, product-type-wht. All use `TestIds.*` random suffix
   (CLAUDE.md §15). Run = Sana ch.3 deep-mode / CI.
6. **Report-Backend31** (= sprint completion), progress.md cont. 55,
   plan.md tick proposed in §→ Sana, mirror `Y:\AccountApp`, notify
   Dispatch → Sana RE-VALIDATE **deep mode**.

## Deferred FE polish from this checkpoint (do these alongside their phases)

These are NOT blocking — the BE side already ships. Pick them up when
touching the same file for another reason.

- **P4 FE** — Q lifecycle UI. BE endpoints `PUT /{id}`, `DELETE /{id}`,
  `/cancel`, `/pdf` all live. FE work remaining:
  - `frontend/app/(dashboard)/quotations/[id]/edit/page.tsx` — NEW; thin
    wrapper hydrating `QuotationForm` with `useQuotation(id).data` +
    edit-mode prop.
  - `frontend/app/(dashboard)/quotations/[id]/page.tsx` — Draft = Edit
    + Delete buttons (link + AlertDialog confirm); Sent/Accepted =
    Cancel button (existing) + Download PDF.
  - `frontend/app/(dashboard)/quotations/page.tsx` — trash icon on
    Draft rows.
  - `frontend/components/forms/QuotationForm.tsx` — accept `initial` +
    `mode` props for edit; if edit + non-Draft, redirect+toast.
  - `frontend/lib/queries.ts` — `useUpdateQuotation`, `useDeleteQuotation`;
    PDF download via existing `downloadFile` helper.
  - `frontend/messages/th.json` + `en.json` — `quotation.edit`/`delete`/
    `cancelConfirm`/`pdfDownload`/`cannotEditAfterSend`.
  - `backend/src/Accounting.Infrastructure/Sales/SalesChainPdfService.cs`
    or similar — `QuotationPdfAsync` is wired to an endpoint at
    `SalesChainEndpoints.cs:42`; verify the QuestPDF generator
    implementation exists. If it's a stub, port the `TaxInvoiceService.Read.BuildPdfAsync`
    skeleton.

- **P7 FE polish** — product_type wiring. BE shipped: column persists +
  cascades Q→SO→DO→TI. FE work remaining:
  - `frontend/components/ui/LineItemsTable.tsx` — when `l.productId != null`,
    render `taxRate` cell as readOnly (greyed); same for `tax_code`.
  - `frontend/app/(dashboard)/tax-invoices/new/page.tsx` — TI form: when
    picker picks product, lock taxRate cell.
  - `frontend/app/(dashboard)/receipts/new/page.tsx` — RC: replace per-line
    free taxRate with locked; WHT auto-base = Σ(SERVICE line ex-VAT);
    remove the manual trim hint.
  - `frontend/components/forms/AdjustmentNoteForm.tsx` — CN/DN form: lock
    taxRate.
  - `backend/tests/Accounting.Domain.Tests/Sales/*` — new tests: product_type
    snapshot preserves across Q→SO→DO→TI; WHT base = SERVICE-only.

- **P3 sweep tail** — single-source Thai date util `lib/format/date.ts` is
  in place. `lib/utils.ts` `formatDate` now uses `dateStyle: 'medium'` +
  `calendar: 'buddhist'` → "20 พ.ค. 2569". Toast keys `common.posted` +
  `common.draftSaved` added (TH + EN). Remaining sweep:
  - AdjustmentNoteForm RC date label EN (BUG #5).
  - Audit every chapter-3 toast call site for hard-coded "Posted" / "Draft
    saved" / "Saved" — replace with `tc('posted')` / `tc('draftSaved')` /
    `tc('save')`. Grep recipe: `Grep "toast\\.success\\('[A-Z]"
    frontend/app/(dashboard)`.
  - Form date input convention: native `<input type="date">` can't be
    Thai-localized; document `frontend/lib/format/date.ts` header decision
    and add a UX label clarifying the CE input vs. BE display split.

## What is NOT a verification

- "BUILD-PENDING" handoff is a fallback. Toolchain works in-session via
  `subst U:`. Run real build + tests for every phase. FE `tsc` 0 + BE
  `dotnet build` 0/0 + Domain tests no regression per phase.
- UI verification is **Sana Chrome-MCP** (CLAUDE.md §16) — don't claim
  it for yourself.

## Honest-status culture

- Never claim shipped what you didn't run.
- If a phase hits a design ambiguity not in Answer-27, file
  `Question-Backend15.md` and pause that phase.
- If context starts thinning before all 4 phases land, write a
  checkpoint 4 the same way: ship clean, defer rest, update
  Session-Resume.md's phase table + write Report-Backend32 with the
  honest table.

## Things to watch out for

- **BillingNote (P6.2) is brand new** — full multi-tenant scaffolding:
  global query filter + explicit `Where(b => b.CompanyId == ...)` in
  every service method (CLAUDE.md §4.7).
- **Seed 321 (if you create one)**: no literal `{...}` in comments
  (gotcha §35). Spell out names. Idempotent `ON CONFLICT DO NOTHING`.
- **StatusBadge already has `Issued`/`Delivered`** from P9 this session.
  For BillingNote `Settled`, status badge map is in
  `frontend/components/ui/StatusBadge.tsx`.
- **Posted TI/RC/CN/DN/Billing-Note-Issued are immutable** (CLAUDE.md §4.2).
  Draft-only edit pattern is the Q P4 BE this session — mirror it
  for BillingNote.
- **DO Pattern X auto-TI now fires on `MarkDeliveredAsync`** (P9 this
  session). Existing chain tests (`Sprint10ChainTests.cs`) updated to
  call `IssueAsync` + `MarkDeliveredAsync`. New BillingNote E2E and any
  new chain tests must follow the 4-state pattern.

## When you're done

1. `Report-Backend31.md` written — sprint COMPLETION report.
2. `progress.md` cont. 55 prepended with gates table + decisions +
   → Sana.
3. `docs/Session-Resume.md` overwritten — Sprint 13h ☑ COMPLETE +
   forward queue (Sprint 13i print/PDF revamp queued, only after Sana
   RE-VALIDATE deep mode green).
4. Mirror `Y:\AccountApp`.
5. Notify Dispatch → Sana RE-VALIDATE deep mode (every button, every
   PDF, every XML, every field, every role).

Caveman + pordee modes are auto-active globally (terse responses, Thai
acceptable; code / commits / security / reports in normal English).
Don't drop them unless the user says `stop caveman` / `หยุดพอดี`.

Start by reading `docs/Session-Resume.md`.
