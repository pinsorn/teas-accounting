# Expense Claims (Cycle C, feature #3)

<!-- Living document. Worker updates the checklist as it works; a retry uses
     the SAME file and grows the attempt log. Do NOT rewrite for a retry. -->

Employee expense reimbursement: submit -> approve -> pay (GL posting).
Submitters key claims in on behalf of an employee from the Employee master
(no login for the employee). Multi-line; each line carries an ExpenseCategory
(-> GL account) and receipts (Attachment infra). Phase 2 (NOT now): receipt OCR.

Source of scope: `PLAN-feature-cycle-2026-07.md` section 3.

---

## 0. BLOCKING open question — read FIRST (money-path architecture)

**The locked scope says "pay generates/links a Payment Voucher (PV)". Taken
literally this is NOT buildable inside the blast-radius cap, and is
semantically wrong for Thai tax. Ham / Fable must rule before the money path
is implemented.** Evidence (all read from the code):

- `PaymentVoucher.VendorId` is **non-nullable** (`backend/src/Accounting.Domain/Entities/Purchase/PaymentVoucher.cs:28`),
  **required** by the DTO (`PaymentVoucherDtos.cs:23`) and validator
  (`RuleFor(x => x.VendorId).GreaterThan(0)`), and `CreateDraftAsync` hard-throws
  `pv.vendor_missing` if it does not resolve to a `Vendor` row
  (`PaymentVoucherService.cs:116-117`).
- Every PV payee field (`VendorName`, `VendorTaxId`, `VendorType`) is a
  **snapshot copied from the Vendor** (`PaymentVoucherService.cs:237-241`). There
  is no free-text payee, no employee->vendor bridge, no standalone-payee path.
  `VendorType` also drives PND3-vs-PND53 WHT classification + 50-ทวิ certificate
  generation (`PaymentVoucherService.cs:326-403`).
- `Employee` has **no link to a Vendor and no GL account**
  (`backend/src/Accounting.Domain/Entities/Master/Employee.cs`; class comment
  "Fully standalone"). It carries its own `BankName/BankAccountNo/BankAccountName`.
- There is **no "generate PV from an upstream document" pattern** anywhere, and
  **no generic `SourceDocType`/`SourceDocId`** on PV (only a `VendorInvoiceId`
  settlement link).

Making PV accept an employee payee = editing PV entity + DTO + validator +
`CreateDraftAsync` + snapshots + WHT/GL posting = **PV-core / money-machinery
edit = blast-radius STOP**. Reimbursing an employee's out-of-pocket spend is
also **not** a WHT-taxable vendor payment (any WHT was already handled by the
employee at the original purchase), so it must NOT generate a 50-ทวิ cert or hit
`WhtPayableAccount`, and must NOT pollute the Vendor master / AP aging / PND3-53
filings with employees.

### Options for Ham/Fable
- **(A) RECOMMENDED — self-contained cash disbursement.** Expense Claim is its
  own document. On *pay* it posts its own Journal Entry
  (Dr expense per line / Dr recoverable Input VAT / Cr Cash-or-Bank) via a **new,
  additive** `GlPostingService.PostExpenseClaimAsync` that mirrors the structure
  of the existing `PostPaymentVoucherAsync` and reuses the same low-level JE
  writer + `INumberSequenceService` + attachment infra. **Touches zero PV code.**
  Semantically correct (no vendor/AP/WHT artifacts). The Expense Claim document
  *is* the payment voucher (it is a cash-out authorization). Deviates from the
  literal "create a PV row" wording only.
- **(B) Employee<->Vendor bridge.** Auto-create/link a Vendor per employee and
  reimburse through a real PV. Pollutes Vendor master + AP aging + WHT filings;
  still needs PV-core changes to suppress WHT. NOT recommended.
- **(C) Single internal "Employee Reimbursement" clearing Vendor per company.**
  All claims pay via one PV to a generic vendor; employee identity lives only on
  the claim. Reuses PV literally but routes reimbursements through vendor/AP/WHT
  machinery and needs WHT=0 discipline. Middling.

**This spec fully designs Option A** (schema, state machine, worked money
example, tests) so it is immediately actionable on an "A" ruling. If Ham picks
B or C, sections 3 and 5's money/pay parts are re-specced; everything else
(schema header/lines, employee dropdown, attachments, approval state machine,
FE list/form/approve, seeds, tests for non-money invariants) is unchanged.

---

## Context / footguns

Env: Windows 11, PowerShell 5.1 (no `&&`; write files UTF-8 `-Encoding utf8`).
Current `TEAS_TEST_PG` = `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true` (troubles-wiki "Stale TEAS_TEST_PG"). Schema via **EF migrations**; RLS/triggers/seeds via numbered
`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/*.sql` applied at
startup by `DbInitializer.ApplyScriptsAsync` (idempotent, tracked in
`sys.applied_sql_scripts`). Highest existing scripts: `614`, `615` -> **next
free = 616**.

**FOOTGUN 1 — Startup seed RLS 42501 / silent zero fan-out (troubles-wiki top
entry; MANDATORY for `617_seed_expense_claim_perms.sql`).** `ApplyScriptsAsync`
runs BEFORE `TenantMiddleware`, so `app.company_id` is UNSET and (on prod) the
connection is a NOBYPASSRLS role. `sys.roles / sys.permissions /
sys.role_permissions` are G3 system-global: without bypass, a per-company
fan-out `INSERT ... SELECT ... FROM sys.roles WHERE company_id IS NOT NULL`
sees **zero** rows and **silently inserts nothing on prod** — no crash, and
**invisible on teas_test** (superuser bypasses RLS). Fix: the perms seed MUST
have `SET LOCAL app.bypass_rls = 'on';` as its FIRST statement (matches
`615_seed_bank_rec_perms.sql` and every existing bypass call site). **Deploy
probe is a ROW COUNT, not an exit code** (see section 6 / verification gates).

