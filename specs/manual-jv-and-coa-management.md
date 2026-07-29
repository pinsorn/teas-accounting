# Manual journal vouchers + chart-of-accounts management (design, opus-designer 2026-07-29)

Origin: Ham asked how to record (a) a **director/shareholder loan** — money the director
transfers into the company — and (b) **income that is not from selling goods/services**
(interest received, rent, scrap). Neither is recordable today. Ham's decision: build the
**general capability**, not a bespoke director-loan feature.

**Part A** — chart-of-accounts management (list / create / edit / deactivate, per company).
**Part B** — manual journal vouchers (create a balanced multi-line JE, post it, list, view).
**Part C** — the three accounts that make a director loan recordable the day this ships.

---

## 0. Facts established in code (verified 2026-07-29, file:line)

Read these before touching anything. Several of the premises in the original problem
statement turned out to be **wrong in the system's favour** — most of the backend already
exists. The work is *hardening + exposing*, not *building a posting engine*.

### 0.1 The manual-JV backend already exists (partially)
| Fact | Evidence |
|---|---|
| `POST /journals` (create draft) + `POST /journals/{id}/post` + `GET /journals/{id}` are mapped and permission-gated | `backend/src/Accounting.Api/Endpoints/JournalEndpoints.cs:14-36` |
| Gates are `gl.journal.create` / `gl.journal.post` / `gl.journal.read` | same file, lines 27 / 31 / 36 |
| Those three permission codes are **already seeded** | `SqlScripts/110_seed_roles_and_permissions.sql:28-30` |
| …and **already granted**: create → ACCOUNTANT/CHIEF_ACCOUNTANT/COMPANY_ADMIN; post → CHIEF_ACCOUNTANT/COMPANY_ADMIN; read → +AUDITOR/TAX_OFFICER | `SqlScripts/530_seed_rbac_grant_reconcile.sql:108-113` |
| Balance IS validated on create by FluentValidation (≥2 lines, each line pure Dr xor Cr, ΣDr==ΣCr) | `Accounting.Application/Ledger/JournalDtos.cs:36-58` |
| Balance is re-validated at post: `MarkPosted` throws `je.unbalanced` unless `TotalDebit == TotalCredit && TotalDebit > 0` | `Accounting.Domain/Entities/Ledger/JournalEntry.cs:52,58-71` |
| `CreateDraftAsync` **silently discards `req.DocDate`** and pins `_clock.TodayInBangkok()` | `Accounting.Infrastructure/Ledger/JournalService.cs:41-51` |
| `PostAsync` does **NOT** call `EnsureOpenAsync` — no period gate, no fiscal-year gate | `JournalService.cs:75-99` |
| Neither create nor post validates the referenced `AccountId` at all | `JournalService.cs:58-67` |
| The only existing consumer of the draft path is ภ.พ.36 reverse-charge, which passes a real `docDate` that is then thrown away | `Accounting.Infrastructure/TaxFilings/WhtFilingService.cs:311-319` |
| There is **no** `GET /journals` list endpoint | `JournalEndpoints.cs` (whole file) |

### 0.2 The posting seam
- `GlPostingService.BuildAndPostAsync` (`Ledger/GlPostingService.cs:510-551`) is the single
  private engine: stamps BU, sums Dr/Cr, throws `gl.unbalanced` if `totalD != totalC ||
  totalD == 0m`, builds the `JournalEntry` with `PrefixCode = "JV"`, allocates the doc_no via
  `NumberedDocumentWriter.AllocateAndSaveAsync` (bounded retry on 23505) and calls `MarkPosted`.
- `PostManualEntryAsync` (`GlPostingService.cs:498-508`) is the **public** thin wrapper taking
  already-resolved AccountIds and an arbitrary `docDate`. It is the seam Part B must use.
- `BuildAndPostAsync` deliberately does **not** enforce the period gate — the *caller* does.
  Documented at `Accounting.Infrastructure/Bank/BankReconciliationService.cs:235`
  ("the RECON SERVICE enforces period-close; PostManualEntryAsync itself does not").
- `PostClosingEntryAsync` (`GlPostingService.cs:448`) is a separate copy for year-end only —
  do not touch it, do not route through it.

### 0.3 The trust boundary on `journal_lines.account_id` — already discovered once
`gl.journal_lines` has **no company_id column** (`Persistence/Configurations/Ledger/JournalLineConfiguration.cs:38-39`
says so explicitly) and **no DB-level FK to chart_of_accounts**
(`BankReconciliationService.cs:220-223`, Opus Tier-2 fix 2026-07-09). Therefore RLS cannot
stop a forged / foreign / header / inactive account id from being posted. The proven
in-repo guard is `BankReconciliationService.CreateJournalAsync:224-233`:

```csharp
var contraAccount = await db.ChartOfAccounts.AsNoTracking()
        .FirstOrDefaultAsync(a => a.AccountId == req.ContraAccountId && a.CompanyId == tenant.CompanyId, ct)
    ?? throw new DomainException("bank.contra_account_not_found", ...);
if (!contraAccount.IsActive) throw new DomainException("bank.contra_account_inactive", ...);
if (contraAccount.IsHeader) throw new DomainException("bank.contra_account_is_header", ...);
```
Part B replicates this shape for N lines in ONE query. **Copy the pattern, do not invent one.**

### 0.4 The CoA backend already exists
| Fact | Evidence |
|---|---|
| `POST /accounts`, `PUT /accounts/{id}`, `GET /accounts` mapped, all gated `master.coa.manage` | `Endpoints/MasterEndpoints.cs:88-111` |
| `master.coa.manage` seeded and granted to COMPANY_ADMIN + CHIEF_ACCOUNTANT **only** (not ACCOUNTANT) | `110_...sql:23`, `530_...sql:123,125` |
| `ChartOfAccountService` = Create / Update / List | `Infrastructure/Master/MasterDataServices.cs:144-183` |
| `UpdateAccountRequest` carries only `AccountNameTh, AccountNameEn, IsHeader, IsActive` — **code / type / normal-balance / parent are already immutable after create** | `Application/Master/ChartOfAccountDtos.cs:8`, comment at `:22` |
| No DELETE route exists anywhere | `MasterEndpoints.cs:88-111` |
| Unique index `(company_id, account_code)` | `Configurations/Master/ChartOfAccountConfiguration.cs:46` |
| Tenant query filter is applied to every `ITenantOwned` (incl. ChartOfAccount) | `Persistence/AccountingDbContext.cs:163-174` |
| `created_at` is `NOT NULL` with **no DB default** | `Migrations/20260616130322_InitialCreate.cs:135` |
| **`CreateAsync` never sets `CreatedAt`** → every UI-created account is stamped `0001-01-01` | `MasterDataServices.cs:151-158` (bug) |
| `CreateAsync` never validates `ParentId` (cross-tenant / non-header parent accepted) | `MasterDataServices.cs:151-158` (bug) |
| `UpdateAsync` lets `IsHeader` be flipped on with no check for existing postings | `MasterDataServices.cs:164-171` (bug) |
| There is **no** frontend page for the chart of accounts | `frontend/app/(dashboard)/settings/` = api-keys, business-units, companies, company, employees, expense-categories, products, roles, users, wht-types |

### 0.5 The account picker problem (and its existing answer)
`GET /accounts` is `master.coa.manage`-gated, which ACCOUNTANT does not hold — so it is the
wrong source for a JV form's account dropdown. `GET /reports/general-ledger/accounts` already
exists for exactly this reason (`Endpoints/ReportEndpoints.cs:165-170`, comment says so),
is gated `report.general_ledger.read` (granted to ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/
TAX_OFFICER/COMPANY_ADMIN via `590_seed_general_ledger_perms.sql`), and **already filters
`IsActive && !IsHeader`** — i.e. it returns exactly the postable set
(`Reports/FinancialReportService.cs:352-360`). The FE hook `useGlAccounts()` already exists
(`frontend/lib/queries.ts:1338-1343`). **Reuse it. Do not add a new picker endpoint.**

### 0.6 Immutability is already enforced in the DB
- `SqlScripts/020_journal_immutability.sql` — `trg_je_immutable` blocks UPDATE of
  doc_no/doc_date/posting_date/total_debit/total_credit/company_id/branch_id on a POSTED
  header; `trg_je_no_delete_posted` blocks DELETE of any non-DRAFT entry.
- `SqlScripts/580_posted_lines_immutability.sql` + `582_posted_lines_immutable_v2.sql` —
  UPDATE/DELETE on `gl.journal_lines` of a posted journal.
- Check constraint `ck_journal_lines_amount_sign` — a line must be pure Dr or pure Cr, `> 0`.

### 0.7 Reports and `IsActive`
- `BalanceSheetAsync` reads **all** accounts regardless of `IsActive` — with an explicit
  comment saying an inactive account with a balance must still appear
  (`FinancialReportService.cs:91-96`). ✅
- `ProfitLossAsync` joins all accounts, no `IsActive` filter (`:156-168`). ✅
- `TrialBalanceAsync` filters `includeInactive || a.IsActive` in SQL **and then sums
  `td`/`tc` over only the surviving rows** (`:42-62`). ❌ Deactivating an account that still
  carries movement makes the trial balance report **itself unbalanced**. Fixed in A2.

### 0.8 Numbering
`sys.document_prefixes` already carries `('JV','JOURNAL_VOUCHER','ใบสำคัญทั่วไป','Journal
Voucher',...)` (`SqlScripts/100_seed_document_prefixes.sql:16`), it is system-global (no
company_id), and **every** journal in the system — auto-posted or manual — already uses it
(`GlPostingService.cs:23` `private const string JvPrefix = "JV"`). No new prefix, no seed.

