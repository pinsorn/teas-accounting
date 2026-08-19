# C1 — Backend cleanup batch (U9 PO tax-code laundering, McpScopes narrow, seed-640 test coverage)

> Source: `PROGRESS-cleanup-2026-08-19.md` board row C1. Living document.
> `[ ]` not started · `[~]` partial + note · `[x]` done + evidence.
> Retry = SAME file, the attempt log grows.

## Item 1 — U9: PurchaseOrderService verbatim TaxCodeId

### Facts established

- `PurchaseOrderService.Fill` (called by `CreateDraftAsync` and `UpdateDraftAsync`) writes
  `TaxCodeId = l.TaxCodeId` verbatim from `PurchaseOrderLineInput` — a request-fed `int?`
  with **no validation anywhere** in the create/update path.
- **No purchase-side resolver exists** (searched `backend/src/Accounting.Infrastructure/Purchase/`
  for a `SalesLineBackstop`-equivalent — none found). `VendorInvoiceService.BuildLinesAsync`
  derives `TaxCodeId = cat.DefaultTaxCodeId` from the expense category (never reads a
  request-supplied id). `PaymentVoucherService` either copies an own-company VI line's
  already-safe id (`CreateFromVendorInvoiceAsync`) or also writes `input.TaxCodeId` verbatim
  in `BuildLinesAsync` (line 300) — **a second latent instance, out of this item's scope**
  (item names `PurchaseOrderService.cs:90` only; noted here for a future finder, not fixed).
- `PurchaseOrderLine.TaxCodeId` has **no FK**, **no downstream consumer**:
  `PurchaseOrderLineDto` doesn't even project it, and `VendorInvoiceService.CreateFromPurchaseOrderAsync`
  (PO→VI) never reads it — VI re-derives its own `TaxCodeId` from the expense category. It really
  is a loaded gun with nothing wired to the trigger yet.
- Live evidence, `accounting_dev` (2026-08-19, read-only query): `purchase.purchase_order_lines`
  has 0 rows total, so 0 violating rows trivially.
  ```
  SELECT count(*) FROM purchase.purchase_order_lines l
  JOIN purchase.purchase_orders h ON h.purchase_order_id = l.purchase_order_id
  WHERE l.tax_code_id IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                     WHERE t.tax_code_id = l.tax_code_id AND t.company_id = h.company_id);
  -- => 0
  ```
  `teas_test` (bloated fixture DB, memory `teas-test-fixture-apply-once`) shows 78/483 violating
  rows — pre-existing test-fixture noise from before any hardening existed. **No migration is
  written**: the item only requires the write path be hardened going forward; historical
  `teas_test` rows are untouched (matches U2's own "no migration for purchase side" ruling,
  §8 of `specs/fix-r2-u2-billing-tax-integrity.md`).
- **Fix shape chosen: reject typed, not launder.** Unlike the sales chain (copy-forward from an
  immutable source document, where refusing would strand a document — U2 §0), every PO line is
  REQUEST-fed at the point of origin; there is no immutable upstream to inherit from. The
  purchase module's own existing convention for an invalid foreign reference in a request is
  "reject typed" (`bu.invalid` for a foreign/inactive BusinessUnitId — `PurchaseOrderService.cs`
  `ApproveAsync`/create path; `vendor.not_found`; MCP's `GuardVendorAsync`/`GuardProductAsync`
  list-only enforcement). The MCP tool description for `create_purchase_order_draft`'s
  `TaxCodeId` already documents the contract: *"Id of an active tax code in the caller's
  company — resolve via list_tax_codes."* Laundering-by-code-string would invent a new,
  unprecedented convention for zero live benefit; reject typed matches the existing module.

### Design — REVISED 2026-08-19 (coordination update mid-task)

Fable's coordination update: C2's FE worker already shipped `PurchaseOrderForm.tsx` sending
`taxCodeId: l.taxCodeId ?? null, taxCode: l.taxCode ?? null` (commit `a1e9ff3`, dropping the
old hardcoded `taxCodeId: 1`) with an explicit comment: *"Null (untouched line) ... lets the
backend's own fix resolve it."* So the backend fix must handle three request shapes, not just
reject-on-foreign-id:

1. **id supplied** → must be an ACTIVE row of the caller's own company's master (mirrors
   `bu.invalid`'s `IsActive` check, and the MCP tool description "active tax code"), else
   **reject typed** `po.tax_code_invalid` — never stored (unchanged from the original design).
   A valid id **passes through unchanged** (id + string both, no rewrite).
2. **id null, `TaxRate > 0`** (the line actually charges VAT — the FE already encodes the
   vendor's VAT status into `TaxRate`: `taxRate: vendorVat ? l.taxRate : 0`, so `TaxRate` is the
   caller-visible proxy for "should this line carry a real VAT-code label") → resolve:
   - if `TaxCode` (string) matches this company's own master, case-insensitively → that row's
     (id, master-cased Code);
   - else → this company's own standard PURCHASE (input) VAT code: `IsActive && Direction ==
     Input && !IsExempt && !IsZeroRated`, preferring `Code == "VAT-IN7"`, else lowest id
     (mirrors `SalesLineBackstop.LoadStandardOutputTaxCodeAsync`'s shape, input side);
   - if the company's master has NO input tax code at all → **never throw** (mirrors
     `SalesLineBackstop`'s "no master at all" invariant, memory
     `seed-cos-bypass-createasync-taxcodes`) — leave the pair as sent (null).
3. **id null, `TaxRate == 0`** → leave the pair as sent (null). Nothing is charged; a null
   pair on a nullable column is honest, not a defect.

**Money invariant (states what must never break):** the stored `(TaxCodeId, TaxCode)` pair is
ALWAYS consistent with the stored `TaxRate` — never a foreign id, never a real VAT-code label
on a rate the line doesn't charge. `TaxRate`/`ChainMath.Line` money math is **untouched** —
this unit only ever writes the reference pair, exactly as the original design did.

**Why not literally mirror VendorInvoiceService (`cat.DefaultTaxCodeId`)?** Its own comment
(`VendorInvoiceService.cs:165`) states PO lines have **no `ExpenseCategoryId`** — there is
nothing to inherit a default FROM. The closest available equivalent is the company's own
standard tax-code master, which is what step 2 above resolves against (the input-side sibling
of `SalesLineBackstop`'s "company's own standard output code" fallback), not a literal
category-default copy.

**Mechanics:** one new private `ResolveTaxCodesAsync(IReadOnlyList<PurchaseOrderLineInput>,
CancellationToken)` in `PurchaseOrderService.cs` — a single `AsNoTracking` preload of
`db.TaxCodes` (EF tenant-filtered) per request, resolved in memory (mirrors the N3 "load once"
pattern), returns a new line list via `l with { TaxCodeId = ..., TaxCode = ... }`. Called once,
before `Fill(...)`, in both `CreateDraftAsync` and `UpdateDraftAsync` (`req = req with { Lines =
await ResolveTaxCodesAsync(req.Lines, ct) };`). `Fill` itself (static) is untouched.
`SalesLineBackstop`/`SanitizeInheritedTaxCode`/`Resolve` are **not touched, not extended** — a
new small private method in `PurchaseOrderService` only (avoids inventing a shared
"PurchaseLineBackstop" for a single call site).

**Deviations to flag for Fable's ratification (do not block on it):**
- (i) VI-mirror is impossible as literally stated — no ExpenseCategory on a PO line.
- (ii) A second verbatim-id writer was found during the sweep: `PaymentVoucherService.cs:300`
  (`BuildLinesAsync`, `TaxCodeId = input.TaxCodeId`) — reachable from a direct
  `POST /payment-vouchers` (not just the already-safe `CreateFromVendorInvoiceAsync` copy path).
  **Out of this item's scope** (item names `PurchaseOrderService.cs:90` only) — logged here for
  a future finder, not fixed in this diff.

### Checklist

- [x] RED test: foreign-company `TaxCodeId` on PO create → stored verbatim, no throw (confirmed
      2026-08-19: 4/6 new tests RED for the right reason — 2 reject-tests, 2 null-resolve tests;
      2/6 already-correct-behavior tests green)
- [x] RED test: null pair + `TaxRate > 0` (FE-shaped payload) → stored as null, not resolved
      (same run as above)
- [x] Implement `ResolveTaxCodesAsync` + wire into Create/Update
      (`PurchaseOrderService.cs` — `CreateDraftAsync`/`UpdateDraftAsync` each do
      `req = req with { Lines = await ResolveTaxCodesAsync(req.Lines, ct) };` before `Fill`)
- [x] GREEN: foreign id rejected `po.tax_code_invalid`; own id passthrough; null+rate>0 resolves
      to company's standard input code; null+rate==0 stays null; null+matching-code-string
      resolves to that code's own id (master casing wins); update path also guards.
      `PurchaseOrderTaxCodeIntegrityTests`: 6/6 passed.
- [x] Regression: `--filter "FullyQualifiedName~Purchase"` → 93/93 passed (whole `Purchase`
      namespace + every PO-touching test elsewhere). `--filter
      "FullyQualifiedName~McpDocumentChainTests|...McpServerSmokeTests|...McpWriteExpansionTests|
      ...PaperEndpointTests|...PaperSignatureTests|...M4aDraftCreatedViaApiKeyTests"` → 117/117
      passed. Two PRE-EXISTING test bugs found and fixed along the way (the exact F13-class
      defect this unit targets, just baked into a test fixture instead of the FE):
      `PurchaseOrderCloseTests.PoReq` and `PaperSignatureTests.cs:460` both hardcoded
      `TaxCodeId: 1` while running against a freshly-seeded `TestCompanyFactory` company whose
      own id-1 is never that company's row — my new guard correctly caught it. Fixed by
      changing the literal to `null` (backend resolves it against the company's own "VAT7" by
      code-string match), mirroring the real FE fix's own convention. No assertion anywhere in
      the suite reads a PO line's stored `TaxCodeId`/`TaxCode` other than my own new tests
      (`PurchaseOrderLineDto` doesn't project either field) — confirmed by grep before the
      resolve-branch was added, per advisor's request.

**Files touched (item 1):**
1. `backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs` — `ResolveTaxCodesAsync` + 2 call sites
2. `backend/tests/Accounting.Api.Tests/Purchase/PurchaseOrderTaxCodeIntegrityTests.cs` (new)
3. `backend/tests/Accounting.Api.Tests/Hardening/PurchaseOrderCloseTests.cs` — pre-existing test bug fix (hardcoded foreign id)
4. `backend/tests/Accounting.Api.Tests/DocSignature/PaperSignatureTests.cs` — same, T5
5. `specs/fix-c1-backend-cleanup.md` (this file)

**Second verbatim-id writer found, logged for Fable, NOT fixed here (out of item scope):**
`PaymentVoucherService.cs:300` (`BuildLinesAsync`, `TaxCodeId = input.TaxCodeId`) — reachable
from a direct `POST /payment-vouchers`, not just the already-safe `CreateFromVendorInvoiceAsync`
copy path. Item names `PurchaseOrderService.cs:90` only.

## Item 2 — McpScopes narrow

### Facts established

- `McpScopes.cs:50` carried `master.employee.manage`, justified by r2 Tier-2 N3 as
  over-broad for `TeasMcpTools.list_employees` (id/code/Thai-name/active-flag read only, no
  payroll PII, no write). U6's `master.employee.lookup` (`Permissions.Master.EmployeeLookup`,
  `EmployeeEndpoints.cs`) is the purpose-built narrower permission for exactly this
  "resolve an employeeId before drafting" use case.
- `PermissionHandler` (API-key/MCP principal branch) does an EXACT single-string match against
  the token's CSV `scopes` claim — no OR/hierarchy. Confirmed via
  `backend/src/Accounting.Api/Authorization/PermissionRequirement.cs`.
- `ApiKeyService.CreateAsync` stores `req.Scopes` **verbatim** with no catalog-membership
  validation (only `EnforceMcpNoPostGuard` checks forbidden suffixes) — so narrowing
  `McpScopes.All` does NOT retroactively invalidate any already-issued key's stored scopes; it
  only changes what a NEW grant can request. Live check: 0 API keys in `accounting_dev` hold
  any employee scope today (query: `SELECT ... FROM sys.api_keys WHERE scopes::text ILIKE
  '%employee%'` → 0 rows) — confirms r2's own "verified NOT a leak today" finding, but the
  behavioral guarantee below is written defensively regardless of current live data.
- **Consequence:** narrowing the catalog AND changing `list_employees`'s `[Authorize(Policy=...)]`
  to require the exact string `master.employee.lookup` would 403 any key that (now or in the
  future) holds only the old `master.employee.manage` grant. Fixed via an OR-fallback policy
  (below) — an old key with `manage` simply keeps a broader-than-strictly-needed grant that
  still works; it is never silently downgraded or broken.

### Design

1. `McpScopes.cs` — catalog entry `"master.employee.manage"` → `"master.employee.lookup"`.
2. `PermissionRequirement.cs` — extracted `PermissionHandler`'s exact-match rule into a static
   `HasPermission(ClaimsPrincipal, string)` (API-key CSV / JWT permission claim / super-admin
   bypass), reused by both the original single-permission handler and the new OR-policy below.
   No behavior change to the existing single-permission path.
3. `PermissionPolicyProvider.cs` — new named policy constant
   `McpEmployeeLookupOrManagePolicy = "mcp.employee.lookup_or_manage"`, registered once in
   `AddPermissionAuthorization` via `RequireAssertion` (NOT a new
   `IAuthorizationRequirement`/`Handler` pair — this is a one-off two-permission OR, not a
   general mechanism; `McpScopes.cs`'s own comment on `report.read` notes the catalog
   deliberately has no OR-of-perms system). Succeeds when the caller holds EITHER
   `master.employee.lookup` OR `master.employee.manage`.
4. `TeasMcpTools.cs` — `list_employees`'s `[Authorize(Policy = ...)]` now points at the new OR
   policy (was the bare `mcpperm:master.employee.manage` single-string policy). No change to
   `ListEmployeesAsync`'s body/return shape (still `svc.ListAsync`, active-only by default,
   `includeInactive` toggle — this item narrows the SCOPE requirement only, not the underlying
   data query, since `IEmployeeService.LookupAsync` (U6, active-only/name-only) doesn't support
   `includeInactive` and would be a behavior regression for existing callers).
