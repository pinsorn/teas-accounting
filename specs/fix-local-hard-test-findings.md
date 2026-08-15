# SPEC — fix the two shipping defects found in the 2026-08-15 local hard-test run

Source of findings, with full live evidence: `PROGRESS-local-hard-test.md` (F5, F2/F4).
Both were reproduced against a running local stack, not inferred from reading code.

**Blast-radius cap: max 6 files, no schema change, no new endpoint, no new dependency.**
Hitting the cap = stop and re-spec, do not improvise.

**Do NOT `git commit`.** Fable runs the gates, reviews the diff, and commits.

---

## WP-1 (SECURITY, do this first) — `create_invoice_draft` must check the permission for the document it actually mints

### The defect
`backend/src/Accounting.Api/Mcp/TeasMcpTools.cs:693` declares:

```csharp
[McpServerTool(Name = "create_invoice_draft"), Authorize(Policy = BillingNoteManage)]
```

then at `:710-719` branches on company VAT mode:

```csharp
var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
long id = (vatMode, deliveryOrderId, salesOrderId) switch
{
    (true,  { } doId, null) => await tiSvc.CreateFromDeliveryOrderAsync(doId, ct),  // mints a TAX INVOICE
    (true,  null, { } soId) => await tiSvc.CreateFromSalesOrderAsync(soId, ct),     // mints a TAX INVOICE
    (false, { } doId, null) => await bnSvc.CreateFromDeliveryOrderAsync(doId, ct),  // mints a billing note
    (false, null, { } soId) => await bnSvc.CreateFromSalesOrderAsync(soId, ct),     // mints a billing note
    ...
```

On a VAT-registered company the tool mints a **Tax Invoice** while requiring only
`sales.billing_note.manage`. `TaxInvoiceService.CreateFromDeliveryOrderAsync` /
`CreateFromSalesOrderAsync` perform no permission check of their own — only `IsAuthenticated` and
`EnsureVatRegisteredAsync`.

This matters most on the API-key surface, because `PermissionHandler`
(`backend/src/Accounting.Api/Authorization/PermissionRequirement.cs:17-29`) authorizes an API key
against the **key's own scopes CSV**, never a user's roles and never with a super-admin bypass. The
scope list on the key *is* the security boundary, and this tool crosses it.

### Proven exploit (reproduce this exact sequence to confirm your fix)
An API key on a VAT-registered company scoped to `sales.billing_note.manage`,
`sales.sales_order.manage`, `sales.billing_note.read` — and **not** `sales.tax_invoice.create`:

- `create_tax_invoice_draft` → correctly denied: `"Access forbidden: This tool requires authorization."`
- `create_invoice_draft(deliveryOrderId: <a Delivered DO>)` → **succeeded**, returned
  `{"id":2,"approvalUrl":".../tax-invoices/2?action=approve"}`, and `sales.tax_invoices` gained a
  DRAFT row while `sales.billing_notes` stayed empty.

Consequences verified live: no DELETE route exists for tax invoices (`DELETE /tax-invoices/{id}` → 405;
the OpenAPI document has no delete verb) and the UI offers no delete, so the draft cannot be removed;
`POST /periods/{y}/{m}/close` then fails with 422 `period.draft_present`, and year close fails with 422
`year.periods_not_closed`. The only exit is to post the document the key was never allowed to create.

### Required behaviour
When the VAT branch is taken, the caller must additionally hold **`sales.tax_invoice.create`**. When
the non-VAT branch is taken, today's `sales.billing_note.manage` remains correct and sufficient.

A refusal must surface as an MCP error the agent can read — use the same error path the file's other
tools use for a refusal (`McpE2Exception`), with a message naming the missing permission, in the style
of the HTTP twin: `"'sales.tax_invoice.create' required to create this document."`

### The pattern to mirror (do not invent a new one)
`backend/src/Accounting.Api/Endpoints/SalesChainEndpoints.cs:112-133` already solves exactly this for
`POST /sales-orders/{id}/create-invoice`: it resolves the caller's granted permissions at runtime and
403s when the VAT-mode branch needs `TaxInvoiceCreate` and the caller lacks it. Read that code first and
follow its shape, including how it obtains the grants.

### Traps — read before writing code
1. **A static `[Authorize]` attribute cannot express this.** Which document gets minted depends on the
   company's VAT mode, known only at runtime. Adding `TaxInvoiceCreate` to the attribute would break the
   non-VAT branch, where the tool legitimately mints a billing note and the caller may hold only
   `billing_note.manage`. That change would be an automatic REJECT.