### 0.9 Account codes in use (swept across every `SqlScripts/*.sql`, `db/**/*.sql`, and `DefaultChartOfAccounts`)
4-digit (the standard chart): `1110 1120 1130 1170 1180 1610 1690 2110 2151 2152 2153 2160
2170 2180 3300 4000 4100 4200 5000 5100 5110 5200 5300 5350 5400 5410 5450 5460`.
5-digit (demo-company-only granular chart): `10100 10110 10120 12200 51010 61010 61020
62010…62130 62990 81010 00000`.
Verified **free** across `backend/src`, `frontend`, `db`, `docs`: `2190 2200 2210 2220 4300 4310 5500 5510`.

---

## 1. Design — Part A: chart-of-accounts management

### A1 — backend hardening (`Infrastructure/Master/MasterDataServices.cs`, `ChartOfAccountService` only)

**A1a. Stamp `CreatedAt` and `IsActive` on create.** Mirrors `MasterDataServices.cs:311`.
```csharp
var e = new ChartOfAccount
{
    CompanyId = tenant.CompanyId,
    AccountCode = req.AccountCode,
    AccountNameTh = req.AccountNameTh, AccountNameEn = req.AccountNameEn,
    AccountType = req.AccountType, ParentId = req.ParentId,
    IsHeader = req.IsHeader, NormalBalance = req.NormalBalance,
    IsActive = true, CreatedAt = DateTimeOffset.UtcNow,   // ← was missing: every UI-created row stamped 0001-01-01
};
```

**A1b. Validate `ParentId`.** No DB FK enforcement can be trusted across tenants (§0.3 logic
applies to `parent_id` too — a Postgres FK check runs as the table owner and bypasses RLS).
Insert into `CreateAsync` **before** building the entity:
```csharp
if (req.ParentId is { } pid)
{
    // Tenant query filter (AccountingDbContext.cs:174) scopes this read to the caller's company,
    // so a foreign parent id resolves to null → "not found", never a cross-tenant link.
    var parent = await db.ChartOfAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == pid, ct)
        ?? throw new DomainException("coa.parent_not_found", $"Parent account {pid} not found.");
    if (!parent.IsHeader)
        throw new DomainException("coa.parent_not_header", "Parent account must be a header account.");
}
```

**A1c. Refuse turning an account with postings into a header.** Insert into `UpdateAsync`
after loading `e`, before mutating:
```csharp
// Header accounts are excluded from every postable-account picker (FinancialReportService.cs:353)
// and must not carry postings. Flipping an account that ALREADY has posted lines into a header
// retroactively invalidates those lines and hides the account from the GL drill-down.
if (req.IsHeader && !e.IsHeader)
{
    var hasPostings = await db.JournalLines
        .Join(db.JournalEntries, l => l.JournalId, j => j.JournalId, (l, j) => new { l, j })
        .AnyAsync(x => x.l.AccountId == accountId && x.j.Status == DocumentStatus.Posted, ct);
    if (hasPostings)
        throw new DomainException("coa.has_postings",
            "Account already carries posted journal lines — it cannot become a header account.");
}
```
(`db.JournalEntries` is `ITenantOwned`, so the join is company-scoped; `journal_lines` alone is not.)

**A1d. Refuse deactivating or header-flipping a *system* account.** Every code in
`GlAccountsOptions` is resolved by `GlPostingService.ResolveAccountIdAsync` on **every**
document post for **every** tenant. Losing one to a UI edit breaks posting company-wide.
Add to `Infrastructure/Ledger/GlAccountsOptions.cs`:
```csharp
/// <summary>Every account code this options object maps. Master-data admin refuses to
/// deactivate or header-flip these — GlPostingService resolves them on every post, for
/// every document type. Kept explicit (no reflection) and pinned by GlAccountsOptionsTests.</summary>
public IEnumerable<string> AllCodes() =>
[
    ArAccount, ApAccount, CashAccount, BankAccount, SalesAccount, OutputVatAccount,
    InputVatAccount, WhtPayableAccount, WhtReceivableAccount, SalesReturnAccount,
    IrrecoverableVatExpenseAccount, SalaryExpenseAccount, EmployerSsoExpenseAccount,
    PitPayableAccount, SsoPayableAccount, NetWagesPayableAccount, OtherDeductionsPayableAccount,
    FixedAssetCostAccount, AccumulatedDepreciationAccount, DepreciationExpenseAccount,
    GainOnAssetDisposalAccount, LossOnAssetDisposalAccount,
];
```
`ChartOfAccountService` takes `IOptions<GlAccountsOptions> glAccounts` as a new ctor param
(primary-constructor style, same as `db`/`tenant`; it is already registered in DI — see
`GlPostingService`'s own ctor). In `UpdateAsync`:
```csharp
if ((!req.IsActive || req.IsHeader) && glAccounts.Value.AllCodes().Contains(e.AccountCode))
    throw new DomainException("coa.system_account",
        $"Account {e.AccountCode} is a system account used by GL posting — it cannot be deactivated or made a header.");
```
> The three **new** Part-C accounts (2190/5500/4300) are deliberately **NOT** added to
> `GlAccountsOptions` — nothing auto-posts to them, and adding them there would make
> `ResolveAccountIdAsync` throw `gl.account_missing` for any tenant that lacks them. They stay
> ordinary, deactivatable user accounts.

**A1e. Residual accepted, do not fix:** the `coa.duplicate` pre-check
(`MasterDataServices.cs:148`) races with the unique index, so a simultaneous double-create
surfaces as a 500 (`23505`) instead of 422. Same class as every other master-data create in
this repo. Out of scope; note it in the attempt log if a reviewer raises it.

### A2 — trial balance must survive deactivation (`Reports/FinancialReportService.cs`)
Move the `IsActive` filter out of SQL and into the row loop so an inactive account that still
carries movement is always included — mirroring `BalanceSheetAsync`'s own documented rule
(`:91-92`). Replace `:42-47` and add one guard at the top of the `foreach` at `:51`:
```csharp
var accounts = await db.ChartOfAccounts.AsNoTracking()
    .OrderBy(a => a.AccountCode)
    .Select(a => new { a.AccountId, a.AccountCode, a.AccountNameTh,
                       a.AccountType, a.NormalBalance, a.IsActive })
    .ToListAsync(ct);
...
foreach (var a in accounts)
{
    // An INACTIVE account that still carries movement must appear, or td/tc are summed over a
    // subset and the report declares itself unbalanced. Same rule BalanceSheetAsync already states.
    if (!includeInactive && !a.IsActive && !sums.ContainsKey(a.AccountId)) continue;
    ...
}
```
No signature change, no DTO change, no FE change.

### A3 — frontend page `frontend/app/(dashboard)/settings/chart-of-accounts/page.tsx` (new)
Pattern reference — **copy the structure of** `frontend/app/(dashboard)/settings/business-units/page.tsx`
(214 lines: `PageHeader` + `DataTable` + edit-modal state + `PermissionGate` + `useConfirm` +
`errorToToast`). Differences:
- `const SCOPE = 'master.coa.manage';`
- Columns: `accountCode` (font-mono), `accountNameTh`, `accountNameEn`, `accountType`,
  `normalBalance` (DR/CR badge), `isHeader` (✓/✗), `isActive` (✓/✗). Row action: pencil → edit modal.
- **Create modal fields:** accountCode (text, `^\d{2,10}$`, help text: 4-digit convention),
  accountNameTh (required), accountNameEn, accountType (select: ASSET/LIABILITY/EQUITY/
  REVENUE/EXPENSE), normalBalance (select DR/CR, auto-defaulted from accountType —
  ASSET/EXPENSE→DR, LIABILITY/EQUITY/REVENUE→CR, but **user-overridable**: 1690 and 4100 are
  legitimate contra accounts).
  **`parentId` is NOT exposed in the UI** — user-created accounts are flat (`ParentId = null`).
  The API keeps accepting it, hence A1b.
- **Edit modal fields:** accountNameTh, accountNameEn, isActive toggle **only**. `accountCode`,
  `accountType`, `normalBalance` render as read-only text with a "cannot be changed" hint.
  `isHeader` is **not** editable from the UI (the API still accepts it, hence A1c/A1d).
- **No delete button, no delete confirm, anywhere.** Deactivate is the retire operation.
- Uses `useAccounts()` / `useCreateAccount()` / `useUpdateAccount()` — new hooks in
  `frontend/lib/queries.ts` hitting `accounts` (GET with `activeOnly=false`), `accounts`
  (POST), `accounts/{id}` (PUT), invalidating `['accounts']` **and** `['gl-accounts']`.

### A4 — nav + i18n
`frontend/components/app-shell/SidebarNav.tsx`, settings group, after `businessUnits`:
```ts
{ href: '/settings/chart-of-accounts', key: 'chartOfAccounts', Icon: BookOpen, perm: 'master.coa.manage' },
```
i18n: new `coa` namespace in **both** `frontend/messages/th.json` and `en.json`
(`title`, `code`, `nameTh`, `nameEn`, `type`, `normalBalance`, `isHeader`, `isActive`,
`create`, `edit`, `codeImmutableHint`, `typeImmutableHint`, `deactivateHint`) + `nav.chartOfAccounts`.

---

## 2. Design — Part B: manual journal vouchers

### B0 — the shape decision, and why
**Create-and-post in one call. No draft state for a manual JV.**

Rationale (this is the load-bearing choice; a reviewer should push back here or nowhere):
1. The immutability invariant (INV-2) is then structurally true — there is no mutable state to
   protect and no edit/delete endpoint to write or to get wrong.
2. `PeriodCloseService.CloseAsync:64-68` refuses to close a period while **any** draft
   `JournalEntry` exists. An abandoned draft JV from the UI would brick month-end close, and
   the fix would be a draft-delete endpoint + a drafts list + a "who owns this draft" story —
   a whole feature to service a state nobody asked for.
3. Every other JV in the system (TI/RC/PV/VI/CN/DN/payroll/depreciation/bank-rec/year-end)
   is created already-posted. A manual JV that behaves differently is a special case with no
   payoff.
4. It is the smallest diff that delivers the capability.

The existing `POST /journals` + `/{id}/post` draft path is **left exactly as it is** — ภ.พ.36
(`WhtFilingService.cs:318-319`) depends on it. Do not "unify" them; do not change
`CreateDraftAsync`'s today-pinning (that would silently move every ภ.พ.36 reverse-charge JV's
date). Flag-only: `CreateDraftAsync` discarding its `docDate` argument is a latent bug in
ภ.พ.36 (the JV lands on today, not on the filing period date). **Out of scope here** — record
it in `troubles-wiki.md` as a known issue, do not fix it in this spec's diff.