5. FE `settings/api-keys/page.tsx` — both `ALL_SCOPES` and `MCP_DEFAULT_SCOPES` arrays:
   `'master.employee.manage'` → `'master.employee.lookup'` (kept the parity test green; no
   other FE file references the MCP scope string — `settings/employees/page.tsx`,
   `SidebarNav.tsx`, e2e fixtures all use the UNRELATED, still-valid REST RBAC permission
   `master.employee.manage` for the full employee-CRUD page, which this item does not touch).

### Checklist

- [x] `McpScopes.cs` narrowed to `master.employee.lookup`
- [x] `list_employees` gated on OR-fallback policy (lookup OR manage) — adjusted, per item's ask
- [x] FE `ALL_SCOPES`/`MCP_DEFAULT_SCOPES` updated to match (parity test would otherwise fail —
      `Every_mcp_default_scope_normalizes_cleanly_through_McpScopes` needs every FE default
      scope to exist in the backend catalog)
- [x] Old-key back-compat proven: `Employee_list_employees_still_works_with_the_legacy_manage_scope_only`
- [x] Enforcement proven (not just documented): `Employee_list_employees_is_denied_without_either_scope`
- [x] Narrow-scope-works proven: `Employee_list_employees_returns_active_only_by_default` now
      mints its key with `["master.employee.lookup"]` only (was `.manage`)