**FOOTGUN 2 — RBAC seed-ordering.** Permission-code INSERT must come before the
grants that reference it, in the SAME file (memory "RBAC seed-ordering"). `615`
does insert-first / grant-second in one file; mirror exactly. Do NOT split
across files.

**FOOTGUN 3 — literal `{` `}` in any SqlScript.** `ExecuteSqlRawAsync` parses
the whole file as a `string.Format` template even with zero params — a literal
brace anywhere (comments included) throws `FormatException` and takes down every
test touching `PostgresFixture` (troubles-wiki `613`). Describe things in prose;
no braces.

**FOOTGUN 4 — number-gap view int overflow.** `tax.v_number_gaps`
(`613_number_gap_view_bigint.sql`) casts trailing digit-runs across ALL
companies. Keep `doc_no` trailing digits <= 18. Any test that seeds a synthetic
`DocNo` directly (bypassing `INumberSequenceService`) MUST end it in a
non-digit char (troubles-wiki `22003`).

**FOOTGUN 5 — server re-pins DocDate to today.** Sales/purchase services re-pin
`DocDate` to `IClock.TodayInBangkok()` at post (troubles-wiki "exact past/future
DocDate"). The *pay* JE post-date is server-today; the line-level `expense_date`
(when the cost was incurred) may be in the past, but **do not assert a backdated
JE post date in tests** — post at today, vary the query range instead. Fresh
`teas_test` closes the previous month per `CURRENT_DATE` (memory
"relative-date seed"), so use today/future dates in tests, never hardcoded past
months, or `period.closed` fails.

**FOOTGUN 6 — RLS masked by superuser tests (memory).** teas_test connects as
superuser -> RLS bypassed -> a true "company A row invisible to company B" test
proves scoping via the EF query filter, NOT the DB RLS policy. Do not claim RLS
is verified from a green teas_test run. For a real RLS-behavioural test use
`SET ROLE pg_database_owner` + explicit `GRANT SELECT` (troubles-wiki "New RLS
test SKIPs"), otherwise keep it a query-filter test and label it so.

**FOOTGUN 7 — always rewrite lines on draft edit.** The repo convention for
`UpdateDraftAsync` is to DELETE+recreate all child line rows every edit, never a
diff/skip (troubles-wiki "immutability trigger ... always rewrite lines"). This
means **line-row ids are NOT stable across draft edits** — which is why
attachments parent to the stable **header** id, not the volatile line id (see
section on attachments).

**FOOTGUN 8 — inert concurrency token on PV.** The `long Version`
`.IsConcurrencyToken()` on PV/PO is **declared but never incremented** anywhere
(no interceptor, no trigger, no `Version++`), so PV's approve/post transitions
are TOCTOU-racy despite the comments. Expense Claims MUST do better: increment
`Version` in each state-transition method so the optimistic lock actually fires
(see section 4). This is local to the new entity; do NOT "fix" PV here.

**FOOTGUN 9 — teas_test fixture apply-once + false-green skips.** Each SQL seed
runs ONCE (tracked); to re-exercise a changed `616/617/618` on teas_test,
`DELETE FROM sys.applied_sql_scripts WHERE script_name = '<name>.sql'` then
re-run. Skipped tests fake a green run — always check the skip count vs baseline
(memory "TEAS_TEST_PG per-shell").

**FOOTGUN 10 — new public route topology.** Authenticated FE pages reach the
backend through the existing `/api/proxy/[...path]` BFF — a NEW authenticated
REST route needs NO new passthrough. Verify a new route on the public domain
with `curl .../api/proxy/expense-claims` (expect 401), not the bare path
(troubles-wiki "307 not 401"). No `PUBLIC_PATHS` entry (these routes are
session-gated, not anonymous).

---

## Reuse map (every path below was read for this spec)

| Need | Reuse | Path |
|---|---|---|
| Doc status enum | `DocumentStatus`? NO — new `ExpenseClaimStatus` (different states) | `backend/src/Accounting.Domain/Enums/` (add) |
| Concurrency token | `IConcurrencyVersioned` (long Version) | `backend/src/Accounting.Domain/Common/IConcurrencyVersioned.cs` |
| Tenant filter | `ITenantOwned` (int CompanyId) — auto EF query filter | `AccountingDbContext.cs:160 ApplyTenantFilter` |
| Category -> GL acct | `ExpenseCategory.DefaultExpenseAccountId` (nullable), resolved `line.ExpenseAccountId ?? category default` | `backend/src/Accounting.Domain/Entities/Sys/ExpenseCategory.cs:19`; consumed `PaymentVoucherService.cs:162` |
| Input VAT / Cash / Bank accts | ChartOfAccounts resolver `_accounts.InputVatAccount` etc. | used in `GlPostingService.cs:153-191` |
| JE building reference | `GlPostingService.PostPaymentVoucherAsync` (mirror structure, new sibling method) | `backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs:147-226` |
| Doc numbering | `INumberSequenceService.NextAsync(company,branch,prefix,subPrefix,date,ct)` on caller tx | `backend/src/Accounting.Infrastructure/Numbering/NumberSequenceService.cs`; call `PaymentVoucherService.cs:316` |
| Attachments (polymorphic) | `IAttachmentService.UploadAsync(parentType,parentId,category,...)` | `backend/src/Accounting.Infrastructure/Attachments/AttachmentService.cs`; **bank-rec integration exemplar** `StatementImportService.cs:85-89` |
| Attach category (exists) | `AttachmentCategory.ExpenseClaimForm = "EXPENSE_CLAIM_FORM"` already defined | `backend/src/Accounting.Domain/Enums/AttachmentEnums.cs:33`, `AttachmentCodes.cs:39` |
| Employee master | `Employee` entity + `GET /employees` list | `backend/src/Accounting.Domain/Entities/Master/Employee.cs`; `EmployeeEndpoints.cs:42`; FE `useEmployees()` `frontend/lib/queries.ts:556` |
| Bank account -> GL cash acct | bank_accounts.gl_account_id (Cycle B) | `bank.bank_accounts` (verify column name at impl) |
| RLS policy template | G1 `company_isolation` per table | `SqlScripts/614_bank_reconciliation_rls.sql` |
| Perms seed template | insert-first/grant-second + bypass | `SqlScripts/615_seed_bank_rec_perms.sql` |
| Permission constants | add to `Permissions.cs` + `.All` + `PermissionCatalog.cs` | `backend/src/Accounting.Api/Authorization/Permissions.cs` |
| FE list page | DataTable + PermissionGate + PageHeader | `frontend/app/(dashboard)/bank-accounts/page.tsx` |
| FE detail + approve/post | approve/post buttons via PermissionGate + mutateAsync | `frontend/app/(dashboard)/payment-vouchers/[id]/page.tsx` |
| FE multi-line form | line-array `rows` state + add/remove | `frontend/app/(dashboard)/payment-vouchers/new/page.tsx` |
| FE simple picker | clone `BusinessUnitSelector` (select from master list) | `frontend/components/ui/BusinessUnitSelector.tsx`; category picker exists `frontend/components/ui/ExpenseCategorySelector.tsx` |
| Approve mutation hook | `useApprovePaymentVoucher` | `frontend/lib/queries.ts:480` |
| i18n | add `expenseClaims` namespace | `frontend/messages/en.json`, `th.json` |
| Nav | add `NavItem` to SECTIONS | `frontend/components/app-shell/SidebarNav.tsx` |

---

## Requirements (checklist)

### 1. Schema (EF migration + DDL sketch)
- [x] New entities `ExpenseClaim` + `ExpenseClaimLine` at
  `backend/src/Accounting.Domain/Entities/Expense/`. Header implements
  `ITenantOwned, IAuditable, IConcurrencyVersioned`. Type deviations from the DDL
  sketch (verified against actual FK-target CLR types): `BankAccountId` is `int?`
  (BankAccount.BankAccountId is int, not bigint) and
  `ExpenseClaimLine.ExpenseCategoryId` is `int` (ExpenseCategory.CategoryId is int).
- [x] Config `backend/src/Accounting.Infrastructure/Persistence/Configurations/Expense/ExpenseClaimConfiguration.cs`
  (both header + line configs in one file; auto-discovered by
  `ApplyConfigurationsFromAssembly`). Table names in a new `expense` schema.
- [x] DbSets in `AccountingDbContext.cs` (2 lines).
- [x] EF migration `dotnet ef migrations add ExpenseClaims` — evidence: generated
  migration creates exactly 2 tables (`expense_claims`, `expense_claim_lines`) +
  `EnsureSchema("expense")`, FKs to master.employees/business_units,
  bank.bank_accounts, gl.journal_entries, sys.expense_categories; unique
  `(company_id,branch_id,doc_no)` filtered; unique line `(expense_claim_id,line_no)`;
  no drops of any existing table. `dotnet build Accounting.sln` → 0 Error(s).

DDL sketch (authoritative shape; column list is exact):

```
expense.expense_claims
  expense_claim_id     bigint  PK generated always as identity
  company_id           int     NOT NULL              -- tenant (ITenantOwned)
  branch_id            int     NOT NULL
  business_unit_id     int     NULL
  employee_id          bigint  NOT NULL  FK master.employees   -- payee/beneficiary
  doc_no               varchar(40) NULL              -- assigned at PAY only
  prefix_code          varchar(20) NOT NULL DEFAULT 'EX'
  sub_prefix           varchar(20) NULL
  claim_date           date    NOT NULL              -- date claim keyed in
  title                varchar(200) NULL
  status               varchar(20) NOT NULL DEFAULT 'DRAFT'   -- ExpenseClaimStatus
  payment_method       varchar(20) NULL              -- CASH | TRANSFER, set at pay
  bank_account_id      bigint  NULL  FK bank.bank_accounts     -- Cr source when TRANSFER
  subtotal_amount      numeric(19,4) NOT NULL DEFAULT 0        -- sum(line.amount)
  vat_amount           numeric(19,4) NOT NULL DEFAULT 0        -- sum(line.vat_amount)
  total_amount         numeric(19,4) NOT NULL DEFAULT 0        -- subtotal + vat
  journal_entry_id     bigint  NULL  FK gl.journal_entries     -- set at pay
  reject_reason        varchar(500) NULL
  notes                varchar(1000) NULL
  version              bigint  NOT NULL DEFAULT 0               -- concurrency (Version++ each transition)
  created_at/by, updated_at/by, approved_at/by, paid_at/by     -- audit
  UNIQUE (company_id, branch_id, doc_no) WHERE doc_no IS NOT NULL

expense.expense_claim_lines
  expense_claim_line_id bigint PK identity
  expense_claim_id      bigint NOT NULL FK expense.expense_claims ON DELETE CASCADE
  company_id            int    NOT NULL             -- ITenantOwned (RLS)
  line_no               int    NOT NULL
  expense_category_id   bigint NOT NULL FK sys.expense_categories   -- per-LINE (see note)
  expense_account_id    bigint NOT NULL             -- frozen at draft: line ?? category.DefaultExpenseAccountId
  description           varchar(300) NOT NULL
  expense_date          date   NULL                 -- when the cost was incurred
  amount               numeric(19,4) NOT NULL        -- net (ex-VAT)
  tax_code_id          bigint NULL
  vat_rate             numeric(5,2)  NOT NULL DEFAULT 0
  vat_amount           numeric(19,4) NOT NULL DEFAULT 0
  is_recoverable_vat   boolean NOT NULL DEFAULT false
  line_total           numeric(19,4) NOT NULL        -- amount + (is_recoverable? 0 : vat)  -- see money rules
  UNIQUE (expense_claim_id, line_no)
```

**Note (deliberate deviation from PV):** ExpenseCategory is **per-LINE** here
(PV keeps it header-level). Scope says "each line has an ExpenseCategory" and a
real claim mixes taxi/hotel/meals across lines. `expense_account_id` is resolved
and frozen on the line at draft time (line override `??`
`category.DefaultExpenseAccountId`, else throw `expense_claim.expense_account_missing`),
identical to `PaymentVoucherService.cs:162`.

### 2. Permission codes + seeds (SqlScripts)
- [x] Add constants to `Permissions.cs` (new nested class `Permissions.Expense`):
  `ClaimRead="expense.claim.read"`, `ClaimCreate="expense.claim.create"`,
  `ClaimApprove="expense.claim.approve"`, `ClaimPay="expense.claim.pay"`. Add
  each to the `Permissions.All` array AND to `PermissionCatalog.cs` (bilingual
  TH/EN labels).
- [x] `SqlScripts/616_expense_claims_rls.sql` — G1 `company_isolation` for BOTH
  `expense.expense_claims` and `expense.expense_claim_lines` (copy `614`
  verbatim, swap table names). DDL-only (CREATE POLICY) -> no 42501 risk at
  apply time. No bypass arm (G1 tenant data). Skeleton per table:

  ```sql
  ALTER TABLE expense.expense_claims ENABLE ROW LEVEL SECURITY;
  ALTER TABLE expense.expense_claims FORCE ROW LEVEL SECURITY;
  DROP POLICY IF EXISTS company_isolation ON expense.expense_claims;
  CREATE POLICY company_isolation ON expense.expense_claims
      USING ( company_id = NULLIF(current_setting('app.company_id', true), '')::INT );
  ```
- [x] `SqlScripts/617_seed_expense_claim_perms.sql` — **mirror `615` verbatim**.
  FIRST statement `SET LOCAL app.bypass_rls = 'on';` (FOOTGUN 1). Then, in ONE
  file: (1) INSERT 4 codes into `sys.permissions` `ON CONFLICT (permission_code)
  DO NOTHING`; (2) grant all `expense.%` to `SUPER_ADMIN` where `company_id IS
  NULL`; (3) INSERT into `sys.role_permission_templates` for `COMPANY_ADMIN,
  CHIEF_ACCOUNTANT, ACCOUNTANT` (so new companies inherit); (4) fan out to every
  existing company's matching roles (`sys.role_permissions` with `company_id`,
  `NOT EXISTS` guard). NO literal braces anywhere (FOOTGUN 3).
- [x] `SqlScripts/618_seed_expense_claim_prefix.sql` — add `'EX'` +TH/EN label to
  `sys.document_prefixes` `ON CONFLICT DO NOTHING` (cosmetic/UI+gap-view only;
  `NumberSequenceService` self-seeds `number_sequences`). If
  `sys.document_prefixes` is RLS'd/tenant-scoped, apply the FOOTGUN-1 rule; if
  it is plain metadata (like the `100` seed), a bare INSERT is fine — verify
  which at impl.
- [x] Roles that get the perms: `COMPANY_ADMIN`, `CHIEF_ACCOUNTANT`, `ACCOUNTANT`
  get read/create. **Who gets approve/pay is an open question** (see section
  Open Questions) — default: `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT` get approve +
  pay; `ACCOUNTANT` gets create + read only. Encode that split in `617` via
  per-role template rows, NOT a blanket `LIKE 'expense.%'` to all three.

### 3. Money path (Option A) — GETS FABLE LINE-BY-LINE REVIEW
- [x] New **additive** method `GlPostingService.PostExpenseClaimAsync(long
  expenseClaimId, ...)` mirroring `PostPaymentVoucherAsync` (`GlPostingService.cs:147-226`).
  Confirmed at impl: `PostPaymentVoucherAsync` does NOT call a shared helper for
  JE line-building — it calls the SAME PRIVATE `BuildAndPostAsync` (balance-check
  + JV-number + `MarkPosted`) that every other poster (incl. `PostManualEntryAsync`)
  calls. `PostExpenseClaimAsync` builds its own JE line list inline (mirroring the
  inline pattern) and calls that SAME private `BuildAndPostAsync` — zero PV code
  touched (`git diff --stat` on GlPostingService.cs/IGlPostingService.cs = pure
  insertions, 89 lines added, 0 removed/changed).
- [x] Accounts resolved via the existing ChartOfAccounts resolver `_accounts`:
  expense = `line.ExpenseAccountId` (frozen at draft); recoverable VAT =
  `_accounts.InputVatAccount`; credit = Cash account if `payment_method=CASH`,
  else the selected bank account's OWN `GlCashAccountId` (bank.bank_accounts has
  no column literally named `gl_account_id` — the actual column/property is
  `GlCashAccountId`/`gl_cash_account_id`, confirmed in BankAccount.cs and
  BankAccountService.cs; not a schema-break per the STOP trigger, just a name).