### B1 — the `docDate` decision, and why
**The manual JV accepts a client-supplied `docDate`, bounded by the period gate.**

The `§10` "docDate is always today in Asia/Bangkok" rule exists for VAT/tax-point documents
(TI/RC/VI/PV — ม.86/4: the date is a legal assertion about when tax was due). A manual JV is
not a tax-point document, and the system already agrees: `PostManualEntryAsync` takes an
arbitrary `docDate`, and bank reconciliation passes the **bank statement line's own past
date** (`BankReconciliationService.cs:256`) — shipped, in prod, and correct. The control that
makes a past date safe is the **period gate**, not date-pinning:
`PeriodCloseService.IsOpenAsync:40-41` treats a never-opened past month as **CLOSED**, so a
user can only back-date into a month somebody explicitly opened or reopened
(`POST /periods/{y}/{m}/reopen` shipped as O14, `PeriodEndpoints.cs:20-28`).

Bounds, all server-side:
- `docDate <= _clock.TodayInBangkok()` — no future-dating.
- `EnsureOpenAsync(docDate)` — the month must be Open.
- not inside a non-reversed `FiscalYearClose` — `EnsureOpenAsync` does **not** check this
  (compare `PeriodCloseService.ReopenAsync:130-136`, which checks it separately).
- `PostingDate = DocDate` (every other poster does this).

### B2 — extend the posting seam with per-line detail (`GlPostingService`)
`PostManualEntryAsync`'s current tuple `(long AccountId, decimal Debit, decimal Credit)`
carries no line description and no BU — but `JournalLine.Description` is what the JE detail
page renders (`journals/[id]/page.tsx`), and `JournalLine.cs:21-24` says BusinessUnitId is
"settable per-line on manual JV entries". Add an **overload** (do not change the existing
signature — bank reconciliation is money-critical, shipped, and does not need this):

`Accounting.Application/Ledger/IGlPostingService.cs`:
```csharp
/// <summary>A manual-JV line. Richer than the 3-tuple overload (per-line description + BU),
/// which bank reconciliation keeps using unchanged.</summary>
public sealed record ManualJvLine(
    long AccountId, decimal Debit, decimal Credit, string? Description, int? BusinessUnitId);

Task<long> PostManualEntryAsync(
    int companyId, int branchId, DateOnly docDate, string description, string? reference,
    IReadOnlyList<ManualJvLine> lines, CancellationToken ct);
```
`Infrastructure/Ledger/GlPostingService.cs`, immediately below the existing overload
(`:498-508`):
```csharp
/// <summary>Manual JV overload — same private BuildAndPostAsync engine as every other poster
/// (balance check, JV numbering, MarkPosted). NOT a second posting path: the only difference
/// from the 3-tuple overload above is that the caller may set a per-line description and BU.</summary>
public async Task<long> PostManualEntryAsync(
    int companyId, int branchId, DateOnly docDate, string description, string? reference,
    IReadOnlyList<ManualJvLine> lines, CancellationToken ct)
{
    var journalLines = lines.Select((l, i) => new JournalLine
    {
        LineNo = i + 1, AccountId = l.AccountId,
        DebitAmount = l.Debit, CreditAmount = l.Credit,
        Description = l.Description, BusinessUnitId = l.BusinessUnitId,
    }).ToList();
    return await BuildAndPostAsync(companyId, branchId, docDate, description, reference, journalLines, ct);
}
```
(`BuildAndPostAsync`'s `l.BusinessUnitId ??= businessUnitId` at `:518` is a no-op here — the
default `businessUnitId` param is null, so an explicitly-set per-line BU survives.)

### B3 — DTOs + validator (`Accounting.Application/Ledger/JournalDtos.cs`, additive)
```csharp
public sealed record ManualJournalLineInput(
    long AccountId, decimal DebitAmount, decimal CreditAmount, string? Description, int? BusinessUnitId);

public sealed record CreateManualJournalRequest(
    DateOnly DocDate, string Description, string? Reference,
    IReadOnlyList<ManualJournalLineInput> Lines);

public sealed record JournalListItem(
    long JournalId, string? DocNo, DateOnly DocDate, string Description, string? Reference,
    string Status, decimal TotalDebit, decimal TotalCredit, bool IsClosingEntry);
```
`CreateManualJournalValidator` — mirror `CreateJournalValidator:36-58` and **add the 2dp rule**:
```csharp
RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
RuleFor(x => x.Reference).MaximumLength(255);
RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count >= 2)
    .WithMessage("A journal needs at least 2 lines (debit + credit).");
RuleForEach(x => x.Lines).ChildRules(line =>
{
    line.RuleFor(l => l.AccountId).GreaterThan(0);
    line.RuleFor(l => l.DebitAmount).GreaterThanOrEqualTo(0);
    line.RuleFor(l => l.CreditAmount).GreaterThanOrEqualTo(0);
    line.RuleFor(l => l).Must(l => (l.DebitAmount > 0) ^ (l.CreditAmount > 0))
        .WithMessage("Each line must be either pure debit or pure credit (not both, not neither).");
    // THB is a 2-decimal currency; the columns are numeric(19,4) so a 3rd/4th decimal would
    // be STORED and would make ΣDr==ΣCr pass on invisible satang. Reject at the edge.
    line.RuleFor(l => l).Must(l => decimal.Round(l.DebitAmount, 2) == l.DebitAmount
                                && decimal.Round(l.CreditAmount, 2) == l.CreditAmount)
        .WithMessage("Amounts must have at most 2 decimal places.");
});
RuleFor(x => x.Lines).Must(l => l.Sum(x => x.DebitAmount) == l.Sum(x => x.CreditAmount))
    .WithMessage("Total debit must equal total credit.");
```
No `CurrencyCode`/`ExchangeRate` on the request — multi-currency is deferred repo-wide
(`ThbOnly`, `JournalDtos.cs:41`); the entity defaults to `"THB"` / `1m`.

### B4 — service (`Infrastructure/Ledger/JournalService.cs`, additive method)
`IJournalService` gains:
```csharp
Task<JournalPostedResult> CreateAndPostManualAsync(CreateManualJournalRequest req, CancellationToken ct);
Task<IReadOnlyList<JournalListItem>> ListAsync(
    DateOnly? from, DateOnly? to, string? search, int page, int pageSize, CancellationToken ct);
```
`JournalService` gains ctor params `IGlPostingService gl, IPeriodCloseService period` (both
already DI-registered) and:
```csharp
public async Task<JournalPostedResult> CreateAndPostManualAsync(
    CreateManualJournalRequest req, CancellationToken ct)
{
    if (!_tenant.IsAuthenticated)
        throw new DomainException("auth.required", "User must be authenticated.");

    // --- date bounds (spec §B1) ---------------------------------------------------------
    if (req.DocDate > _clock.TodayInBangkok())
        throw new DomainException("je.future_date", "A journal cannot be dated in the future.");
    await _period.EnsureOpenAsync(req.DocDate, ct);            // throws period.closed
    var fyClosed = await _db.FiscalYearCloses.AsNoTracking()
        .AnyAsync(x => x.ReversedAt == null
                    && x.FiscalStartDate <= req.DocDate && x.FiscalEndDate >= req.DocDate, ct);
    if (fyClosed)
        throw new DomainException("je.year_closed",
            "This date is inside a closed fiscal year. Reopen the fiscal year first.");

    // --- postable-account gate (spec §0.3; mirrors BankReconciliationService.cs:224-233) ---
    // journal_lines has no company_id and no FK to chart_of_accounts, so RLS cannot stop a
    // forged/foreign/header/inactive account id. ONE tenant-scoped read covers all lines.
    var ids = req.Lines.Select(l => l.AccountId).Distinct().ToList();
    var accounts = await _db.ChartOfAccounts.AsNoTracking()
        .Where(a => ids.Contains(a.AccountId) && a.CompanyId == _tenant.CompanyId)
        .ToDictionaryAsync(a => a.AccountId, ct);
    foreach (var id in ids)
    {
        if (!accounts.TryGetValue(id, out var a))          // missing OR another tenant's — same 404-ish answer
            throw new DomainException("je.account_not_found", $"Account {id} not found.");
        if (!a.IsActive)
            throw new DomainException("je.account_inactive", $"Account {a.AccountCode} is not active.");
        if (a.IsHeader)
            throw new DomainException("je.account_is_header",
                $"Account {a.AccountCode} is a header account — pick a postable (non-header) account.");
    }

    // --- post through the SAME seam every other document uses ----------------------------
    var journalId = await _gl.PostManualEntryAsync(
        _tenant.CompanyId, _tenant.BranchId, req.DocDate, req.Description, req.Reference,
        req.Lines.Select(l => new ManualJvLine(
            l.AccountId, l.DebitAmount, l.CreditAmount, l.Description, l.BusinessUnitId)).ToList(),
        ct);

    var entry = await _db.JournalEntries.AsNoTracking()
        .FirstAsync(j => j.JournalId == journalId, ct);
    return new JournalPostedResult(journalId, entry.DocNo!, entry.PostedAt!.Value);
}
```
`ListAsync` — posted-and-draft, newest first, mirroring `IVendorService.ListAsync`'s plain
`IReadOnlyList<T>` shape (no paging envelope in this repo):
```csharp
var q = _db.JournalEntries.AsNoTracking();
if (from is { } f) q = q.Where(j => j.DocDate >= f);
if (to   is { } t) q = q.Where(j => j.DocDate <= t);
if (!string.IsNullOrWhiteSpace(search))
    q = q.Where(j => (j.DocNo != null && EF.Functions.ILike(j.DocNo, $"%{search}%"))
                  || EF.Functions.ILike(j.Description, $"%{search}%"));
return await q.OrderByDescending(j => j.DocDate).ThenByDescending(j => j.JournalId)
    .Skip((page - 1) * pageSize).Take(pageSize)
    .Select(j => new JournalListItem(j.JournalId, j.DocNo, j.DocDate, j.Description, j.Reference,
        j.Status.ToString(), j.TotalDebit, j.TotalCredit, j.IsClosingEntry))
    .ToListAsync(ct);
```
(`JournalEntry` is `ITenantOwned` → the query filter scopes it; do not add a manual
`CompanyId ==` clause, no other list service does.)

