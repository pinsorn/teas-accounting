# Draft edit for Receipts (ใบเสร็จรับเงิน, RC) and Tax Invoices (ใบกำกับภาษี, TI)

> **Blast-radius cap: max 30 files** (see §9). Public-API changes allowed: 4 additive BFF routes
> (`PUT /receipts/{id}`, `GET /receipts/{id}/draft-input`, `PUT /tax-invoices/{id}`,
> `GET /tax-invoices/{id}/draft-input`) + 2 new error codes + `update_receipt_draft` MCP behaviour/description.
> NO DTO widening, NO new permission, NO migration/SQL script, NO `/api/v1` route.
> Designed 2026-10-04 (opus-designer). Branch `feat/draft-edit-receipt-taxinvoice`.

## 0. Headline

Ship web draft-edit for RC and TI, mirroring Quotation/SO/BillingNote (`PUT` + `[id]/edit` page reusing
the create form). The service methods (`UpdateDraftAsync`) already exist and are only reached by MCP
today. The investigation changed the task in four places — read these before anything else:

1. **There is a real money race in the existing `ReceiptService.UpdateDraftAsync`.** An edit racing a
   post can rewrite a *posted* VAT receipt's settlement rows undetected: `receipt_applications` /
   `receipt_wht_lines` have **no** immutability trigger, a VAT receipt has **zero** `receipt_lines`
   (so the "always rewrite lines → trigger 582 backstop" argument in the code comments is false for
   it), header trigger 570 only fires on a named field allowlist, and `Version` is a concurrency token
   that **nothing ever increments**. Fix = row lock (`SELECT … FOR UPDATE`) + `Version++` in both
   `UpdateDraftAsync`s (§3.2). Exposing the route to every web user without this fix would widen the
   hole from "MCP agents" to "everyone".
2. **The detail DTOs cannot prefill an edit form faithfully** (they drop payment method/bank account
   → GL debit account, `quotationId`, `paymentTerms`, discount %, tax-code id, product type, BN/DO
   applications…). Round-tripping them is the F8 dropped-field bug class for the third time. Fix = a
   `GET /{id}/draft-input` that returns the exact `Create*Request` that reproduces the draft, plus a
   FE rule "payload = `{...draftInput, ...formManagedFields}`" (§3.4, §3.6).