- [x] **JE line rules (exact):**
  - Per line: `Dr line.ExpenseAccountId = is_recoverable_vat ? amount : amount +
    vat_amount` (non-recoverable VAT is expensed into the cost, exactly like
    `GlPostingService.cs:177-183`).
  - Per recoverable line: `Dr InputVatAccount = vat_amount` (aggregate or
    per-line; aggregate is fine — one Dr Input VAT = sum of recoverable
    vat_amount).
  - Single balancing credit: `Cr Cash-or-Bank = total_amount`
    (= subtotal + vat).
  - **WHT: NONE.** No `WhtPayableAccount` line, no 50-ทวิ `WhtCertificate`, no
    `WhtType`/rate anywhere in the claim. State this explicitly in the method and
    in a code comment: reimbursing an employee's out-of-pocket expense is not a
    withholding event.
  - Invariant asserted before write: `sum(debits) == sum(credits) ==
    total_amount + sum(recoverable vat already inside debits...)` — concretely
    `sum(Dr) == total_amount` and `Cr == total_amount`. Reuse the existing JE
    writer's own balance check.
- [x] Doc number allocated **only at pay**, inside the pay transaction:
  `_numbers.NextAsync(claim.CompanyId, claim.BranchId, "EX", subPrefix,
  postDate, ct)` (postDate = `IClock.TodayInBangkok()`), mirroring
  `PaymentVoucherService.cs:315-316`. Set `claim.DocNo`, `claim.JournalEntryId`,
  `claim.Status=Paid`, `claim.PaidAt/By`, `Version++`, all in one DB transaction
  (`BeginTransactionAsync` ... `CommitAsync`, mirror `PaymentVoucherService.cs:293/451`).

