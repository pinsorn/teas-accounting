# MCP expansion v2 — bank rec (B) + expense claims (C) + fixed assets (D)

Ham ruling 2026-07-10: scope = READ + CREATE-DRAFT. The MCP invariant is
unchanged and absolute: **no tool may post/approve/pay/activate/dispose/run
depreciation** — state-changing money actions stay human-only via the UI.
Exemplar for every convention (Description style, draft-only wording, deep-link
returns, E3 list-only enforcement, company scoping): existing tools in
`backend/src/Accounting.Api/Mcp/TeasMcpTools.cs`.

## Tools to add

### Bank reconciliation (read-only)
- [x] `list_bank_accounts` — id, bank, account no (masked per existing masking
  convention if one exists), linked GL account, active flag. Evidence: no
  masking convention exists anywhere in the codebase (grepped `MaskAccountNo`
  etc — 0 hits), so `BankAccountListItem` is returned as-is (AccountNo
  verbatim). Wraps `IBankAccountService.ListAsync`. Policy `bank.account.read`.
- [x] `get_bank_reconciliation_report` — wraps `IBankReconciliationReportService
  .GetAsync`. Policy `bank.report.read`. Throws `bank_account.not_found` for
  an unknown id (test: `Bank_get_reconciliation_report_unknown_account_is_rejected`).

### Expense claims (read + draft)
- [x] `list_employees` — REQUIRED prerequisite: create_expense_claim_draft
  needs an employeeId; MCP has no employee tool today. Returns a new slim
  `EmployeeOption` projection (id, code, Thai name, active flag ONLY —
  payroll fields NationalId/BaseSalary/bank details deliberately excluded,
  test-asserted). Policy `master.employee.manage` (no separate .read scope
  in the catalog — mirrors the VendorManage reuse pattern).
- [x] `create_expense_claim_draft` — employee guarded via new
  `GuardEmployeeAsync` (mirrors GuardVendorAsync); expense category validated
  company-scoped by the existing ExpenseClaimService (same pattern as PV/VI —
  no MCP-layer guard needed there). CreateExpenseClaimRequest used directly
  (already non-nullable EmployeeId/ExpenseCategoryId — no MCP-only wrapper
  needed). Returns draft id + `/expense-claims/{id}?action=approve` deep-link.
  Description states the agent cannot submit/approve/pay. Policy
  `expense.claim.create`.
- [x] `update_expense_claim_draft` — Draft/Rejected only, full replace (mirrors
  `update_vendor_invoice_draft`); service throws `expense_claim.not_editable`
  on a Submitted/Approved/Paid claim (test-covered).
- [x] `list_expense_claims` (status/employee/date filters) + `get_expense_claim`
  (header + lines + status + JE id when paid). Policy `expense.claim.read`.

### Fixed assets (read + draft)
- [x] `create_fixed_asset_draft` — name, category, acquire date, cost, salvage,
  useful life months, optional vendor invoice link + account overrides
  (CreateFixedAssetRequest used directly — VendorInvoiceId already nullable/
  optional, unguarded like QuotationId on create_tax_invoice_draft). Draft
  only; Description states activation is human-only. Policy
  `fixedasset.manage`. Returns `/fixed-assets/{id}?action=approve`.
- [x] `update_fixed_asset_draft` — Draft only, full replace; service throws
  `fixed_asset.not_editable` after Activate (test-covered).
- [x] `list_fixed_assets` (status/category filters) + `get_fixed_asset`
  (incl. accumulated depreciation, NBV, run-line history). Policy
  `fixedasset.read`.
- [x] `get_fixed_asset_register` (asOf) + `get_accumulated_depreciation_report`
  (year) — wrap the existing report services. Policy `fixedasset.read`.
- [x] `list_depreciation_runs` — read-only run history (year/month/total/JE id).
  Policy `fixedasset.read`.

14 tools total (spec listed "13" in the dispatch summary — the checklist
itself enumerates 14 distinct tool names across the three groups; all 14
implemented, none dropped).

### Auth finding (critical check, done BEFORE writing tools)
MCP identity authorization is NOT RBAC-role-based at request time: an
mcp-kind API key's permissions live in its own `ScopesJson` CSV column,
checked directly against `mcpperm:<scope>` by `PermissionHandler` (see
`Authorization/PermissionRequirement.cs`). `sys.roles`/`role_permissions`
(the tables 615/617/620 write to) govern JWT-authenticated human users only,
and — for the OAuth/DCR consent flow only — filter which of the OAuth
token's requested scopes survive via `McpConsentScopes.FilterToRbac`
(identity-mapped: scope code == permission code, the default case, applies
to every new scope here — no special-case needed).