### B5 — endpoints (`Endpoints/JournalEndpoints.cs`, two additions)
```csharp
// Manual JV — create AND post in one call (specs/manual-jv-and-coa-management.md §B0).
// Gated on gl.journal.post, NOT .create: with no draft state, POST is the only act, and
// posting arbitrary journals is the most powerful write in the product (see §B6).
group.MapPost("/manual", async (
    [FromBody] CreateManualJournalRequest req,
    IValidator<CreateManualJournalRequest> validator,
    IJournalService service, CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(req, ct);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    return Results.Ok(await service.CreateAndPostManualAsync(req, ct));
})
.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalPost);

// Optional query params MUST be nullable (MasterEndpoints.cs:75-77 — the minimal-API binder
// rejects a param-less call before the handler body otherwise).
group.MapGet("/", async (
    [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search,
    [FromQuery] int? page, [FromQuery] int? pageSize,
    IJournalService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(from, to, search, page ?? 1, pageSize ?? 50, ct)))
.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalRead);
```
Both use **named permission policies**, never `RequireAssertion` — so
`RbacEndpointInventory.AssertionOverrides` needs **no** curation (that dictionary only covers
assertion-gated routes; `RbacEndpointInventory.cs:148-158`).

### B6 — permission choice, argued
| Candidate | Verdict |
|---|---|
| **New code `gl.journal.manual`** | Rejected. It would need a new seed script + grant fan-out (the `rbac-seed-ordering-footgun`: code inserted before any grant references it, in ONE file), and it would fragment the existing three-code GL model for zero added control — anyone who can post a JV via `POST /journals/{id}/post` can already post any journal at all. |
| `gl.journal.create` | Rejected. Create is the *weak* half — a draft posts nothing. Gating the only act that touches the ledger on the weaker code would **widen** access: ACCOUNTANT holds `create` but deliberately not `post` (`530_...sql:108-110`). |
| OR-assertion (`create` AND/OR `post`) | Rejected. Requires curating `AssertionOverrides` (`RbacEndpointInventory.cs:68`) — the exact thing that broke the RBAC suite on 2026-07-28 — for no behavioural gain. |
| **`gl.journal.post`** ✅ | Chosen. Posting arbitrary journals can move money between any two accounts with no source document and no approval; it is the most powerful write in the product. `gl.journal.post` is already granted to exactly **CHIEF_ACCOUNTANT + COMPANY_ADMIN** — the correct blast radius. No new code, no new seed, no grant change. |
| `GET /journals` → `gl.journal.read` ✅ | Already granted to ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/TAX_OFFICER/COMPANY_ADMIN; matches `GET /journals/{id}`. |
| CoA management → `master.coa.manage` ✅ (unchanged) | Already granted to COMPANY_ADMIN + CHIEF_ACCOUNTANT. Inventing an ACCOUNTANT-visible `master.coa.read` is unnecessary: the JV form's picker uses `GET /reports/general-ledger/accounts` (§0.5), which ACCOUNTANT already holds. |

**Net RBAC change: ZERO. No new permission code, no new seed script, no grant edit, no
`AssertionOverrides` entry, no `ExpectedAuthnOnly` entry.** The generated map
(`docs/rbac/endpoint-permission-map.generated.md`) gains two rows and must be regenerated by
**running the RBAC tests** — never hand-edited.

### B7 — bank reconciliation's inline journal: **leave it alone** (recommendation)
Do not fold it onto the new path. It is not a duplicate posting engine — since 2026-07-09 it
already calls `PostManualEntryAsync` → `BuildAndPostAsync`, i.e. the *same* seam this spec
uses. What it adds is not journal logic but reconciliation logic: it requires an *Unmatched*
statement line, derives Dr/Cr direction from `StatementDirection`, resolves the bank side from
`BankAccount.GlCashAccountId`, and — critically — wraps the JE post and the
`MatchStatus → Posted` claim in ONE transaction with a rollback on a lost race
(`BankReconciliationService.cs:253-278`). Routing it through a generic JV endpoint would
either lose that atomicity or push reconciliation state into the JV service. Folding it is a
pure-cost refactor of shipped, money-critical, Tier-2-reviewed code.

### B8 — frontend
1. **`frontend/app/(dashboard)/journals/page.tsx`** (new) — list. `PageHeader` + `DataTable`
   (`useJournals({from,to,search})`), columns: docNo (font-mono, `Link` to `/journals/{id}`),
   docDate, description, reference, totalDebit, totalCredit, status badge. Date-range filter
   defaulting to the current month. "สร้างใบสำคัญทั่วไป" button → `/journals/new`, wrapped in
   `<PermissionGate scope="gl.journal.post">`.
2. **`frontend/components/forms/ManualJournalForm.tsx`** (new) — structure copied from
   `ExpenseClaimForm.tsx` (275 lines: header fields + a repeating line grid + `addLine`).
   Header: docDate (date input, **default today**, `max` = today), description (required),
   reference. Line grid ≥2 rows: account `<select>` from `useGlAccounts()` (already returns
   only active non-header accounts, §0.5) rendered `{accountCode} — {accountNameTh}`, debit,
   credit, line description. Live footer: Σ Dr, Σ Cr, and **difference**; submit disabled
   while `ΣDr !== ΣCr` or either is 0 — **UI convenience only, never the guarantee** (INV-1).
   Submit → `useCreateManualJournal()` → on success `toast.success` + `router.push('/journals/'+journalId)`;
   on error `toast.error(errorToToast(e))`.
3. **`frontend/app/(dashboard)/journals/new/page.tsx`** (new) — 5-line wrapper, exactly like
   `expense-claims/new/page.tsx`.
4. **`frontend/lib/types.ts` / `queries.ts`** — `JournalListItem`, `CreateManualJournalRequest`,
   `ManualJournalLineInput`; hooks `useJournals`, `useCreateManualJournal` (invalidates
   `['journals']`, `['gl-accounts']`, `['trial-balance']` if such a key exists).
5. **`SidebarNav.tsx`** — in the `reports` group, immediately **before** `generalLedger`
   (precedent: `/period-close` already lives there and is not a report):
   ```ts
   { href: '/journals', key: 'journals', Icon: BookOpenCheck, perm: 'gl.journal.read' },
   ```
6. **i18n** — new `jv` namespace in **both** `th.json` and `en.json` + `nav.journals`; no
   hardcoded Thai in any component.
7. **`frontend/lib/i18n/problems.ts`** — Thai messages for every new stable code (this is
   where "a clear Thai message" actually lives; the BE emits codes, the FE resolves them —
   `frontend/lib/api/errors.ts:47-56`). Add:
   `je.unbalanced`, `gl.unbalanced`, `je.future_date`, `je.year_closed`, `je.account_not_found`,
   `je.account_inactive`, `je.account_is_header`, `coa.parent_not_found`, `coa.parent_not_header`,
   `coa.has_postings`, `coa.system_account`, **and `period.closed`** — which is currently
   missing from that dict (only `period.year_closed` / `period.not_closed` are there), so the
   single most likely JV rejection surfaces today as raw English.

---

## 3. Design — Part C: the three accounts

### C1 — codes (each verified free across `backend/src`, `frontend`, `db`, `docs` — §0.9)
| Code | ชื่อไทย | English | Type | Normal | Why this number |
|---|---|---|---|---|---|
| **2190** | เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น | Director & Shareholder Loan | LIABILITY | CR | Next free slot in the existing current-liability block (2110, 2151–2180). Opening a 22xx band would be a new numbering convention for one account. |
| **5500** | ดอกเบี้ยจ่าย | Interest Expense | EXPENSE | DR | Next free 100-block after the existing expense run (5000–5460). |
| **4300** | รายได้อื่น | Other Income | REVENUE | CR | Next free slot after 4000/4100/4200; covers interest received, rent, scrap — Ham's second question. |

None goes into `GlAccountsOptions` (§A1d rationale). With 2190 + 1120 seeded, a director loan
is one JV: **Dr 1120 เงินฝากธนาคาร / Cr 2190 เงินกู้ยืมจากกรรมการ**.

### C2 — both creation paths (the O10 D1b lesson: an account seeded in only one path is a time bomb)
1. **`Infrastructure/Master/MasterDataServices.cs`, `DefaultChartOfAccounts` (`:379-413`)** —
   append the three tuples. Covers every **future** company created via `CompanyService.CreateAsync`.
2. **`Infrastructure/Migrations/SqlScripts/631_seed_director_loan_and_other_income_accounts.sql`
   (new)** — covers every **existing** company.