**Worked money example (numbers in -> JE lines out).** Claim by employee
"Somchai", pay by TRANSFER from a bank account whose `gl_account_id` = 1010
(Cash at Bank). Recoverable VAT account = 1155 (Input VAT).

| Line | Category -> acct | net (amount) | VAT rate | vat_amount | recoverable? | line_total |
|---|---|---|---|---|---|---|
| 1 Taxi | Travel 6120 | 500.00 | 0% | 0.00 | n/a | 500.00 |
| 2 Hotel | Accommodation 6130 | 1,000.00 | 7% | 70.00 | yes | 1,000.00 |
| 3 Client meal | Entertainment 6140 | 200.00 | 7% | 14.00 | **no** (non-deductible) | 214.00 |

Header: subtotal = 1,700.00, vat = 84.00, total = 1,784.00.

Resulting Journal Entry (post date = today):
```
Dr 6120 Travel                 500.00
Dr 6130 Accommodation        1,000.00
Dr 6140 Entertainment          214.00   (200 net + 14 non-recoverable VAT)
Dr 1155 Input VAT (recov.)      70.00   (only line 2's VAT)
   Cr 1010 Cash at Bank                1,784.00
```
Debits = 500 + 1,000 + 214 + 70 = **1,784.00**. Credits = **1,784.00**.
Balanced. WHT lines: **zero**. No 50-ทวิ cert issued.