The permission codes + role grants for bank/expense/fixed-asset (and
`master.employee.manage`) ALREADY EXIST — SqlScripts 615/617/620 already ran
(main is at v1.17.0 with these features live) and already grant
COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT the read/create-class codes. **No
new SqlScript 623 was needed** — this is a deviation from the spec's
assumption, reported per instruction before proceeding.

What WAS actually missing: `Accounting.Application.Abstractions.McpScopes
.All` — the OAuth-consent scope catalog — did not list these 7 new codes.
Without that catalog entry, a scope can never survive `McpScopes.Normalize`
during OAuth/DCR consent, so the new tools would be permanently unreachable
via that path even for a user holding the RBAC permission (same gap the
file's own C2 comment documents having fixed once before). Manually-created
mcp-kind API keys were unaffected (`ApiKeyService.CreateAsync` never
filtered scopes against `McpScopes.All`, only blocks forbidden `.post`-class
suffixes) but the frontend's key-creation scope picker
(`app/(dashboard)/settings/api-keys/page.tsx`, `MCP_DEFAULT_SCOPES`) mirrors
this same backend catalog for its checkbox list — so that catalog is the
practical, only reachable grant surface either way.
Fix applied: added the 7 scope strings to `McpScopes.All` (a code-catalog
addition, not a SQL script) — treated as "MCP registration" plumbing, within
the dispatch's blast-radius cap. `McpConsentScopesTests`/`McpScopesTests`
still pass unmodified (property-based, no snapshot count).
**Follow-up flagged, NOT done (frontend is out of blast radius):** the
frontend `MCP_DEFAULT_SCOPES` checkbox list should get the same 7 scopes
added so a human can actually pick them when minting an mcp-kind key via the
UI; until then, the raw REST admin API (`POST /api-keys` with an explicit
`scopes` array) still works for granting them.

## Constraints
- Wrap EXISTING services only (ExpenseClaimService, FixedAssetService, bank
  rec services) — zero new business logic, zero schema changes, zero edits to
  the services themselves. If a needed method is missing from a service
  interface, STOP and report (do not add service methods without a re-spec).
- Auth: verify how existing MCP tools map to permissions and whether the MCP
  agent identity/role holds the NEW perms (expense.claim.read/create,
  fixedasset.read/manage, bank.* read). If the MCP role needs new grants →
  that is a new numbered SqlScript following the 617/620 bypass pattern —
  flag it in the report and follow the pattern exactly (insert/grant one file,
  SET LOCAL app.bypass_rls first, NOT EXISTS fan-out).
- Blast radius: TeasMcpTools.cs + MCP registration + tests (+ the one seed
  script IF the auth check demands it). Nothing else.
- Tests: mirror the existing MCP tool test pattern — happy path + validation
  failure (missing employee / missing category / non-draft update) per tool.

## Verification gates
- `dotnet build` green; full suite green, skip == 8 baseline (916 total + new).
- Every new tool Description documents draft-only/read-only semantics.
- Grep proof: no new tool calls Submit/Approve/Pay/Activate/Dispose/
  GenerateDepreciation on any service.

## Attempt log
- 2026-07-10 Fable: spec authored from Ham ruling (read + draft-create).
- 2026-07-10 sonnet-implementer: all 14 tools implemented in TeasMcpTools.cs
  (+ GuardEmployeeAsync guard, + EmployeeOption slim projection). Auth finding:
  no SqlScript 623 needed (RBAC grants already exist); instead added the 7
  missing scope strings to McpScopes.All (OAuth-consent catalog) — see finding
  above. New test file McpBankExpenseFixedAssetTests.cs (17 tests: tool
  inventory + happy path for all 14 tools + explicit failure/guard tests for
  the 5 tools with real guards: unknown bank account, unknown employee,
  non-Draft expense-claim update, invalid-salvage validator, non-Draft
  fixed-asset update). Gates: `dotnet build` green (0 warnings/errors); full
  suite 933 total (916 baseline + 17 new) = 925 passed + 8 skipped + 0 failed,
  skip count unchanged from baseline. Grep proof: zero calls to
  Submit/Approve/Pay/Activate/Dispose/WriteOff/GenerateDepreciation anywhere
  in TeasMcpTools.cs (only a comment naming them as NOT exposed); zero
  McpServerTool names matching activate/dispose/writeoff/generate_depreciation/
  submit/approve/pay/reject/cancel. Blast radius: TeasMcpTools.cs, McpScopes.cs
  (catalog-only), 1 new test file. No SqlScript, no service edits, no frontend
  edits (flagged as follow-up, not done).