### C3 — the seed script's runtime security context (read this before writing a single line of SQL)
- **Who runs it:** `DbInitializer.ApplyScriptsAsync`, at **API startup**, on the app's
  connection — prod role `teas`, **NOBYPASSRLS**.
- **What is set at that moment: nothing.** `TenantMiddleware` has not run. `app.company_id` is
  **UNSET**, `app.bypass_rls` is **UNSET**, for the whole script-application phase, on every
  environment including prod.
- **`master.chart_of_accounts` is G1** — `010_rls_policies.sql:10`; `company_isolation USING
  (company_id = current_setting('app.company_id'))`, deliberately **no bypass arm**. Postgres
  reuses `USING` as the implicit `WITH CHECK`, so **any INSERT with `app.company_id` unset
  fails 42501** and takes the whole deploy down. This exact mistake shipped and auto-rolled
  back v1.24.0 on **2026-07-28** (`troubles-wiki.md:158`, memory `rls-masked-by-superuser-tests`).
- **How each read and write satisfies the policy:**
  - Driving read `SELECT company_id FROM master.companies` — `master.companies` carries **no
    RLS policy at all** (it is the tenant root; absent from `010_rls_policies.sql`'s list, and
    `MasterDataServices.cs:233` states it). Unfiltered, always. ✅
  - `WHERE NOT EXISTS (SELECT 1 FROM master.chart_of_accounts …)` — **this read IS
    RLS-filtered**, and it runs *inside* the loop **after** `set_config('app.company_id', …)`,
    so it sees exactly the current company's rows. **The pin is load-bearing for the READ, not
    just the write.** Without the pin the NOT-EXISTS would see zero rows (guard always true)
    and then the INSERT would 42501. If someone ever "fixes" that 42501 by bolting a bypass
    arm onto the policy instead, the NOT-EXISTS flips to seeing **every** company's rows and
    the seed **silently inserts nothing for companies 2..N** — the quiet variant of the same bug.
  - INSERT — `app.company_id` pinned to `c.company_id` and the inserted `company_id` is
    `c.company_id`; WITH CHECK passes. ✅
- **Why the test suite cannot catch any of this:** `teas_test` connects as a Postgres
  **superuser**, which bypasses RLS unconditionally. A green `dotnet test` proves the SQL is
  syntactically valid and idempotent — **nothing** about the RLS branch.

**Script body — mirror `621_seed_fixed_asset_accounts.sql` / `630_seed_payroll_other_deductions_account.sql`
exactly. Never a bare `INSERT … FROM master.companies CROSS JOIN (VALUES …)`. Never a curly
brace anywhere in the file (EF `ExecuteSqlRawAsync` treats `{}` as `string.Format` placeholders).**
```sql
-- Director/shareholder loan + interest expense + other income, for EVERY existing company.
-- New companies get them via DefaultChartOfAccounts (MasterDataServices.cs).
-- Additive + idempotent; all three are zero-balance on arrival (dropped by the balance sheet's
-- zero-row filter) — safe for co2/co3 demo data.
-- master.chart_of_accounts is a G1 (never-bypassable) tenant table: pin app.company_id per
-- company, do NOT add a bypass arm, do NOT use a bare multi-company INSERT — startup runs with
-- app.company_id UNSET under the NOBYPASSRLS `teas` role and every row would 42501
-- (prod v1.24.0, 2026-07-28, rolled back clean). teas_test connects as superuser and cannot
-- catch this. Mirrors 621/630.
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);
        INSERT INTO master.chart_of_accounts
            (company_id, account_code, account_name_th, account_name_en, account_type,
             normal_balance, is_header, is_active, created_at)
        SELECT c.company_id, v.code, v.th, v.en, v.acct_type, v.normal_bal, FALSE, TRUE, now()
        FROM (VALUES
            ('2190','เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น','Director & Shareholder Loan','LIABILITY','CR'),
            ('5500','ดอกเบี้ยจ่าย','Interest Expense','EXPENSE','DR'),
            ('4300','รายได้อื่น','Other Income','REVENUE','CR')
        ) AS v(code, th, en, acct_type, normal_bal)
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = v.code)
        ON CONFLICT (company_id, account_code) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;
```
> **Thai glyph check before commit:** the Bengali U+09AE creeps into Thai strings where a Thai
> ม (U+0E21) belongs (memory `thai-mo-glyph-pitfall`). `กรรมการ` above contains one. Verify with
> gate 10 — and note that gate 10 must **exclude this spec file**, which quotes the Bengali
> character on purpose as an example.

### C4 — deploy probe (row counts, not exit codes)
An exit code of 0 proves nothing here — the G3 variant of this bug succeeds and inserts zero
rows. After deploying to prod, run against the prod DB:
```sql
-- 1. The script was actually tracked (a G1 crash rolls back its own transaction → never tracked).
SELECT count(*) FROM sys.applied_sql_scripts
 WHERE script_name = '631_seed_director_loan_and_other_income_accounts.sql';   -- expect 1

-- 2. Every company got every code. Expect 0 rows for each.
SELECT c.company_id, v.code
FROM master.companies c
CROSS JOIN (VALUES ('2190'),('5500'),('4300')) AS v(code)
WHERE NOT EXISTS (SELECT 1 FROM master.chart_of_accounts a
                  WHERE a.company_id = c.company_id AND a.account_code = v.code);

-- 3. Sanity: total new rows == 3 × #companies (first deploy).
SELECT count(*) FROM master.chart_of_accounts WHERE account_code IN ('2190','5500','4300');
SELECT count(*) * 3 FROM master.companies;
```
To **re-exercise** the script on `teas_test` after editing it (the tracker skips already-applied
names regardless of content): `DELETE FROM sys.applied_sql_scripts WHERE script_name =
'631_seed_director_loan_and_other_income_accounts.sql';` then re-run.

Deploy verification must also include **one end-to-end probe through the public domain**
(CDN→proxy→app), not just localhost — a route can be green on 127.0.0.1 and unreachable
publicly (cost a hotfix release 2026-07-08).

---

## 4. Invariants (state these in the PR description; each has a named test)

**INV-1 — a journal balances.** For any posted journal, `Σ debit == Σ credit` exactly (decimal
equality, no epsilon, both > 0), with every amount at ≤2 decimal places. The **guarantee** is
server-side, in `GlPostingService.BuildAndPostAsync` (`gl.unbalanced`) and
`JournalEntry.MarkPosted` (`je.unbalanced`); the FluentValidation rule and the disabled submit
button are conveniences that produce nicer errors, and neither is ever the guarantee.
*Test `Unbalanced_manual_jv_is_refused_and_writes_nothing`*: POST lines summing to Dr 100.00 /
Cr 99.99 → non-2xx; then assert `gl.journal_entries` contains **zero** rows with that
description (a refusal that leaves a row is the real failure mode).
*Test `Three_decimal_amount_is_refused`*: Dr 100.005 / Cr 100.005 → 400 (the columns are
`numeric(19,4)`, so without the 2dp rule this would round-trip and balance on invisible satang).

**INV-2 — money invariant of a manual JV.** A manual JV changes the balance of **exactly** the
accounts named on its own lines, each by exactly `Dr − Cr`, and the sum of all changes is zero.
Nothing else in the ledger moves.
*Test `Director_loan_jv_ties_out`*: snapshot the trial balance; post Dr 1120 / Cr 2190 = 100,000.00;
re-snapshot and assert **(a)** Δ1120 = **+100,000.00 exactly**, **(b)** Δ2190 = **+100,000.00
exactly on the credit side**, **(c)** every other account's Δ = 0, **(d)** the trial balance
still reports `total_debit == total_credit`, and **(e)** — the invariant that catches the
classic error — **profit & loss net income over the period is UNCHANGED**: a director loan is
a liability, never income. A test that only asserts "two rows exist with the right amounts"
does not pin (c) or (e) and is not sufficient.

**INV-3 — a posted journal is immutable.** No route can edit or delete one; `PUT /journals/{id}`
and `DELETE /journals/{id}` do not exist and must not be added. The DB backs it independently
(`020_journal_immutability.sql`, `580`/`582_posted_lines_immutable_v2.sql`).
**The correction path is a manual reversing JV** — the user enters the same lines with Dr/Cr
swapped and puts the original `docNo` in `reference`. **Automated reversal is explicitly
deferred**: `JournalEntry.ReversalOfId` exists and the detail page already renders a link to
it (`journals/[id]/page.tsx`), but no "Reverse" button, no `POST /journals/{id}/reverse`, and
no auto-population is built here. It is a separate item — see §7.
*Test `Posted_manual_jv_cannot_be_mutated`*: post a JV, then a raw
`UPDATE gl.journal_entries SET total_debit = total_debit + 1 WHERE journal_id = @id` →
`check_violation`; and `DELETE FROM gl.journal_entries WHERE journal_id = @id` → `check_violation`.
*Test `No_journal_mutation_routes_exist`*: assert the `EndpointDataSource` contains no
`PUT /journals/{...}` and no `DELETE /journals/{...}`.

**INV-4 — period and fiscal-year gates apply, via the shared implementation.** A JV cannot post
into a closed month, a closed fiscal year, or the future. `PeriodCloseService.EnsureOpenAsync`
is **reused**, never reimplemented.
*Tests*: `Jv_into_closed_month_is_refused` (close a month, post into it → `period.closed`, zero
rows written); `Jv_into_closed_fiscal_year_is_refused` (→ `je.year_closed`);
`Jv_dated_tomorrow_is_refused` (→ `je.future_date`);
`Jv_into_a_reopened_month_succeeds` (close → reopen via O14 → post succeeds — proves the gate
is the *only* thing blocking back-dating, and that back-dating is legitimately reachable).

