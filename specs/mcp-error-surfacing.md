# Spec: MCP business-error surfacing + master-data resolver tools

Status: ACTIVE 2026-07-12 (night session). Owner: Fable (design) → sonnet implements, opus reviews.
Branch: `feat/mcp-error-surfacing` from origin/main. Blast-radius cap: backend only;
`TeasMcpTools.cs`, `Program.cs`, one new filter file, tests. Max ~6 files touched
(excluding tests). NO schema changes, NO migrations, NO FE.

## Background (from prod investigation 2026-07-12, log evidence in PROGRESS-mcp-butest-sweep.md)

An external AI agent (Claude "Sana") exercised every MCP tool at Repttown (company 2,
`vatRegistered: false`). Four create tools "failed" — all were in fact **intentional
business validations** thrown as `DomainException` / `McpE2Exception`:

| tool | actual server error (from api-out.log) |
|---|---|
| create_tax_invoice_draft | `DomainException: VAT-not-registered companies cannot issue Tax Invoices (ม.86/4)...` (TaxInvoiceService.EnsureVatRegisteredAsync) |
| create_vendor_invoice_draft | `DomainException: Expense category 1 not found.` / `Business Unit is required for this company.` |
| create_payment_voucher_draft | `DomainException: Expense category 1 not found.` |
| create_expense_claim_draft | `McpE2Exception: [mcp.employee_required] Employee id 1 does not exist...` |

**The bug:** the MCP client sees only `An error occurred invoking 'create_tax_invoice_draft'.`
The ModelContextProtocol SDK's catch-all swallows every exception message. The agent
cannot distinguish "illegal per Thai tax law" from "server crashed" and burns retries.
Note: the doc comment on `McpE2Exception` (TeasMcpTools.cs:1655) *claims* the SDK
surfaces the message — empirically false on prod v1.18.0. That comment must be corrected.

Also observed: input-shape errors (`System.Text.Json.JsonException: ... $.lines[0].uomId`
when the client sent `"uomId": null`) are swallowed the same way.

Second gap: `create_vendor_invoice_draft` / `create_payment_voucher_draft` require
`expenseCategoryId`, `taxCodeId`, `whtTypeId` — but NO MCP tool lists any of them.
An agent cannot resolve valid ids even in principle (Sana guessed "1"). Precedent:
`list_gl_accounts` exists exactly as the picker for `get_general_ledger`.

## Deliverables

### 1. Central error-surfacing filter (the fix)

One call-tool filter registered in `Program.cs` next to `.AddAuthorizationFilters()`
that wraps tool invocation and maps exceptions to a **message-bearing tool error**
(`CallToolResult { IsError = true }` with a single text content block):

| exception | client-visible text |
|---|---|
| `McpE2Exception` | its `Message` verbatim (already `[code] detail`) |
| `DomainException` | `[mcp.domain_rule] {Message}` |
| `FluentValidation.ValidationException` | `[mcp.validation] {joined property: error list}` |
| `System.Text.Json.JsonException` (input binding) | `[mcp.bad_input] {Message}` (includes the JSON path) |
| anything else | UNCHANGED — keep today's generic text; never leak internals/stack |

Implementation constraints:
- ModelContextProtocol.AspNetCore **1.4.0** (see backend/Directory.Packages.props).
  VERIFY the public filter API against the installed package source
  (`~/.nuget/packages/modelcontextprotocol*/1.4.0/**`) before coding — the stack trace
  on prod shows a filter pipeline (`AuthorizationFilterSetup.ConfigureCallToolFilter`
  wrapping `McpServerImpl.ConfigureTools`), and exceptions DO propagate through filter
  frames, so a filter can try/catch around `next()`. If 1.4.0 exposes no public
  filter-registration API, fallback = a private static
  `Surface<T>(Func<Task<T>>)` helper applied per tool body (mechanical, 76 tools —
  last resort only; say so in the attempt log before doing it).