### 4. State machine
`ExpenseClaimStatus` enum: `Draft, Submitted, Approved, Paid, Rejected,
Cancelled`. Transitions in entity methods that guard the current status
(in-memory, mirror PV `MarkApproved`/`MarkPosted` guards
`PaymentVoucher.cs:110-134`) AND `Version++` (FOOTGUN 8). Permission enforced at
the route via `PermissionPolicyProvider.PolicyPrefix + <const>`.

| Transition | From -> To | Method / guard (throws) | Permission | Notes |
|---|---|---|---|---|
| Create draft | (none) -> Draft | `POST /expense-claims/` | `expense.claim.create` | doc_no NULL |
| Edit draft | Draft/Rejected -> same | `PUT /expense-claims/{id}` | `expense.claim.create` | **always rewrite line rows** (FOOTGUN 7) |
| Submit | Draft/Rejected -> Submitted | `Submit()` throws `expense_claim.not_draft` | `expense.claim.create` | locks editing |
| Approve | Submitted -> Approved | `Approve()` throws `expense_claim.not_submitted` | `expense.claim.approve` | |
| Reject | Submitted -> Rejected | `Reject(reason)` throws `expense_claim.not_submitted` | `expense.claim.approve` | reason required; returns to editable |
| Pay | Approved -> Paid | `Pay()` throws `expense_claim.not_approved`; posts JE + doc_no | `expense.claim.pay` | in a DB tx; terminal |
| Cancel | Draft/Rejected -> Cancelled | `Cancel()` throws `expense_claim.cannot_cancel` | `expense.claim.create` | only pre-GL; terminal |