2. **The check must work for an API-key principal, whose grants live in the scopes claim, not in role
   permissions.** Verify your lookup path is the one that reads what `PermissionHandler` reads for a key
   (`TenantClaims.Scopes`), or the fix will pass for JWT users and silently do nothing for keys — which
   is the only surface the exploit uses. Confirm this by reading `PermissionRequirement.cs` before
   choosing your mechanism.
3. **Do not widen the fix to `create_billing_note_draft`** (`:724`). It mints a billing note on every
   path, so `BillingNoteManage` is the right and complete gate there.
4. **Super-admin.** A JWT super-admin bypasses permission checks by design (CLAUDE.md §4.1). Whatever
   mechanism you choose must keep that true for JWT users and must NOT grant a bypass to an API key —
   `PermissionHandler` is explicit that a key never gets super-admin bypass.

### Gates for WP-1
- `dotnet build` 0 warnings / 0 errors.
- A test proving the VAT branch refuses a principal holding `billing_note.manage` without
  `tax_invoice.create`, **and** a test proving the non-VAT branch still succeeds for that same
  principal. Both are required: the second is what catches an over-broad fix.
- A test proving the VAT branch still succeeds for a principal that does hold `tax_invoice.create`.
- Report the test names and their output. Do not run the full suite — Fable runs it.

### WP-1 attempt log
- **[x] DONE** (2026-08-16, sonnet-implementer). Fixed `CreateInvoiceDraftAsync` in
  `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` to runtime-check `sales.tax_invoice.create`
  on the VAT branch. Deviated from the letter of "mirror SalesChainEndpoints.cs" on ONE point,
  deliberately, because of trap 2: `IPermissionLookup.LoadAsync(tenant.UserId ?? 0, ...)` queries
  `sys.role_permissions` by `ITenantContext.UserId`, which `AmbientTenantContext` (and
  `ApiKeyAuthenticationHandler`, which never emits `ClaimTypes.NameIdentifier`/`sub`) leaves
  **null for every API-key/MCP caller**. Reusing that pattern verbatim would have queried
  `UserId == 0`, returning zero grants for every key — silently denying every VAT-branch MCP
  call (including one holding `tax_invoice.create`), which would have failed gate 3. Instead
  the fix re-runs the tool's own `mcpperm:sales.tax_invoice.create` policy via
  `IAuthorizationService.AuthorizeAsync(HttpContext.User, null, TaxInvoiceCreate)` — this invokes
  `PermissionHandler` directly (the exact same handler the static `[Authorize]` attributes use),
  which reads the key's `Scopes` claim for API keys and `Permission` claims (+ super-admin
  bypass, never granted to keys) for JWT users. Confirmed empirically: all 4 tests below passed,
  including the "key with `tax_invoice.create` succeeds" case, which a literal `IPermissionLookup`
  port would have failed.
  - Also updated the pre-existing test `Mcp_create_invoice_draft_is_polymorphic_and_wraps_the_delivery_required_guard`
    (`backend/tests/Accounting.Api.Tests/Mcp/McpDocumentChainTests.cs`) — its VAT-company key
    previously held only `billing_note.manage`/`sales_order.manage`/`delivery_order.manage` and
    relied on the pre-fix vulnerable behaviour to succeed; added `sales.tax_invoice.create` to
    keep testing the guard it actually targets (delivery-required), not the permission bug this
    WP closes.
  - Gates: `dotnet build` (Api + Tests) 0 warnings/0 errors. Targeted run
    `--filter "FullyQualifiedName~McpDocumentChainTests.Mcp_create_invoice_draft"` →
    **4 Passed, 0 Failed, 0 Skipped** (confirms `TEAS_TEST_PG` applied — no skip-count jump).
- **[x] Tier-2 review N1 applied** (2026-08-16). APPROVE-WITH-NITS; reviewer independently
  confirmed the `IPermissionLookup` deviation above was correct. Nit N1: replaced the
  `IHttpContextAccessor httpContextAccessor` param + `httpContextAccessor.HttpContext!.User`
  (null-forgiving) with a directly constructor-injected `ClaimsPrincipal user` param, matching
  the existing `CompanySwitchService` precedent and the scoped `ClaimsPrincipal` DI registration
  at `Program.cs:127-129` (`IHttpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(...)`
  anonymous fallback). Removes a `NullReferenceException` failure mode that depended on
  `Program.cs`'s `WithHttpTransport(o => o.Stateless = true)` staying true; the guard now fails
  closed (deny) instead of throwing if that transport setting ever changes. No other change —
  policy constant, guard position, tests, `create_billing_note_draft` all untouched per
  instruction. Re-ran gates: `dotnet build` (Api, Tests, full `Accounting.sln`) 0 warnings/0
  errors; targeted `McpDocumentChainTests.Mcp_create_invoice_draft*` → **4 Passed, 0 Failed, 0
  Skipped** (TEAS_TEST_PG confirmed applied, no fake-green).

