Sprint 13h CHECKPOINT 4 — resume. Checkpoint 3 (prev session) shipped P6.2 BillingNote
full (BE + FE + E2E). 3 phases + 7 E2E specs + small FE tails + Report remain.
Honest goal: finish P8 + P10 + P11 + 7 E2E specs + Report-Backend31 = sprint complete,
then Sana RE-VALIDATE deep mode.

## Read in this exact order before any code

1. `docs/Session-Resume.md` (≤200 lines — checkpoint-4 phase-status table;
   prescribed phase order is `P8 → P10 → P11 → E2E (7) → Report-31`).
2. `Report-Backend31-checkpoint3.md` (prev session's checkpoint — P6.2 shipped,
   decisions, deferred FE polish items, → Sana proposed deltas).
3. `Report-Backend31-checkpoint2.md` (ckpt2 — what shipped before P6.2).
4. `Report-Backend30.md` (ckpt1 — earliest sprint 13h work).
5. `docs/Answer-Sana-Backend27.md` (full 13-phase spec — sprint scope).
6. `progress.md` cont. 55 (this session's entry, prepended).
7. `CLAUDE.md` §4 compliance + §10 do-not list + §15 TestIds + §16
   chapter-sequential workflow.

## Hard environment facts (do not re-discover)

- **Build through `subst U:` short-path** (`subst | findstr "U:"` to verify;
  remap to absolute code root if absent). MSIX long path breaks MSBuild.
- **`dotnet` via the PowerShell tool, NOT Bash.** Bash for git only.
- **Frontend tsc**: `node node_modules\typescript\bin\tsc --noEmit` from
  `U:\frontend`. (`pnpm` is NOT on PATH in the PowerShell tool.)
- **EF migrations**: real build always (`dotnet ef migrations add <Name>
  --project src/Accounting.Infrastructure --startup-project src/Accounting.Api`).
  **NEVER `--no-build` for `add`** (runtime-gotchas §25). Never `remove` on a
  desynced snapshot.
- **EF migration tooling stale-DLL caveat (§36, reaffirmed ckpt3):** after
  generating a migration, run `dotnet build src/Accounting.Api/Accounting.Api.csproj`
  BEFORE `database update --no-build`. Otherwise `--no-build` sees the pre-scaffold
  Infrastructure DLL and reports the migration as missing.
- **Seeds**: idempotent, `ON CONFLICT DO NOTHING`. **NEVER literal `{...}`
  in seed comments** (gotcha §35).
- **Tenant**: every new entity needs EF global query filter **AND**
  explicit `Where(x => x.CompanyId == tenant.CompanyId)` (gotcha §26,
  CLAUDE.md §4.7). Pattern from ckpt3 BillingNoteService is the template.

## Live state at handoff

- Backend was running this session. Restart with
  `dotnet run --project src/Accounting.Api` from U:\backend after pulling
  this session's changes. Swagger 200 expected at :5080.
- Frontend :3000 — `next dev` (long-running across sessions).
- Postgres 18 at `S:\Program Files\PostgreSQL\18\bin\psql.exe`. DB
  `accounting_dev`. **Connect as `postgres` (pw `egoist`), NOT `accounting`**
  — the `accounting` role auth still fails. Defer pg_hba fix.
- Dev DB now has 4 ckpt2 + 1 ckpt3 migrations applied:
  - `20260520132712_AddDeliveryOrderDeliveredStage` (ckpt2)
  - `20260520133244_AddTaxInvoiceQuotationReference` (ckpt2)
  - `20260520144906_AddLineItemProductTypeSnapshot` (ckpt2)
  - `20260520165849_AddBillingNotes` (ckpt3) — `sales.billing_notes` + `sales.billing_note_lines`.
- Existing data:
  - DO #1 = `DELIVERED` (was Posted), Q/SO/DO/TI lines all `product_type = 'GOOD'` (ckpt2).
  - 13 BN role-permission grants seeded (ckpt3).
- Login: `admin` / `Admin@1234` (super-admin). Also `demo-accountant` /
  `Demo@1234` — has BN manage+read this session.

## Phase order (Session-Resume's prescription — follow strictly)

1. **P8** — Receipt cleanup: PostConfirmDialog → docType prop +
   i18n; RC post nav; `IDocumentCrossRefService.GetReferencesForTaxInvoice`
   + `useCrossReferences` hook; cross-ref chips on TI/RC/CN/DN detail.
   **BN detail page (ckpt3) already pre-wires the chip pattern** — extending
   to CN/DN is mostly UI. ~2-3 hr.
2. **P10** — Company logo: multipart endpoint via attachments table
   parent=COMPANY_PROFILE; /settings/company UI; every doc header
   renders logo with text fallback; PDF embed via QuestPDF `Image()`. ~3 hr.
3. **P11** — XML 0-byte: live-debug Tier 1 config; verify DO→TI
   auto-create triggers signing pipeline; download endpoint reads
   `etax.submissions.signed_xml_blob` with 404 fallback
   (`urn:teas:error:etax.not_yet_signed`). Repost a TI → MailHog +
   XML > 0 bytes + valid XAdES-BES `<ds:Signature>`. ~2 hr.
4. **E2E specs (7 remaining):** quotation-lifecycle, sales-order-flow,
   delivery-order-flow, tax-invoice-from-quotation, receipt-cross-ref,
   rbac-chapter3, product-type-wht. All use `TestIds.*` random suffix
   (CLAUDE.md §15). Pattern: `billing-note-flow.spec.ts` from ckpt3.
   Run = Sana ch.3 deep-mode / CI.
5. **Report-Backend31** (= sprint completion), progress.md cont. 56,
   plan.md tick proposed in §→ Sana, mirror `Y:\AccountApp`, notify
   Dispatch → Sana RE-VALIDATE **deep mode**.

## Deferred FE polish from prev checkpoints (do these alongside their phases)

These are NOT blocking — the BE side already ships. Pick them up when
touching the same file for another reason.

- **P4 FE tail** — Q lifecycle UI. BE endpoints `PUT /{id}`, `DELETE /{id}`,
  `/cancel`, `/pdf` all live (ckpt2). FE work remaining:
  - `frontend/app/(dashboard)/quotations/[id]/edit/page.tsx` — NEW.
  - `frontend/app/(dashboard)/quotations/[id]/page.tsx` — Draft = Edit + Delete buttons.
  - `frontend/app/(dashboard)/quotations/page.tsx` — trash icon on Draft rows.
  - `frontend/components/forms/QuotationForm.tsx` — accept `initial` + `mode` props.
  - `frontend/lib/queries.ts` — `useUpdateQuotation`, `useDeleteQuotation`.
  - `frontend/messages/{th,en}.json` — `quotation.edit/delete/cancelConfirm/...`.

- **P7 FE polish** — product_type wiring. BE shipped ckpt2. FE work remaining:
  - `frontend/components/ui/LineItemsTable.tsx` — readOnly tax_rate when product picked.
  - `frontend/app/(dashboard)/tax-invoices/new/page.tsx` — lock taxRate cell.
  - `frontend/app/(dashboard)/receipts/new/page.tsx` — WHT auto-base = Σ(SERVICE ex-VAT).
  - `frontend/components/forms/AdjustmentNoteForm.tsx` — lock taxRate.
  - `backend/tests/Accounting.Domain.Tests/Sales/*` — new tests: product_type
    snapshot preserves Q→SO→DO→TI; WHT base = SERVICE-only.

- **P3 sweep tail** — single-source Thai date util in place. Remaining sweep:
  - AdjustmentNoteForm RC date label EN (BUG #5).
  - Audit every chapter-3 toast call site for hard-coded "Posted" / "Draft
    saved" / "Saved" — replace with `tc('posted')` / `tc('draftSaved')` / `tc('save')`.
  - Form date input convention note — document CE input vs. BE display split.

- **BN P6.2 polish** (NEW this session; defer to Sprint 13i):
  - Replace browser `confirm()` for Draft delete with shared AlertDialog.
  - Multi-TI picker on BillingNoteForm (currently API-only field).
  - BN → settled auto-derive on full receipt application (currently manual).

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
- If context starts thinning before all 3 phases + E2E land, write a
  checkpoint 5 the same way: ship clean, defer rest, update
  Session-Resume.md's phase table + write Report-Backend32 with the
  honest table.

## Things to watch out for

- **P11 XML 0-byte is the highest-risk phase** — it crosses signing pipeline +
  download endpoint + e-Tax submission table. Verify Tier 1 config FIRST
  (`ETax:Enabled=true`, `ETax:AutoSendOnTaxInvoicePost=true`, PFX path/password)
  before chasing the byte-count bug. Common cause: DO→TI auto-create bypasses
  the e-Tax pipeline because the TI is created from a sibling document.
- **P10 logo PDF embed** uses QuestPDF `Image()` which expects a byte[]. Read
  the file from local disk storage (LocalDiskFileStorage already in DI) — DO
  NOT inline base64 in the PDF.
- **P8 cross-ref service** is generalisable — design for `docType`:
  `tax_invoice | receipt | credit_note | debit_note | quotation | billing_note`.
  The BN detail page already has the chip-render shape.

## When you're done

1. `Report-Backend31.md` written — sprint COMPLETION report.
2. `progress.md` cont. 56 prepended with gates table + decisions + → Sana.
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