- Terminal states: `Paid` (immutable — JE posted, doc_no set), `Cancelled`.
- **Race safety.** Every transition method does `Version++`; EF optimistic
  concurrency (`.IsConcurrencyToken()`) then throws `DbUpdateConcurrencyException`
  on a losing concurrent write -> endpoint maps to **409**. `Pay` additionally
  wraps in a DB transaction and re-loads+re-guards status inside it (mirror
  `PostPaymentVoucherAsync`'s tx) so a double-pay cannot post two JEs. This is
  strictly stronger than PV (whose Version is inert). Reference the bank-rec
  match-state race handling (`backend/src/Accounting.Infrastructure/Bank/`, Cycle
  B) for the in-repo conditional-transition idiom and align to it.
- **SoD:** permission-only (creator MAY self-approve), matching PV which dropped
  its `ck_pv_sod` CHECK. Enforcing creator != approver is an open question.

### 5. API endpoints + FE
Endpoints in `backend/src/Accounting.Api/Endpoints/ExpenseClaimEndpoints.cs`,
registered in `Program.cs`; service DI in
`backend/src/Accounting.Infrastructure/DependencyInjection.cs` (scoped, pattern
lines 50/86/104). All `.RequireAuthorization(PolicyPrefix + <perm>)`.
- [x] `GET /expense-claims` (list; filters status, employee_id, date range) — `read`
- [x] `GET /expense-claims/{id}` (detail incl. lines + attachments) — `read`
- [x] `POST /expense-claims/` (create draft) — `create`
- [x] `PUT /expense-claims/{id}` (update draft; Draft/Rejected only; rewrite lines) — `create`
- [x] `POST /expense-claims/{id}/submit` — `create`
- [x] `POST /expense-claims/{id}/approve` — `approve`
- [x] `POST /expense-claims/{id}/reject` (body: reason) — `approve`
- [x] `POST /expense-claims/{id}/pay` (body: payment_method, bank_account_id?) — `pay`
- [x] `POST /expense-claims/{id}/cancel` — `create`
- [x] Attachments: reuse the generic `/attachments` endpoints with
  `parent_type = "EXPENSE_CLAIM"`, `parent_id = expense_claim_id`,
  `category = "EXPENSE_CLAIM_FORM"`. Wire a new parent type: add `ExpenseClaim`
  to `AttachmentEnums.cs`, `"EXPENSE_CLAIM"` to `AttachmentCodes.ParentDb`, and
  arms to `AttachmentService.ParentExistsAsync` (`db.ExpenseClaims.AnyAsync(...)`)
  and `ParentReadPermission` (`expense.claim.read`). **Attachments parent to the
  header** (stable id), NOT the line (FOOTGUN 7 — line ids churn on edit). To
  satisfy "each line has attachments" at the UX level, store the associated
  `line_no` in the attachment `description` so the UI can group receipts per
  line without a volatile parent id.

FE (Next.js App Router + DaisyUI):
- [ ] `frontend/app/(dashboard)/expense-claims/page.tsx` — list (clone
  `bank-accounts/page.tsx`: `DataTable` + status/employee filters + PermissionGate
  "New" button).
- [ ] `frontend/app/(dashboard)/expense-claims/new/page.tsx` — multi-line create
  (clone `payment-vouchers/new/page.tsx` `rows` state pattern). Header:
  **EmployeeSelector** (new component). Each line row: **ExpenseCategorySelector**
  (existing) + description + expense_date + amount + tax code + recoverable-VAT
  toggle. Save draft -> returns id -> attach receipts (header parent) -> optional
  submit.
- [ ] `frontend/app/(dashboard)/expense-claims/[id]/page.tsx` — detail + action
  buttons (submit/approve/reject/pay), each wrapped in `<PermissionGate
  scope="expense.claim.*">`, `disabled={m.isPending}` (clone
  `payment-vouchers/[id]/page.tsx`). Pay opens a small modal for payment_method +
  bank account picker.
- [ ] Optional edit page or reuse `new` in edit mode for Draft/Rejected.
- [ ] `frontend/components/ui/EmployeeSelector.tsx` — clone
  `BusinessUnitSelector.tsx`; `useEmployees()` already exists
  (`queries.ts:556`); option label `code — nameTh`.
- [ ] `frontend/lib/queries.ts` — add `useExpenseClaims`, `useExpenseClaim`,
  `useCreate/UpdateExpenseClaim`, `useSubmit/Approve/Reject/Pay/CancelExpenseClaim`
  (clone `useApprovePaymentVoucher` `queries.ts:480`; invalidate
  `['expense-claims']` + `['expense-claim', id]`).
- [x] i18n: add `expenseClaims` namespace to `frontend/messages/en.json` +
  `th.json`; add `nav.expenseClaims` label.
- [x] Nav: add `{ href:'/expense-claims', key:'expenseClaims', Icon:<lucide>,
  perm:'expense.claim.read' }` to SECTIONS in
  `frontend/components/app-shell/SidebarNav.tsx` (a purchase/AP-adjacent section).
  Used `Banknote` icon (new import) — placed right after `paymentVouchers`.

### 6. Tests (Accounting.Api.Tests, `[Collection(PostgresCollection)]`)
Use today/future dates only (FOOTGUN 5). Do not assert a backdated JE post date.
New files: `backend/tests/Accounting.Api.Tests/Expense/ExpenseClaimServiceTests.cs` (9 tests),
`.../Expense/ExpenseClaimPermissionTests.cs` (2 HTTP-level RBAC tests). All 11 pass; verified
race tests stable across 3 consecutive runs.
- [x] **Money invariants:** `Pay_reproduces_the_worked_example_JE_exactly` reproduces
  the section-3 worked example verbatim (500/1000/200 net, 0%/7%/7% VAT, one
  non-recoverable line) and asserts all 5 JE lines by AccountId + amount, Dr==Cr==1784,
  the Input VAT line = 70 (only the recoverable line), the non-recoverable line's Dr =
  amount+vat (214), zero WhtCertificate rows, and no JE line hits the WHT payable
  account. `Pay_with_CASH_credits_the_company_cash_account` covers the CASH credit
  branch. Deviation: `bank_account_id`'s GL column is `GlCashAccountId` (see §3 note),
  asserted via `db.BankAccounts...GlCashAccountId`.
- [x] **State transitions:** `State_transitions_happy_path_docno_and_journal_only_set_at_pay`
  (doc_no/journal_entry_id NULL until pay, EX embedded in the doc number after —
  `.Contain("EX")` not `.StartWith` since the shape is `MM-YYYY-EX-NNNN`, mirroring PV);
  `Illegal_transitions_throw_the_named_domain_error` (pay-on-Draft ->
  `expense_claim.not_approved`, approve/reject-on-Draft -> `expense_claim.not_submitted`,
  submit-on-Approved -> `expense_claim.not_draft`, cancel-on-Approved ->
  `expense_claim.cannot_cancel`); `Cancel_is_legal_from_Draft_and_Rejected`.
- [x] **Race:** `Concurrent_Approve_second_stale_save_throws_DbUpdateConcurrencyException`
  — deterministic two-preloaded-DbContext form (both load the same Version, first
  SaveChangesAsync wins, second throws `DbUpdateConcurrencyException` directly — proves
  `Version++` fires, unlike PV's inert token). A `Task.WhenAll` race on the SERVICE call
  was tried FIRST and found unreliable on fast local Postgres (the loser's fresh re-load
  sometimes already observed the winner's commit, hitting the ordinary status guard
  instead of racing at SaveChanges) — logged here per the attempt-log convention, not a
  new troubles-wiki entry since it's a generic async-timing fact, not project-specific.
  `Double_pay_race_posts_exactly_one_journal_entry` uses genuine `Task.Run` concurrency
  against the real transactional `PayAsync` and accepts EITHER failure mode on the loser
  (`expense_claim.locked_mismatch` or `expense_claim.not_approved` are both a correct
  rejection) — asserts exactly one success and exactly one JournalEntry by `Reference`.
- [x] **Tenant scope:** `Claim_from_company_A_is_invisible_to_company_B_via_query_filter`
  — labelled query-filter (FOOTGUN 6), not RLS (teas_test connects as superuser). No
  separate RLS test added (optional per spec).
- [x] **Attachment:** `Attachment_upload_and_ParentExistsAsync_arm` — uploads against a
  real claim (succeeds) and a missing id 999999999 (`attachment.parent_not_found`).
  Storage root overridden to a per-test temp dir (`Path.GetTempPath()/teas-it-expense-*`,
  cleaned up in a `finally`) per the file-storage test-isolation footgun.
- [x] **Permission:** `ExpenseClaimPermissionTests` (HTTP-level, `RbacApiFactory`, mirrors
  `GeneralLedgerEndpointTests`) — an ACCOUNTANT-shaped token (read+create only, matching
  `617`'s role split) is 403 on approve+pay; a CHIEF_ACCOUNTANT-shaped token (also
  approve+pay) succeeds on both. Tokens carry explicit JWT `Permissions` claims (same
  technique as `GeneralLedgerEndpointTests`) — this asserts the ENDPOINT's permission
  requirement; the SQL seed's DB fan-out is separately verified by the deploy-probe row
  count in the verification gates (not re-proven via a live RBAC-seeded HTTP round trip).
- [x] Skip count vs baseline: baseline measured BEFORE any Expense Claims changes =
  **Total 743, Passed 734, Failed 1 (pre-existing `Pnd50FilingServiceTests` flake, confirmed
  in troubles-wiki "single DIFFERENT test fails each run"), Skipped 8**. New Expense tests
  (11) all pass; full-suite re-run recorded below in Verification gates.
- [ ] **Permission:** an `ACCOUNTANT` (default create+read only) is 403 on
  `approve`/`pay`; a `CHIEF_ACCOUNTANT` succeeds (adjust per the role-split
  ruling).
- [ ] Check skip count vs baseline; a new seed runs once (FOOTGUN 9).

---

## Verification gates
- `dotnet build` green (watch for locked `testhost.exe` — troubles-wiki MSB3027).
- `dotnet test backend/tests/Accounting.Api.Tests` — new tests pass; **skip count
  == baseline** (a rise = false-green). Isolate a single flaky failure before
  calling it a regression (troubles-wiki "single DIFFERENT test fails each run").
- `dotnet ef migrations add ExpenseClaims` produces exactly the two tables +
  `expense` schema; review the generated SQL (no unexpected drops).
- FE: `npx tsc --noEmit` (or the repo's FE typecheck) green; `npx vitest run
  lib/<touched>.test.ts` scoped (do NOT run bare `vitest` — Playwright specs
  false-fail, troubles-wiki).
- **Deploy probe for `617` (ROW COUNT, not exit code — FOOTGUN 1):**
  `SELECT count(*) FROM sys.role_permissions rp JOIN sys.permissions p ON
  p.permission_id = rp.permission_id WHERE p.permission_code LIKE 'expense.%' AND
  rp.company_id IS NOT NULL;` must be > 0 and roughly `#companies × #granted
  roles`. Zero = the bypass was missing and the fan-out silently no-op'd on prod.
  Also `SELECT count(*) FROM sys.applied_sql_scripts;` must equal the number of
  `.sql` files on disk (a short count = a script hard-crashed + rolled back).
- Public-domain E2E: `curl https://teas.kazaki-rio.com/api/proxy/expense-claims`
  -> 401 (route exists + auth-gated); bare `/expense-claims` -> 200/307 (FE page
  resolves). FOOTGUN 10.

## Blast-radius cap
New files + edits confined to: `Accounting.Domain/Entities/Expense/*`,
`Accounting.Infrastructure/Persistence/Configurations/Expense/*`, a new EF
migration, `SqlScripts/616-618`, `AccountingDbContext.cs` (2 DbSet lines),
`GlPostingService.cs` (ONE new additive `PostExpenseClaimAsync` method — no edits
to existing methods or the shared JE writer), a new `ExpenseClaimService.cs`,
`ExpenseClaimEndpoints.cs` + `Program.cs` registration + `DependencyInjection.cs`,
attachment wiring (`AttachmentEnums.cs`/`AttachmentCodes.cs`/`AttachmentService.cs`
— add-an-arm only), `Permissions.cs`/`PermissionCatalog.cs`, and the FE files +
`messages/*.json` + `SidebarNav.tsx` listed above.

**STOP-and-re-spec triggers (do NOT design around):**
- Any change to `PaymentVoucher*`, `Vendor*`, `WhtCertificate`, or the shared JE
  writer that `PostPaymentVoucherAsync` calls -> that means the money-path
  ruling was B/C or the "additive method" assumption broke. Stop, report.
- If `expense.expense_categories.DefaultExpenseAccountId` is null for a category a
  test needs, seed the account mapping in the test — do NOT change the
  ExpenseCategory schema.
- If `bank.bank_accounts` has no GL-account column, stop (Cycle B contract broke)
  — the pay credit account depends on it.

## Open questions — ALL RULED 2026-07-09 (Ham for 1-3, Fable for 4-7)
1. **Money path = A** (Ham ruling) — claim posts its own JE via additive
   `PostExpenseClaimAsync`. Zero PV code edits. WHT none. Sections 3/5 apply as
   written.
2. **Approve/Pay role split** (Ham ruling) — `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT`
   get approve + pay; `ACCOUNTANT` gets create + read only. Encode in `617`.
3. **SoD** (Ham ruling) — permission-only; creator MAY self-approve. No
   creator != approver guard.
4. **Attachments** (Fable ruling) — per-HEADER with `line_no` tag in description
   (FOOTGUN 7). Satisfies scope.
5. **Doc prefix** (Fable ruling) — `EX`.
6. **Schema name** (Fable ruling) — dedicated `expense` schema.
7. **Number-gap audit** (Fable ruling) — skip for now; revisit if RD asks. Do
   NOT add a `v_number_gaps` arm this cycle.

## Attempt log
<!-- - <date> <worker>: <result / failure summary> -->
- 2026-07-09 Fable (designer): initial spec authored. Read PV/PO approve + GL
  posting + WHT (`PaymentVoucher.cs`, `PaymentVoucherService.cs`,
  `GlPostingService.cs`, `WhtPayerModes.cs`), ExpenseCategory->GL mapping,
  Employee master, Attachment infra + bank-rec integration
  (`StatementImportService.cs`), doc numbering, RLS/migration patterns, bank-rec
  seeds `614`/`615`, and FE patterns. Surfaced the BLOCKING PV-vs-employee-payee
  collision (section 0) — designed Option A fully; B/C need a ruling.
- 2026-07-09/10 Sonnet (implementer): full implementation (backend entities/
  config/migration/seeds/service/endpoints/attachment wiring, 11 tests, FE
  pages/hooks/i18n/nav). All gates green (`dotnet build`, EF migration review,
  FE typecheck, 11/11 Expense tests, full-solution `dotnet test Accounting.sln`
  901 total / 893 passed / 0 failed / 8 skipped — matches baseline skip count).
- 2026-07-10 Sonnet (implementer): Tier-2 review — 2 MINOR findings applied.
  (1) `UpdateDraftAsync` now clears `claim.RejectReason = null` on every edit
  (a Rejected -> edited -> resubmitted claim no longer carries stale rejection
  text). (2) `ExpenseClaimLine.VatRate` precision `numeric(5,2)` ->
  `numeric(5,4)` (stored as a fraction — 0.075 didn't fit in 2dp) in
  `ExpenseClaimConfiguration.cs`. Migration was uncommitted so re-generated via
  `dotnet ef migrations remove` + `add ExpenseClaims` (new timestamp
  `20260709184105_ExpenseClaims`, same class name) rather than hand-editing —
  reviewed the regenerated SQL: still exactly 2 tables + `expense` schema, the
  filtered unique `(company_id,branch_id,doc_no)` index, cascade FK
  `expense_claim_lines -> expense_claims`, all money columns still
  `numeric(19,4)`, and `vat_rate` now `numeric(5,4)`. FOOTGUN — regenerating a
  migration with a NEW timestamp on the shared persistent `teas_test` DB left a
  stale entry in `sys.__ef_migrations` (the OLD id, from earlier test runs)
  plus the OLD tables already created — the next fixture init's `MigrateAsync()`
  would have hit "relation already exists" trying to re-run `CreateTable` under
  the new id. Fixed via a one-off standalone Npgsql console app (PowerShell
  `Add-Type` on the built `Npgsql.dll` failed — .NET Framework PS 5.1 can't load
  a net10 assembly) that dropped `schema expense CASCADE` and deleted the stale
  `__ef_migrations`/`applied_sql_scripts` rows directly, letting the new
  migration + seeds apply cleanly on the next test run (confirmed — this exact
  scenario, worth a troubles-wiki entry if anyone else regenerates an
  already-tested migration on the shared teas_test DB). `dotnet build` green;
  `dotnet test --filter "FullyQualifiedName~ExpenseClaim"` = 11/11 pass. Full
  suite NOT re-run per dispatch (gate runner owns that next).