---

## WP-2 — stop returning raw 500s (and leaking .NET exception text) for out-of-range period input

### The defect, in two places with one shape
Both construct a `DateOnly` straight from request input with no validation, so an out-of-range value
raises `ArgumentOutOfRangeException`, which nothing maps, and the generic handler in
`DomainExceptionMiddleware` returns **500 `internal_error`** with the framework's own message in the
body.

**(a) VAT reports — bad month.** `backend/src/Accounting.Infrastructure/Reports/VatReportService.cs:91-93`:

```csharp
private static (DateOnly from, DateOnly to) MonthRange(int year, int month) =>
    (new DateOnly(year, month, 1),
     new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
```

Verified live, all **500** with detail *"Year, Month, and Day parameters describe an un-representable
DateTime."*:
`GET /reports/pnd30?year=2026&month=13` · `?month=0` · `GET /reports/output-vat-register?year=2026&month=13`
· `GET /reports/input-vat-register?year=2026&month=13` · `GET /reports/vat-register?year=2026&month=13`.

**(b) CIT year endpoints — bad year.** `backend/src/Accounting.Infrastructure/Tax/CitYearDataService.cs`
builds `new DateOnly(fiscalYear, ...)` in `ProfileAsync` (~:201) and `FiscalBoundsAsync` (~:43) with no
guard. Verified live, all **500**: `GET /tax-filings/cit/profile?year=9999` · `?year=99999` · `?year=0` ·
`?year=-1`, and `POST /tax-filings/cit/years/99999/compute` · `.../0/compute`.

### Required behaviour
Every one of those calls returns a typed **422** carrying a stable error code, and no response body
contains framework exception text.

### The pattern to mirror
`TaxFilingPeriod` in `backend/src/Accounting.Infrastructure/TaxFilings/ProportionalInputVatService.cs`
already does this correctly and is the intended home for these guards:

- `MonthRange(int period)` (:40-48) validates `m is < 1 or > 12 || y < 2000 || y > 9999` and throws
  `DomainException("tax_filing.bad_period", ...)`.
- `EnsureYear(int year)` (:74-79) validates `year is < 2000 or >= 9999` and throws
  `DomainException("tax_filing.bad_year", ...)`.

Reuse these. Do not write a third validation helper, and do not copy the range literals into new
places — call the existing guards.

### Traps — read before writing code
1. **Do not change the accepted year window.** `EnsureYear`'s doc comment explains that the ceiling is
   deliberately loose because the test suite files sentinel years up to ~7499 against the shared,
   never-reset `teas_test` database, and the floor is deliberately low so a legitimately late filing for
   an old year is never refused. Tightening the window is a separate, discussed change — narrowing it
   here would break unrelated tests and is an automatic REJECT. `year=3000` must keep returning 200.
2. **Preserve the existing error codes.** `tax_filing.bad_period` and `tax_filing.bad_year` are already
   observable in responses; introducing new codes for the same conditions is churn.
3. **The CIT endpoints have two entry points** — the `year` query parameter on `/cit/profile` and the
   `{year:int}` route value on `/cit/years/{year}/compute`. Both must be covered. Guard in the service
   so every caller is covered, rather than sprinkling checks per endpoint.
4. **Out of scope, do not touch:** `GET /tax-filings/pnd50/preview?year=99999999999` returns 500 from
   ASP.NET model binding before any app code runs (`"Failed to bind parameter \"int year\""`). That is a
   framework-level concern and is deliberately excluded from this work package.

### Gates for WP-2
- `dotnet build` 0 warnings / 0 errors.
- A test per affected route family asserting **422** and the expected error code for an out-of-range
  month (VAT reports) and an out-of-range year (CIT), and one asserting `year=3000` still returns 200 on
  a CIT endpoint — that last one is the regression guard for trap 1.
- Report test names and output. Do not run the full suite.