**INV-5 — only postable accounts accept postings.** No posting to a header account, an inactive
account, or **another tenant's** account. Enforced in `JournalService`, because
`gl.journal_lines` has no `company_id` and no FK — RLS and the DB cannot enforce it (§0.3).
*Tests*: `Jv_to_header_account_is_refused`; `Jv_to_inactive_account_is_refused`;
**`Jv_to_another_companys_account_is_refused`** — create company B, take a real `account_id`
from B's CoA, post it from company A → `je.account_not_found` (**not** a 500, **not** success),
and assert zero journal rows in **both** companies. This is the security test; do not skip it.

**INV-6 — chart-of-accounts edits never corrupt history.**
- An account is **never deleted** — no delete route exists, in the API or the UI. Deactivation
  is the retire operation. *Test `No_account_delete_route_exists`* (EndpointDataSource).
- `account_code`, `account_type`, `normal_balance`, `parent_id` are **frozen after create** —
  structurally, because `UpdateAccountRequest` does not carry them
  (`ChartOfAccountDtos.cs:8`). *Test `UpdateAccountRequest_exposes_only_mutable_fields`*: assert
  by reflection that its property set is exactly `{AccountNameTh, AccountNameEn, IsHeader,
  IsActive}` — this pins the invariant against a future "just add code to the update DTO".
- An account with posted lines cannot become a header. *Test `Header_flip_on_used_account_is_refused`.*
- A `GlAccountsOptions` account cannot be deactivated or made a header.
  *Test `System_account_cannot_be_deactivated`* (try `1130`) and
  *`GlAccountsOptions_AllCodes_covers_every_mapped_code`* — reflection: `AllCodes().Count() ==`
  the number of `string` properties on `GlAccountsOptions` (pins drift when someone adds a prop).
- **Deactivation never unbalances a report.** *Test `Deactivating_an_account_with_movement_keeps_the_trial_balance_balanced`*:
  post a JV touching account X, deactivate X, request the trial balance with
  `includeInactive=false` → X still appears and `total_debit == total_credit`. (Fails today; A2 fixes it.)

**INV-7 — account codes are unique per company and system codes stay resolvable.** The
`(company_id, account_code)` unique index is the guarantee;
`ChartOfAccountService.CreateAsync`'s pre-check turns the normal case into a 422 `coa.duplicate`.
*Test `Duplicate_account_code_is_refused`.*
*Test `Every_GlAccountsOptions_code_resolves_to_exactly_one_row_per_company`*: for each code in
`AllCodes()`, `COUNT(*) == 1` for the test company — this is the assertion that would catch a
seed or a UI create fragmenting `ResolveAccountIdAsync`.
*Test `Seeded_codes_2190_5500_4300_exist_for_every_company`* (proves C2 covered both paths).

---

## 5. Requirements (checklist)

### WP-BE (backend) — depends on nothing
- [x] **C2a** `MasterDataServices.cs` `DefaultChartOfAccounts` — append 2190 / 5500 / 4300 (§C1). Evidence: `Seeded_codes_2190_5500_4300_exist_for_every_company` green.
- [x] **C2b** New `SqlScripts/631_seed_director_loan_and_other_income_accounts.sql` — body exactly as §C3; no curly braces; ม glyph check. Evidence: `grep -c "{"` → 0; Bengali-glyph grep → clean; test confirms pre-existing companies (ids 1-5, predating this session) already carry 2190/5500/4300, proving the script ran and applied retroactively.
- [x] **A1a** `ChartOfAccountService.CreateAsync` sets `CreatedAt` + `IsActive`. Evidence: `CreateAsync_sets_CreatedAt_and_IsActive` green.
- [x] **A1b** `ChartOfAccountService.CreateAsync` validates `ParentId` (exists in tenant, is a header). Evidence: `CreateAsync_refuses_nonexistent_parent`, `CreateAsync_refuses_non_header_parent`, `CreateAsync_accepts_a_valid_header_parent` green.
- [x] **A1c** `ChartOfAccountService.UpdateAsync` refuses header-flip on an account with posted lines. Evidence: `UpdateAsync_refuses_header_flip_on_account_with_posted_lines` (drives a REAL post via `IJournalService`, not seeded rows) + `UpdateAsync_allows_header_flip_on_account_without_postings` green.
- [x] **A1d** `GlAccountsOptions.AllCodes()` added; `ChartOfAccountService` takes `IOptions<GlAccountsOptions>`; `UpdateAsync` refuses deactivate/header-flip of a system code. Evidence: `System_account_cannot_be_deactivated`, `System_account_cannot_be_header_flipped`, `GlAccountsOptions_AllCodes_covers_every_mapped_string_property` (reflection, 22/22), `Every_GlAccountsOptions_code_resolves_to_exactly_one_row_per_company` green.
- [x] **A2** `FinancialReportService.TrialBalanceAsync` keeps inactive-but-used accounts. Evidence: `Deactivating_an_account_with_movement_keeps_the_trial_balance_balanced` green.
- [x] **B2** `ManualJvLine` record + `PostManualEntryAsync` overload on `IGlPostingService` / `GlPostingService`. Existing 3-tuple overload and every current caller untouched (grep-verified: `BankReconciliationService.cs` still calls the 3-tuple overload; gate 5 green — see EVIDENCE).
- [x] **B3** `CreateManualJournalRequest` / `ManualJournalLineInput` / `JournalListItem` / `CreateManualJournalValidator` in `JournalDtos.cs`. Validator auto-discovered via `Accounting.Application`'s `AddValidatorsFromAssembly` (`DependencyInjection.cs:15`) — no separate registration needed, confirmed by the `Three_decimal_amount_is_refused_by_the_validator` unit test resolving it directly and by the live endpoint returning 400 shape via `Results.ValidationProblem`.
- [x] **B4** `IJournalService.CreateAndPostManualAsync` + `ListAsync`, implemented in `JournalService` per §B4. `CreateDraftAsync` / `PostAsync` / `GetDetailAsync` untouched (diff-reviewed — only ctor params and two new methods added).
- [x] **B5** Two routes in `JournalEndpoints.cs`, named permission policies only (`gl.journal.post` / `gl.journal.read`) — no `RequireAssertion`, confirmed by gate 3 (RBAC) passing with no `AssertionOverrides` edit.
- [x] **Tests** — new `backend/tests/Accounting.Api.Tests/Ledger/ManualJournalTests.cs` (now 15 tests, +2 Tier-2 F1/F1b tests) and `backend/tests/Accounting.Api.Tests/Master/ChartOfAccountAdminTests.cs` (14 tests), covering every named test in §4. Two named tests (`Unbalanced_manual_jv_is_refused_and_writes_nothing`, `Three_decimal_amount_is_refused`) are implemented as service-layer/validator-unit tests rather than literal HTTP 400s — see SKIPPED/SIMPLIFIED in the attempt log; the underlying guarantee (throws + zero rows written) is exercised either way. Gate 2: 29/29 green, 0 skipped (was 27/27; +2 for Tier-2 F1/F1b).
- [x] **RBAC map** regenerated by *running the RBAC tests* (never hand-edited): `docs/rbac/endpoint-permission-map.generated.md`. Evidence: gate 3 green (see attempt log).
- [x] **troubles-wiki** — appended the `CreateDraftAsync` discards `docDate` finding (§B0) and marked the stale `period.closed` "permanently bricked" entry (line 25) as **superseded by O14**.