3. **A draft TI can be linked into a Billing Note** (`BillingNoteService.BuildTaxInvoiceLinksAsync`
   has no status check and snapshots the TI's totals/lines). Editing such a TI would strand the BN
   in `Issued` forever (it settles only when Σ TI.AmountPaid ≥ BN total). New refusal
   `ti.linked_to_billing_note`, with a traced in-app exit (§3.3).
4. **Status codes differ from the brief.** "Edit a posted doc" returns **422**
   (`rc./ti.cannot_edit_after_post`), not 409 — the repo convention for every `*.cannot_edit_after_*`
   code (`DomainExceptionMiddleware.StatusFor`). 409 is `rc./ti.locked_mismatch` (lost race). The
   middleware is NOT changed.

The MCP `update_receipt_draft` wipe bug is confirmed and fixed (§3.5).

## 1. Facts established in code (VERIFIED unless marked ASSUMED)

### 1.1 Routes / services
- `ReceiptEndpoints.cs` (whole file): POST `/`, POST `/{id}/post`, POST `/{id}/wht-cert`, GET `/`,
  POST `/wht-base-suggest`, GET `/{id}`, `/pdf`, `/paper`. **No PUT, no DELETE.** Policies:
  `readPol`/`createPol`/`postPol` locals at :15-17.
- `TaxInvoiceEndpoints.cs` (whole file): POST `/`, `/{id}/post`, GET `/`, `/{id}`, `/xml`, `/pdf`,
  `/paper`, POST `/{id}/resend`. **No PUT, no DELETE.**
- Convention for draft PUT = same policy as create, body = same create DTO + same validator, returns
  204: `SalesChainEndpoints.cs:50-57` (quotation, `.manage`), `:103-110` (SO), `BillingNoteEndpoints.cs:33-40`.
- `IReceiptService.UpdateDraftAsync` — `ReceiptDtos.cs:160`; impl `ReceiptService.cs:420-478`.
  Loads with `Include(Applications/Lines/WhtLines)`, guard `rc.cannot_edit_after_post` (:430),
  calls the SHARED `RebuildLinesAndTotalsAsync` (:180-405, same as create), delete-and-recreates all
  three child sets, single `SaveChangesAsync`, **no explicit transaction, no lock, no `Version++`,
  no activity record.**
- `ITaxInvoiceService.UpdateDraftAsync` — impl `TaxInvoiceService.cs:547-637`. Guard
  `ti.cannot_edit_after_post` (:560), re-runs `EnsureVatRegisteredAsync`, BU checks,
  `EnsureQuotationNotInvoicedAsync(req.QuotationId, taxInvoiceId)` then **assigns
  `ti.QuotationId = req.QuotationId`** (a missing `quotationId` in the body UNLINKS the quotation),
  rebuilds lines with `deriveLineTax: true`. Same gaps: no tx, no lock, no `Version++`, no activity.
- Error→HTTP: `Accounting.Api/Middleware/DomainExceptionMiddleware.cs:30-41`: `*.not_found`→404,
  `*.locked_mismatch`/`*.body_mismatch`/`*.already_invoiced`→409, everything else →422.
  So `rc./ti.cannot_edit_after_post` = **422** (same as `quotation.cannot_edit_after_send`).
- `ReceiptService.PostAsync` (:487-501) and `TaxInvoiceService.PostAsync` (:639-660) already map
  `DbUpdateConcurrencyException` and 23514 to `rc./ti.locked_mismatch` (409). Their doc-comments say
  "Version is never incremented … EF's optimistic-concurrency check never actually diverges" — this
  becomes false after this spec and must be updated.
- `NumberedDocumentWriter.cs:88` catches ONLY `IsDocNoCollision` → a `DbUpdateConcurrencyException`
  inside post propagates to `PostAsync`'s mapper. VERIFIED.
- Audit-activity on edit is the sibling convention: `QuotationChainServices.cs:253`
  (`activity.Record("Quotation", …, "Updated")`), `SalesOrderDeliveryServices.cs:143`.

### 1.2 Numbering / GL / balances (the money facts)
- RC DocNo: allocated only in `PostCoreAsync` via `NumberedDocumentWriter.AllocateAndSaveAsync`
  (`ReceiptService.cs:~556`). `CreateDraftAsync` never touches `_numbers`. TI DocNo: `TaxInvoice.cs`
  doc-comment "NULL until posted"; allocated only in `PostCoreAsync` (`TaxInvoiceService.cs:~720`).
  ⇒ **editing a draft consumes no number.**
- GL: receipt JE only in `PostCoreAsync` (`_gl.PostReceiptAsync`), TI JE only in `PostCoreAsync`
  (`_gl.PostTaxInvoiceAsync`). Drafts have no JE. `gl.journal_entries` has **no source-doc column**
  (`JournalEntry.cs` fields) — tests count JEs per company.
- GL debit account of a receipt depends on `PaymentMethod` (`GlPostingService.cs:106`:
  Cash → CashAccount, else BankAccount). ⇒ the FE must never overwrite a draft's payment method.
- **Drafts reserve nothing.** `TaxInvoice.AmountPaid` is incremented only in receipt `PostCoreAsync`
  (:~575). Create/update over-application check is `app.AppliedAmount > ti.TotalAmount - ti.AmountPaid`
  (`ReceiptService.cs:~247`). The draft's OWN old applications are not in `AmountPaid`, so **no
  self-exclusion is needed**. Post re-checks (`receipt.over_applied`, :~579).
- Direct BillingNote applications (non-VAT): create/update only check status (not Draft, not Settled,
  same customer — :~280-300); the amount guard runs at post (`receipt.over_applied`, :~650). Same
  validations on create and update by construction (shared helper). Do NOT add a new draft-time check.
- Receipt DocDate: pinned to today at create (:~105), **not re-pinned at edit or at post**
  (`PostCoreAsync` uses `rc.DocDate`). TI DocDate/TaxPointDate: pinned at create, untouched by edit,
  **re-pinned to today at post** (`TaxInvoiceService.cs:~690`).

### 1.3 Concurrency backstops (what exists — read the SQL, not the comments)
- `SqlScripts/570_receipt_immutability_rls.sql`: header trigger fires only if one of
  doc_no, doc_date, customer_id, customer_tax_id, amount, total_amount, total_amount_thb, wht_amount,
  cash_received, currency_code, exchange_rate, company_id, branch_id changes on a POSTED row. No
  trigger on `sales.receipt_applications` or `sales.receipt_wht_lines` (grep of all SqlScripts:
  `receipt_applications` appears only in a 570 comment).
- `SqlScripts/582_posted_lines_immutable_v2.sql`: line triggers for `sales.tax_invoice_lines`,
  `sales.receipt_lines` (UPDATE/DELETE blocked when parent non-DRAFT; INSERT not guarded).
- A VAT receipt (applies TIs) has an empty `Lines` collection (create builds `Lines` only from
  `req.Lines`). ⇒ for it, an edit performs NO line DELETE, so 582 never fires.
- `Receipt.Version` / `TaxInvoice.Version` are `IsConcurrencyToken()`
  (`ReceiptConfiguration.cs:57`, `TaxInvoiceConfiguration.cs:75`); grep for `Version++` in
  Infrastructure/Domain finds none for RC/TI.
- ⇒ Race "edit loaded Draft → post commits → edit saves": edit deletes+inserts applications (no
  trigger), header UPDATE `WHERE version = old` still matches (post didn't bump), header trigger 570
  passes if amount unchanged and only e.g. Notes changed. **Posted VAT receipt silently re-pointed
  from TI A to TI B while the JE/AmountPaid were booked against A.** Race "post loaded → edit commits
  → post saves": post books AmountPaid against its in-memory (old) applications, then the GL reads
  the new ones from DB.

### 1.4 RLS / tenancy
- `sales.receipts` and `sales.tax_invoices` have FORCE RLS `company_isolation` (570 / 040 family);
  child tables inherit via FK + EF global filter. Prod runs NOBYPASSRLS; `teas_test`/dev connect as
  SUPERUSER (memory `rls-masked-by-superuser-tests`) — EF filter is the only scoping in tests unless
  the test switches role. Pattern: `IdempotencyDocumentFenceTests.cs:960-1001` (`SET ROLE
  pg_database_owner` + `set_config('app.company_id', …)` on a manually opened connection, raw ADO.NET
  command, `RESET ROLE` in finally). `teas_rls_test` role has NO `sales` grants
  (`PostgresFixture.cs:143-148`) — do not use it.

### 1.5 Idempotency fence (WP-J)
- `IdempotencyMiddleware.cs:61`: only `/api/v1` + POST/PUT/PATCH + API-key principal. The new PUTs
  are BFF routes (not `/api/v1`) → middleware not involved; `UpdateDraftAsync` never reads `_idem`.
  ⇒ `(created_via_api_key_id, idempotency_key, idempotency_request_hash)` and
  `created_via_api_key_name` are untouched by an edit. Consequence (intended): replaying the ORIGINAL
  keyed create (same key, same body) after an edit returns the same id — it converges on the
  (now edited) draft; a different body is still `idempotency.body_mismatch` 409.

### 1.6 Billing Note ↔ draft TI (new consumer found by the sweep)
- `BillingNoteService.BuildTaxInvoiceLinksAsync` (:229-245): links ANY TI of the company (no status
  filter), snapshots `AppliedAmount = TI.TotalAmount`; when the BN has no manual lines,
  `ApplyTaxInvoiceLinesAsync` copies the TI lines (:80-81).
- BN settles only in receipt post: Σ `TI.AmountPaid` over linked TIs ≥ `bn.TotalAmount`
  (`ReceiptService.cs:~605-635`).
- Exit verification: `BillingNoteService.DeleteDraftAsync` (:294, Draft only),
  `UpdateDraftAsync` (:247, Draft only — can drop the TI link), `CancelAsync` (:367-385) allows
  Draft/Issued → Cancelled **unless `bn.JournalEntryId` is set**; a JE is posted on issue only when
  `!tax.VatMode` (:360-361). TIs exist only in VAT companies (`EnsureVatRegisteredAsync`), and an
  edit on a non-VAT company is already refused (`ti.non_vat_blocked`). ⇒ for every BN that can link
  a TI that can be edited, cancel is available.
- `BillingNoteService.IssueAsync` (:~311-335) checks only Draft status and line/TI reconciliation —
  it does NOT require linked TIs to be Posted. ⇒ an **Issued** BN linking a **Draft** TI is reachable
  (T10's second half is valid).
- FE RBAC e2e (`rbac-ui-gating.spec.ts`, `rbac-chapter3.spec.ts`) has no assertion on receipt/TI
  detail buttons (grep `rc-post-action|ti-post-action|receipts/|tax-invoices/`: no match) — the new
  `rc-edit`/`ti-edit` links break no exact-set assertion.

### 1.7 Other consumers of a draft RC/TI (sweep, all VERIFIED harmless)
| consumer | needs status | file |
|---|---|---|
| Receipt applies TI | Posted only (`rc.ti_not_posted`) | `ReceiptService.cs:~240` |
| CN/DN on TI | Posted only | `TaxAdjustmentNoteService.cs:59` |
| DO.TaxInvoiceId | set by `GenerateTiAsync`, which posts immediately | `SalesOrderDeliveryServices.cs:453,470-473` |
| e-Tax submission | post-time only | `TaxInvoiceService.cs:~740` |
| Bank rec match on receipt | Posted only | `BankReconciliationService.cs:64,121` |
| WhtCertificate.ReceiptId | created at post (`AddReceivableCertsAsync`) | `ReceiptService.cs:~590` |
| BN ↔ TI join | **any status** → guard §3.3 | `BillingNoteService.cs:229` |

### 1.8 MCP
- `update_receipt_draft` (`TeasMcpTools.cs:1537-1575`): always sends `Applications: []` and never
  passes `WhtLines`; on a settlement receipt sent with lines it silently converts it into a cash bill
  (applications wiped); on ANY receipt it silently drops WHT. Without lines on a settlement receipt it
  fails validation (cannot express the receipt at all).
- `create_receipt_draft` settlement mode (:479-555): `InvoiceId` → VAT: re-runs
  `authz.AuthorizeAsync(user, null, TaxInvoiceRead)` (F5 mirror) then full-outstanding application;
  non-VAT: BillingNote full-total application; optional single WHT line from `WhtTypeId/WhtBaseAmount`.
- `update_tax_invoice_draft` (:1500-1535) passes `request.QuotationId` through — OK, no change needed.

### 1.9 Frontend
- No `receipts/[id]/edit`, no `tax-invoices/[id]/edit`. Create pages are monolithic:
  `app/(dashboard)/receipts/new/page.tsx` (682 lines), `app/(dashboard)/tax-invoices/new/page.tsx`
  (315 lines). Pattern to follow: thin page → `components/forms/<Doc>Form.tsx` with optional `edit`
  prop: `invoices/new/page.tsx` (24 lines) + `invoices/[id]/edit/page.tsx` (29 lines) +
  `components/forms/BillingNoteForm.tsx` (`edit` prop, `isEdit`, `reset` on load, PUT, redirect to
  detail).
- Receipt form HARDCODES on save (`receipts/new/page.tsx:288-298`): `paymentMethod:'Transfer'`,
  `chequeNo/chequeDate/bankAccountId: null`, `notes: null`, `currencyCode:'THB'`, `exchangeRate:1`.
- TI form HARDCODES (`tax-invoices/new/page.tsx:100-127`): `quotationId:null`,
  `isTaxInclusive:false`, `notes/paymentTerms/dueDate:null`, `uomId:1`; zod `lineSchema` (:28-45)
  has no `productType`/`uomId` → zod strips them.
- Receipt form WHT is entered PER LINE and aggregated by type on save (`aggregatedWhtLines`,
  :222-230); two effects rebuild the WHT table automatically (:236-238 seed-from-suggestion,
  :243-254 standalone sync-from-lines) — both would overwrite a prefilled stored WHT table.
- `lib/i18n/problems.ts` has **zero** `rc.*` / `ti.*` entries → those codes toast raw English today.
- Mutation-hook pattern: `useUpdateBillingNote` (`lib/queries.ts:1907-1921`), `useUpdateQuotation`
  (:1707). Detail hooks: `useReceipt` (:212, key `['receipt', id]`), `useTaxInvoice` (:160, key
  `['tax-invoice', id]`). `usePostReceipt` (:226-241) shows the invalidation set.
- Detail pages: `receipts/[id]/page.tsx` (`useHasScope` :35, post CTA `rc-post-action` :126-134);
  `tax-invoices/[id]/page.tsx` (post CTA `ti-post-action` :119-125). Edit-link reference:
  `invoices/[id]/page.tsx:100-105` (`bn-edit`, `Pencil` icon, `btn btn-secondary btn-sm gap-1`).
- TS types: `CreateReceiptRequest` (`lib/types.ts:1520`), `ReceiptApplicationInput` (:1508, has
  `taxInvoiceId?/billingNoteId?/deliveryOrderId?`), `ReceiptLineInput` (:1515, has `productCode?`),
  `CreateTaxInvoiceRequest` (:398), `CreateTaxInvoiceLineInput` (:383-397, **no `productType`**).

### 1.10 Gates that exist / do NOT exist (checked)
- EXISTS: `RbacAuthMapTests` regenerates `docs/rbac/endpoint-permission-map.generated.md`
  (`RbacAuthMapTests.cs:138-150`); `RbacCartesianTests` fires every Perm route (PUT with body `{}`
  → validator 400; GET with id 999999999 → 404) — safe for the new routes. CI (`.github/workflows/ci.yml`):
  `dotnet test`, `tsc --noEmit`, `pnpm lint`, `pnpm vitest run`, `pnpm build`. **CI does NOT run
  Playwright.**
- DOES NOT EXIST: any openapi-vs-routes test (grep for `openapi.yaml` in tests/CI: none);
  any th/en message-parity test (grep: none); any Bengali-glyph automated check. These are MANUAL steps.

### 1.11 Footguns folded in (troubles-wiki + memory) — do not rediscover
- Wiki "Posted-document immutability trigger doesn't fire on a header-only field edit" (:516) —
  correct as far as it goes; its claim that always-rewriting lines is a *uniform* backstop is false
  for VAT receipts (empty `Lines`). Update the entry (WP-4).
- Wiki "A draft tax invoice created from a quotation cannot be edited or deleted" (:1658, E4) — this
  spec resolves the edit half. Mark it.
- Wiki "Test asserts an exact past/future DocDate" (:780) — docs are always dated today; never assert
  a client DocDate.
- Wiki "Stale TEAS_TEST_PG connection strings" — current string is the one in that entry; port 5432.
- Memory `teas-test-pg-env-per-shell`: env dies between PowerShell calls — set `$env:TEAS_TEST_PG`
  in the SAME command as `dotnet test`; compare the **skipped** count to baseline (skips fake green).
- Memory `teas-repo-root-rbac-tests`: `RbacAuthMapTests` throws "Could not locate the TEAS repo root"
  unless `$env:TEAS_REPO_ROOT` is set (from subst drives). Set it.
- Memory `relative-date-seed-temporal-tests` / `subagent-misattributes-fresh-db-fail`: use today's
  date; read the failing assertion before blaming "known co2/co3 gap".
- Memory `thai-mo-glyph-pitfall`: Bengali `ম` (U+09AE) creeps into Thai strings — grep before done.
- Memory `stale-next-dev-no-hot-reload`: restart the `:3000` server before e2e.
- Memory `git-add-u-misses-new-files`: this spec creates NEW files (forms, edit pages, test, e2e) —
  report the full new-file list; the orchestrator stages explicitly.
- Memory `test-data-via-ui-only` applies to the local/dev stack (e2e builds its data through the
  UI/API, never SQL INSERT). Integration tests on `teas_test` may use raw SQL only where stated (§6 T9).
- Memory `webappfactory-usesetting-minimal-hosting`: HTTP tests use `RbacApiFactory` (already does
  `UseSetting`); don't build a new factory.

## 2. Consumer sweep

No enum/discriminator is widened. Two seams ARE widened and swept:

**(a) New writer of a draft TI's totals (web PUT, previously MCP-only).** Consumers that snapshot a
draft TI: table §1.7. Disposition: BN join → **extend** (guard §3.3); all others → **skip** (they
require Posted).

**(b) New error codes** `ti.linked_to_billing_note` (422) and use of `rc./ti.locked_mismatch` from
the edit path.

| consumer | disposition |
|---|---|
| `DomainExceptionMiddleware.StatusFor` | skip — suffix rules already give 422 / 409 correctly |
| `lib/i18n/problems.ts` | extend — add 6 Thai entries (§3.6.6) |
| MCP `update_tax_invoice_draft` | skip — DomainException surfaces verbatim like every tool; its existing 23514 catch stays |
| MCP `update_receipt_draft` | extend (§3.5) |
| FE `errorToToast` callers | none to change — resolve by code |

## 3. Design

### 3.1 Endpoints (exact)

`ReceiptEndpoints.cs` — add after the POST `/` (keep the file's local policy vars):
```csharp
// draft-edit-receipt-taxinvoice — Draft-only full replace; same policy/validator/DTO as create
// (mirrors SalesChainEndpoints quotation PUT). 204. Posted → 422 rc.cannot_edit_after_post.
group.MapPut("/{id:long}", async (long id, [FromBody] CreateReceiptRequest req,
    IValidator<CreateReceiptRequest> validator, IReceiptService service, CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(req, ct);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    await service.UpdateDraftAsync(id, req, ct);
    return Results.NoContent();
})
.RequireAuthorization(createPol);

// The exact CreateReceiptRequest that reproduces this Draft — the edit form's prefill.
group.MapGet("/{id:long}/draft-input", async (long id, IReceiptService svc, CancellationToken ct) =>
    await svc.GetDraftInputAsync(id, ct) is { } r ? Results.Ok(r) : Results.NotFound())
.RequireAuthorization(createPol);
```
`TaxInvoiceEndpoints.cs` — identical shape with `CreateTaxInvoiceRequest`,
`IValidator<CreateTaxInvoiceRequest>`, `ITaxInvoiceService`, policy
`PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceCreate`.

**Permission decision: reuse `sales.receipt.create` / `sales.tax_invoice.create`.** Repo convention:
every draft PUT reuses the create-side permission (Q/SO/BN reuse `.manage`, the only write perm they
have); the MCP edit tools already use `ReceiptCreate`/`TaxInvoiceCreate`. Editing a draft has no GL
effect, so it carries no more authority than creating one. Rejected: a new `.update` perm — would
need a seed script + grants for every role + RbacAuthMap/Cartesian churn (memory
`rbac-seed-ordering-footgun`) for zero security gain. ⇒ **no SQL seed file.**

`draft-input` policy = create (only editors need it; a read-only user has the detail page).

### 3.2 Service: `UpdateDraftAsync` hardening (both services) — the concurrency fix

Exact skeleton for `ReceiptService.UpdateDraftAsync` (TI identical with `sales.tax_invoices` /
`tax_invoice_id`, `ti.*` codes, and `EnsureVatRegisteredAsync` kept FIRST, before the tx):
```csharp
public async Task UpdateDraftAsync(long receiptId, CreateReceiptRequest req, CancellationToken ct)
{
    if (!_tenant.IsAuthenticated) throw new DomainException("auth.required", "User must be authenticated.");

    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    // Row lock FIRST, before the EF load. Serializes against PostCoreAsync, whose own UPDATE of this
    // row takes the same lock. Predicate is id-only on purpose: RLS (prod) hides other companies'
    // rows from FOR UPDATE; the EF load below + its not_found throw do the scoping in tests.
    await _db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT receipt_id FROM sales.receipts WHERE receipt_id = {receiptId} FOR UPDATE", ct);

    var rc = /* existing Include(...) load, unchanged */;
    if (rc.Status != DocumentStatus.Draft) throw /* existing rc.cannot_edit_after_post */;
    // ... existing body UNCHANGED (BU lock, customer, RebuildLinesAndTotalsAsync, field copy,
    //     delete-and-recreate children, totals) ...
    rc.Version++;                                   // makes a racing post's UPDATE miss (WHERE version=old)
    _activity.Record("Receipt", rc.ReceiptId, rc.DocNo, rc.CompanyId, "Updated");
    try { await _db.SaveChangesAsync(ct); }
    catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsPostedRaceViolation(ex))
    {
        throw new DomainException("rc.locked_mismatch",
            "This receipt was changed by someone else. Reload and try again.");
    }
    await tx.CommitAsync(ct);
}
```
Two-direction trace (put this reasoning in the method's doc-comment, replacing the "582 is the
uniform backstop" paragraph):
- **Edit locks first:** post's header UPDATE blocks on the row lock; when the edit commits, Postgres
  re-evaluates post's `WHERE … version = @old` against the new row (version+1) → 0 rows →
  `DbUpdateConcurrencyException` → `PostAsync` maps to `rc.locked_mismatch` (409); post's tx rolls
  back (no DocNo, no JE, no AmountPaid). User reloads and posts the edited draft.
- **Post updates first:** edit's `FOR UPDATE` blocks until post commits, then the EF load (same tx,
  READ COMMITTED, new snapshot per statement) reads `Status = Posted` → `rc.cannot_edit_after_post`
  (422). No child row touched.
- Rejected: `pg_advisory_xact_lock` — post never takes it, so it serializes nothing. Rejected:
  `Version++` alone — post never bumps Version, so edit-after-post-commit would still pass.
  Rejected: a trigger on `receipt_applications` — migration, larger blast radius, and still blind to
  the post-loaded-before-edit direction.
- Update the doc-comments of BOTH `PostAsync` methods: Version is now incremented by
  `UpdateDraftAsync`, so the `DbUpdateConcurrencyException` branch is reachable (edit-vs-post race).
- Keep the existing `IsPostedRaceViolation` helper (it already exists in both classes).
- MCP tools' existing 23514 catch stays (now mostly unreachable; harmless).

**TI only — BN guard** (after the status check, before any mutation):
```csharp
var bnRef = await _db.BillingNotes.AsNoTracking()
    .Where(b => b.Status != BillingNoteStatus.Cancelled
             && b.TaxInvoiceLinks.Any(j => j.TaxInvoiceId == taxInvoiceId))
    .Select(b => new { b.BillingNoteId, b.DocNo })
    .FirstOrDefaultAsync(ct);
if (bnRef is not null)
    throw new DomainException("ti.linked_to_billing_note",
        $"ใบกำกับภาษีนี้ถูกอ้างอิงในใบแจ้งหนี้ {bnRef.DocNo ?? "#" + bnRef.BillingNoteId} — " +
        "แก้ไข/ลบใบแจ้งหนี้ฉบับร่าง หรือยกเลิกใบแจ้งหนี้ก่อน " +
        $"(Tax Invoice {taxInvoiceId} is linked from Invoice {bnRef.DocNo ?? bnRef.BillingNoteId.ToString()}; " +
        "edit/delete that draft Invoice or cancel it first.)");
```

### 3.3 Guard-exit trace for `ti.linked_to_billing_note`
State when it fires: Draft TI T is referenced by BN B (status Draft or Issued; Settled is impossible
because T, being draft, has AmountPaid 0 and receipts can't apply drafts; Cancelled is excluded).
Exits, all in-app, no DBA:
- B is Draft → edit B (`/invoices/{id}/edit`) to remove T, or delete B (`bn-delete`) → edit T.
- B is Issued → cancel B (`bn-cancel-toggle`; allowed because a VAT company's BN has no JE, §1.6) →
  edit T. B's BN number is consumed (normal for a cancelled document).
- Or don't edit: post T as-is (still allowed).
Test T10 walks the Draft-BN exit end-to-end.

**No customer / currency pin on chain-created TIs (deliberate).** Currency is THB-only by validator
(`ThbOnly`, `TaxInvoiceDtos.cs:134`). Customer change is allowed because no GL/VAT/AR invariant keys
on source-document customer equality (receipts validate `TI.CustomerId == receipt.CustomerId` at
apply time), a lock would add a refusal with no exit (a BN→TI draft blocks re-conversion via
`bn.ti_exists` and has no delete route — the edit IS the exit), and the MCP tool already permits it.
Source links (`QuotationId` via body passthrough; `BillingNoteId/SalesOrderId/DeliveryOrderId` not in
the DTO → never touched by update) survive the edit. No remaining-quantity logic exists for TI
sources (grep `remaining|InvoicedQty`: none), so nothing to recompute.

### 3.4 `GetDraftInputAsync` (both services; put in the `.Read.cs` partials)

Add to the interfaces (`ReceiptDtos.cs` `IReceiptService`, `ITaxInvoiceService.cs`):
```csharp
/// <summary>The exact request that reproduces this Draft (edit-form prefill). Null = not found
/// (tenant-scoped). Non-Draft → rc.cannot_edit_after_post. PUT(GetDraftInputAsync(id)) is a
/// no-op on every persisted column except UpdatedAt/UpdatedBy/Version (invariant I4).</summary>
Task<CreateReceiptRequest?> GetDraftInputAsync(long receiptId, CancellationToken ct);
```
Receipt mapping (AsNoTracking, Include Applications/Lines/WhtLines; auth check like `GetDetailAsync`):
```csharp
new CreateReceiptRequest(
    r.DocDate, r.CustomerId, r.PaymentMethod, r.ChequeNo, r.ChequeDate, r.BankAccountId,
    r.CurrencyCode, r.ExchangeRate, r.Notes,
    Applications: r.Applications.OrderBy(a => a.ApplicationId)
        .Select(a => new ReceiptApplicationInput(a.TaxInvoiceId, a.AppliedAmount, a.DeliveryOrderId, a.BillingNoteId)).ToList(),
    BusinessUnitId: r.BusinessUnitId,
    WhtAmount: r.WhtAmount, WhtTypeId: r.WhtTypeId,          // verbatim; rebuild prefers WhtLines when non-empty
    CustomerWhtCertNo: r.CustomerWhtCertNo, CustomerWhtCertDate: r.CustomerWhtCertDate,
    WhtLines: r.WhtLines.OrderBy(w => w.ReceiptWhtLineId).Select(w => new ReceiptWhtLineInput(w.WhtTypeId, w.BaseAmount)).ToList(),
    Lines: r.Lines.OrderBy(l => l.LineNo).Select(l => new ReceiptLineInput(
        l.DescriptionTh, l.Quantity, l.UnitPrice, l.Amount, l.ProductId, l.ProductCode, l.ProductType, l.UomText)).ToList())
```
TI mapping:
```csharp
new CreateTaxInvoiceRequest(
    t.DocDate, t.CustomerId, t.IsTaxInclusive, t.CurrencyCode, t.ExchangeRate,
    t.Notes, t.PaymentTerms, t.DueDate,
    t.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceLineInput(
        l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity, l.UomId, l.UomText,
        l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.ProductType)).ToList(),
    t.BusinessUnitId, t.QuotationId)
```
Rejected: widening `ReceiptDetail` / `TaxInvoiceDetail(Line)` — they feed PDF/paper/MCP `get_*`
output and mix derived rows (TI-derived receipt lines) with stored rows; a separate edit model makes
the round-trip invariant directly testable and keeps the read DTOs untouched.

If T2/T4 (round-trip) exposes a divergence on a FRESHLY created row (e.g. `SalesLineBackstop.Resolve`
maps a stored code/rate differently under `deriveLineTax:true` than the chain-copy stored it), **STOP
and report** with the diff — do not patch `Resolve`.

### 3.5 MCP `update_receipt_draft` fix (TeasMcpTools.cs)

Decision: **full-replace semantics kept, expressed through the same two modes as
`create_receipt_draft`; refuse every case the tool's input cannot faithfully express** (no silent
conversion, no silent WHT loss). Tool signature/name/request type unchanged (backward compatible:
cash-bill edits behave byte-identically).

1. Extract the settlement-resolution block of `CreateReceiptDraftAsync` (the `if (request.InvoiceId is { } invoiceId) { … }`
   body, including the `authz.AuthorizeAsync(user, resource: null, TaxInvoiceRead)` check and the WHT
   line construction) into one private helper, e.g.
   `ResolveSettlementAsync(long invoiceId, int? whtTypeId, decimal? whtBaseAmount, ICompanyTaxConfigService taxCfg, AccountingDbContext db, IAuthorizationService authz, ClaimsPrincipal user, CancellationToken ct)`
   returning `(IReadOnlyList<ReceiptApplicationInput> Applications, List<ReceiptWhtLineInput>? WhtLines)`.
   **The authz check travels with the helper** — create's behaviour must be byte-identical
   (existing tests `Mcp_create_receipt_draft_settlement_mode_*` stay green).
2. `UpdateReceiptDraftAsync` gains DI params `ICompanyTaxConfigService taxCfg, AccountingDbContext db,
   IAuthorizationService authz, ClaimsPrincipal user` (same as create). After `GuardCustomerAsync` and
   the payment-method parse, load the existing draft's shape:
   ```csharp
   var existing = await db.Receipts.AsNoTracking().Where(r => r.ReceiptId == receiptId)
       .Select(r => new {
           AppCount = r.Applications.Count(),
           HasDoApp = r.Applications.Any(a => a.DeliveryOrderId != null),
           WhtCount = r.WhtLines.Count() })
       .FirstOrDefaultAsync(ct);
   ```
   `existing == null` → skip the guards (the service throws `rc.not_found`, preserving
   `D3_rc_update_cross_tenant_id_not_found`).
3. Rules (in order; each refusal = `McpE2Exception("mcp.domain_rule", …)`, nothing written):
   - `existing.HasDoApp || existing.AppCount > 1` → "Receipt {id} settles several documents (or a
     delivery order); this tool can only express one invoice — edit it in the web app."
   - `existing.WhtCount > 1` → "Receipt {id} carries multi-category withholding; edit it in the web app."
   - `request.InvoiceId` set → `(apps, wht) = ResolveSettlementAsync(...)`; `Lines` = empty.
   - `request.InvoiceId` absent and `existing.AppCount > 0` → "Receipt {id} settles an invoice — pass
     invoiceId to edit it in settlement mode (converting it to a cash bill is not allowed)."
   - `request.InvoiceId` absent and `existing.WhtCount > 0` → "Receipt {id} carries withholding,
     which cash-bill mode cannot express — edit it in the web app."
   - else cash-bill mode exactly as today (per-line `GuardProductAsync`, `Applications: []`).
4. Build `CreateReceiptRequest` with `Applications: apps`, `WhtLines: wht`, the lines, the rest as today.
5. Replace the tool `[Description]` with: "Edit a DRAFT receipt — full replace (delete-and-recreate).
   Same two modes as create_receipt_draft: (1) invoiceId set — settlement mode: re-settles that posted
   invoice in FULL (amount = current outstanding), optional whtTypeId+whtBaseAmount (omitting them
   REMOVES withholding); requires sales.tax_invoice.read on a VAT company. (2) invoiceId absent —
   standalone cash-bill: every line needs a valid productId. Refused (mcp.domain_rule): a settlement
   receipt without invoiceId, a receipt settling several documents or a delivery order, and any
   withholding this tool cannot express (multi-category, or cash-bill mode) — edit those in the web
   app. Totals/WHT are recomputed server-side. Only Draft; posted → rc.cannot_edit_after_post.
   doc_date is server-controlled."

### 3.6 Frontend

#### 3.6.1 File moves (repo pattern, like BillingNoteForm)
- Move the body of `app/(dashboard)/receipts/new/page.tsx` → `components/forms/ReceiptForm.tsx`
  exporting `ReceiptForm({ edit }: { edit?: ReceiptEditProps } = {})`. `receipts/new/page.tsx`
  becomes `'use client'; export default function NewReceiptPage() { return <ReceiptForm />; }`.
- Same for TI → `components/forms/TaxInvoiceForm.tsx`, `TaxInvoiceForm({ edit }: { edit?: TaxInvoiceEditProps } = {})`.
- Create-mode behaviour must be byte-identical (same testids, same payload except the TI additions
  in §3.6.3 bullet 2). Keep `useSearchParams` usage inside the form (unchanged semantics).

```ts
type ReceiptEditProps = {
  id: number;
  input: CreateReceiptRequest;          // from GET /receipts/{id}/draft-input
  customerName: string;                 // from useReceipt(id).data
  whtViews: ReceiptWhtLineView[];       // useReceipt(id).data.whtLines — labels + rates for the WHT table
};
type TaxInvoiceEditProps = { id: number; input: CreateTaxInvoiceRequest; customerName: string };
```
(`ReceiptWhtLineView` TS type: add to `lib/types.ts` only if `ReceiptDetail.whtLines` is not already
typed — check first.)

#### 3.6.2 ReceiptForm — edit mode (exact)
Managed by the form (overridden on save): `customerId, applications, lines, businessUnitId,
whtLines, customerWhtCertNo, customerWhtCertDate`, plus forced `whtAmount: 0, whtTypeId: null`.
Passthrough from `edit.input` (never overwritten): `docDate, paymentMethod, chequeNo, chequeDate,
bankAccountId, currencyCode, exchangeRate, notes`.
- Ignore `?ti/?bn/?customer/?amount` when `edit` is set.
- Initial mode: `input.applications.some(a => a.taxInvoiceId) ? 'ti' : input.applications.some(a => a.billingNoteId) ? 'invoice' : 'standalone'`; set `modeInit.current = true` up front so the vatMode effect never overrides it.
- `apps` = `input.applications.map(a => ({ docId: a.taxInvoiceId ?? a.billingNoteId ?? 0, appliedAmount: a.appliedAmount }))`, or the single empty row if none.
- `lines` = `input.lines?.length ? input.lines.map(l => ({ description: l.descriptionTh, quantity: l.quantity, unitPrice: l.unitPrice, amount: l.amount, productType: (l.productType ?? 'GOOD') as ProductTypeStr, productId: l.productId ?? null, productCode: l.productCode ?? null, uomText: l.uomText ?? null })) : [emptyLine()]`. Add `productCode: string | null` to `LineRow`/`emptyLine` and send it in the payload (create sends `productCode: null`-equivalent today → unchanged server result).
- `customerId` default `input.customerId`; `customerLabel` = `edit.customerName`; `businessUnitId` = `input.businessUnitId ?? null`.
- WHT prefill: `whtOn = (input.whtLines?.length ?? 0) > 0`; `lineWht` = one row per stored line:
  `{ description: \`${v.whtTypeCode}${v.incomeTypeCode ? ' (' + v.incomeTypeCode + ')' : ''}\`, productType: 'SERVICE', base: w.baseAmount, whtTypeId: w.whtTypeId, rate: v.whtRate }` where `v = edit.whtViews.find(x => x.whtTypeId === w.whtTypeId)` (fallback `rate: 0`, `description: '#'+id`); `whtSeeded = true`; new state `whtFrozen = true`.
- `whtFrozen` rules: the standalone sync effect (:243-254) starts with `if (whtFrozen) return;`;
  `applySuggestion()` sets `whtFrozen=false`; the WHT checkbox `onChange` sets `whtFrozen=false` as
  well as `whtOn`. Consequence (accepted, documented in a code comment): in a frozen standalone edit,
  changing line amounts does not re-derive WHT rows — the user edits the base or re-ticks WHT.
- `whtCertNo = input.customerWhtCertNo ?? ''`, `whtCertDate = input.customerWhtCertDate ?? ''`.
- Header/preview date in edit = `input.docDate` (receipt date is not re-pinned).
- Save payload in edit:
  ```ts
  const payload: CreateReceiptRequest = {
    ...edit.input,
    customerId: v.customerId, applications,
    lines: mode === 'standalone' ? reqLines : [],
    businessUnitId,
    whtAmount: 0, whtTypeId: null,
    whtLines: whtOn ? aggWht : [],
    customerWhtCertNo: whtOn ? (whtCertNo || null) : null,
    customerWhtCertDate: whtOn && whtCertDate ? whtCertDate : null,
  };
  await update.mutateAsync({ id: edit.id, req: payload }); return edit.id;
  ```
- Title `isEdit ? t('editTitle') : t('create')`. Cancel → `/receipts/${edit.id}`. Save → toast
  `tc('save')` → `router.push(\`/receipts/${edit.id}\`)`. Post button: save (PUT) then the existing
  confirm dialog with `{ id: edit.id }` → existing post flow → `/receipts/${id}`.

#### 3.6.3 TaxInvoiceForm — edit mode (exact)
Managed: `customerId, lines, businessUnitId`. Passthrough from `edit.input`: `docDate, isTaxInclusive,
currencyCode, exchangeRate, notes, paymentTerms, dueDate, quotationId`.
- `toLine = (l) => ({ descriptionTh: l.descriptionTh, quantity: l.quantity, unitPrice: l.unitPrice, taxRate: l.taxRate, uomText: l.uomText, discountPercent: l.discountPercent, productId: l.productId, productCode: l.productCode, taxCode: l.taxCode, taxCodeId: l.taxCodeId, productType: l.productType ?? undefined, uomId: l.uomId })`; `defaultValues` + a `useEffect` → `reset(...)` keyed on `edit?.id` (BillingNoteForm :182-201 pattern).
- zod `lineSchema` add: `productType: z.enum(['GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE']).nullable().optional(), uomId: z.number().int().positive().optional()`. Payload line map (create AND edit): `uomId: l.uomId ?? 1`, `productType: l.productType ?? null`. Add `productType?: string | null` to `CreateTaxInvoiceLineInput` in `lib/types.ts`. (Create behaviour: product-linked lines are re-typed from the master server-side anyway; free-text lines now send `null` → server default — unchanged result.)
- Save payload in edit: `{ ...edit.input, customerId: v.customerId, businessUnitId, lines: mapped }`.
- `businessUnitId` initial `edit.input.businessUnitId ?? null`; `customerLabel` = `edit.customerName`.
- Header date stays `bangkokToday()` locked (TI is re-pinned at post — same honesty note as BillingNoteForm :35-41).
- Title `isEdit ? t('editTitle') : t('post')`. Cancel/Save → `/tax-invoices/${edit.id}`; Post = PUT then confirm dialog with `{ id: edit.id }`.
- Known, NOT fixed here: the live-preview totals ignore discount/inclusive (pre-existing create behaviour); the detail page after save shows the server truth.

#### 3.6.4 Edit pages (new)
`app/(dashboard)/receipts/[id]/edit/page.tsx` — model on `invoices/[id]/edit/page.tsx`:
```tsx
const d = useReceipt(rcId).data;
const input = useReceiptDraftInput(rcId, d?.status === 'Draft').data;
if (!d) loading; if (d.status !== 'Draft') { router.replace(`/receipts/${rcId}`); loading }
if (!input) loading;
const unsupported =
  input.applications.some((a) => a.deliveryOrderId != null)
  || (input.applications.length > 0 && (input.lines?.length ?? 0) > 0)
  || ((input.whtAmount ?? 0) > 0 && (input.whtLines?.length ?? 0) === 0);   // legacy scalar-only WHT
if (unsupported) return <div data-testid="rc-edit-unsupported" ...>{t('editUnsupported')} + Link back to /receipts/{id}</div>;
return <ReceiptForm edit={{ id: rcId, input, customerName: d.customerName, whtViews: d.whtLines ?? [] }} />;
```
Reason for `unsupported` (FE-only, not a server guard): the form has no DO mode and no "apps + own
lines" mode; legacy scalar WHT would be dropped by the forced `whtAmount:0`. The draft stays
postable and API/MCP-editable, so no state is trapped.
`app/(dashboard)/tax-invoices/[id]/edit/page.tsx` — same minus `unsupported`.

#### 3.6.5 Detail pages — Edit button
- `receipts/[id]/page.tsx`: render `{d.status === 'Draft' && hasScope('sales.receipt.create') && <Link data-testid="rc-edit" href={\`/receipts/${id}/edit\`} className="btn btn-secondary btn-sm gap-1"><Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}</Link>}` next to the Post CTA — visible in BOTH the normal and the `?action=approve` layouts (a human reviewing an agent draft may fix it before approving).
- `tax-invoices/[id]/page.tsx`: same, `data-testid="ti-edit"`, scope `sales.tax_invoice.create`.

#### 3.6.6 Hooks, i18n, error codes
`lib/queries.ts` (place next to the existing receipt / TI hooks):
- `useReceiptDraftInput(id, enabled = true)` → `useQuery({ queryKey: ['receipt-draft-input', id], queryFn: () => apiGet<CreateReceiptRequest>(\`receipts/${id}/draft-input\`), enabled: enabled && id > 0, staleTime: 0 })`.
- `useUpdateReceipt()` → `apiPut(\`receipts/${v.id}\`, v.req)`; `onSuccess` invalidates `['receipts']`, `['receipt', v.id]`, `['receipt-draft-input', v.id]`, `['paper-doc']`, `['doc-chain']`, `['activity']`.
- `useTaxInvoiceDraftInput`, `useUpdateTaxInvoice` — same with `tax-invoices`, `['tax-invoices']`, `['tax-invoice', v.id]`, `['tax-invoice-draft-input', v.id]`, `['paper-doc']`, `['doc-chain']`, `['activity']`.

`messages/th.json` + `messages/en.json` (MANUAL parity — no gate exists):
- `rc.editTitle`: "แก้ไขใบเสร็จรับเงิน" / "Edit receipt"
- `rc.editUnsupported`: "ใบเสร็จฉบับร่างนี้มีรูปแบบที่แก้ไขผ่านหน้าเว็บไม่ได้ (อ้างอิงใบส่งของ หรือภาษีหัก ณ ที่จ่ายแบบเดิม) — บันทึก (Post) ได้ตามเดิม หรือแก้ไขผ่าน API" / "This draft receipt can't be edited in the browser (delivery-order settlement or legacy withholding). You can still post it, or edit it via the API."
- `ti.form.editTitle`: "แก้ไขใบกำกับภาษี" / "Edit tax invoice"

`lib/i18n/problems.ts` — add a commented block `// rc.* / ti.* (specs/draft-edit-receipt-taxinvoice.md)`:
- `'rc.cannot_edit_after_post': 'ใบเสร็จรับเงินนี้บันทึก (Post) แล้ว แก้ไขไม่ได้'`
- `'ti.cannot_edit_after_post': 'ใบกำกับภาษีนี้บันทึก (Post) แล้ว แก้ไขไม่ได้'`
- `'rc.locked_mismatch': 'ใบเสร็จรับเงินนี้ถูกแก้ไขหรือบันทึก (Post) โดยผู้อื่นแล้ว กรุณาโหลดหน้าใหม่แล้วลองอีกครั้ง'`
- `'ti.locked_mismatch': 'ใบกำกับภาษีนี้ถูกแก้ไขหรือบันทึก (Post) โดยผู้อื่นแล้ว กรุณาโหลดหน้าใหม่แล้วลองอีกครั้ง'`
- `'rc.overpaid': 'ยอดรับชำระเกินยอดค้างชำระของใบกำกับภาษี'`
- `'ti.linked_to_billing_note': 'ใบกำกับภาษีนี้ถูกอ้างอิงในใบแจ้งหนี้แล้ว กรุณาแก้ไขหรือลบใบแจ้งหนี้ฉบับร่าง หรือยกเลิกใบแจ้งหนี้ก่อน แล้วจึงแก้ไขใบกำกับภาษี'`

### 3.7 Docs
- `docs/api/openapi.yaml` (manual): under `/receipts/{id}` add `put` (summary "แก้ไขใบเสร็จรับเงิน (Draft only)",
  requestBody = the same schema the POST `/receipts` uses, responses 204 / 404 / 409 locked_mismatch /
  422); add path `/receipts/{id}/draft-input` (`get`, 200 = create-request schema, 404, 422). Same for
  `/tax-invoices/{id}` + `/tax-invoices/{id}/draft-input`. Mirror the quotation entry (:82-118) style.
- `docs/rbac/endpoint-permission-map.generated.md`: regenerated by running `RbacAuthMapTests` (never
  hand-edit). Expect 4 new rows: `PUT /receipts/{id:long}` + `GET /receipts/{id:long}/draft-input`
  → `sales.receipt.create`; the TI pair → `sales.tax_invoice.create`; Perm count +4.

## 4. Invariants (each → test)
- **I1 No GL, no number.** An edit creates no journal entry, allocates no DocNo, and does not move
  any `sys.number_sequences` row; `DocNo` stays NULL. → T1, T3
- **I2 Edit ≡ Create.** For the same body, the post-edit draft's persisted header money columns
  (RC: Amount, TotalAmount, TotalAmountThb, WhtAmount, WhtTypeId; TI: Subtotal, Discount, Taxable,
  NonTaxable, Tax, Total, TotalThb) and child rows (RC applications/lines/wht lines; TI lines with
  every amount + tax code pair) equal those of a fresh `CreateDraftAsync` with that body. Carve-outs:
  ids, DocDate/TaxPointDate (edit keeps the create-time value), Created*/Updated*/Version,
  `CreatedViaApiKey*`, idempotency columns. → T1, T3
- **I3 Balances.** Editing a draft RC never changes `TaxInvoice.AmountPaid/PaymentStatus` or BN
  status; over-application is refused with the same rule as create (`rc.overpaid`, outstanding =
  Total − AmountPaid of POSTED receipts; no self-exclusion needed). Post of the edited draft settles
  exactly the edited applications (AR clears by the edited amount, cash = Amount − WHT). → T3, T5
- **I4 Round-trip.** `PUT(GET draft-input)` changes no persisted column except UpdatedAt/UpdatedBy/
  Version (and adds one activity row). → T2, T4
- **I5 Status + race.** A non-Draft doc is never mutated by edit (422 `cannot_edit_after_post`); an
  edit racing a post never leaves a posted doc whose children differ from what was posted (one side
  loses with 409/422 and its tx rolls back). → T6, T9
- **I6 Tenancy.** Another company's draft is 404 over HTTP and invisible to the lock statement under
  a NOBYPASSRLS role. → T7
- **I7 Fence.** `created_via_api_key_id/_name, idempotency_key, idempotency_request_hash` unchanged by
  edit; replay of the original keyed create returns the same id. → T8
- **I8 BN consistency.** A draft TI referenced by a non-cancelled BN cannot change; the exit works.
  → T10
- **I9 MCP never silently drops settlement or WHT.** → T12
- **What is NOT changing:** post logic/GL mapping, numbering, Receipt/TI create paths, DB schema,
  RBAC seeds, `/api/v1` surface, read DTOs.

## 5. Requirements checklist

### WP-1 — Backend services + endpoints + tests *(first; sonnet implements, opus reviews — money/concurrency)*
- [ ] `ReceiptService.UpdateDraftAsync`: tx + `FOR UPDATE` + `Version++` + activity "Updated" + locked_mismatch mapping (§3.2); doc-comment rewritten (two-direction trace).
- [ ] `TaxInvoiceService.UpdateDraftAsync`: same + BN guard `ti.linked_to_billing_note` (§3.2); `EnsureVatRegisteredAsync` stays first.
- [ ] Both `PostAsync` doc-comments corrected re Version (§3.2).
- [ ] `GetDraftInputAsync` on both interfaces + impls (§3.4), in `ReceiptService.Read.cs` / `TaxInvoiceService.Read.cs`.
- [ ] Endpoints: PUT + GET draft-input on both (§3.1).
- [ ] New test file `backend/tests/Accounting.Api.Tests/Sales/DraftEditReceiptTaxInvoiceTests.cs` with T1–T10 (§6).
- [ ] Run `RbacAuthMapTests` → commit-ready regenerated `docs/rbac/endpoint-permission-map.generated.md` (4 new rows); `RbacCartesianTests` green.
- [ ] `docs/api/openapi.yaml` entries (§3.7).
- Done = §7.1 gates green, evidence pasted.

### WP-2 — MCP `update_receipt_draft` *(after WP-1, SAME worker via SendMessage — shares the test DB and TeasMcpTools is BE)*
- [ ] Extract `ResolveSettlementAsync` (authz travels with it); create behaviour unchanged (§3.5.1).
- [ ] Update tool rules + DI params + description (§3.5.2-5).
- [ ] T12 tests appended to `Mcp/McpWriteExpansionTests.cs` (same harness as `D3_rc_*`).
- Done = §7.1 gates incl. `FullyQualifiedName~Mcp` green.

### WP-3 — Frontend *(sonnet; separate git worktree; tsc/lint/vitest may run in parallel with WP-1/2 — no DB; e2e only after WP-1 is in the tree)*
- [ ] `components/forms/ReceiptForm.tsx` (moved + edit mode §3.6.2); `receipts/new/page.tsx` thin.
- [ ] `components/forms/TaxInvoiceForm.tsx` (moved + edit mode §3.6.3); `tax-invoices/new/page.tsx` thin.
- [ ] `receipts/[id]/edit/page.tsx`, `tax-invoices/[id]/edit/page.tsx` (§3.6.4).
- [ ] Edit buttons on both detail pages (§3.6.5).
- [ ] Hooks (§3.6.6) in `lib/queries.ts`; `lib/types.ts` (`productType` on TI line input; WHT view type only if missing).
- [ ] `messages/th.json`, `messages/en.json`, `lib/i18n/problems.ts` (§3.6.6).
- [ ] `frontend/e2e/draft-edit-receipt-taxinvoice.spec.ts` (E1, E2 §6).
- Done = §7.2 gates green.

### WP-4 — Wiki *(any worker, last)*
- [ ] `troubles-wiki.md` :1658 entry ("A draft tax invoice created from a quotation cannot be edited…") — append "**RESOLVED (edit) by specs/draft-edit-receipt-taxinvoice.md**; delete still has no route."
- [ ] `troubles-wiki.md` :516 entry — append: "Not a uniform backstop for VAT receipts: they have no `receipt_lines`, and `receipt_applications`/`receipt_wht_lines` have no trigger. Since draft-edit-receipt-taxinvoice, `UpdateDraftAsync` takes `SELECT … FOR UPDATE` on the header row and bumps `Version`; that, not the line trigger, is the race backstop."

## 6. Test list

Harness references: service-level `TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: …)`
+ `TestCompanyFactory.BuildProvider(...)` (see `McpWriteExpansionTests.D3_rc_*` :851-1086); posted-TI
→ receipt setup: `Sales/…` / `Mcp/McpDocumentChainTests.cs` and `Hardening/Sprint8BusinessUnitTests.cs`
(grep `Applications: [new ReceiptApplicationInput(`); HTTP: `Hardening/H3PutValidationTests.cs`
(`RbacApiFactory` + `JwtTokenIssuer`, mint a NON-super token for the test company with the needed
perms); keyed v1 create: `Hardening/IdempotencyDocumentFenceTests.cs`; RLS: same file :960-1001.
Behavioural tests drive the REAL transitions (service/HTTP), never seed `Status='POSTED'` — except T9,
which must hold a row lock from a second connection (stated there).

- **T1 TI edit via HTTP** — VAT co; create draft TI (2 lines). `PUT /tax-invoices/{id}` with changed
  qty, a 10% discount line, and an exempt tax-code line → 204. Assert I2 vs a second draft created by
  `CreateDraftAsync` with the same body (header money cols + every line col); `DocNo` null; company JE
  count and `sys.number_sequences` rows for the company unchanged; one `Updated` activity row.
- **T2 TI round-trip** — (a) POST-created TI with discount, `isTaxInclusive=true`, notes,
  paymentTerms, dueDate; (b) TI from `CreateFromQuotationAsync` (has `QuotationId`). For each:
  snapshot row + lines (AsNoTracking), `GET draft-input` → `PUT` it unchanged → re-snapshot → equal
  except UpdatedAt/UpdatedBy/Version; `QuotationId` preserved; tax code + rate per line equal.
- **T3 RC edit (VAT, money)** — post TI A (1,070.00 = 1,000 + 7%) and TI B (same customer). Draft RC
  applying 500 to A + one WHT line (service type, base 400). `PUT` → applications = [A 1,070], WHT base
  1,000. Assert Amount 1,070, WhtAmount recomputed from the in-force rate, applications rows replaced,
  A.AmountPaid still 0 / PaymentStatus UNPAID, no JE, DocNo null, I2 vs a fresh create. Then `PostAsync`
  → A PAID, CashReceived = 1,070 − WHT (I3 settles the EDITED amount).
- **T4 RC round-trip** — (a) the VAT receipt from T3 before post, with `PaymentMethod.Cheque`,
  chequeNo/date, notes, BU; (b) non-VAT co: issued BN → receipt applying it; (c) non-VAT standalone
  with 2 lines (one with productId). `GET draft-input` → `PUT` unchanged → only UpdatedAt/By/Version differ.
- **T5 over-application** — TI 1,070, a POSTED receipt of 500 against it; draft RC applying 100 → PUT
  applying 600 → 422 `rc.overpaid`, DB unchanged (row + children); PUT applying 570 → 204.
- **T6 posted** — post an RC and a TI; `PUT` each → **422** with title `rc.cannot_edit_after_post` /
  `ti.cannot_edit_after_post`; `GET draft-input` on each → 422 same codes; rows unchanged.
- **T7 cross-company** — draft RC + TI in company A; HTTP PUT and GET draft-input with a company-B
  token holding the create perms → 404; A's rows unchanged. RLS leg (pattern :960-1001 + the GRANT
  step at :1337-1339 — `pg_database_owner` has NO implicit privileges, and `FOR UPDATE` needs UPDATE
  privilege): first `GRANT USAGE ON SCHEMA sales TO pg_database_owner; GRANT SELECT, UPDATE ON
  sales.receipts, sales.tax_invoices TO pg_database_owner;` (idempotent), then on the same opened
  connection `SET ROLE pg_database_owner`, `set_config('app.company_id', B)`, run
  `SELECT receipt_id FROM sales.receipts WHERE receipt_id = @a FOR UPDATE` (and the TI twin) inside a
  tx → 0 rows; then with `app.company_id = A` → 1 row; `RESET ROLE` in finally.
- **T8 fence** — keyed v1 create of an RC draft and a TI draft (copy the J-test harness); BFF `PUT`
  (JWT) editing notes/lines; assert `created_via_api_key_id`, `created_via_api_key_name`,
  `idempotency_key`, `idempotency_request_hash` unchanged; replay the ORIGINAL v1 POST (same key +
  body) → same id returned, still one row.
- **T9 race (discriminating)** — VAT co, draft RC applying TI A only (no own lines). Connection X:
  `BEGIN; SELECT … FROM sales.receipts WHERE receipt_id=@id FOR UPDATE;` (simulates post holding the
  row). Start `UpdateDraftAsync` (re-point to TI B, same amount, Notes changed) on a separate scope as
  a task; assert not completed after 1s. On X: `UPDATE sales.receipts SET status='POSTED', doc_no='T9-'||@id WHERE receipt_id=@id; COMMIT;`
  (test-only raw transition — the point is the lock). Await the edit → `DomainException`
  `rc.cannot_edit_after_post`; applications still → A. Second half (post loses): load a stale tracked
  receipt in scope S (Draft, Version v); edit via service (commits Version v+1); in S call
  `MarkPosted(...)` + `SaveChangesAsync` → `DbUpdateConcurrencyException`. Repeat both halves for TI
  (lines exist there; assert lines unchanged). Without §3.2 the first half SUCCEEDS on a posted row —
  that is what makes it discriminating; state this in the test comment.
- **T10 BN guard + exit** — VAT co; draft TI T; BN draft created with `TaxInvoiceIds=[T]`; PUT T →
  422 `ti.linked_to_billing_note`. Delete the BN draft (`DeleteDraftAsync`) → PUT T → 204. Second
  case: issue the BN, PUT T → 422; `CancelAsync` the BN → PUT T → 204.
- **T11 RBAC** — `RbacAuthMapTests` + `RbacCartesianTests` green; generated map has the 4 rows.
- **T12 MCP (WP-2)** — (a) settlement RC via `create_receipt_draft` (invoiceId) → `update_receipt_draft`
  with same invoiceId + notes → applications preserved (1 row, amount = outstanding), notes changed;
  (b) same receipt, update WITHOUT invoiceId but with lines → `IsError`, text contains `mcp.domain_rule`,
  DB applications unchanged; (c) web-created RC with 2 applications → update with invoiceId → refused;
  (d) settlement update by a key WITHOUT `sales.tax_invoice.read` → `mcp.forbidden`; (e) standalone
  RC that has a WHT line (created via service) → cash-bill update → refused; (f) existing
  `D3_rc_*` tests still green (cash-bill byte-identical).
- **E1 (Playwright) TI** — login → `/tax-invoices/new` → customer + 1 line (qty 1, 1000) → save draft
  → open the draft from the list → `ti-edit` → change qty to 2 → save → detail shows 2,000.00 /
  VAT 140.00 / 2,140.00 → `ti-post-action` → confirm → doc no `-TI-\d{4}` and total 2,140.00.
- **E2 (Playwright) RC** — `createAndPostTaxInvoice` helper (1,070) → `/receipts/new` → pick customer
  + TI, applied 500 → Save (not post) → open the draft → `rc-edit` → applied 1070 → save → detail
  shows 1,070.00 → `rc-post-action` → confirm → `-RC-\d{4}`.
- Not automated (report honestly): true OS-level interleaving of post vs edit (T9 simulates it with an
  explicit lock); PDF of an edited-then-posted doc (covered by existing post-path PDF tests).

## 7. Verification gates

### 7.1 Backend worker (WP-1/WP-2) — PowerShell, ONE call per run (env dies between calls)
```powershell
$env:TEAS_TEST_PG='<current string from troubles-wiki "Stale TEAS_TEST_PG" entry>'; $env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'; dotnet build Y:\ClaudePlayground\TEAS-Project\backend\Accounting.sln -c Debug; if ($?) { dotnet test Y:\ClaudePlayground\TEAS-Project\backend\tests\Accounting.Api.Tests --no-build --filter "FullyQualifiedName~DraftEditReceiptTaxInvoice|FullyQualifiedName~McpWriteExpansion|FullyQualifiedName~RbacAuthMap|FullyQualifiedName~RbacCartesian|FullyQualifiedName~IdempotencyDocumentFence|FullyQualifiedName~ChainConversionIntegrity|FullyQualifiedName~McpDocumentChain" }
```
Expected: 0 failed, **0 skipped among the new tests** (a skip = env not set = fake green; report the
skip count). Build must be from the real path `Y:\…` (memory `minver-subst-stamping` — not critical
here, but don't build from `W:`). Do NOT run the full suite — the orchestrator does.
`git status` → list every NEW untracked file explicitly in the report.

### 7.2 Frontend worker (WP-3)
```powershell
cd Y:\ClaudePlayground\TEAS-Project\frontend; pnpm exec tsc --noEmit; if ($?) { pnpm lint }; if ($?) { pnpm vitest run }
```
Then glyph check (must print nothing): `rg -n "ম" Y:\ClaudePlayground\TEAS-Project\frontend\lib\i18n\problems.ts Y:\ClaudePlayground\TEAS-Project\frontend\messages\th.json Y:\ClaudePlayground\TEAS-Project\frontend\components\forms\ReceiptForm.tsx Y:\ClaudePlayground\TEAS-Project\frontend\components\forms\TaxInvoiceForm.tsx`
E2E (only after WP-1 is merged into the worktree and the local stack is (re)started — API :5080 +
`next start`/restarted dev :3000, memory `local-stack-boot-recipe`, `stale-next-dev-no-hot-reload`):
`pnpm exec playwright test e2e/draft-edit-receipt-taxinvoice.spec.ts e2e/issue-receipt.spec.ts e2e/login-and-create-tax-invoice.spec.ts`
→ all pass (the two existing specs prove create mode is unchanged after the move).

### 7.3 Orchestrator
- Full backend suite in one backgrounded call; compare skip count to baseline.
- `pnpm build` (CI parity).
- Never overlap the Tier-3 gate with any WP-1/WP-2 test run (shared `teas_test`).

## 8. Out of scope
- Delete-draft for RC/TI: **no route exists for either** (verified both endpoint files); the receipt
  no-delete trigger already permits deleting a DRAFT row, but no endpoint/UI is added here.
- `/api/v1` PUT for RC/TI; multi-currency; new `.update` permission.
- Receipt DocDate re-pin at edit/post (pre-existing behaviour: a draft from a since-closed month is
  unpostable — separate issue; log to troubles-wiki if hit, don't fix).
- Live-preview totals ignoring discount/inclusive on the TI form (pre-existing).
- Allowing receipt own-lines on a VAT company (pre-existing; `RebuildLinesAndTotalsAsync` does not
  forbid it) — note only.
- `BuildTaxInvoiceLinksAsync` linking DRAFT TIs at all (root cause of §3.3) — leave; guard covers the
  edit path. Log a troubles-wiki candidate if the implementer agrees it is a bug.
- A "source document" warning banner on the TI edit page.

## 9. Blast-radius cap — **max 30 files**
Expected set (28): BE — `ReceiptEndpoints.cs`, `TaxInvoiceEndpoints.cs`, `ReceiptDtos.cs`,
`ITaxInvoiceService.cs`, `ReceiptService.cs`, `ReceiptService.Read.cs`, `TaxInvoiceService.cs`,
`TaxInvoiceService.Read.cs`, `Mcp/TeasMcpTools.cs`; tests — `Sales/DraftEditReceiptTaxInvoiceTests.cs`
(new), `Mcp/McpWriteExpansionTests.cs`; docs — `docs/rbac/endpoint-permission-map.generated.md`,
`docs/api/openapi.yaml`; FE — `components/forms/ReceiptForm.tsx` (new), `components/forms/TaxInvoiceForm.tsx`
(new), `receipts/new/page.tsx`, `tax-invoices/new/page.tsx`, `receipts/[id]/edit/page.tsx` (new),
`tax-invoices/[id]/edit/page.tsx` (new), `receipts/[id]/page.tsx`, `tax-invoices/[id]/page.tsx`,
`lib/queries.ts`, `lib/types.ts`, `lib/i18n/problems.ts`, `messages/th.json`, `messages/en.json`,
`e2e/draft-edit-receipt-taxinvoice.spec.ts` (new); `troubles-wiki.md`. (+ this spec.)
Stop-and-re-spec triggers: any SQL script/migration; any change to `PostAsync`/`PostCoreAsync` logic
beyond comments; any change to `DomainExceptionMiddleware`, `SalesLineBackstop`, `RebuildLinesAndTotalsAsync`,
read DTOs (`ReceiptDetail`, `TaxInvoiceDetail*`), or RBAC seeds; T2/T4 round-trip divergence on a
fresh row; needing a new permission; touching > 30 files.

## Attempt log
- 2026-10-04 opus-designer: spec written (read-only investigation; no code changed).