### WP-2 attempt log
- **[x] DONE** (2026-08-16, sonnet-implementer). Fixed both cited files:
  - `VatReportService.MonthRange(year, month)` now delegates to `TaxFilingPeriod.MonthRange(year *
    100 + month)` instead of constructing `DateOnly` directly, reusing `tax_filing.bad_period` (no
    new code/helper). Added a round-trip check (`range.from.Year != year || range.from.Month !=
    month`) — advisor-caught gap: `year*100+month` is only a faithful yyyymm encoding for `month` in
    [1,12]; an out-of-band month like `-88` or `112` aliases to a DIFFERENT, in-range period (e.g.
    `year=2026,month=-88` → period 202512 → silently returns December 2025's data with 200) instead
    of failing. Covered by a dedicated test.
  - `CitYearDataService`: added `TaxFilingPeriod.EnsureYear(fiscalYear)` as the first line of BOTH
    `FiscalBoundsAsync` (covers `ComputeYearAsync` → `POST /cit/years/{year}/compute`, and
    `ExpenseByAccountAsync`) and `ProfileAsync` (covers `GET /cit/profile?year=` — it does NOT call
    `FiscalBoundsAsync`, it duplicates the same `DateOnly` construction inline, so it needed its own
    guard call per trap 3).
  - Did **not** touch the accepted year window (trap 1) — `EnsureYear` unchanged; `year=3000` still
    200s (tested).
  - Did **not** add a third validation helper or copy range literals (trap 2) — both fixes call the
    pre-existing `TaxFilingPeriod.MonthRange`/`EnsureYear` and preserve their exact error codes.
  - **Noticed, not fixed** (outside this WP's cited files — flagging per the dispatch's report
    channel, not fixing): the defect's repro-URL list includes `GET /reports/output-vat-register` and
    `GET /reports/input-vat-register` with `?year=2026&month=13`, but those two routes bind a single
    `[FromQuery] int period` (yyyymm), not `year`/`month` — so that exact query string can't reach the
    DateOnly construction the way described. Reading the code: `OutputVatRegisterAsync`
    (`TaxFilingService.cs:177`) already calls `TaxFilingPeriod.MonthRange(period)` — confirmed via
    `git log -S "TaxFilingPeriod.MonthRange" -- .../TaxFilingService.cs` that this guard is in the
    Phase-1-baseline commit `ce1f6fe`, i.e. it predates this spec entirely — so a bad `period` there
    already 422s, not 500s. `InputVatRegisterAsync` (same file, :156) does no `DateOnly` construction
    at all (filters `VendorInvoices` by `VatClaimPeriod == period`), so a garbage period there
    returns 200 with an empty register, not a 500. Neither matches the "raw 500 leaking exception
    text" defect shape this WP closes; the repro-list entries for these two URLs look like a
    transcription mismatch (possibly against `/reports/vat-register`, which IS fixed). Confirmed with
    advisor before proceeding — did not touch `TaxFilingService.cs` (not cited in the defect, and
    blast-radius cap is 4 files for WP-2).
  - Gates: `dotnet build` (full solution) 0 warnings/0 errors. New file
    `backend/tests/Accounting.Api.Tests/Reports/PeriodValidation422Tests.cs` (HTTP-level, mirrors
    `GeneralLedgerEndpointTests.cs` — `RbacApiFactory` + super-admin JWT). Targeted run
    `--filter "FullyQualifiedName~PeriodValidation422Tests"` with `TEAS_TEST_PG` set inline →
    **4 Passed, 0 Failed, 0 Skipped**. RED-first verified: temporarily `git stash`-reverted both
    source files and re-ran the same filter → **3 Failed with literal `HttpStatusCode.InternalServerError
    {value: 500}`** (the exact pre-fix defect) on the three 422 tests, while the `year=3000` guard
    test passed both before and after (green-first by design, since it asserts pre-existing correct
    behaviour). Then `git stash pop` restored the fix and re-ran → 4/4 green again.

---

## Working rules for this dispatch
- Minimal diff (Ponytail). No refactors, no renames, no "while I was in there" changes. Scope belongs to
  Fable; if something outside this spec looks wrong, write it down and report it rather than fixing it.
- `troubles-wiki.md` first on any unexpected error, before debugging from scratch.
- The local stack (API :5080, FE :3000) will be **stopped** before you start, so `dotnet build` can write
  to `bin/`. Do not start a long-running server yourself; if you need to check behaviour, write a test.
- Attempt log: append what you tried and what happened to this spec as you go, so a retry starts from
  the log instead of from zero.