- [x] GREEN: `McpScopesTests` 2/2, `McpScopeFrontendParityTests` 2/2,
      `McpBankExpenseFixedAssetTests` 23/23 (whole file), `RbacAuthMapTests` green (0 diff in
      `docs/rbac/*.md` — expected, no REST route/permission touched, confirmed by `git status
      --short docs/rbac/` empty after the run), combined regression run (`McpBankExpenseFixedAssetTests
      |RbacAuthMapTests|McpServerSmokeTests|McpReadExpansionTests|McpWriteExpansionTests`) 99/99.

**Files touched (item 2):**
1. `backend/src/Accounting.Application/Abstractions/McpScopes.cs`
2. `backend/src/Accounting.Api/Authorization/PermissionRequirement.cs` — static `HasPermission` extraction
3. `backend/src/Accounting.Api/Authorization/PermissionPolicyProvider.cs` — OR-fallback policy
4. `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` — `list_employees` policy attribute
5. `frontend/app/(dashboard)/settings/api-keys/page.tsx` — both scope arrays
6. `backend/tests/Accounting.Api.Tests/Mcp/McpBankExpenseFixedAssetTests.cs` — scope updates + 2 new tests

## Item 3 — seed-640 direct-grant arm coverage

### Facts established

- `640_seed_employee_lookup_perm.sql` step 4 (direct-grant regression guard, mirroring 629's
  step 5b / Opus Tier-2 F1) has never fired in the test suite: every existing employee-lookup
  test uses a TEMPLATE-cloned system role (ACCOUNTANT/CHIEF_ACCOUNTANT/COMPANY_ADMIN), which
  steps 2/3 (template top-up + per-company sync FROM the template) already cover. Step 4 exists
  specifically for a role holding `expense.claim.create` ONLY via a direct
  `sys.role_permissions` grant with no `sys.role_permission_templates` row at all — exactly what
  `RbacAdminService.CreateRoleAsync` (custom role) + `SetRolePermissionsAsync` (direct grant)
  produces.