### WP-FE (frontend) — parallel-safe with WP-BE (different build system, no DB); wire shapes are pinned in §B3/§B5
- [x] **A3** `settings/chart-of-accounts/page.tsx` per §A3 (no delete, code/type read-only on edit, no parent field). Evidence: file created; single pencil row action; create modal exposes accountCode/nameTh/nameEn/accountType/normalBalance (auto-defaulted, overridable) only, no parentId/isHeader; edit modal shows accountCode/accountType/normalBalance as disabled+hint, editable nameTh/nameEn/isActive toggle + deactivateHint; `isHeader` carried through unchanged on save (never hardcoded) so editing a header account's name can't silently un-header it; no delete button/route anywhere.
- [x] **A4** Sidebar settings entry (`SidebarNav.tsx`, after `businessUnits`, perm `master.coa.manage`, `BookOpen` icon per spec) + `coa` i18n namespace in both `th.json`/`en.json` (inserted after `businessUnit` namespace) + `nav.chartOfAccounts` in both files.
- [x] **B8.1** `journals/page.tsx` — list with from/to date filter (defaults to current-month via `bangkokMonthStart/End`) + search feeding `useJournals`, columns docNo(link)/docDate/description/reference/totalDebit/totalCredit/status badge, Create button gated `gl.journal.post`.
- [x] **B8.2**/**B8.3** `components/forms/ManualJournalForm.tsx` + `journals/new/page.tsx`. Built a JV-specific inline line editor (NOT `LineItemsTable` — see rationale in file header comment: LineItemsTable's shape is product/qty-based and always keeps one undeletable blank row, wrong for a Dr/Cr GL-account line with a hard minimum of 2 rows). Debit/credit inputs mutually clear each other per row (enforces pure-Dr-XOR-Cr in the UI). Submit disabled while `ΣDr!==ΣCr` or either total is 0 (INV-1 convenience only); running Σ Dr/Σ Cr/difference shown live; server rejection surfaced via `errorToToast` (Thai-resolved by code, §B8.7).
- [x] **B8.4** `lib/types.ts` (AccountListItem/CreateAccountRequest/UpdateAccountRequest/JournalListItem/ManualJournalLineInput/CreateManualJournalRequest/JournalPostedResult/AccountTypeStr/NormalBalanceStr) + `lib/queries.ts` hooks (`useAccounts`, `useCreateAccount`, `useUpdateAccount`, `useJournals`, `useCreateManualJournal`) added inline near `useGlAccounts`/`useJournal`, matching the file's existing scattered-`import type` precedent. `useAccounts` always sends `activeOnly` explicitly (BE's `[FromQuery] bool activeOnly` is non-nullable — confirmed by reading `MasterEndpoints.cs:75-77`'s own comment on this exact footgun). Invalidates `['accounts']`/`['gl-accounts']` on CoA writes; `['journals']`/`['gl-accounts']`/`['trial-balance']` on JV post.
- [x] **B8.5** Sidebar `journals` entry (reports group, immediately before `generalLedger`, `BookOpenCheck` icon, perm `gl.journal.read`).
- [x] **B8.6** `jv` i18n namespace in both locale files (inserted after `je`) + `nav.journals`. Zero hardcoded Thai in components — every Thai string reachable only from `messages/th.json` via `useTranslations`; verified by reading both new components for literal Thai (none) and by gate 10 (glyph grep, unrelated but confirms no stray Thai-adjacent bytes were hand-typed wrong).
- [x] **B8.7** `lib/i18n/problems.ts` — all 12 codes added: `period.closed` (was missing, now present), `je.unbalanced`, `gl.unbalanced`, `je.future_date`, `je.year_closed`, `je.account_not_found`, `je.account_inactive`, `je.account_is_header`, `coa.parent_not_found`, `coa.parent_not_header`, `coa.has_postings`, `coa.system_account`.

### Tier-2 review fixes (2026-07-29, opus-reviewer REJECT — 4 findings)
- [x] **F1** (HIGH, security) — `JournalService.CreateAndPostManualAsync` now validates every line's
  `BusinessUnitId` in ONE batched query (mirrors `ReceiptService.cs:224-227`): a BU id must
  resolve to a live (`IsActive`), tenant-owned `BusinessUnit`, else `bu.invalid`. Evidence:
  `Jv_with_another_companys_business_unit_is_refused` green (posts company B's real BU id from
  company A → `bu.invalid`, zero rows written in either company).
- [x] **F1b** (same root) — `Company.RequiresBusinessUnit` now enforced per line: when the
  company requires a BU, a Revenue/Expense line without one is refused `bu.required`;
  balance-sheet (asset/liability/equity) lines stay exempt, exactly as scoped (no widening).
  Frontend: `ManualJournalForm.tsx` adds a per-line BU `<select>` gated on
  `useCompanyBuSetting().data?.requiresBusinessUnit` (same hook every other document form
  already uses, e.g. `PurchaseOrderForm.tsx:91`) — invisible, and the form unchanged byte-for-
  byte in its rendered output, on a company that does not require BU. Evidence:
  `Jv_to_expense_account_without_bu_is_refused_when_company_requires_it` green (Expense line
  5100 with no BU on a `RequiresBusinessUnit=true` company → `bu.required`; the paired 1120
  Asset line carries no BU either and is not what trips the rejection).
- [x] **F2** (MEDIUM, frontend) — `ManualJournalForm.tsx` `canSubmit` now compares rounded
  satang integers (`Math.round(total*100)`) instead of raw JS-double `===`, so a 3-way split
  (33.33+33.33+33.34 vs 100.00) no longer locks Save on a visibly balanced entry. Comment
  reiterating "UI convenience only, never the guarantee" kept intact. FE-only fix; the repo has
  vitest + `@testing-library/react` as dependencies, but every existing frontend test is a
  pure-logic `lib/*.test.ts` (no `components/forms/*.test.tsx` precedent), and the dispatch's
  test requirement named only the two backend tests (F1/F1b) — no new FE test file added, to
  stay inside the requested scope. Verified by inspection + `tsc`/`next build` gates; a manual
  smoke (gate 11, Fable's) is the load-bearing check for this one.
- [x] **F3** (LOW) — `CreateManualJournalValidator`: added `MaximumLength(500)` on
  `ManualJournalLineInput.Description` (matches the entity's `HasMaxLength(500)` and the header
  `Description` rule) and `Lines.Count <= 200` upper bound. No dedicated new test (validator
  rule, same shape as the existing header-length rules already covered structurally); build +
  gate 2 confirm the validator still compiles/registers and the existing 2dp/count>=2 rules
  are undisturbed.
- [x] **F4** (LOW) — `troubles-wiki.md`: separated the spliced ESLint entry from the Thai-`?`
  encoding entry. The encoding entry now ends with its own `- **Seen:** 2026-07-26 (co7's...)`
  bullet restored; the ESLint entry keeps only its own `- **Seen:** 2026-07-29, WP-FE...` bullet.

---

## 6. Verification gates

**Order matters: the Tier-3 gate runner counts as a test-running worker — never overlap it with
any dispatch that runs tests (a concurrent run crashed the test host mid-gate, 2026-07-08).**
`TEAS_TEST_PG` dies between PowerShell calls — set it in the *same* call as `dotnet test`, and
compare the **skip count** against the baseline (a skipped suite fakes a green run).
Build from the real path, not a `subst` drive (MinVer stamps 0.0.0 otherwise).

| # | Command | Expected |
|---|---|---|
| 1 | `dotnet build backend/TEAS.sln` | 0 errors, 0 new warnings |
| 2 | `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~ManualJournal\|FullyQualifiedName~ChartOfAccountAdmin"` | all new tests green |
| 3 | `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~Rbac"` | green; `docs/rbac/endpoint-permission-map.generated.md` regenerated and shows `POST /journals/manual → gl.journal.post`, `GET /journals/ → gl.journal.read` |
| 4 | `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~FinancialReport\|FullyQualifiedName~TrialBalance\|FullyQualifiedName~BalanceSheet"` | green (A2 regression surface) |
| 5 | `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~BankReconciliation"` | green (B2 overload must not disturb it) |
| 6 | **Full suite** — `dotnet test backend/tests/Accounting.Api.Tests` | pass count ≥ baseline, **0 failed**, skip count == baseline. Per CLAUDE.md this single long run is **Fable's** to execute (backgrounded, log read) — the worker reports code-complete with gates 1–5. |
| 7 | Seed re-exercise: `DELETE FROM sys.applied_sql_scripts WHERE script_name='631_...sql';` then re-run the suite | script re-tracked; `SELECT count(*) FROM master.companies c WHERE NOT EXISTS (SELECT 1 FROM master.chart_of_accounts a WHERE a.company_id=c.company_id AND a.account_code='2190')` → **0** |
| 8 | `cd frontend && corepack pnpm exec tsc --noEmit` | 0 errors |
| 9 | `cd frontend && corepack pnpm lint` | clean (never run `next build` while `next dev` is up — it corrupts the dev server) |
| 10 | `grep -rn $'ম' backend/src frontend/messages frontend/app frontend/components` | **no matches.** (Bengali U+09AE masquerading as Thai ม U+0E21.) Do **not** include `specs/` — this spec quotes the Bengali glyph deliberately and would always match. |
| 11 | Manual smoke on a running stack | as CHIEF_ACCOUNTANT: create account 2190 is already present → post Dr 1120 / Cr 2190 100,000 → JV appears in `/journals` with a `JV-…` doc_no → trial balance still balances → P&L net income unchanged |
| 12 | **Post-deploy, prod, public domain** | §C4 probes 1–3 all pass, **plus** one authenticated end-to-end request to `/journals` through the public domain (not 127.0.0.1) |

---

## 7. Explicitly OUT of scope
- **A dedicated director-loan screen.** The general JV + account 2190 records it. Phase 2.
- **ภ.ง.ด.2 filing** (WHT on interest/dividends paid to individuals). Phase 2. Nothing here
  blocks it: 5500 and 4300 are ordinary accounts, and the JV path is generic.
- **Share capital / paid-up capital accounting.** `Company.PaidUpCapital` exists as a scalar
  field; a 3xxx share-capital account and its equity statement are a separate item.
- **Automated reversing entries** (`POST /journals/{id}/reverse`, a "Reverse" button,
  auto-populating `ReversalOfId`). Judged a separate item: it needs its own period-gate story
  (which period does a reversal of a prior-period JV land in?), its own permission question,
  and its own reversal-of-a-reversal guard. The manual reversing JV unblocks correction on day
  one. **Do not** build a half version of it here.
- **Folding bank reconciliation's inline journal onto the new path** — see §B7.
- **Fixing `CreateDraftAsync`'s discarded `docDate`** (the latent ภ.พ.36 bug) — flag in
  `troubles-wiki.md`, do not fix; changing it silently moves every reverse-charge JV's date.
- **Remapping the `INTR` expense category from 5200 → 5500.** `DefaultExpenseCategorySpecs`
  currently points "ดอกเบี้ยจ่าย" at 5200 (`MasterDataServices.cs:439`). Remapping changes
  where **future PV/VI lines post** for every existing tenant — a data decision for Ham, not a
  side effect of adding an account. Raise it with him after this ships.
- **An MCP tool for posting JVs.** Deliberate: an API-key-scoped agent that can post arbitrary
  journals is a different risk conversation.
- **A parent/child CoA tree UI, account groups, or code-range reservation.** YAGNI.
- **New EF migration.** Nothing here changes the schema. If the implementer finds themselves
  running `dotnet ef migrations add`, they have left the spec — stop and re-spec.

---

## 8. Blast-radius cap

**Max 21 files.** Hitting the cap = stop and re-spec, never a silent overrun.

*Backend (9):* `MasterDataServices.cs`, `GlAccountsOptions.cs`, `FinancialReportService.cs`,
`IGlPostingService.cs`, `GlPostingService.cs`, `JournalDtos.cs`, `JournalService.cs` (+ its
interface, same file group), `JournalEndpoints.cs`, `SqlScripts/631_….sql` **(new)**.
*Backend tests (2):* `Ledger/ManualJournalTests.cs` **(new)**, `Master/ChartOfAccountAdminTests.cs` **(new)**.
*Generated (1):* `docs/rbac/endpoint-permission-map.generated.md` (by running tests only).
*Frontend (8):* `settings/chart-of-accounts/page.tsx` **(new)**, `journals/page.tsx` **(new)**,
`journals/new/page.tsx` **(new)**, `components/forms/ManualJournalForm.tsx` **(new)**,
`lib/types.ts`, `lib/queries.ts`, `lib/i18n/problems.ts`, `components/app-shell/SidebarNav.tsx`.
*i18n (2, counted as one pair):* `frontend/messages/th.json`, `frontend/messages/en.json`.
*Docs (1):* `troubles-wiki.md`.

**Public-API changes:** allowed, and **only** these two routes —
`POST /journals/manual`, `GET /journals`. Any third new route = stop.
**Permission codes:** **none may be added.** If the implementer concludes one is needed, that
is a stop-and-re-spec, not a judgment call — a new code needs its own seed script with the
code inserted **before** any grant references it, in **one** file (`rbac-seed-ordering-footgun`).
**Schema:** no EF migration, no new table, no new column.
**Do NOT touch:** `PostClosingEntryAsync`, `YearCloseService`, `BankReconciliationService`,
`WhtFilingService`, `PeriodCloseService`, the existing `PostManualEntryAsync` 3-tuple overload,
`CreateDraftAsync`, `PostAsync`, `GetDetailAsync`, or any RLS policy.

---

## 9. Suggested dispatch split
1. **WP-BE** → sonnet-implementer, spec §5 WP-BE, gates 1–5 + 7 + 10. Runs tests → must not
   overlap with any other test-running dispatch or the Tier-3 gate.
2. **WP-FE** → sonnet-implementer, spec §5 WP-FE, gates 8–10. Wire shapes are fully pinned in
   §B3/§B5, so it is parallel-safe with WP-BE (FE `tsc`, no DB) — per CLAUDE.md's
   different-build-systems rule. It must **not** run `dotnet test`.
3. **Tier 2** → opus-reviewer, lenses: *money* (INV-2 tie-out, the 2dp rule), *security*
   (INV-5 cross-tenant account, the `gl.journal.post` gate choice in §B6), *schema/RLS* (§C3 —
   read the seed script against the 621/630 pattern line by line), *spec compliance*.
4. **Tier 3** → haiku-gate-runner, gate 6 (full suite), only after 1 and 2 have both landed.
5. Fable: full-diff review → commit → deploy → gate 12 probes.

---

## Attempt log
<!-- - <date> <worker>: <result / failure summary> -->
- 2026-07-29 opus-designer: design written. All code facts in §0 verified by reading the named
  files at the named lines. Nothing assumed. Flagged-not-verified: none.
- 2026-07-29 sonnet-implementer (WP-FE only): all WP-FE checklist items implemented — see
  per-item evidence above. Gates 8 (`tsc --noEmit`) and 10 (glyph grep) pass clean. Gate 9
  (`corepack pnpm lint`) is BLOCKED by a pre-existing repo gap unrelated to this diff: `frontend/`
  has no ESLint config at all (never committed — confirmed via `git log --all`), so `next lint`
  always drops into its first-run interactive wizard and cannot run non-interactively; logged in
  `troubles-wiki.md`. Substituted `next build` (0 errors, all pages incl. the 3 new routes
  compiled and statically analyzed) as the load-bearing FE build-health check in its place — this
  also re-runs the TS typecheck internally. Did NOT run any `dotnet` command (WP-BE was running in
  parallel). Did NOT run a live click-through: no backend/dev server was already running, starting
  one myself risked the exact concurrent-build collision the dispatch forbade, and WP-BE's two new
  routes (`POST /journals/manual`, `GET /journals`) may not exist yet on a fresh checkout — so the
  balanced-post / unbalanced-rejection / create-then-deactivate flows were verified by tracing code
  against the pinned §B3/§B5 wire shapes, not observed live. Flagging this plainly rather than
  spending an hour standing up a full stack mid-parallel-dispatch.
- 2026-07-29 sonnet-implementer (WP-BE only): all WP-BE checklist items implemented — see
  per-item evidence above. Gates 1–5, 7, 10 all green (see report EVIDENCE). Did NOT run
  `tsc`/`next build`/full Api suite per dispatch instruction (FE ran in parallel; full suite is
  Fable's). Two named INV-1 tests (`Unbalanced_manual_jv_is_refused_and_writes_nothing`,
  `Three_decimal_amount_is_refused`) are implemented as service-layer/validator-unit tests rather
  than literal HTTP-endpoint 400 assertions — the underlying guarantee (throws + zero rows
  written) is still exercised, since the imbalance check is enforced twice (FluentValidation at
  the endpoint AND `GlPostingService.BuildAndPostAsync`/`JournalEntry.MarkPosted` server-side) and
  the 2dp check is validator-only (deliberately — `TotalDebit==TotalCredit` alone would let
  100.005==100.005 slip through the GL engine, which is exactly why the rule needs its own direct
  unit test). Pre-existing defects A1e (`coa.duplicate` TOCTOU race → 500 not 422) and the
  `TrialBalanceAsync` inactive-account bug (fixed here, A2) both handled per spec. RBAC map
  regenerated cleanly: +2 rows (`GET /journals/`, `POST /journals/manual`), both named `Perm`
  policies, zero `AssertionOverrides` edits — confirms §B6's "net RBAC change: ZERO" claim.
  Gate 7 (seed re-exercise) verified by ROW COUNT, not exit code: untracked 631 in one `dotnet
  test` process, a FRESH process's `PostgresFixture.InitializeAsync` re-applied it, then a raw-SQL
  probe confirmed `tracked=1` and `missing_rows=0` across every company in the shared teas_test DB
  (companies that predate this session, ids 1-5, already had 2190/5500/4300 from the FIRST
  application — the C2b/631 SQL path is proven, not just C2a). No EF migration added; no RLS
  policy touched; `CreateDraftAsync`/`PostAsync`/`GetDetailAsync`/`PostClosingEntryAsync`/the
  3-tuple `PostManualEntryAsync` overload all diff-verified byte-for-byte untouched. 13 files
  touched (9 backend + 2 new tests + 1 generated), matching §8's per-category budget exactly.
- 2026-07-29 opus-reviewer (Tier-2): **REJECT**, 4 findings (F1 HIGH security, F1b same root,
  F2 MEDIUM frontend, F3/F4 LOW). Money lens (INV-2 tie-out, 2dp rule) and schema/RLS lens (§C3
  seed script vs 621/630 pattern) both explicitly clean. F1: per-line `BusinessUnitId` rode in
  on the DTO unchecked — a real FK to `master.business_units`, but the FK check runs as the
  table owner and bypasses RLS (same class of bug §A1b already guarded for `parent_id`). F1b:
  `Company.RequiresBusinessUnit` silently unenforced for JVs while every other document type
  refuses; company 2 on prod has it set `true`, so live not theoretical. F2: `canSubmit` used
  raw JS-double `===` on accumulated Dr/Cr sums. F3: line `Description` unbounded → prod 500.
  F4: troubles-wiki ESLint entry spliced into the Thai-`?`-encoding entry, corrupting both.
- 2026-07-29 sonnet-implementer (Tier-2 fix round): all four findings fixed — see per-item
  evidence in §5's new "Tier-2 review fixes" block above. F1/F1b: `JournalService.cs` gained a
  batched BU-validation block (mirrors the existing account-validation block immediately above
  it) — one query resolves valid BU ids for the tenant, one query reads `RequiresBusinessUnit`,
  then a single loop per line checks `bu.invalid` (forged/foreign/inactive) and `bu.required`
  (Revenue/Expense line, company requires BU, no BU set). `ManualJournalForm.tsx` wires a
  per-line BU `<select>` gated on the same `useCompanyBuSetting` hook every other document form
  uses, options from `useBusinessUnits()`; on a company that does not require BU nothing new
  renders. F2: `totalDebitSatang`/`totalCreditSatang` (`Math.round(total*100)`) replace the raw
  double comparison. F3: `MaximumLength(500)` on the line `Description` + `Lines.Count<=200`.
  F4: the two spliced troubles-wiki entries un-spliced, each keeping its own `Seen:` bullet.
  Gates run: `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false`
  (0 warnings, 0 errors — the parallel-graph build fails silently in this sandbox per
  troubles-wiki, `Accounting.sln` is the real solution file, not `TEAS.sln`); filtered
  `dotnet test` runs (env `TEAS_TEST_PG`/`TEAS_REPO_ROOT` set inline, same call as each
  `dotnet test`): `ManualJournal|ChartOfAccountAdmin` → 29/29 (was 27/27, +2 new Tier-2 tests),
  `Rbac` → 57/57, `FinancialReport|TrialBalance|BalanceSheet` → 12/12, `BankReconciliation` →
  25/25, `BusinessUnit` (the dispatch's requested BU-related filter, covers
  `Sprint8BusinessUnitTests` + `PurchaseBusinessUnitTests`) → 17/17 — all 0 skipped, 0 failed.
  Bengali-glyph grep (gate 10, excluding `specs/`) → clean. `frontend`: `tsc --noEmit` → 0
  errors; `next build` → compiled successfully, all 87 routes incl. the 3 JV/CoA routes,
  substituted for `pnpm lint` per the existing WP-FE troubles-wiki entry (no ESLint config
  ever committed to this repo — confirmed unrelated to this diff). Did NOT run the full Api
  suite (gate 6) or gate 7/11/12 — out of scope for this fix round per dispatch ("I run it").
  No new files added; touched files: `JournalService.cs`, `JournalDtos.cs`,
  `ManualJournalForm.tsx`, `messages/th.json`, `messages/en.json`, `ManualJournalTests.cs`,
  `troubles-wiki.md` — 7 files, well inside the untouched §8 cap (still 21 max, no new public
  routes, no new permission code, no schema change).