- JsonException from argument marshalling: verify by test whether it reaches the filter
  (prod stack suggests it's thrown inside `ReflectionAIFunction.InvokeCoreAsync`, i.e.
  inside the filter's `next()`); if it does not, leave it generic and note it.
- Server-side logging must stay (the `fail:` log lines are how we diagnosed this).
- Fix the `McpE2Exception` doc comment (TeasMcpTools.cs:1655-1657) to describe the
  filter as the surfacing mechanism.

### 2. Three read-only resolver tools (pattern: `list_gl_accounts`, TeasMcpTools.cs:776)

- [ ] `list_tax_codes` — active tax codes for the caller's company: id, code, nameTh,
      rate, plus whatever direction/kind field the entity has. Picker for
      `taxCodeId` on all draft-create line inputs.
- [ ] `list_wht_types` — active WHT types: whtTypeId, code, nameTh, incomeTypeCode,
      formType, rate. Picker for `whtTypeId` (payment voucher lines, vendor invoice).
- [ ] `list_expense_categories` — active expense categories: categoryId, code, nameTh,
      nameEn, defaultExpenseAccountId, defaultTaxCodeId, defaultWhtTypeId, isCapex,
      isCogs. Picker for `expenseCategoryId`.
- [ ] `list_business_units` — active business units (`master.business_units`): id, code,
      name. Picker for `businessUnitId`. ADDED 2026-07-12 after the sweep: company 2
      REQUIRES a business unit on every draft ("Business Unit is required for this
      company") yet no tool exposes them — agents cannot draft anything without guessing.
- [ ] `uomId` note (NOT a new tool): there is no UOM master table — doc lines store a
      loose `uom_id` int with no FK plus free-text `uom_text`; products carry only
      `default_uom_text`. Fix by DOCUMENTATION: update the `[Description]` on every
      MCP line-input `UomId`/`UomText` to say uomId has no master list (pass 1 unless
      known; uomText is the human-facing unit). Do NOT invent a uoms table.

Rules:
- Company-scoped via the same automatic RLS/global-filter tenancy as every other tool.
  All four tables carry company_id — confirm the EF entities exist and are filtered.
- Authorize: REUSE existing scopes/policies (subledger precedent — no new McpScopes
  entry): pick the closest read policy the PV/vendor-invoice tools' audience already
  holds (e.g. the purchase/expense read policy that gates get_payment_voucher /
  get_expense_claim). Justify choice in code comment. RbacEndpointInventory must pass.
- If a read service/method already exists (BFF pickers likely need these too), reuse
  it; add a minimal read method to the closest existing service otherwise. No new
  service interfaces unless nothing fits.
- Update the `[Description]` of create_vendor_invoice_draft, create_payment_voucher_draft
  (and tax-invoice/quotation line docs where they name taxCodeId) to point at the new
  resolvers.

### 3. Explicitly OUT of scope

- Making `uomId` nullable — dropped: with (1), the agent sees the exact JSON path and
  self-corrects. (If (1)'s JsonException surfacing turns out impossible, re-open.)
- Seeding expense categories / employees on prod (data op, handled outside this spec).
- Default expense categories at company-creation (product design — Ham decision).
- Any FE change.

## Gates (all must pass; report evidence per item)

- [x] New integration tests (McpServerSmokeTests style, WebApplicationFactory +
      real /mcp tools/call round-trip) — `tests/Accounting.Api.Tests/Mcp/McpErrorSurfacingTests.cs`,
      11 tests, all passing (see attempt log):
      a. [x] create_tax_invoice_draft on a non-VAT company → IsError=true AND content text
         contains "VAT-not-registered" (the ม.86/4 message).
      b. [x] create_expense_claim_draft with bogus employeeId → content contains
         "[mcp.employee_required]".
      c. [x] tax-invoice line with `"uomId": null` → IsError=true AND content contains
         `[mcp.bad_input]` + `uomId` (the JSON path). CORRECTED 2026-07-13 (coordinator
         caught a test-harness artifact in the first pass — see attempt log): reaches the
         filter and works exactly as the prod stack trace shows, PROVIDED the wire JSON
         carries an EXPLICIT `null` token (as a real non-C#-SDK client sends). The test's
         first attempt used a C# anonymous object (`uomId = (int?)null`), which the
         `ModelContextProtocol.Client` SDK's own serializer OMITS from the wire entirely
         (`JsonIgnoreCondition.WhenWritingNull`) — that variant never reaches the filter
         because there is no exception to reach it with (missing key ≠ explicit null for
         STJ's constructor binding). Fixed by hand-building the request JSON with a literal
         `null` and passing a pre-parsed `JsonElement` (bypasses the SDK's re-serialization).
      d. [x] each new list tool returns seeded rows and excludes other-company rows
         (tenancy) — reuse existing fixture data where possible.
- [x] Full backend suite green: `dotnet test` with TEAS_TEST_PG set IN THE SAME shell
      command; compare skip count vs baseline (skipped tests fake a green run).
- [x] `dotnet build` zero warnings-as-errors regressions.
- [x] RbacEndpointInventory / RbacAuthMap tests pass (set TEAS_REPO_ROOT if running
      from a subst drive).
- [x] No `git commit` by the worker — Fable reviews the diff and commits.

## Attempt log
- 2026-07-12/13 (sonnet implementer, worktree `feat/mcp-error-surfacing`):
  - **Filter API**: decompiled the installed `ModelContextProtocol.Core`/`.AspNetCore` 1.4.0
    DLLs (ilspycmd, no source package available) since XML docs alone were ambiguous.
    Confirmed a PUBLIC filter API exists: `McpServerOptions.Filters.Request.CallToolFilters`
    (`IList<McpRequestFilter<CallToolRequestParams, CallToolResult>>`). No fallback
    `Surface<T>` helper was needed.
  - **Ordering proof** (decompiled `McpServerImpl.ConfigureTools`/`BuildFilterPipeline`):
    `IConfigureOptions<McpServerOptions>` callbacks run in DI-registration order; the FIRST
    registered `CallToolFilters` entry becomes the OUTERMOST wrapper. Registering
    `AddErrorSurfacingFilter()` AFTER `.AddAuthorizationFilters()` in Program.cs means the
    auth filter (`AuthorizationFilterSetup.ConfigureCallToolFilter`) stays outermost — an
    unauthorized call throws `McpProtocolException("Access forbidden...")` before our
    filter's try/catch ever runs. The SDK's own built-in catch-all (added via
    `BuildFilterPipeline`'s `initialHandler` param) wraps EVERYTHING outside both filters,
    so any exception type we don't catch falls through unchanged to today's generic text —
    "anything else: UNCHANGED" required zero extra code.
  - **JsonException finding (gate c) — CORRECTED 2026-07-13**: first pass wrongly concluded
    "empirically unreachable." Root cause of that false negative: the test used a C#
    anonymous object (`uomId = (int?)null`) as the `CallToolAsync` argument.
    `ModelContextProtocol.Client.McpClient`'s own serializer (`McpJsonUtilities.DefaultOptions`,
    confirmed by decompiling `ModelContextProtocol.Core` 1.4.0) sets `DefaultIgnoreCondition =
    JsonIgnoreCondition.WhenWritingNull` — a null-valued C# property is OMITTED from the
    outgoing wire JSON entirely, so the server saw a MISSING key, not an explicit `null`
    token. System.Text.Json's record/constructor-parameter binding silently defaults a
    MISSING value-type argument to `0` (no exception) — a completely different code path
    from an EXPLICIT `null` token against a non-nullable value type, which DOES throw
    (`JsonValueKind.Null` → `System.Int32` conversion failure, unconditional). The coordinator
    flagged the discrepancy against the real prod stack trace (`$.lines[0].uomId`,
    BytePositionInLine 273) — that's the client-sent-a-literal-null path; my first test
    exercised the client-omitted-the-key path instead, which is why they disagreed. FIXED:
    hand-built the request JSON as a raw string with a literal `null`, parsed it into a
    `JsonElement`, and passed THAT as the argument value — `ToArgumentsDictionary` uses a
    `JsonElement` argument AS-IS, bypassing the SDK's own re-serialization/omission. Re-run
    now reproduces the exact prod behavior: `IsError=true`, content
    `[mcp.bad_input] The JSON value could not be converted to System.Int32. Path:
    $.lines[0].uomId | ...`. New troubles-wiki.md entry filed (test-harness footgun, any
    future MCP null-argument test needs this). The `uomId`/`uomText` `[Description]` fixes
    (documentation-only, no UOM master) remain correct/unaffected either way.
  - **4th resolver `list_business_units`** (coordinator scope update, post prod-sweep):
    added per the updated checklist above. Authorize policy for ALL FOUR new resolver tools
    reuses `VendorInvoiceRead` (`purchase.vendor_invoice.read`) — already in `McpScopes.All`,
    zero new grants — since `create_vendor_invoice_draft`'s audience is the one that hit
    every one of these gaps (taxCodeId/whtTypeId/expenseCategoryId/businessUnitId all
    required fields per the background). `list_tax_codes` needed a brand-new
    `ITaxCodeService`/`TaxCodeService` (no prior read service existed for tax codes at all);
    the other three reuse existing services (`IWhtTypeService`, `IExpenseCategoryService`,
    `IBusinessUnitService`) — `ExpenseCategoryDto` was widened (3 new trailing fields,
    additive/backward-compatible) to carry `DefaultExpenseAccountId`/`DefaultTaxCodeId`/
    `DefaultWhtTypeId` per the spec's field list.
  - **TaxCode rate**: `tax.tax_codes` carries NO scalar rate column (confirmed by
    `SqlScripts/450_seed_demo_company_tax_codes.sql`'s own header comment — rate lives on
    `companies.vat_rate` + the per-line snapshot; `tax.tax_rates` is never seeded).
    `list_tax_codes`'s `rate` field is therefore derived: 0 for exempt/zero-rated codes,
    else the company's single `VatRate` — matches how VAT is actually computed elsewhere in
    the codebase, not a fictional per-code rate-table join.
  - Files touched (6, matching the blast-radius cap, excluding tests): `TeasMcpTools.cs`,
    `Program.cs`, new `McpErrorSurfacingFilter.cs`, `ReferenceDtos.cs`, `MasterDataServices.cs`,
    `DependencyInjection.cs`.
  - Full suite: see CHANGED/EVIDENCE in the worker's final report to the orchestrator.
- 2026-07-13, Tier-2 Opus review fix round (same worker, same worktree):
  - **BLOCKER fixed**: `McpErrorSurfacingFilter.cs` now logs BEFORE returning in every catch
    (`LogLevel.Warning`, message shape `"{ToolName}" rejected: {SurfacedText}`, exception
    attached). Root cause the review correctly named: because the filter sits INSIDE the
    SDK's own built-in catch-all (by design — see the ordering note above) and returns a
    normal, non-throwing result, the SDK's own "unhandled exception" log line never fires
    for these 4 exception classes — spec §1's "server-side logging must stay" would have
    silently regressed for exactly this surfaced set (the ones that most need a trace).
    Wiring: `builder.Services.AddOptions<McpServerOptions>().Configure<ILoggerFactory>((options,
    loggerFactory) => ...)` — the options-with-dependency overload, since the plain
    `Action<McpServerOptions>` `Configure(...)` used in the first pass has no way to reach
    `ILoggerFactory`. One `ILogger` is created once per options build and closed over by the
    filter delegate.
  - **Regression test added** (not skipped — cheap and not flaky, ~35s for the whole file):
    `McpErrorSurfacingTests.CreateTaxInvoiceDraft_on_non_vat_company_still_logs_server_side`.
    Layers a captured `ILoggerProvider` onto the shared `McpApiFactory` via
    `WithWebHostBuilder(b => b.ConfigureLogging(...))` (no edit to the shared fixture class),
    asserts a `Warning`-level record with category `Accounting.Api.Mcp.McpErrorSurfacingFilter`
    containing both the tool name and the surfaced text. 12/12 `McpErrorSurfacingTests` pass
    (was 11; +1 for this test).
  - **LOW nit fixed** (documentation only): `list_tax_codes`'s `[Description]` now explains the
    `rate` field is the company's standard VAT rate regardless of `VatRegistered` — an INPUT
    tax code still reflects what a vendor charged even when the caller itself can't issue Tax
    Invoices (ม.86/4 blocks OUTPUT use only). No logic change — `TaxCodeService.ListAsync`
    was already correct; only the wording was ambiguous.
  - IsActive client-side filter in `ListExpenseCategoriesAsync` left as-is per reviewer
    (cosmetic, no action).
  - Evidence: `dotnet build` 0 warnings/0 errors (full solution); `McpErrorSurfacingTests` 12/12
    passed, run synchronously with `TEAS_TEST_PG` in the same shell command as `dotnet test`.