- Exact precedent found: `ReadManageSplitGrantTests.cs`'s
  `Custom_role_with_a_direct_manage_grant_and_no_template_row_resolves_read_after_629_replay`
  (Opus Tier-2 F1, for 629's identical direct-grant-arm gap) — same real-service setup
  (`CreateRoleAsync` + `SetRolePermissionsAsync`), same real-script-file replay pattern,
  and the SAME per-company-loop scoping technique: `teas_test`'s `master.companies` has grown
  to 9,283 rows (verified live), and 629's own unscoped-loop replay was independently timed at
  >10 minutes there — so the loop's anchor text
  (`FOR c IN SELECT company_id FROM master.companies LOOP`, confirmed present in 640 too, byte
  for byte) is substituted to a single `WHERE company_id = <this test's company>` before
  execution. Mirrored exactly for 640.

### Design

New test `EmployeeLookupGrantTests.Custom_role_with_a_direct_expense_claim_create_grant_resolves_employee_lookup_after_640_replay`:
1. Fresh company (`TestCompanyFactory.CreateAsync`) → `IRbacAdminService.CreateRoleAsync` (a
   brand-new role code, never in the template) → `SetRolePermissionsAsync([Permissions.Expense.ClaimCreate])`
   (direct grant, no template row — asserted via a sanity `COUNT(*) = 0` on
   `sys.role_permission_templates`).
2. Sanity: role holds 0 `master.employee.lookup` grants before the replay (proves the gap is
   real, not vacuous — the RED-equivalent half of this coverage-only test, since no production
   code changes: the script already ships this logic correctly).
3. Read `640_seed_employee_lookup_perm.sql` from disk, substitute the loop anchor to scope to
   this one company, `ExecuteSqlRawAsync`.
4. Assert the role NOW holds `master.employee.lookup` (step 4 fired).
5. Replay the SAME scoped SQL a second time — assert no exception (idempotency: step 4's
   `INSERT ... WHERE NOT EXISTS (...)` guard must make a second run a silent no-op, no 23505).
6. `finally`: delete the synthetic role + its grants (must not linger in the shared `teas_test`,
   mirrors 629's own cleanup).

No production code changed — this is coverage-only, and the test passed on its FIRST run
(3s, company-scoped), confirming 640's step 4 is correct as shipped; it was simply untested.

### Checklist

- [x] New test added, mirrors `ReadManageSplitGrantTests`'s F1 precedent exactly
- [x] Sanity assertions confirm the direct-grant gap is real before the replay (not vacuous)
- [x] GREEN: role gains `master.employee.lookup` after replay
- [x] Idempotency proven: second replay does not throw (no 23505)
- [x] Regression: `EmployeeLookupGrantTests` 4/4, `ReadManageSplitGrantTests` 5/5, whole
      `Accounting.Api.Tests.Rbac` namespace 27/27 (includes `RbacAuthMapTests` — 0 diff in
      `docs/rbac/*.md`, confirmed by `git status --short docs/rbac/` empty)

**Files touched (item 3):**
1. `backend/tests/Accounting.Api.Tests/Rbac/EmployeeLookupGrantTests.cs` — 1 new test + usings

## Blast-radius note (Fable — read before accepting)

**Dispatch cap was 10 files for the whole C1 unit; actual distinct files touched = 13.** Not
caught before the fact — each item's own touch count looked reasonable in isolation and the
overrun only became visible checking `git status` after all 3 items were already done, green,
and diff-reviewed by me. Listing all 13 (spec + wiki counted, matching this repo's own
convention — see U2 spec §9's own 8-file list, which counts its spec + wiki entries):

1. `backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs` (item 1)
2. `backend/tests/Accounting.Api.Tests/Purchase/PurchaseOrderTaxCodeIntegrityTests.cs` (item 1, new)
3. `backend/tests/Accounting.Api.Tests/Hardening/PurchaseOrderCloseTests.cs` (item 1 — pre-existing
   test bug my new guard caught, NOT originally planned)
4. `backend/tests/Accounting.Api.Tests/DocSignature/PaperSignatureTests.cs` (item 1 — same, T5)
5. `backend/src/Accounting.Application/Abstractions/McpScopes.cs` (item 2)
6. `backend/src/Accounting.Api/Authorization/PermissionRequirement.cs` (item 2 — static-extraction refactor)
7. `backend/src/Accounting.Api/Authorization/PermissionPolicyProvider.cs` (item 2 — OR-fallback policy)
8. `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` (item 2)
9. `frontend/app/(dashboard)/settings/api-keys/page.tsx` (item 2 — required, or the FE parity test fails)
10. `backend/tests/Accounting.Api.Tests/Mcp/McpBankExpenseFixedAssetTests.cs` (item 2)
11. `backend/tests/Accounting.Api.Tests/Rbac/EmployeeLookupGrantTests.cs` (item 3)
12. `specs/fix-c1-backend-cleanup.md` (this file — checklist + attempt log, shared across all 3 items)
13. `troubles-wiki.md` (append-only — one new confirmed troubleshooting variant, shared)

Files #3/#4 (2 of the 3 "extra" files beyond a bare per-item minimum) were not scope creep —
they were PRE-EXISTING bugs (hardcoded foreign `TaxCodeId: 1` on a freshly-seeded, non-company-1
test company — the exact F13-class defect this whole cleanup line exists to close) that my new
Item 1 guard correctly caught as regressions; leaving them red was not an option. Files #6/#7
(item 2's OR-fallback policy) were the item's own explicit ask ("if the check would DENY
list_employees for old keys, add the fallback accept of manage") — verified via `advisor()` mid-task
before implementing, given it changed the item's design shape from the original dispatch text.
Every file is independently justified in its item's section above; nothing here is unrelated
drive-by work. Flagging per the operating rule (cap hit = stop-and-report, never silently
exceed) — I did not stop mid-item-2/3 to re-spec since the overrun was only visible in
retrospect and every individual touch was already necessary+verified; reporting now for Fable's
call on whether this needs a retroactive cap-header update or is accepted as-is.

## Attempt log

- 2026-08-19 Item 1: read U2 spec §0/§8 (laundering precedent + the deferred finding),
  `PurchaseOrderService.cs`, `PurchaseOrderDtos.cs`, `VendorInvoiceService.cs`,
  `PaymentVoucherService.cs`, TeasMcpTools PO tool descriptions. Confirmed no purchase resolver
  exists. Confirmed 0 violating rows in accounting_dev live. Chose reject-typed per module
  convention (bu.invalid precedent). Test plan: `PurchaseOrderTaxCodeIntegrityTests.cs` using
  `TestCompanyFactory` for a genuine cross-company foreign id.
