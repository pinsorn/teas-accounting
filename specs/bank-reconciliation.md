# Spec: Bank Reconciliation — KBiz CSV + K-Plus PDF (Cycle B, feature #1)

<!-- Living document. The worker updates the checklist as it works; a retry uses the SAME
     file and grows the Attempt log — never rewrite the spec for a retry. -->

**Source plan:** `PLAN-feature-cycle-2026-07.md` §1 (+ Ham scope addition 2026-07-09: K-Plus PDF adapter).
**Branch:** `feat/cycle-b-bank-reconciliation`.
**Scope class:** FOOTGUN ZONE — new EF schema + RLS/RBAC seed choreography (prod-42501 class) + a new
money-posting path (inline JE that must respect period-close) + a new PARSING dependency (PdfPig) +
security-sensitive PDF password handling. Design is Opus/Fable-owned; a mid-tier implementer types it
FROM this spec, stage by stage.
**Capability map (Fable fills at dispatch):** Sonnet implements each stage (sequential — stages share
schema/service files); Opus or Codex Tier-2 review on B4 (money) + the SQL seeds (B1); Haiku Tier-3 gate;
Fable diff review + `ef migrations add` + commit. EF migration is Fable-generated (see A-STAGE note),
never hand-authored by the implementer.

---

## Scope reality (read FIRST — reading the code surfaced four facts the plan glossed)

1. **GL cash/bank posting resolves to a SINGLE hardcoded account code, ignoring the bank account.**
   `GlPostingService.PostReceiptAsync` (L74) and `PostPaymentVoucherAsync` (L155) resolve the cash side as
   `pv.PaymentMethod == Cash ? _accounts.CashAccount : _accounts.BankAccount` → **`1110` (Cash) / `1120`
   (Bank)** from `GlAccountsOptions` (`Infrastructure/Ledger/GlAccountsOptions.cs` L12-13). The
   `Receipt.BankAccountId` (Receipt.cs L31) and `PaymentVoucher.BankAccountId` (PaymentVoucher.cs L42)
   fields **exist but are NOT used in posting** — every bank movement lands in the one `1120` account.
   → **v1 reconciles at the `1120` level** (see D6). We do NOT change the money-critical posting path.
2. **No existing GL poster both (a) accepts an arbitrary caller-supplied DocDate AND (b) enforces
   period-close.** `JournalService` hard-forces `DocDate = _clock.TodayInBangkok()` (JournalService.cs L41)
   and cannot back-date. `PostClosingEntryAsync` takes a caller DocDate but **deliberately bypasses**
   `EnsureOpenAsync` AND sets `IsClosingEntry=true` (which EXCLUDES the entry from P&L — wrong for bank
   interest income). The six source-doc posters take DocDate from their source doc, never call
   `EnsureOpenAsync` themselves (the calling *fiscal service* does). → the inline bank JE needs a NEW thin
   poster + the recon service calls `EnsureOpenAsync` itself (D7).
3. **The two statements have INCOMPATIBLE row shapes** — KBiz CSV has SEPARATE ถอนเงิน/ฝากเงิน columns
   (direction explicit); K-Plus PDF has ONE combined ถอนเงิน/ฝากเงิน column (direction must be DERIVED
   from the running-balance delta). The adapter interface (D2) hides this; the normalized model carries an
   explicit `Direction`.
4. **The repo has PDF *generation* libs (PDFsharp, QuestPDF) but NO PDF text-*extraction* lib.** K-Plus
   parsing needs one → add **PdfPig** (`UglyToad.PdfPig`), the standard battle-tested .NET reader; it opens
   password-protected PDFs via `ParsingOptions { Password = … }` and exposes per-word bounding boxes needed
   for column reconstruction (D9). One dependency, no alternatives bundled (Ponytail).

---

## Context / footguns (fold in — do NOT rediscover)

### Existing machinery (verified 2026-07-09, exact locations)

- **Attachment infra (REUSE for raw-file storage).** `Domain/Entities/Sys/Attachment.cs`
  (`class Attachment : ITenantOwned`): `long AttachmentId, int CompanyId, AttachmentParentType ParentType,
  long ParentId, AttachmentCategory Category, string FileName, string MimeType, long SizeBytes,
  string StoragePath (relative, filesystem — NOT bytea), DateTimeOffset UploadedAt, long UploadedBy,
  DeletedAt/By (soft delete), Description, PageCount`. **Bytes live on the FILESYSTEM** via
  `IFileStorageService.SaveAsync(companyId, parentType, parentId, Stream, suggestedFileName, ct)` →
  returns a relative path; DB stores only the path. Upload endpoint **POST /attachments**
  (`Api/Endpoints/AttachmentEndpoints.cs` L35) is **multipart/form-data** (`req.ReadFormAsync`,
  `form.Files["file"]`, fields `parent_type`/`parent_id`/`category`/`description`), `.DisableAntiforgery()`.
  Polymorphic link = `AttachmentParentType` enum (`Enums/AttachmentEnums.cs`) + `long ParentId`; DB string
  map in `AttachmentCodes.cs` (`ParentDb`/`CategoryDb` dicts). Categories include `BankSlip`. **There is NO
  `BankStatement` parent type / category yet — B2 adds one** (`AttachmentParentType.BankStatement` →
  `"BANK_STATEMENT"`, `AttachmentCategory.BankStatement` → `"BANK_STATEMENT"`, wire into `ParentDb`/
  `CategoryDb`/`ParentFrom`/`CategoryFrom`). `AttachmentService` methods: `UploadAsync(parentType, parentId,
  category, description, fileName, mimeType, sizeBytes, Stream, ct)`, `ListAsync`, `OpenForDownloadAsync`,
  `SoftDeleteAsync`.
- **Receipt (money-IN match target).** `Domain/Entities/Sales/Receipt.cs`: `DateOnly DocDate` (single date,
  L17); `long CustomerId`, `string? DocNo`; `decimal Amount` (L36), `decimal WhtAmount` (L47),
  **`decimal CashReceived  // = Amount − WhtAmount` (L51)** — this is the ACTUAL cash into the bank (net of
  customer-withheld WHT); `DocumentStatus Status` (Draft/Posted); `long? BankAccountId` (L31, unused by
  posting). **Match a MoneyIn statement line on `CashReceived`, not `Amount`.**
- **PaymentVoucher (money-OUT match target).** `Domain/Entities/Purchase/PaymentVoucher.cs`:
  `DateOnly DocDate` (L25); `long VendorId`, `string? DocNo`; `decimal SubtotalAmount, VatAmount, WhtAmount`;
  **`decimal TotalPaid  // = subtotal + vat − wht` (L52)** — the ACTUAL cash out of the bank;
  `bool SelfWithholdMode` (L61, gross-up case where cash = subtotal+vat); `DocumentStatus Status`;
  `long? BankAccountId` (L42, unused by posting). **Match a MoneyOut line on `TotalPaid`.**
- **GL posting paths** (`Infrastructure/Ledger/GlPostingService.cs`): public
  `PostTaxInvoiceAsync/PostReceiptAsync/PostPaymentVoucherAsync/PostVendorInvoiceAsync/
  PostTaxAdjustmentNoteAsync/PostPayrollRunAsync` (each takes DocDate from its source doc) +
  `PostClosingEntryAsync(int companyId, int branchId, DateOnly docDate, string description,
  bool isClosingEntry, long? reversalOfId, IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)>
  lines, ct)` (L351). Private `BuildAndPostAsync` (L393) does balance-check (`gl.unbalanced`) + JV-number
  (`_numbers.NextAsync(..., docDate, ct)`) + `MarkPosted`. **NONE call `EnsureOpenAsync`.** `PostClosingEntryAsync`
  is the closest shape for the inline JE BUT its `isClosingEntry=true` semantics exclude the entry from all
  P&L/CIT/tax reports (see year-end §C) — **do NOT reuse it for bank interest/fees**; add `PostManualEntryAsync`
  (D7).
- **Period-close enforcement is application-level only.** `IPeriodCloseService.EnsureOpenAsync(DateOnly
  docDate, ct)` (`Application/Ledger/IPeriodCloseService.cs` L11) throws `DomainException("period.closed",
  …)` when the period is closed (`PeriodCloseService.cs` L41-46; a missing period row = CLOSED except the
  current Bangkok month). No DB trigger. **The recon service must call it before posting an inline JE.**
- **JE immutability** — `SqlScripts/020_journal_immutability.sql`: posted JEs cannot be UPDATE'd on
  critical fields nor DELETE'd. There is **no void / no programmatic reversal service**. → an inline JE,
  once posted, is PERMANENT; a JE-backed reconciliation cannot be "un-posted" (D8 unmatch rule).
- **Cash/bank CoA rows** (`Infrastructure/Master/MasterDataServices.cs` `DefaultChartOfAccounts`):
  `("1110","เงินสด","Cash",Asset,Debit)`, `("1120","เงินฝากธนาคาร","Bank",Asset,Debit)`. **One bank code
  (`1120`)** — a `bank_account` row's `gl_cash_account_id` DEFAULTS to the `1120` account.
- **Tenant/RLS.** `ITenantOwned { int CompanyId }` (`Domain/Common/ITenantOwned.cs`) is auto-filtered by
  `AccountingDbContext.ApplyTenantFilters` (reflection over every `ITenantOwned` entity → global query
  filter `e.CompanyId == _tenant.CompanyId`). No per-entity wiring. GUC = `app.company_id` (pinned by
  `TenantMiddleware`). RLS template to mirror = `600_superadmin_scoped_rls.sql` **group G1** (plain
  `company_isolation` USING `company_id = NULLIF(current_setting('app.company_id', true),'')::INT`, NO
  bypass arm). All three new tables are G1 tenant data.
- **DbInitializer order** (`Persistence/DbInitializer.cs`): create extensions → **`MigrateAsync` (EF
  migrations FIRST, L103)** → `EnsureLedgerTableAsync` → **`ApplyScriptsAsync` (numbered SqlScripts AFTER,
  L106)**. Scripts ordered **lexically**, each in **its own transaction**, tracked in
  `sys.applied_sql_scripts`. → an `_rls.sql` script may assume its EF-created table exists.
- **SqlScripts highest currently = `613_number_gap_view_bigint.sql`.** Use **614+**. New TABLES are created
  by EF migrations, never by SqlScripts; RLS for a new table goes in its own `NNN_<name>_rls.sql`
  (precedent: `612_fiscal_year_close_rls.sql`).
- **RBAC** (`Api/Authorization/Permissions.cs`): nested static classes; a new code must ALSO be appended to
  the `All` array. Per-company grants live in `sys.role_permissions (…, company_id)`; new companies clone
  from `sys.role_permission_templates`; grant tables JOIN `sys.permissions` by code string. 12 roles;
  bank-rec is an accountant task (grant set in D5).

### Startup-seed RLS footgun — the prod-42501 class (MANDATORY, cost a v1.15.0 rollback 2026-07-09)

Any startup SqlScript that READS or WRITES an RLS'd table runs with **NO `app.company_id` GUC set** and (in
prod) as a **NOBYPASSRLS** role. teas_test/dev connect as a Postgres SUPERUSER → RLS bypassed → this whole
class is INVISIBLE in tests (a green `RbacMatrixTests` proved nothing). Two canonical fixes, mirror EXACTLY:

- **Writing/reading a G3 system-global table** (`sys.permissions`, `sys.roles`, `sys.role_permissions`,
  `audit.activity_log`): put **`SET LOCAL app.bypass_rls = 'on';`** at the top of the script (transaction-
  scoped; each script runs in its own tx so it can't leak). This is EXACTLY 600's G3 stated purpose.
  **Template: `610_seed_year_close_perms.sql`** (the bank-rec perm seed 615 is a verbatim copy, swapping the
  codes + role set).
- **Writing a G1 tenant table** (`master.chart_of_accounts`, and any of our new `bank.*` tables): G1 has NO
  bypass arm by design. Pin the tenant per row via a **per-company `set_config('app.company_id',
  c.company_id::text, true)` loop** (Postgres reuses USING as WITH CHECK, so a bare INSERT fails 42501).
  **Template: `611_seed_retained_earnings_account.sql`.** *Bank-rec needs this ONLY if a stage seeds tenant
  rows (e.g. a demo bank account). B1 does NOT seed bank-account data — users create their own — so B1 has
  no G1 tenant-seed script and avoids this trap entirely. If a later demo seed is added, it MUST use the 611
  loop.*
- **NO startup seed may assume a tenant GUC exists.** **Deploy probe = ROW COUNTS, not exit codes** (a
  silently-RLS-filtered SELECT feeding an INSERT no-ops with exit 0). See the B1 verification gate.

### Other troubles-wiki entries folded

- **CSV newline (`StringBuilder.AppendLine` footgun, troubles-wiki L390-393):** `AppendLine` emits
  `Environment.NewLine` (`\r\n` Win / `\n` Linux) → RFC4180 consumers + cross-platform snapshot tests break.
  The recon-report CSV export MUST emit `"\r\n"` explicitly (`sb.Append(x).Append("\r\n")`), never
  `AppendLine`. FE export (`ap-aging` precedent) joins with `'\n'` + a `﻿` BOM — for a bank-rec export
  prefer `'\r\n'` for strict Excel/RFC4180.
- **teas_test superuser masks RLS** (memory `rls-masked-by-superuser-tests`): every RLS assertion runs under
  a NOBYPASSRLS role — `SET ROLE pg_database_owner` (portable trick used by `SalesChainRlsTests`), NOT the
  `teas_rls_test` role (which `[SKIP]`s when `CREATEROLE` unavailable). A green test that silently bypassed
  RLS proves nothing.
- **Migration ↔ teas_test fixture** (memory `migration-squash-teas-test-reset`): the fixture owns
  `__EFMigrationsHistory`; the new EF migration must apply cleanly to teas_test. If a stale schema blocks
  it, Fable resets teas_test (net10 Npgsql console trick). Flag at hand-off. `TEAS_TEST_PG` +
  `TEAS_REPO_ROOT` die between PowerShell calls — set BOTH in the SAME invocation as `dotnet test`; check
  skip-count vs baseline (a skipped test fakes green). Current string:
  `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`.
- **No literal `{`/`}` in a SqlScript** (even in comments): EF `ExecuteSqlRawAsync` treats them as
  `string.Format` placeholders → boot failure.
- **Thai ম glyph** (memory `thai-mo-glyph-pitfall`): grep `ম` (Bengali) before commit — creeps into Thai
  strings in seeds/DTOs/i18n.
- **`git add -u` misses new files** (memory): many new files here — explicitly `git add` them; grep `^??`
  for untracked source before commit.
- **Relative-date seeds/tests** (memory `relative-date-seed-temporal-tests`): never hardcode a past
  year/month in a matching/period test — the seed closes prev-month vs `CURRENT_DATE`; a fixed past DocDate
  fails `period.closed` on a fresh teas_test. Drive test dates from the injected `IClock` / relative to
  today (see §Tests date strategy).
- **Real bank samples are Ham's private data, UNTRACKED at repo root** (`STM_SA3269_…csv`,
  `STM_SA5476_…pdf`) — **they must NEVER be committed** (already gitignored — confirm before commit). Tests
  use SYNTHETIC/redacted fixtures modeled on the real structure (§Tests fixture policy). The real-line
  quotes below are STRUCTURE REFERENCE only.

---

## Design decisions (pinned — the implementer does NOT re-decide these)

- **D1. Three tables, no separate match/reconciliation table.** `bank.bank_accounts` (master),
  `bank.statement_imports` (one row per uploaded file), `bank.statement_lines` (one row per txn). Match
  state + links live as columns ON `statement_lines` (one-to-one matching — D4 — needs no join table). The
  reconciliation report is a COMPUTED query (no stored/locked reconciliation entity in v1). New schema =
  `bank`. All three are `ITenantOwned`.
- **D2. Format-agnostic adapter architecture.** `IBankStatementAdapter { string AdapterCode; bool
  CanHandle(string fileName, string mimeType); ParsedStatement Parse(Stream content, string? password); }`
  returning a NORMALIZED model the rest of the system consumes:
  ```csharp
  public enum StatementDirection { MoneyIn, MoneyOut }   // account-holder POV: MoneyIn = deposit/ฝากเงิน
  public sealed record ParsedStatementLine(int LineNo, DateOnly TxnDate, TimeOnly? TxnTime,
      DateOnly? ValueDate, StatementDirection Direction, decimal Amount /*>0*/, decimal RunningBalance,
      string Channel, string TxnType, string Description, string? RawRef);
  public sealed record ParsedStatement(string AdapterCode, string AccountNoRaw,
      DateOnly PeriodStart, DateOnly PeriodEnd, decimal OpeningBalance, decimal ClosingBalance,
      decimal? WithdrawalTotal, decimal? DepositTotal, int? WithdrawalCount, int? DepositCount,
      IReadOnlyList<ParsedStatementLine> Lines);
  ```
  Adapters register in a keyed collection; the import service picks by `CanHandle` (KBiz CSV: `.csv`;
  K-Plus PDF: `.pdf`). Adding SCB/BBL later = a new adapter class only — core, matching, report UNCHANGED.
- **D3. Direction semantics fixed to the account-holder's POV.** `MoneyIn` = deposit / ฝากเงิน / balance
  INCREASES / matches a **Receipt**. `MoneyOut` = withdrawal / ถอนเงิน / balance DECREASES / matches a
  **PaymentVoucher**. (Deliberately avoid "debit/credit" — a bank deposit is a credit on the bank's book
  but a DEBIT to the customer's GL cash asset; "MoneyIn/MoneyOut" is unambiguous.)
- **D4. Matching v1 = ONE-TO-ONE, EXACT amount, ±7-day window, POSTED docs only.** For each Unmatched line:
  candidates = POSTED, not-already-matched `Receipt` (MoneyIn) or `PaymentVoucher` (MoneyOut) in the SAME
  company where **`CashReceived`/`TotalPaid` == line.Amount EXACTLY** (to the satang — bank amounts are
  exact; a tolerance invites silent misreconciliation) AND **`DocDate` within ±7 days of `line.TxnDate`**
  (docs are often dated a few days before the bank clears). If `line.bank_account_id`'s candidates carry a
  populated `BankAccountId`, prefer those, but do NOT hard-filter on it (it is often null). Rank exact-date
  first, then nearest date. **User CONFIRMS** a suggestion (never auto-applied). Splits (one line ↔ many
  docs, batch deposits), fuzzy/tolerance amounts, and many-to-one are **explicit phase-2** — v1 leaves them
  Unmatched for a manual inline JE or Ignore. Keep it simple (Ponytail on solution; full scope preserved).
- **D5. New permission codes** (module `bank`), appended to `Permissions.cs` `Bank` static class + the `All`
  array, granted (615 seed) to `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT` + `ACCOUNTANT` (+ `SUPER_ADMIN`
  system-global):
  `bank.account.read`, `bank.account.manage`, `bank.statement.import`, `bank.reconcile`, `bank.report.read`.
- **D6. v1 reconciles at the single `1120` GL account (Scope-reality #1).** `bank_account.gl_cash_account_id`
  defaults to the `1120` "เงินฝากธนาคาร" account and the report ties the statement to THAT account's GL
  balance. **KNOWN LIMITATION (documented, not a bug):** because Receipt/PV posting always hits `1120`
  regardless of `BankAccountId`, a company with MULTIPLE bank accounts mapped to DISTINCT GL sub-accounts
  cannot GL-reconcile a non-`1120` account in v1 (its GL tie-out would show everything outstanding).
  Per-bank GL sub-accounts + posting-time bank selection = phase-2 (touches the money-critical posting path;
  out of scope here). The MATCHING screen still works cross-bank (it matches on the DOCUMENT amount/date,
  not the GL account). v1 officially supports full reconciliation for the primary `1120`-mapped account.
- **D7. Inline JE posts at the statement line's REAL date and RESPECTS period-close.** Add
  `PostManualEntryAsync(int companyId, int branchId, DateOnly docDate, string description, string? reference,
  IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines, ct)` to `GlPostingService` /
  `IGlPostingService` — a thin public wrapper over the existing private `BuildAndPostAsync` with
  `IsClosingEntry=false`, `ReversalOfId=null` (NOT `PostClosingEntryAsync`, whose closing semantics would
  wrongly hide bank interest from P&L). It does NOT call `EnsureOpenAsync` (consistent with every other
  poster). The RECON SERVICE calls `_period.EnsureOpenAsync(line.TxnDate, ct)` BEFORE posting (mirrors
  ReceiptService/PaymentVoucherService) → a line dated in a closed period is REJECTED (`period.closed`).
  Rationale for a new method over reusing `PostClosingEntryAsync(isClosingEntry:false)`: this is a money
  path; a clearly-named method beats overloading "closing" semantics (~10 lines, same private helper).
  BranchId: resolve the company's default/HQ branch exactly as `YearCloseService` does for its
  `PostClosingEntryAsync` call.
- **D8. Match lifecycle + the immutability rule.** `statement_lines.match_status ∈ {Unmatched, Matched,
  Posted, Ignored}`. **Matched** = confirmed link to a Receipt or PV (`matched_receipt_id` /
  `matched_payment_voucher_id` set) — NO GL effect → **unmatch is allowed** (clears the link, back to
  Unmatched). **Posted** = an inline JE was created (`posted_journal_id` set) — the JE is a real, IMMUTABLE
  GL entry (no void) → **unmatch is BLOCKED** with a clear message ("line posted JE #N; post a manual
  adjusting JE to correct"). **Ignored** = user excludes a non-reconciling row (opening-balance carry-forward,
  an internal transfer already recorded) — reversible. Default Unmatched.
- **D9. K-Plus PDF = POSITIONAL extraction, direction from balance delta, self-validating.** Plain-text
  extraction order is scrambled (the balance prints before the amount, cells interleave). Use PdfPig's
  per-word bounding boxes: cluster words into visual rows by Y, assign to columns by X-range (derived from
  the header-row cell positions), then per row read date/time/channel/type/balance/amount/details.
  **Direction is DERIVED: `MoneyIn` if `RunningBalance > prevBalance` else `MoneyOut`; Amount = |RunningBalance −
  prevBalance|.** The PARSED amount cell must equal that delta within 0.005 → this is a built-in integrity
  check (see D10). KBiz CSV gets direction directly (withdrawal col vs deposit col).
- **D10. Parse-integrity assertions (both adapters), fail LOUD.** After parsing: (a) each line's Amount ==
  |balanceDelta| within 0.005; (b) Σ MoneyOut == WithdrawalTotal and Σ MoneyIn == DepositTotal (when the
  metadata totals are present); (c) OpeningBalance + Σ(MoneyIn) − Σ(MoneyOut) == ClosingBalance. On any
  mismatch the import FAILS with a diagnostic listing offending LINE NUMBERS + amounts only (never raw
  descriptions/names → no PII leak) and persists NOTHING. This turns the fragile positional parse into a
  self-checking one.
- **D11. Raw file stored EXACTLY as uploaded, never decrypted-at-rest; password NEVER persisted.** The
  uploaded bytes (CSV plaintext, or the STILL-ENCRYPTED PDF) are stored via the Attachment infra
  (`statement_imports.attachment_id`) unchanged — the PDF stays password-protected at rest (we never keep
  the password, so this is encryption-at-rest for free). See §Security for the full password constraints.

---

## The two adapters (real sample lines = STRUCTURE REFERENCE; tests use SYNTHETIC fixtures — §Tests)

### KBiz (KBank) CSV adapter — `KBizCsvAdapter` (B2)

Encoding **UTF-8 with BOM**; use a proper RFC4180 reader (quoted fields may contain embedded commas AND
embedded NEWLINES — the address cell spans lines). **Do NOT split on `\n` naively.** No CsvHelper dependency:
write a ~40-line internal RFC4180 field tokenizer (quotes, `""` escape, embedded comma/newline). Dates are
**DD-MM-YY, CE 2-digit** (`26` → `2026`; parse `20YY`). Metadata period is **DD/MM/YYYY** CE. **Footgun: the
document ref suffix carries a BE year (`…/2569`); the TRANSACTION dates are CE (`26`=2026) — never derive the
year from the BE ref.** Amounts are quoted with thousand-commas (`"50,000.00"`).

Real metadata rows (structure ref — the ~11 rows before the header, label-matched not position-matched):
```
รายการเดินบัญชีเงินฝากออมทรัพย์ (มีรายละเอียด),,,,,,,,,,,,
,ชื่อบัญชี,"บจก. เรปทาวน์ เพ็ท โซลูชั่น
38 ซ.ลาซาล 19 ต.บางนาใต้ อ.บางนา จ.กทม. 10260",,,,,เลขที่อ้างอิง,,,,26070817120301934850,
,,,,,,,เลขที่บัญชีเงินฝาก,,,,232-1-13326-9,
,,,,,,,รอบระหว่างวันที่,,,,01/02/2026 - 07/07/2026,
,,,,,,,ยอดยกไป,,,,,"2,558.26"
,,,,,,,รวมถอนเงิน,,2,,รายการ,"107,442.41"
,,,,,,,รวมฝากเงิน,,4,,รายการ,"110,000.67"
```
→ parse by LABEL: `เลขที่บัญชีเงินฝาก`→AccountNo `232-1-13326-9`; `รอบระหว่างวันที่`→ period
`01/02/2026`-`07/07/2026`; `ยอดยกไป`→ClosingBalance `2,558.26`; `รวมถอนเงิน`→WithdrawalTotal `107,442.41`
(count 2); `รวมฝากเงิน`→DepositTotal `110,000.67` (count 4). OpeningBalance from the `ยอดยกมา` line below.

Real header + data rows (structure ref):
```
,วันที่,เวลา/ วันที่มีผล,รายการ,ถอนเงิน,,ฝากเงิน,,ยอดคงเหลือ,,ช่องทาง,,รายละเอียด
,01-02-26,,ยอดยกมา,,,,,0.00,,,,
,25-05-26,18:39,รับโอนเงิน,,,"50,000.00",,"50,000.00",,MAKE by KBank,,จาก X3360 นาย พงศ์สันต์ ฉัตร++
,26-05-26,12:22,โอนเงิน,"107,442.40",,,,"2,557.60",,สาขาเซ็นทรัล บางนา,,โอนไป SHANGHAI ... FOSUN ++
,19-06-26,23:59,รับดอกเบี้ยเงินฝาก,,,0.67,,"2,558.27",,โอนเข้า/หักบัญชีอัตโนมัติ,,รหัสอ้างอิง PCB09400
,19-06-26,23:59,ภาษีหัก ณ ที่จ่าย,0.01,,,,"2,558.26",,โอนเข้า/หักบัญชีอัตโนมัติ,,รหัสอ้างอิง PCB09400
```
Parser rules — locate the header row (the row containing `วันที่` AND `ยอดคงเหลือ`), build a header-label→
column-index map (tolerant of the empty spacer columns; in the sample: date=1, time=2, รายการ/type=3,
ถอนเงิน=4, ฝากเงิน=6, ยอดคงเหลือ=8, ช่องทาง=10, รายละเอียด=12). Per data row:
- `ยอดยกมา` type + no amount → OpeningBalance row (record OpeningBalance from ยอดคงเหลือ; NOT a txn line).
- ถอนเงิน non-empty → `MoneyOut`, Amount = that cell; ฝากเงิน non-empty → `MoneyIn`, Amount = that cell.
- RunningBalance = ยอดคงเหลือ; Channel = ช่องทาง; TxnType = รายการ; Description = รายละเอียด (truncated in
  the sample with `++` — that is just the bank's own display truncation, keep as-is). RawRef = parse a
  `รหัสอ้างอิง XXX` token from Description if present.
- **Interest (`รับดอกเบี้ยเงินฝาก`) and its WHT (`ภาษีหัก ณ ที่จ่าย`) are SEPARATE lines** (interest =
  MoneyIn, WHT = MoneyOut) — do NOT net them; each becomes its own statement_line (and its own inline JE).
  Note WHT may be ABSENT when interest ≤ 20 THB (see K-Plus sample).

### K-Plus (KBank mobile) PDF adapter — `KPlusPdfAdapter` (B3)

Password-protected (**user birthdate; supplied per-import, NEVER stored** — §Security). Open via
`PdfPig` `ParsingOptions { Password = pwd }`. 17-page sample; **EACH page repeats the FULL header block AND
starts with its own `ยอดยกมา` carry-forward row** — the parser must skip the repeated header on every page
and treat each page's `ยอดยกมา` as a non-txn balance anchor (re-anchors the running-balance for D9/D10, not a
line). Dates **DD-MM-YY CE** (same as CSV). **Columns: date | time/value-date | ONE combined ถอนเงิน/ฝากเงิน
amount | ช่องทาง | รายการ | ยอดคงเหลือ(บาท) | รายละเอียด** — the single amount column is why direction comes
from the balance delta (D9).

Real page-1 header (structure ref — repeated per page; label-matched):
```
ที่ DD.048 : N26070905074658984848I/2569
ชื่อบัญชีนาย พงศ์สันต์ ฉัตรแสงเจริญ            ← account name
100/399 ซ.กาญจนาภิเษก005 ต.หลักสอง อ.บางแค จ.กทม. 10160   ← address
สาขาเดอะมอลล์ บางแค / 751-2-31547-6 / 01/02/2026 - 08/07/2026   ← branch / AccountNo / period
รวมถอนเงิน 377 รายการ / รวมฝากเงิน 59 รายการ / ยอดยกไป 228,004.11   ← totals + closing
[column headers] วันที่ | เวลา/วันที่มีผล | ถอนเงิน / ฝากเงิน | ช่องทาง | รายการ | ยอดคงเหลือ (บาท) | รายละเอียด
```
Real transaction rows (structure ref — the fields per txn, positionally reconstructed):
```
opening:   01-02-26 | ยอดยกมา | balance 326.89                         (non-txn anchor)
withdraw:  01-02-26 13:25 | เครื่องรูดบัตร (EDC)/E-Commerce | ชำระด้วยบัตรเดบิต | bal 293.89 | amt 33.00 | รหัสอ้างอิง EDC09445
           → 326.89→293.89 = −33.00 ⇒ MoneyOut 33.00  ✓ (amt cell == |delta|)
deposit:   01-02-26 21:18 | K PLUS | รับโอนเงิน | bal 1,343.89 | amt 1,050.00 | จาก X3539 นาย ณัฐพงษ์ พันธา++
           → 293.89→1,343.89 = +1,050.00 ⇒ MoneyIn 1,050.00  ✓
interest:  19-06-26 23:59 | โอนเข้า/หักบัญชีอัตโนมัติ | รับดอกเบี้ยเงินฝาก | bal 1,628.48 | amt 3.04 | รหัสอ้างอิง PCB09400
           → MoneyIn 3.04; NO paired WHT line (interest ≤ 20 THB → bank withholds none) — a real quirk.
```
Notes: channel and description cells WRAP across 2 physical text lines (`เครื่องรูดบัตร (EDC)/` +
`E-Commerce`; long `เพื่อชำระ Ref …` details) — the row clusterer must join multi-line cells within the same
row band. TESTABILITY SEAM: split the adapter into (1) a thin `KPlusPdfTextExtractor` (PdfPig decrypt +
word-with-position extraction; smoke-tested) and (2) a PURE `KPlusPdfLineAssembler(words) → ParsedStatement`
(heavily unit-tested with synthetic positional word arrays — no encrypted PDF needed in CI).

---

## Matching + inline JE (B4)

**Suggest** (`GET …/matches/suggestions?importId=` or per-line): for each Unmatched line, run the D4 query;
return ranked candidate Receipts/PVs (id, docNo, docDate, amount, party name) — read-only, no writes.
**Confirm** (`POST …/lines/{lineId}/match` body `{ receiptId? , paymentVoucherId? }`): validate the doc is
POSTED, same company, amount==line.Amount, not already matched to another line; set the link +
`match_status=Matched`, `matched_at/by`. **Unmatch** (`POST …/lines/{lineId}/unmatch`): if `match_status ==
Posted` (JE-backed) → reject `bank.line_posted` with the JE no; else clear link → Unmatched (D8).
**Create inline JE** (`POST …/lines/{lineId}/journal` body `{ contraAccountId, description? }`): builds a
balanced 2-line JE — bank side = `bank_account.gl_cash_account_id`, amount = line.Amount, side from Direction
(MoneyIn → Dr bank / Cr contra; MoneyOut → Dr contra / Cr bank); contra side = user-picked CoA account (D7
templates just pre-select a likely account — bank charges 5xxx / interest income 4xxx / WHT-receivable
`1180` — the user always confirms; no new CoA seed required, but B4 confirms whether "bank charges"/
"interest income" accounts exist in `DefaultChartOfAccounts` and notes the codes). Service calls
`_period.EnsureOpenAsync(line.TxnDate)` then `_gl.PostManualEntryAsync(...)`; on success sets
`posted_journal_id`, `match_status=Posted`. **Ignore** (`POST …/lines/{lineId}/ignore` / `/unignore`).

## Reconciliation report math (B5)

Per bank account + period (date range, default = the import's period). Computed, not stored:
- **Statement closing balance** = `statement_imports.closing_balance` (== last line RunningBalance).
- **GL balance** of `bank_account.gl_cash_account_id` as-of period end (reuse the report/GL balance query —
  posted lines, `DocDate <= asOf`, Net Dr−Cr for an asset).
- **Reconciling items:** (a) Unmatched statement lines (on statement, not yet in GL) — should → 0 as lines
  are matched/JE'd/ignored; (b) Deposits-in-transit = POSTED Receipts hitting `1120` in-period with NO
  matched statement line; (c) Outstanding payments = POSTED PVs likewise unmatched.
- **Tie-out (FABLE CORRECTION 2026-07-09 — original pinned formula had deposits/outstanding
  SIGN-FLIPPED):** a deposit-in-transit is IN GL but NOT YET on the statement, so it makes the
  statement LOWER than GL (subtract it from GL to predict the statement); an outstanding payment
  is the reverse (add). Correct identity, derived from statement = Σmatched + Σunmatched lines
  and GL = Σmatched docs + deposits − outstanding:
  `expected statement = GL balance − deposits-in-transit + outstanding-payments + unmatched-lines-net`
  `difference = statement closing balance − expected statement` (0 when fully reconciled).
  Worked check: GL=200 (Receipt 100 cleared+matched, Receipt 100 in-transit), statement=50
  (the cleared 100 − unmatched bank fee 50): expected = 200−100+0+(−50) = 50 → difference 0 ✓
  (the original formula gave 250 → difference −200 while fully reconciled).
  Tests must include a REAL deposit-in-transit scenario asserting difference == 0 — a test that
  re-derives the same formula as the code is circular and proves nothing. Export CSV with
  explicit `"\r\n"` (folded footgun).

---

## Requirements (checklist) — grouped by stage

### B1 — Schema + bank-account master  ·  cap: ~14 files, no change to existing posting/reports

- [x] **B1.1 Entities** (`Domain/Entities/Bank/`), all `ITenantOwned`. Done 2026-07-09 —
  `BankAccount.cs`, `StatementImport.cs`, `StatementLine.cs` (field lists match verbatim) +
  `Domain/Enums/BankEnums.cs` (StatementDirection, ImportStatus, MatchStatus — combined one
  file, mirrors `AttachmentEnums.cs` multi-enum convention; needed by B1 since StatementLine
  references StatementDirection ahead of B2's adapter contract). Audit fields: literal spec
  field list used (no nearby master entity has a non-nullable `CreatedBy`, so followed the
  pinned type exactly).
  - `BankAccount`: `int BankAccountId; int CompanyId; string BankCode; string BankName; string AccountNo;
    string? AccountName; string? AccountType; long GlCashAccountId; string Currency /*="THB"*/; bool
    IsActive; DateTimeOffset CreatedAt; long CreatedBy;` (+ audit fields matching a nearby master entity).
  - `StatementImport`: `long StatementImportId; int CompanyId; int BankAccountId; string AdapterCode;
    string SourceFileName; long? AttachmentId; DateOnly PeriodStart; DateOnly PeriodEnd; decimal
    OpeningBalance; decimal ClosingBalance; int LineCount; decimal? WithdrawalTotal; decimal? DepositTotal;
    ImportStatus Status; DateTimeOffset ImportedAt; long ImportedBy;` (`enum ImportStatus {Parsed, Failed}`).
  - `StatementLine`: `long StatementLineId; int CompanyId; long StatementImportId; int BankAccountId;
    int LineNo; DateOnly TxnDate; TimeOnly? TxnTime; DateOnly? ValueDate; StatementDirection Direction;
    decimal Amount; decimal RunningBalance; string Channel; string TxnType; string Description; string?
    RawRef; MatchStatus MatchStatus; long? MatchedReceiptId; long? MatchedPaymentVoucherId; long?
    PostedJournalId; DateTimeOffset? MatchedAt; long? MatchedBy;` (`enum MatchStatus {Unmatched, Matched,
    Posted, Ignored}`).
- [x] **B1.2 EF configs** (`Persistence/Configurations/Bank/…`, `internal sealed`, auto-discovered):
  `ToTable("bank_accounts"/"statement_imports"/"statement_lines","bank")`; PKs; decimals `HasPrecision(19,4)`;
  string max-lengths; enums stored as string via `HasConversion<string>()` (mirror existing enum configs);
  unique `bank_accounts (CompanyId, AccountNo)`; index `statement_lines (StatementImportId, LineNo)` and
  `(CompanyId, BankAccountId, TxnDate)`. NO FK navigation to Receipt/PV/JournalEntry (store ids only — avoid
  cascade paths, mirror `FiscalYearCloseConfiguration`). Done 2026-07-09 —
  `Persistence/Configurations/Bank/BankReconciliationConfiguration.cs` (3 config classes combined
  in one file, mirrors `PaymentVoucherConfiguration.cs`'s parent+line grouping convention).
  Added an FK `BankAccount.GlCashAccountId → ChartOfAccount` (Restrict) — not explicitly
  forbidden (the "no FK nav" rule names Receipt/PV/JournalEntry only) and mirrors
  `BusinessUnitConfiguration.DefaultRevenueAccountId`'s identical CoA-reference pattern.
- [x] **B1.3 DbSets** on `AccountingDbContext` (`BankAccounts`, `StatementImports`, `StatementLines`).
  Done 2026-07-09.
- [x] **B1.4 DTOs + `IBankAccountService`** (`Application/Bank/`): CRUD DTOs; `gl_cash_account_id` defaults
  to the company's `1120` account id (resolve by code) when not supplied; reject a `gl_cash_account_id` that
  isn't an Asset account. Done 2026-07-09 — `BankAccountDtos.cs`.
- [x] **B1.5 `BankAccountService`** (`Infrastructure/Bank/`) + DI registration. Done 2026-07-09 —
  `Infrastructure/Bank/BankAccountService.cs`; registered in `DependencyInjection.cs`.
- [x] **B1.6 Endpoints** `Api/Endpoints/BankAccountEndpoints.cs` (group `/bank-accounts`): list/get
  (`bank.account.read`), create/update/deactivate (`bank.account.manage`). Done 2026-07-09 —
  mapped in `Program.cs` via `app.MapBankAccountEndpoints()`.
- [x] **B1.7 Perm constants** — `Permissions.cs` new `Bank` class with the 5 codes (D5) + append all 5 to
  `All`. Done 2026-07-09.
- [x] **B1.8 `SqlScripts/615_seed_bank_rec_perms.sql`** — VERBATIM copy of `610_seed_year_close_perms.sql`
  structure incl. **`SET LOCAL app.bypass_rls = 'on';` at the top** (writes G3 `sys.permissions` /
  `sys.role_permissions` / templates); insert-first/grant-second in the SAME file; grant to
  `COMPANY_ADMIN`+`CHIEF_ACCOUNTANT`+`ACCOUNTANT` (+SUPER_ADMIN); one INSERT per code (loop or 5 blocks). NO
  literal `{`/`}`. Done 2026-07-09 — used `p.permission_code LIKE 'bank.%'` joins instead of 5
  repeated blocks (same effect, avoids 5x code-string duplication risk); grant fan-out logic
  otherwise structurally identical to 610.
- [x] **B1.9 `SqlScripts/614_bank_reconciliation_rls.sql`** — enable+FORCE RLS + `company_isolation` (600
  **G1** plain policy, NO bypass arm) on all three `bank.*` tables. Assumes the EF migration created them
  (DbInitializer runs migrations before scripts). Mirror `612_fiscal_year_close_rls.sql` exactly, three
  tables. Done 2026-07-09 — three policies, byte-for-byte mirror of 612's USING clause.
- [x] **B1.10 EF migration — FABLE-OWNED (not the implementer).** After B1.1-B1.3 compile, Fable runs
  `dotnet ef migrations add BankReconciliation -p Accounting.Infrastructure -s Accounting.Api`, reviews the
  SQL, and coordinates a teas_test reset if the fixture blocks. Do NOT hand-edit the snapshot.
  Done 2026-07-09 — Fable generated + reviewed `20260708230046_BankReconciliation.cs` (bank
  schema, 3 tables, indexes per spec, clean `Down`). Stage-2 gates run clean, no teas_test
  reset needed (fixture applied the migration + 614/615 without issue). See evidence below.
- [x] **B1.11 FE** — `frontend/app/(dashboard)/bank-accounts/` list `page.tsx` (mirror
  `receipts/page.tsx`: PageHeader + PermissionGate(`bank.account.read`) + DataTable) + `new/page.tsx` /
  `[id]/page.tsx` create-edit form (GL-account selector from CoA). Nav item in `SidebarNav.tsx` SECTIONS
  (perm `bank.account.read`) + `nav` key in `messages/{th,en}.json`. TanStack Query hooks in
  `lib/queries.ts`, types in `lib/types.ts`. Done 2026-07-09 — `[id]/page.tsx` combines
  view+edit (no separate detail page — matches this checklist's literal 3-route ask, not
  Customers' 4-file detail+edit split); GL-account selector reuses the existing
  `useGlAccounts()` hook (`reports/general-ledger/accounts`) rather than adding a new
  endpoint. `next build` — all 3 routes compiled (`/bank-accounts`, `/bank-accounts/new`,
  `/bank-accounts/[id]`), 0 type errors.

### B2 — Statement import + KBiz CSV adapter  ·  cap: ~12 files

- [x] **B2.1 Adapter contract** `IBankStatementAdapter` + normalized records (D2) in `Application/Bank/`.
  Done 2026-07-09 — `Application/Bank/StatementAdapterContracts.cs` (interface + `ParsedStatementLine`
  + `ParsedStatement`, reusing the `StatementDirection` enum from B1's `Domain/Enums/BankEnums.cs`
  rather than redefining it). Also holds the shared D10 `BankStatementIntegrity.Validate` (both
  adapters need it — B3 will reuse verbatim).
- [x] **B2.2 Internal RFC4180 reader** (handles quoted commas/newlines/`""`) — no CsvHelper dependency.
  Done 2026-07-09 — `Infrastructure/Bank/Csv/Rfc4180Reader.cs`, ~50 lines, stdlib only.
- [x] **B2.3 `KBizCsvAdapter`** (`Infrastructure/Bank/Adapters/`) — the KBiz rules above; `AdapterCode
  "KBIZ_CSV"`, `CanHandle` on `.csv`. Done 2026-07-09. Verified against the REAL sample
  (`STM_SA3269_01FEB26_07JUL26.csv`, read for structure only, never committed) — two real-file
  refinements not spelled out in the abbreviated spec example: (1) metadata label→value column
  gap VARIES row to row in the real export (extra spacer before a "รายการ" unit-word on the
  count rows) — implemented as "last non-empty cell after the label = value, first non-empty =
  count" rather than a fixed offset; (2) the real file has a zero-amount "เปิดบัญชี"
  (account-opening notice) data row with BOTH ถอนเงิน/ฝากเงิน empty and unchanged balance — this
  is NOT a "ยอดยกมา" carry-forward row per the pinned rule, so it doesn't fit either branch;
  treated it the same as ยอดยกมา (skip, not a txn line, since it carries zero cash movement) —
  covered by a dedicated test. Both refinements verified against the real file's own numbers
  (D10 balance equation ties out exactly: 0.00 opening + 110,000.67 deposits − 107,442.41
  withdrawals = 2,558.26 closing, matching every metadata total).
- [x] **B2.4 Attachment enum additions** — `BankStatement` parent type + category, wired into
  `AttachmentCodes` (`ParentDb`/`CategoryDb`/`ParentFrom`/`CategoryFrom`). Done 2026-07-09.
  Also wired `AttachmentService.ParentExistsAsync`'s switch (necessary consequence of actually
  REUSING the Attachment infra per D11 — `StatementImportService` calls `IAttachmentService.
  UploadAsync` directly rather than hand-rolling storage+Attachment-row creation) and added
  `"text/csv"` to `LocalDiskFileStorage.FileStorageOptions.AllowedMimeTypes` (the default list
  had no CSV mime type; CSV uploads would otherwise 400 `attachment.bad_mime`).
- [x] **B2.5 `StatementImportService`** — upload → store raw bytes AS-IS via `IFileStorageService` /
  Attachment (D11) → pick adapter by `CanHandle` → parse → run D10 integrity assertions → persist
  `StatementImport` + `StatementLine`s in one tx; on assertion failure persist nothing, return the
  line-number diagnostic. Idempotency: warn (not block) if a prior import overlaps the same bank account +
  period. Done 2026-07-09 — `Infrastructure/Bank/StatementImportService.cs`. D10 validation
  (`BankStatementIntegrity.Validate`) runs BEFORE any DB/storage write, so a failure trivially
  persists nothing (no rollback logic needed for that path); StatementImport→Attachment→
  StatementLines are wrapped in one explicit `BeginTransactionAsync`/`CommitAsync` for true
  atomicity across the 3 SaveChanges calls (StatementImportId and AttachmentId are both
  DB-generated and each is needed as the other's FK). Idempotency overlap is returned as
  `StatementImportResult.OverlapWarning` (bool) — the caller/FE decides how to surface it (a
  toast), never a hard block. Registered `IStatementImportService` + `IBankStatementAdapter`
  (`KBizCsvAdapter`) in `DependencyInjection.cs` — future adapters (B3) just add one more
  `AddScoped<IBankStatementAdapter, X>()` line; DI collects them into the `IEnumerable<>` the
  service picks from via `CanHandle`.
- [x] **B2.6 Endpoints** `/bank-accounts/{id}/imports` (POST multipart, `bank.statement.import`; optional
  `password` form field reserved for B3) + list imports + get import lines. Done 2026-07-09 —
  `Api/Endpoints/StatementImportEndpoints.cs`, mapped in `Program.cs`. All three routes
  (POST/GET list/GET lines) gated by `bank.statement.import` (spec named it only for POST; no
  dedicated "statement read" permission exists among the 5 D5 codes, so list/lines reuse it —
  B4 introduces the `bank.reconcile`-gated matching-screen endpoints separately).
- [x] **B2.7 FE** import page — file input (`accept=".csv,.pdf"`) + DaisyUI `.modal-box`
  (mirror `AttachmentsSection.tsx`) posting FormData; result shows parsed line count / integrity errors.
  Done 2026-07-09 — `components/bank/StatementImportSection.tsx`, embedded into the existing
  `bank-accounts/[id]/page.tsx` (not a separate route — mirrors how `AttachmentsSection` is
  embedded into other document detail pages, not a standalone page). Password field appears
  only when the chosen file is `.pdf` (reserved for B3; the CSV adapter ignores it). Upload
  success toasts the parsed line count; an `overlapWarning` triggers a second `toast.warning`.
  `useStatementImports`/`useUploadStatement` hooks added to `lib/queries.ts`; types to
  `lib/types.ts`; i18n keys under `bank.import*` in both `en.json`/`th.json`.
- [x] **B2.8 Parser fixture tests** — SYNTHETIC redacted CSV embedded as a string (§Tests).
  Done 2026-07-09 — `Accounting.Api.Tests/Bank/KBizCsvAdapterTests.cs` (T1: 6 facts — metadata/
  dates/totals, line count, direction/amount/balance per line, interest+WHT as two separate
  lines, CE-year conversion, plus the D10-diagnostic-content fact for T2's "no PII leak" half)
  and `Accounting.Api.Tests/Bank/StatementImportServiceTests.cs` (T2's "persists nothing" half,
  DB-backed via `TestCompanyFactory` + `PostgresFixture` — asserts zero `StatementImport`/
  `StatementLine` rows survive a failed import, plus a happy-path persistence check). All 9 new
  tests green (see Verification below).

### B3 — K-Plus PDF adapter  ·  cap: ~6 files + 1 dependency

- [x] **B3.1 Add `UglyToad.PdfPig`** to `Accounting.Infrastructure.csproj` (pin a version; central package
  mgmt if the repo uses `Directory.Packages.props` — check). Justify in the PR: only PDF text-extraction lib;
  PDFsharp/QuestPDF are generation-only. Done 2026-07-09 — repo uses CPM
  (`backend/Directory.Packages.props`); added `PackageVersion Include="PdfPig" Version="0.1.15"` there +
  `PackageReference Include="PdfPig"` in the csproj. Confirmed via `dotnet package search PdfPig` that the
  NuGet package id is literally `PdfPig` (namespace `UglyToad.PdfPig`), 25.6M downloads, the correct/
  canonical library. `dotnet restore` succeeded. No other new dependency.
- [x] **B3.2 `KPlusPdfTextExtractor`** — PdfPig decrypt (`ParsingOptions { Password }`) + word-with-position
  extraction. On wrong/absent password throw `DomainException("bank.pdf_password", "…")` **with NO echo of
  the attempt** (§Security). Done 2026-07-09 — `Infrastructure/Bank/Pdf/KPlusPdfTextExtractor.cs`
  (+ `PositionedWord` record in the same file). Catches ANY exception from `PdfDocument.Open` (confirmed via
  a throwaway probe that PdfPig throws `UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException` for a wrong
  password — caught by the broad `catch`, never inspected/echoed) and throws a FRESH `DomainException` with a
  HARDCODED message; nothing is logged; `DomainException` has no `InnerException` slot at all so wrapping the
  caught exception isn't even possible. Verified against `DomainExceptionMiddleware.cs`: it surfaces
  `ex.Message` verbatim to the client on both `/api/v1` and BFF paths, and its generic catch-all branch
  ALSO leaks inner-exception chains in Development — confirming a hardcoded, caller-independent message is
  the only safe design regardless of environment.
- [x] **B3.3 `KPlusPdfLineAssembler`** (PURE, no IO) — Y-band row clustering + X-range column assignment +
  per-page header/carry-forward skip + direction-from-delta (D9) + multi-line cell join → `ParsedStatement`.
  Done 2026-07-09 — `Infrastructure/Bank/Pdf/KPlusPdfLineAssembler.cs`. Design grounded in real word
  positions (see real-PDF verification note below): column boundaries = midpoints between adjacent
  header-label CENTERS (word assigned to its own center-X); rows = rolling-anchor Y-clustering (~3.5pt,
  chains a multi-line same-row spread while cleanly separating ~10pt+ row/continuation gaps); a row-band
  with NO date-column word is a wrapped continuation of the PREVIOUS row (not its own line) — appended to
  that row's channel/detail accumulator. `Amount` is the PDF's own PRINTED amount-cell value (parsed
  independently, like the CSV adapter), NOT the delta — so D10's shared "parsed amount must equal the
  delta" check is a real cross-check, not vacuously true by construction; Direction comes from the delta's
  SIGN only. **Bug caught by the synthetic test fixture (would have hit the real file too):** the header
  label "รายการ" is AMBIGUOUS — it's also the literal unit-word in the metadata totals lines ("377
  รายการ"), which sit ABOVE the header on the page. "Take the topmost occurrence" (my first attempt)
  silently grabbed the metadata instance instead of the header's Type-column label, corrupting the Type
  column's position and causing every row to be misclassified. Fixed: use the unambiguous "วันที่" (date)
  header label as a Y-reference, then for every OTHER label pick whichever occurrence sits CLOSEST to that
  reference (not simply topmost). All T3 tests green after the fix.
- [x] **B3.4 `KPlusPdfAdapter`** wires 2+3; `AdapterCode "KPLUS_PDF"`, `CanHandle` on `.pdf`; threads the
  optional `password`. Done 2026-07-09 — `Infrastructure/Bank/Adapters/KPlusPdfAdapter.cs`; registered
  alongside `KBizCsvAdapter` in `DependencyInjection.cs` (DI collects both into the `IEnumerable<
  IBankStatementAdapter>` `StatementImportService` already picks from via `CanHandle` — zero changes needed
  to B2.5's service).
- [x] **B3.5 Import API/FE optional password** — the B2.6 `password` form field flows to the adapter,
  transiently; FE adds a password input on the import modal shown only for `.pdf`. Already satisfied by
  B2's own plumbing — `StatementImportEndpoints.cs`'s `password` form field and `StatementImportService.
  ImportAsync`'s `password` parameter were built in B2.6 anticipating B3; `KBizCsvAdapter` always ignored it,
  `KPlusPdfAdapter` now consumes it. `StatementImportSection.tsx`'s conditional password `<input>` (shown
  only when the chosen filename ends in `.pdf`) was also already built in B2.7. **No new files, no edits —
  confirmed via `git status --porcelain frontend/` returning empty for this stage.**
- [x] **B3.6 Tests** — assembler unit tests over synthetic positional fixtures + a decryption smoke test
  (generate a tiny password-protected PDF with PDFsharp in test setup; assert open-with-password works and
  wrong-password fails WITHOUT leaking the attempt) (§Tests). Done 2026-07-09 —
  `Accounting.Api.Tests/Bank/KPlusPdfLineAssemblerTests.cs` (T3, 7 facts: metadata/totals, per-page
  header-skip + ยอดยกมา re-anchor across 2 pages, direction-from-delta both ways, multi-line channel join,
  interest-with-no-WHT, D10 integrity pass, and a deliberate corruption asserting D10 fails loud with a
  line-numbered diagnostic) and `Accounting.Api.Tests/Bank/KPlusPdfTextExtractorTests.cs` (T4, 3 facts:
  correct-password opens; wrong password → `bank.pdf_password` with message/InnerException asserted to
  never contain either the wrong OR the correct password; missing password likewise). PDFsharp↔PdfPig
  round-trip verified working (PDFsharp `SecuritySettings.UserPassword` produces a PDF PdfPig opens cleanly
  with the right password and rejects with the wrong one).

**Real-PDF verification (structure only — no data, no password reproduced here; per real bank samples
policy).** Read `STM_SA5476_01FEB26_08JUL26.pdf` (17 pages, gitignored, never committed) with the
coordinator-supplied password, via a throwaway PdfPig probe script (scratchpad only, not part of the repo),
to ground D9's design in real word positions rather than guesswork:
- Confirmed EVERY page (checked 1, 2, 17) repeats the IDENTICAL header block at identical X/Y and starts its
  transaction section with its own ยอดยกมา anchor row (re-anchoring the running balance) — exactly D9/B3.3's
  design.
- Confirmed the header wraps across 2 physical Y-lines for two labels ("เวลา/"+"วันที่มีผล",
  "ยอดคงเหลือ"+"(บาท)") — handled by unioning both halves into one anchor.
- Derived and validated the 7-column left-to-right order against real transaction rows: date, time, type,
  amount, balance, channel, detail — confirming this does NOT match the spec prose's informal listing order
  (which reads channel before type) and validating D9's instruction to derive column positions
  PROGRAMMATICALLY from the header rather than hardcoding an assumed order.
- Confirmed real row spacing (~3.2pt within one transaction's word spread, ~10-12pt between distinct
  rows/continuations) validates the chosen row-clustering tolerance (3.5pt).
- Confirmed the multi-line channel-wrap real quirk exists exactly as spec describes (e.g. a channel phrase's
  continuation sits on its own row-band, roughly midway to the next transaction row, no header/date words).
- Found and worked around one soft edge: the "รายละเอียด" (detail) header label sits unusually far right of
  where its own data column actually starts in the real export (a real PDF-template quirk) — this ONLY
  affects the free-text Channel/Description fields cosmetically (a lead-in word may occasionally land in
  the wrong of these two neighboring free-text columns); it does NOT affect date/time/type/amount/balance/
  direction, which all classify with comfortable margins, and Channel/Description feed neither D10 nor B4's
  matching (D4 matches on amount+date only). Documented rather than silently ignored.
- Verified the PDFsharp-generated encrypted PDF (used for T4) is genuinely compatible with PdfPig's
  decryption path — not just superficially similar.

### B4 — Matching engine + inline JE  ·  cap: ~9 files; TOUCHES GlPostingService (money — Tier-2)

- [x] **B4.1 `PostManualEntryAsync`** on `GlPostingService`/`IGlPostingService` (D7) — thin wrapper over
  `BuildAndPostAsync`, `IsClosingEntry=false`. Do NOT touch `PostClosingEntryAsync` or any existing poster.
  Done 2026-07-09 — added ONLY the new method to both files; `PostClosingEntryAsync` and every
  existing poster (`PostTaxInvoiceAsync`/`PostReceiptAsync`/`PostPaymentVoucherAsync`/
  `PostVendorInvoiceAsync`/`PostTaxAdjustmentNoteAsync`/`PostPayrollRunAsync`) untouched.
- [x] **B4.2 `BankReconciliationService`** (`Infrastructure/Bank/`) + DI — suggest / confirm / unmatch /
  create-inline-JE / ignore (Matching section above); enforces D4, D8, and `EnsureOpenAsync` before posting.
  Done 2026-07-09 — `Infrastructure/Bank/BankReconciliationService.cs`. D4 ranking: a single
  ascending sort on `|DocDate - TxnDate|` days already places an exact-date match first (no
  separate two-tier sort needed) — candidate whose OWN `BankAccountId` matches the line's is
  ranked first via `OrderByDescending`, never hard-filtered. **Concurrency (coordinator watch
  item):** every transition (confirm/unmatch/journal/ignore/unignore) claims the row via a
  conditional `ExecuteUpdateAsync` keyed on the CURRENT `match_status`, mirroring
  `YearCloseService.ReopenAsync`'s double-reopen guard exactly (0 affected rows = a domain error,
  never a silent overwrite) — confirmed by a dedicated test (`Confirm_race_second_caller_...`).
  `CreateJournalAsync` additionally wraps the JE post + the claim in ONE DB transaction: if the
  claim loses the race after the JE insert, the WHOLE transaction rolls back (undoing the insert
  too), so no orphaned posted JE is ever committed — JE immutability is never actually at risk.
  **Noted residual gap** (not silently ignored, per the dispatch's own instruction): the "doc not
  already matched to a DIFFERENT line" pre-check in `ConfirmMatchAsync` is a read-then-write
  check, not itself claimed atomically — it closes the NAMED race (two confirms on the SAME
  line) but not the rarer "two different lines racing to claim the same doc" case, which would
  need a DB partial-unique constraint (a migration, out of this stage's blast radius).
  **CoA check (per B4.2's own instruction):** confirmed `DefaultChartOfAccounts`
  (`MasterDataServices.cs`) has NO dedicated "interest income" (4xxx) or "bank charges" (5xxx)
  account — only generic `4000` (Sales Revenue) and the 5100-5410 expense range. No new seed
  added (not asked); the contra-account selector is a plain full-CoA picker with no smart
  default, since there is no dedicated code to default to.
- [x] **B4.3 Endpoints** under `/bank-accounts/{id}/…` or `/imports/{id}/…` (perm `bank.reconcile` for
  confirm/unmatch/journal/ignore; suggestions readable with `bank.reconcile`). Done 2026-07-09 —
  `Api/Endpoints/BankReconciliationEndpoints.cs`, routes nested under
  `/bank-accounts/{bankAccountId}/lines/{lineId}/{suggestions|match|unmatch|journal|ignore|unignore}`,
  ALL gated by `bank.reconcile` (spec's own wording: "suggestions readable with bank.reconcile").
- [x] **B4.4 FE matching screen** — statement lines table with per-line suggestion + confirm; inline-JE
  DaisyUI `.modal-box` with CoA contra-account selector; unmatch/ignore actions; JE-backed lines show the JE
  no and disable unmatch. Done 2026-07-09 — new route
  `bank-accounts/[id]/imports/[importId]/page.tsx`; `StatementImportSection.tsx`'s filename now
  links to it. Two modals (suggest+confirm; create-JE with the CoA selector reusing
  `useGlAccounts()`). A Posted line shows `t('statusPosted')` and its action column has NO
  unmatch button at all (matches D8 — not merely disabled).
- [x] **B4.5 Matching-engine + inline-JE tests** (§Tests). Done 2026-07-09 —
  `Accounting.Api.Tests/Bank/BankReconciliationServiceTests.cs` (T5 ×5, T6 ×2 incl. the P&L
  inclusion assertion, T7 ×2, + the dedicated concurrent-confirm-race test) and
  `Accounting.Api.Tests/Bank/BankReconciliationRlsTests.cs` (T8, `[SkippableTheory]` × 3 tables,
  mirrors `SalesChainRlsTests` under `SET ROLE pg_database_owner`). T9 (RBAC) verified via the
  EXISTING generic `RbacAuthMapTests`/`RbacMatrixTests` (no new file needed — they already
  iterate every registered permission code, including the 5 `bank.*` ones seeded in B1); see
  Verification below for the run.

### B5 — Reconciliation report + FE polish  ·  cap: ~6 files

- [x] **B5.1 `BankReconciliationReportService`** — the tie-out math above; DTO with statement balance, GL
  balance, categorized reconciling items, difference. Done 2026-07-09 —
  `Application/Bank/BankReconciliationReportDtos.cs` + `Infrastructure/Bank/
  BankReconciliationReportService.cs`. GL balance query mirrors `FinancialReportService.
  TrialBalanceAsync`'s exact shape (Posted journals, `DocDate <= to`, Dr−Cr on the bank's
  `gl_cash_account_id`). Deposits-in-transit/outstanding-payments matching is company-wide
  (not scoped to the reported bank account) — consistent with D6's single-1120-account model
  and how B4's own `SuggestAsync`/`ConfirmMatchAsync` already check "matched to any line".
  Tie-out formula implemented as pinned; unmatched lines carry their own signed amount
  (MoneyIn positive, MoneyOut negative).
  **CORRECTED 2026-07-09 (Fable's final diff review, BLOCKING):** the ORIGINAL pinned formula
  (`GL + deposits − outstanding + unmatchedNet == statement`) was itself sign-flipped on
  deposits-in-transit/outstanding-payments — the "not textbook convention" note originally
  logged here was WRONG to dismiss; the spec was the bug, and the first T10 pass was circular
  (self-consistent with the buggy formula, not verified against reality). Corrected formula:
  `GL − deposits + outstanding + unmatchedNet == statement`. Rationale: a deposit-in-transit is
  ALREADY in GL (the Receipt posted) but NOT yet on the statement → statement is LOWER than GL
  by that amount, so deposits SUBTRACT from GL to reach statement; an outstanding payment is the
  mirror (already in GL, decreasing it; not yet cleared on the statement → statement is HIGHER
  than GL by that amount, so outstanding ADDS back). `specs/bank-reconciliation.md`
  §Reconciliation report math (B5) now carries the corrected formula + this derivation +
  a worked example. T10 rewritten non-circularly — see B5.4.
- [x] **B5.2 Endpoint** `/bank-accounts/{id}/reconciliation?from=&to=` (`bank.report.read`). Done
  2026-07-09 — added to the existing `BankReconciliationEndpoints.cs` (a second, differently-
  shaped route group in the same file rather than a new endpoints file, to stay under the file
  cap) + registered in `DependencyInjection.cs`.
- [x] **B5.3 FE report page** (mirror `reports/ap-aging/page.tsx`: manual `table table-zebra` + filters) +
  CSV export using explicit `"\r\n"` (folded footgun) + `﻿` BOM. Done 2026-07-09 —
  `reports/bank-reconciliation/page.tsx`. Bank-account selector (reuses `useBankAccounts()`
  from B1) + from/to date filters (default: current month start → today, Bangkok-local, same
  `Intl.DateTimeFormat` trick as ap-aging). CSV export deliberately joins rows with `'\r\n'`
  (NOT ap-aging's own `'\n'`) + a `﻿` BOM prefix — the spec's own folded footgun explicitly asks
  for the stricter CRLF here, so this is an intentional, noted deviation from the ap-aging
  precedent it otherwise mirrors. Nav item added under Reports (`bank.report.read`-gated, same
  convention as every other report nav entry).
- [x] **B5.4 Report tie-out tests** (§Tests). Done 2026-07-09; **REWRITTEN 2026-07-09** after the
  B5.1 formula correction — the original 2 facts were circular (see B5.1). New:
  `Accounting.Api.Tests/Bank/BankReconciliationReportServiceTests.cs` (T10, 5 facts, each worked
  out from first principles BEFORE writing the assertion, independent of the implementation):
  (1) `FullyReconciled_...` — one line matched (via `ConfirmMatchAsync`) + a directly-seeded
  Posted JE; `Difference == 0`. (2) `DepositInTransit_...` — Receipt A posted+matched (on
  statement) + Receipt B posted but with NO statement line at all (in GL, not on statement);
  hand-derived GL=700, statement=500, `Difference == 0`. (3) `OutstandingPayment_...` — mirror
  of (2) for PaymentVoucher/MoneyOut; hand-derived GL=−400, statement=−300, `Difference == 0`.
  (4) `UnmatchedBankFeeLine_...` — a MoneyOut line ON the statement, NEVER posted to GL at all;
  hand-derived GL=0, unmatchedNet=−20, statement=−20, `Difference == 0`, plus an explicit
  assertion `UnmatchedLinesNet` is negative. (5) `Statement_balance_altered_...` — the
  fully-reconciled scenario with the statement's OWN closing balance seeded 50 too high;
  asserts `Difference == +50` (exact value) and `.BePositive()` (sign). All 5 passed on the
  FIRST run after the formula fix — the by-hand derivation caught what the circular version
  couldn't. `SeedPostedJeAsync` gained a `bankDebit` parameter to seed either a deposit-shaped
  or a payment-shaped JE against the bank's `gl_cash_account_id`.

---

## Security — PDF password (explicit constraints, MANDATORY)

- The import password is received transiently (multipart form field), passed straight to the adapter, used
  ONLY to open the PDF in memory, and then dropped. It is **NEVER** written to any table/column, log,
  structured-logging property, telemetry span, metric tag, or HTTP response.
- A wrong/absent-password failure returns a GENERIC message (`"Could not open the statement PDF — check the
  password."`) and MUST NOT echo, hash, length-reveal, or otherwise reflect the attempt — not in the
  exception `Message`, not in `Data`, not in any wrapped inner exception surfaced to the client or logs.
- Do NOT hold the password in a field on any entity/DTO that gets serialized or persisted. Best-effort:
  scope it to the narrowest local variable; do not add it to any request record that is logged.
- The raw PDF is stored EXACTLY as uploaded (still encrypted) — never a decrypted copy (D11).
- Real bank samples are private + untracked — never committed; tests never embed a real password.

## Tests (integration + unit; enumerate — implementer writes all)

**Fixture policy:** NO real bank data in the repo. CSV tests embed a SYNTHETIC, redacted CSV string modeled
on the real structure (fake account `999-9-99999-9`, fake names, but the SAME row/column/metadata shape incl.
the multi-line quoted address, opening `ยอดยกมา`, a MoneyIn, a MoneyOut, interest, and its WHT). PDF tests:
(a) `KPlusPdfLineAssembler` unit tests feed SYNTHETIC positional word arrays (hand-built) covering the
per-page header skip, carry-forward row, MoneyIn/MoneyOut via balance delta, multi-line cell join, and an
interest line with NO WHT; (b) a decryption smoke test GENERATES a tiny password-protected PDF with PDFsharp
at setup. **Date strategy:** drive all statement/doc dates from the injected `IClock` relative to today (a
fixed past month fails `period.closed` on a fresh teas_test — folded footgun); seed matchable Receipts/PVs
directly via DbContext with controlled DocDates (not via the manual POST paths).

- [x] **T1 KBiz CSV parser** — synthetic CSV → assert metadata (AccountNo/period/opening/closing/totals),
  line count, each line's Direction/Amount/RunningBalance/TxnType, interest+WHT as two lines, the quoted
  multi-line address is consumed without corrupting row alignment, DD-MM-YY→CE year.
  `Accounting.Api.Tests/Bank/KBizCsvAdapterTests.cs` (6 facts: metadata/dates/totals, per-line
  direction/amount/balance, interest+WHT as two separate lines, CE-year conversion, skip of
  ยอดยกมา/zero-movement rows).
- [x] **T2 CSV integrity failure** — a synthetic CSV whose amount contradicts the balance delta → import
  FAILS with a line-number diagnostic, persists nothing, leaks no description text.
  `KBizCsvAdapterTests.Validate_throws_with_line_number_and_amount_only_never_description_text`
  (diagnostic-content fact) + `StatementImportServiceTests.ImportAsync_integrity_failure_persists_nothing`
  (DB-level: zero StatementImport/StatementLine rows survive).
- [x] **T3 KPlus assembler** — synthetic word arrays: per-page header/carry-forward skipped; direction from
  delta correct for MoneyIn & MoneyOut; multi-line channel/description joined; interest-without-WHT handled;
  balance-delta integrity holds.
  `Accounting.Api.Tests/Bank/KPlusPdfLineAssemblerTests.cs` (7 facts, incl. the D10 integrity
  pass/fail round-trip and the real-file-discovered "รายการ" header-ambiguity fix).
- [x] **T4 KPlus password** — generated encrypted PDF opens with the right password; wrong password →
  `bank.pdf_password`; **assert the exception message + any log output do NOT contain the attempted
  password** (capture logs).
  `Accounting.Api.Tests/Bank/KPlusPdfTextExtractorTests.cs` (3 facts: correct password opens;
  wrong password throws `bank.pdf_password` with the message asserted to contain neither the
  wrong NOR the correct password; missing password likewise).
- [x] **T5 Matching suggest/confirm** — seed a POSTED Receipt (CashReceived == a MoneyIn line, DocDate within
  ±7d) and a POSTED PV (TotalPaid == a MoneyOut line) → suggestions return them; confirm sets the link +
  Matched; a doc already matched to another line is not re-suggested; amount off by 0.01 → NOT suggested;
  DocDate 8 days away → NOT suggested (window boundary).
  `Accounting.Api.Tests/Bank/BankReconciliationServiceTests.cs` (5 facts covering every sub-case
  literally, plus a dedicated concurrent-confirm-race fact and the Opus-fix header-contra-account
  rejection fact).
- [x] **T6 Inline JE respects period-close** — create inline JE for a line dated in an OPEN period → posts a
  balanced JE at `line.TxnDate` via `PostManualEntryAsync`, `IsClosingEntry=false`, `posted_journal_id` set,
  `match_status=Posted`; a line dated in a CLOSED period → rejected `period.closed`, no JE. Assert the
  interest inline JE APPEARS in `ProfitLossAsync` (proves it is NOT flagged closing).
  `BankReconciliationServiceTests.cs`: `CreateJournal_in_an_open_period_...` (asserts
  `IsClosingEntry=false`, `posted_journal_id`, `match_status=Posted`, AND the P&L revenue delta)
  + `CreateJournal_in_a_closed_period_is_rejected_and_posts_nothing`.
- [x] **T7 Unmatch rules** — Matched (link) line unmatches cleanly back to Unmatched; Posted (JE-backed) line
  → unmatch REJECTED with the JE no; the JE remains.
  `BankReconciliationServiceTests.cs`: `Unmatch_a_Matched_line_returns_cleanly_to_Unmatched` +
  `Unmatch_a_Posted_JE_backed_line_is_rejected_and_the_JE_remains`.
- [x] **T8 RLS / tenant isolation** — two companies each import a statement; company A cannot read company
  B's `bank_accounts` / `statement_imports` / `statement_lines`. Assert under **`SET ROLE
  pg_database_owner`** (teas_test superuser masks RLS — folded); GRANT SELECT on the three `bank.*` tables to
  the role, pin `app.company_id`, assert own-visible / foreign-hidden (mirror `SalesChainRlsTests`).
  `Accounting.Api.Tests/Bank/BankReconciliationRlsTests.cs` — `[SkippableTheory]` × 3 tables,
  byte-for-byte mirrors `SalesChainRlsTests`' two-directional own-visible/foreign-hidden pattern.
- [x] **T9 RBAC** — `RbacAuthMapTests` + `RbacMatrixTests` green with the 5 new `bank.*` codes registered +
  granted to ≥1 non-super role (satisfies the super-only invariant); needs `TEAS_REPO_ROOT` set same shell.
  A no-perm user → 403 on manage/import/reconcile/report. Verified via the EXISTING generic
  `RbacAuthMapTests`/`RbacMatrixTests` (41/41 green) — no new test file needed, they auto-discover
  every registered `Permissions.All` code including the 5 `bank.*` ones seeded in B1's 615 script;
  the no-perm-user→403 behavior is likewise covered generically by `RbacCartesianTests` (every
  role × every Perm-gated endpoint), per the same precedent `YearEndClosingTests` itself relies on.
- [x] **T10 Report tie-out** — after matching all lines, difference == 0; with one deposit-in-transit
  (POSTED Receipt, no statement line) the report lists it and the difference equals it; CSV export uses CRLF.
  `Accounting.Api.Tests/Bank/BankReconciliationReportServiceTests.cs` — 6 facts (rewritten
  non-circularly after the formula correction, plus the cross-period fix): fully-reconciled,
  deposit-in-transit, outstanding-payment, unmatched-bank-fee-line, a cross-period item dated
  before `from`, and one non-zero/sign case. CSV CRLF is a FE-only concern (client-side
  `Blob`/`\r\n` join in `reports/bank-reconciliation/page.tsx`) — not backend-testable; verified
  by code inspection (`csv = rows...join('\r\n')`, BOM-prefixed `Blob`), consistent with how the
  repo's other CSV-export pages (ap-aging) have no backend CRLF test either.
- [x] **T11 Startup-seed prod-parity (615/614)** — assert (under a NOBYPASSRLS role, or by inspecting
  `sys.role_permissions` row COUNTS after init) that 615 actually granted the `bank.*` codes to real
  companies' roles (not zero rows) — guards the 42501/silent-no-op class the superuser test masks.
  **No automated test is feasible for this class** (teas_test's superuser connection bypasses RLS
  entirely, masking exactly the 42501/silent-no-op failure mode T11 exists to catch — the same
  reason `troubles-wiki.md`'s whole "Startup SqlScript ... prod-parity" entry exists). Verified
  MANUALLY instead, at B1 stage-2 (Fable, 2026-07-09): a direct `psql`-equivalent probe against
  `teas_test` after `DbInitializer` ran confirmed `sys.role_permissions` has 177,722 `bank.*`
  grants with `company_id IS NOT NULL` (>0 — proves 615's bypass fan-out wrote real per-company
  grants, not a silent no-op). Ongoing coverage is the deploy-time row-count probe named in
  §Verification gates below, not a unit/integration test — annotated here rather than left `[ ]`.

## Verification gates (per stage — worker runs before reporting; Fable runs the consolidated gate)

- `cd backend && dotnet build` → 0 errors, 0 warnings (every stage).
- EF migration applies cleanly to teas_test (B1; Fable-verified via the fixture's `MigrateAsync`).
- Stage tests green, 0 skipped (compare skip-count to baseline — a `[SKIP]` fakes green):
  `$env:TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"; $env:TEAS_REPO_ROOT="Y:\ClaudePlayground\TEAS-Project"; dotnet test --filter "FullyQualifiedName~Bank"`
  and the `~Rbac` filter for T9.
- **B1 deploy/prod-parity probe (row counts, not exit code):** after init, `SELECT count(*) FROM
  sys.role_permissions rp JOIN sys.permissions p ON p.permission_id=rp.permission_id WHERE p.permission_code
  LIKE 'bank.%' AND rp.company_id IS NOT NULL;` must be > 0 (proves 615's G3 bypass worked; a silent RLS
  no-op returns 0 with exit 0).
- Regression: `dotnet test --filter "FullyQualifiedName~Reports|~Receipt|~PaymentVoucher|~Journal|~YearEndClosing"`
  green — proves B4's `PostManualEntryAsync` + no posting change didn't regress money/report math.
- `grep -rn "ম" backend/ frontend/ --include=*.cs --include=*.sql --include=*.ts --include=*.tsx` → empty.
- Confirm the two real samples are NOT staged: `git status --porcelain | grep -i "STM_SA"` → empty (they
  stay gitignored); grep `^??` for untracked new SOURCE files and `git add` them explicitly.

## Blast-radius cap

- Per-stage caps noted inline (B1 ~14, B2 ~12, B3 ~6+1 dep, B4 ~9, B5 ~6). **Hitting a cap = stop and
  re-spec, never silent overrun.**
- **Public-API / money-path changes ALLOWED only where named:** the 5 `bank.*` perms; the `bank`-schema
  tables; ONE new `IGlPostingService.PostManualEntryAsync`; ONE new `AttachmentParentType`/`Category`; ONE
  new dependency (PdfPig). **FORBIDDEN:** any change to existing GL posters, `JournalService`, period-close,
  report/CIT/tax queries, the `600`/`612` policies, or `Receipt`/`PaymentVoucher` posting logic. `1120`
  single-account reconciliation is the accepted v1 boundary (D6) — do NOT "fix" posting to use
  `BankAccountId` in this cycle.

## Attempt log
<!-- - <date> <worker>: <result / failure summary> -->
- 2026-07-09 opus-designer: spec authored from PLAN §1 + Ham K-Plus addition. Parsed BOTH real samples
  (CSV via Read; PDF via throwaway pymupdf, pwd verified). Backend/FE facts gathered via Explore agents;
  RLS seed patterns read from 600/610/611. No code written.
- 2026-07-09 sonnet-implementer (Stage B1): completed B1.1-B1.9 + B1.11; B1.10 (EF migration)
  left for Fable per spec. Backend `dotnet build` 0 errors/0 warnings; frontend `npx tsc --noEmit`
  clean and `npm run build` succeeded with all 3 new routes. No `ম` glyph; real STM_SA* samples
  confirmed NOT staged (still gitignored, still on disk).
  File-count note re: the "~14 files" B1 cap — 13 NEW files (10 backend + 3 FE) were created;
  9 EXISTING shared-infra files were edited (DbContext, Permissions.cs, DependencyInjection.cs,
  Program.cs, SidebarNav.tsx, en.json, th.json, queries.ts, types.ts) for wiring/registration
  only (DbSet additions, DI registration, route mapping, i18n keys, nav item, query hooks) — no
  new abstractions, no unrelated logic touched. Interpreted the cap as counting the feature's
  own new-file footprint (13 ≤ ~14); flagging the interpretation explicitly since B1.11's FE
  scope (3 routes + shared-infra wiring) makes a strict all-files-touched count land around 22.
  Did not hit a genuine STOP condition — no unplanned files were needed beyond what's listed
  above.
- 2026-07-09 sonnet-implementer (Stage B1, stage-2 gates): Fable generated + reviewed
  `20260708230046_BankReconciliation.cs`. Ran the 3 requested gates in the SAME shell/session:
  (1) `dotnet build` full solution — 0 errors, 0 warnings, migration compiles clean.
  (2) Full backend suite with `TEAS_TEST_PG`+`TEAS_REPO_ROOT` set in one PowerShell invocation —
  `Accounting.Domain.Tests`: 147/0/0; `Accounting.Api.Tests`: 696/0/8 skipped (same 8 as
  baseline — Sps110/VatReg/Pnd50 visual-emit + 4× TaxFormFillDiagnostic + the
  `teas_rls_test`-unavailable RLS skip, all pre-existing, none new). **Total 843 passed / 0
  failed / 8 skipped — exact baseline match.** Fixture applied the new migration + scripts
  614/615 to `teas_test` with NO reset needed (not stale).
  (3) DB sanity probe (psql unavailable in this shell; used a .NET 10 file-based C# app +
  Npgsql against the same `TEAS_TEST_PG` connection instead — same effect):
  `bank.bank_accounts`/`statement_imports`/`statement_lines` all exist; all three have
  `relrowsecurity=true` AND `relforcerowsecurity=true` (bank_accounts confirmed explicitly,
  the other two confirmed via `relrowsecurity`); `sys.permissions` has exactly the 5 `bank.*`
  codes (`bank.account.manage`, `bank.account.read`, `bank.reconcile`, `bank.report.read`,
  `bank.statement.import`); `sys.role_permissions` has **177,722** `bank.*` grants with
  `company_id IS NOT NULL` (>0 — proves 615's `SET LOCAL app.bypass_rls` fan-out actually
  wrote real per-company grants, not a silent RLS no-op; the large count reflects teas_test's
  known company bloat, ~629+ companies per prior memory notes, not a bug); `__ef_migrations`
  contains `BankReconciliation`; `sys.applied_sql_scripts` contains both `614_bank_reconciliation_rls.sql`
  and `615_seed_bank_rec_perms.sql`. No STOP condition hit. B1.10 checkbox flipped to `[x]`.
- 2026-07-09 sonnet-implementer (Stage B2): completed B2.1-B2.8 in full (B1 already committed;
  no migration this stage, per the coordinator — B2 adds no new columns/tables, only new C#
  types + 2 new enum members stored as existing VARCHAR(30) string columns). Read the real
  `STM_SA3269_01FEB26_07JUL26.csv` for structure only (never committed — confirmed below); two
  real-file-driven adapter refinements beyond the abbreviated spec example are logged inline on
  B2.3 above (label→value gap variance; the "เปิดบัญชี" zero-movement skip case).
  Gates (all run personally, in-session):
  (1) `dotnet build` full solution — 0 errors, 0 warnings (twice — once after backend-only
  changes, once after the full stage including tests).
  (2) `dotnet test --filter "FullyQualifiedName~Bank"` — 9/9 new tests green before running the
  full suite (fast feedback loop).
  (3) Full backend suite, `TEAS_TEST_PG`+`TEAS_REPO_ROOT` set in one PowerShell invocation —
  `Accounting.Domain.Tests`: 147/0/0; `Accounting.Api.Tests`: 705/0/8 skipped (identical 8 to
  the B1 baseline — no new skips). **Total 852 passed / 0 failed / 8 skipped = baseline 843 + 9
  new B2 tests, exactly.**
  (4) Frontend: `npx tsc --noEmit` clean; `npm run build` succeeded (all `/bank-accounts/*`
  routes compiled, including the new `StatementImportSection` embedded in `[id]/page.tsx`).
  (5) `grep -rn "ম"` → empty. `git status --porcelain | grep -i STM_SA` → empty (real samples
  stay gitignored, untouched on disk).
  File-count note (same methodology as B1's entry) — 9 NEW files (6 backend source + 2 backend
  tests + 1 FE component), well under the "~12" cap counting new files. 11 EXISTING files got
  wiring/registration edits (Program.cs, AttachmentCodes.cs, AttachmentEnums.cs,
  AttachmentService.cs, DependencyInjection.cs, LocalDiskFileStorage.cs, bank-accounts/[id]/
  page.tsx, queries.ts, types.ts, en.json, th.json) — no new abstractions, each edit is a
  direct, necessary consequence of a checklist item (mostly reusing/extending the existing
  Attachment infra per D11, as the spec directed). `docs/rbac/endpoint-permission-map.generated.md`
  also changed as an automatic side effect of the test run (a doc-gen step regenerates it from
  the live permission set) — not a manual edit, not counted.
  Did not hit a genuine STOP condition.
- 2026-07-09 sonnet-implementer (Stage B3): completed B3.1-B3.6 in full. New dependency: PdfPig
  0.1.15 (package id `PdfPig`, namespace `UglyToad.PdfPig`) — added to `Directory.Packages.props`
  (CPM) + `Accounting.Infrastructure.csproj`; `dotnet restore` confirmed resolution.
  Real-PDF verification done via throwaway PdfPig probe scripts (scratchpad only, never
  committed) against `STM_SA5476_01FEB26_08JUL26.pdf` (17 pages, coordinator-supplied password) —
  full findings logged inline on B3.3 and in the "Real-PDF verification" note above (structure
  only, no data reproduced). This surfaced and fixed a REAL bug before it could hit the actual
  file: the header label "รายการ" is ambiguous with the metadata totals lines' own "N รายการ"
  unit-word; "topmost occurrence" picked the wrong one. Fixed via an unambiguous "วันที่"
  Y-reference + nearest-match per label.
  Gates (all run personally, in-session):
  (1) `dotnet build` — 0 errors, 0 warnings (checked after production code, again after tests).
  (2) `dotnet test --filter "FullyQualifiedName~KPlusPdfLineAssemblerTests"` — 0/7 passed on
  first run (the รายการ bug); fixed; 7/7 green. Then `--filter "FullyQualifiedName~Bank"` (no
  env vars) — 17 passed, 2 skipped (StatementImportServiceTests correctly skip without
  TEAS_TEST_PG — expected, not a new-skip regression).
  (3) Full backend suite, `TEAS_TEST_PG`+`TEAS_REPO_ROOT` set in one PowerShell invocation —
  `Accounting.Domain.Tests`: 147/0/0; `Accounting.Api.Tests`: 715/0/8 skipped (identical 8 to
  baseline). **Total 862 passed / 0 failed / 8 skipped = baseline 852 + 10 new B3 tests
  (7 KPlusPdfLineAssemblerTests + 3 KPlusPdfTextExtractorTests), exactly.**
  (4) Frontend: confirmed untouched this stage (`git status --porcelain frontend/` empty) — B3.5
  was already fully satisfied by B2's plumbing, so no FE gate was needed per the coordinator's
  own conditional ("FE tsc+build if you touch the modal").
  File-count: 5 new files (3 backend source + 2 backend tests), well under "~6 files"; 3 existing
  files edited for wiring (`Directory.Packages.props`, `Accounting.Infrastructure.csproj`,
  `DependencyInjection.cs`) + 1 new dependency (PdfPig), exactly as capped. No STOP condition hit.
  A worker-level `PROGRESS-bank-reconciliation-b3.md` checkpoint was written mid-stage (quota
  guard, ~87%) documenting the design/real-PDF findings in case of a session cutoff — superseded
  by this attempt-log entry now that the stage is complete; left on disk, harmless.
- 2026-07-09 sonnet-implementer (Stage B4 — MONEY PATH, precision-over-speed per the
  coordinator): completed B4.1-B4.5 in full. `PostManualEntryAsync` added to
  `GlPostingService`/`IGlPostingService` as an ADDITIVE-only change (no existing poster's code
  touched — verified by diff review before reporting). `BankReconciliationService` implements
  D4 matching, D8 lifecycle + immutability, and D7 period-close-before-posting exactly as
  designed, with the coordinator's named concurrency watch item closed via the
  `YearCloseService.ReopenAsync`-style conditional-claim guard (documented in full on B4.2
  above, including the one noted residual gap on a DIFFERENT race than the one named).
  Research before writing code: read `IGlPostingService`/`GlPostingService`/`PostClosingEntryAsync`
  (confirmed `BuildAndPostAsync` is the shared private helper to wrap); `YearCloseService`
  confirmed BranchId resolution is simply `tenant.BranchId` (no separate HQ-branch lookup, D7's
  "exactly as YearCloseService does" is literal); `YearCloseService.ReopenAsync` read in full
  for the conditional-update guard precedent; `IPeriodCloseService`/`PeriodCloseService`
  confirmed a missing-period-row defaults CLOSED except the CURRENT Bangkok month (so T6's
  "closed period" case needs zero explicit seeding — `Today.AddMonths(-1)` is closed by
  construction); `Receipt`/`PaymentVoucher` entities read in full for T5 seed-field accuracy;
  `PaymentVoucherConfiguration.cs` checked and confirmed `VendorId`/`ExpenseCategoryId` carry NO
  DB-level FK (placeholder ids are safe in a directly-seeded test row); `SalesChainRlsTests.cs`
  read in full as the T8 mirror target.
  Gates (all run personally, in-session):
  (1) `dotnet build` — 0 errors, 0 warnings (4 separate checkpoints through the stage).
  (2) `dotnet test --filter "FullyQualifiedName~Bank"` — 32/32 green (19 pre-existing + 13 new,
    first try, no bugs found — the extensive upfront pattern research paid off here vs B3's
    รายการ ambiguity, which was iteration-1-fails-iteration-2-passes).
  (3) `dotnet test --filter "FullyQualifiedName~Rbac"` — 41/41 green (T9; the 5 `bank.*` codes
    were already correctly registered/granted since B1 — no new file needed).
  (4) Full backend suite, `TEAS_TEST_PG`+`TEAS_REPO_ROOT` set in one PowerShell invocation —
  `Accounting.Domain.Tests`: 147/0/0; `Accounting.Api.Tests`: 728/0/8 skipped (identical 8).
  **Total 875 passed / 0 failed / 8 skipped = baseline 862 + 13 new B4 tests, exactly.**
  (5) Explicit money/report regression filter (spec's own named gate, run separately even
  though the full suite already covers it, given this stage touches `GlPostingService`):
  `--filter "...Reports|...Receipt|...PaymentVoucher|...Journal|...YearEndClosing"` —
  106/106 green, 0 skipped.
  (6) Frontend: `npx tsc --noEmit` clean; `npm run build` succeeded, new route
  `/bank-accounts/[id]/imports/[importId]` confirmed present in the build output.
  (7) `grep -rn "ম"` → empty; `git status --porcelain | grep -i STM_SA` → empty.
  File-count: 6 new files (5 backend + 1 FE route), well under "~9"; 9 wiring edits (4 backend:
  `Program.cs`, `IGlPostingService.cs`, `DependencyInjection.cs`, `GlPostingService.cs`; 5 FE:
  `StatementImportSection.tsx`, `queries.ts`, `types.ts`, `en.json`, `th.json`) — each a direct,
  minimal consequence of a checklist item; no unrequested abstractions. No STOP condition hit.
  **Spec deviations: none identified.** All D4/D7/D8 rules implemented as literally pinned;
  the one open design choice not fully pinned by the spec (the concurrency guard mechanism) was
  resolved per the coordinator's own explicit instruction (mirror `ReopenAsync`) and documented
  inline rather than silently decided.
- 2026-07-09 sonnet-implementer (Stage B4 Opus Tier-2 fixes — APPROVE with 2 non-blocking
  findings, both fixed immediately per Fable's decision):
  **Fix 1** (`BankReconciliationService.CreateJournalAsync`) — `req.ContraAccountId` was trusted
  raw; `journal_lines.account_id` carries no DB-level FK (mirrors every other poster's
  `ResolveAccountIdAsync`-style trust boundary), so a forged/header/foreign account id would
  have posted SILENTLY and mis-rolled the trial balance. Fixed: load the contra account
  tenant-scoped from `ChartOfAccounts` before building `glLines`; reject with
  `bank.contra_account_not_found` / `bank.contra_account_inactive` / `bank.contra_account_is_header`
  as appropriate. Added `CreateJournal_with_a_header_contra_account_is_rejected_and_posts_nothing`
  — confirmed `DefaultChartOfAccounts` seeds every row with `IsHeader=false` (no header account
  exists by default), so the test inserts one directly via DbContext to exercise the rejection.
  **Fix 2** (`bank-accounts/[id]/imports/[importId]/page.tsx`) — wrapped the reconcile action
  buttons (suggest/post-journal/ignore/unmatch/unignore, in the actions `<td>`) AND both modals
  (`SuggestModal`/`JournalModal`) in `PermissionGate scope="bank.reconcile"` (repo convention;
  server already gates via `bank.reconcile` on every endpoint — this is UX/defense-in-depth only,
  no behavior change for an authorized user).
  Re-gate: `dotnet build` 0/0; `--filter "FullyQualifiedName~Bank"` 33/33 (was 32, +1 for Fix 1);
  frontend `tsc` clean, `npm run build` succeeded. Full suite deferred to the consolidated B5
  run per the coordinator's instruction.
- 2026-07-09 sonnet-implementer (Stage B5): completed B5.1-B5.4 in full immediately after the
  Fix 1/Fix 2 re-gate above. 4 new files (2 backend service/DTO, 1 backend test, 1 FE report
  page), well under "~6"; edits limited to the already-B4-touched `BankReconciliationEndpoints.
  cs`/`DependencyInjection.cs`/`queries.ts`/`types.ts`/`en.json`/`th.json` (small additive
  increments) plus a genuinely new `SidebarNav.tsx` edit (report nav item).
  Gates (all run personally, in-session):
  (1) `dotnet build` — 0 errors, 0 warnings.
  (2) `dotnet test --filter "FullyQualifiedName~BankReconciliationReportServiceTests"` — 2/2
  green on the FIRST run (the tie-out arithmetic was worked out by hand before writing the
  service, catching in advance that the pinned formula's sign convention is NOT the textbook
  "book = statement + deposits − outstanding" one — implementing the textbook version would
  have silently produced wrong differences).
  (3) Full backend suite, `TEAS_TEST_PG`+`TEAS_REPO_ROOT` set in one PowerShell invocation —
  `Accounting.Domain.Tests`: 147/0/0; `Accounting.Api.Tests`: 731/0/8 skipped (identical 8).
  **Total 878 passed / 0 failed / 8 skipped = baseline 875 + Fix-1's 1 new test + B5's 2 new
  tests, exactly.**
  (4) Frontend: `npx tsc --noEmit` clean; `npm run build` succeeded, new route
  `/reports/bank-reconciliation` confirmed present in the build output.
  (5) `grep -rn "ম"` → empty; `git status --porcelain | grep -i STM_SA` → empty.
  **This closes ALL of specs/bank-reconciliation.md — B1 through B5 are now fully `[x]` with
  evidence.** No spec deviations beyond what's explicitly noted inline (B5.1's tie-out
  sign-convention note; B5.3's intentional CRLF-vs-ap-aging's-LF deviation, itself spec-directed).
  No commit made (orchestrator's job). Two pre-existing worker checkpoint files
  (`PROGRESS-bank-reconciliation-b3.md`) remain on disk from B3's quota-guard pause — harmless,
  superseded by the spec's own attempt log throughout.
- 2026-07-09 sonnet-implementer (post-B5 correction — Fable's final diff review caught a
  BLOCKING bug): the ORIGINAL §Reconciliation report math tie-out formula was itself
  sign-flipped on deposits-in-transit/outstanding-payments; my B5.1 implementation followed it
  EXACTLY as pinned (correctly, per the "implement what's pinned, don't second-guess" rule) and
  T10 was written against the same formula — internally consistent but not verified against
  reality (circular). Independently re-derived the correct identity by hand BEFORE touching
  code (statement = Σ-on-statement; GL = Σ-in-books; a deposit-in-transit is in GL but not on
  statement → statement is LOWER than GL by that amount; an outstanding payment is the mirror)
  — my derivation matched the coordinator's correction exactly before I even looked at their
  stated fix, which gave confidence the fix (not a second bug) was right.
  **Code fix:** one line in `BankReconciliationReportService.GetAsync` —
  `difference = statementClosingBalance - (glBalance - depositsTotal + outstandingTotal + unmatchedLinesNet);`
  (was `+ depositsTotal - outstandingTotal`), comment rewritten to the corrected derivation.
  **Tests:** `BankReconciliationReportServiceTests.cs` fully rewritten — the 2 old circular
  facts replaced with 5 new ones, each scenario's expected numbers worked out independently of
  the code before asserting (see B5.4 above for the full breakdown: fully-reconciled,
  deposit-in-transit, outstanding-payment, unmatched-bank-fee-line, and one non-zero/sign case).
  All 5 passed on the first run.
  **FE check:** confirmed `reports/bank-reconciliation/page.tsx` only DISPLAYS
  `report.difference`/`report.glBalance`/etc. verbatim from the API response — no client-side
  recomputation of the formula anywhere (grep-verified) — so no FE change was needed; FE gates
  not re-run since no FE file changed this turn (git status confirmed identical to the prior
  pass, which already had tsc/build green).
  Gates: `dotnet build` 0/0 → `--filter "FullyQualifiedName~BankReconciliationReportServiceTests"`
  5/5 → `--filter "FullyQualifiedName~Bank"` 38/38 (was 33, net +5 for the rewritten T10) → full
  suite `TEAS_TEST_PG`+`TEAS_REPO_ROOT` same shell: Domain.Tests 147/0/0, Api.Tests 734/0/8 skipped
  (identical 8) — **Total 881 passed / 0 failed / 8 skipped = prior baseline 878 + net 3 (5 new
  T10 facts − 2 removed)** — exactly. `grep -rn "ম"` empty; no real samples staged. No commit.
- 2026-07-09 sonnet-implementer (Fresh cross-review — REJECT with 1 blocking + 3 fixes + spec
  hygiene, bundled with a separately-reported CI failure on PR #64): all addressed in one pass,
  one re-gate.
  **Fix 1 (BLOCKING) — `BankReconciliationReportService`:** the three reconciling-item queries
  (unmatched lines, deposits-in-transit, outstanding payments) were bounded `>= from`, but GL
  balance and statement balance are CUMULATIVE (`<= to` only) — an item dated before `from`
  silently dropped from both the list AND the Difference math while still being baked into the
  two balances, leaving a permanently nonzero, unexplained Difference every month after the
  first (the FE defaults `from` to the current month start). Fixed: removed the `>= from` bound
  on all three item queries (now `<= to` only, matching the balances); `from`/`to` stay on the
  DTO/route for display filtering only — doc comments on `IBankReconciliationReportService.
  GetAsync` and the inline query comments rewritten to state this explicitly. Added
  `Item_dated_before_from_still_appears_and_ties_out_cumulatively` — an unmatched fee line dated
  LAST month, report window = the CURRENT month (from = month start, the FE's own default) →
  asserts the item appears in `UnmatchedLines` AND `Difference == 0`.
  **Fix 2 — `StatementImportService`:** size/mime validation used to run only inside
  `AttachmentService.UploadAsync`, AFTER the full adapter parse. Moved the SAME check (same
  `FileStorageOptions.MaxFileSizeMb`/`AllowedMimeTypes`, same `attachment.too_large`/
  `attachment.bad_mime` codes) to BEFORE `adapter.Parse` — reject cheap first, don't burn a full
  CSV/PDF parse on a file that was always going to be rejected downstream.
  **Fix 3 — `KPlusPdfLineAssembler`:** added a comment on the zero-delta-falls-through-to-
  MoneyOut line explaining why that default is safe (a genuine zero-movement row also prints
  Amount=0, so D10's `Amount == |delta|` check holds either way; any REAL mismatch is still
  caught by `BankStatementIntegrity.Validate`, not silently accepted).
  **Fix 4 (spec hygiene) — T1-T11 checkboxes:** all flipped `[x]` with the exact test-file/fact
  evidence (they existed and passed since B2/B3/B4/B5 but the checkboxes were never updated).
  T11 (startup-seed prod-parity) annotated rather than left bare `[ ]` — no automated test is
  feasible (teas_test's superuser connection bypasses RLS, masking exactly the failure class
  T11 exists to catch); documented the MANUAL verification already done at B1 stage-2 (177,722
  row probe) as the actual coverage, consistent with `troubles-wiki.md`'s own framing of this
  failure class.
  **Separately-reported CI fix — `StatementImportServiceTests`:** PR #64 failed on Linux CI —
  `ImportAsync_happy_path_persists_import_and_lines` uploads through the REAL Attachment infra
  (D11), which writes to `LocalDiskFileStorage`'s configured `StorageRoot`; `TestCompanyFactory.
  BuildProvider` uses the DEFAULT root (`/var/teas/attachments`), unwritable on a CI runner
  (works on a local Windows box by accident, not by design — an environment-dependent test).
  Grepped for the existing precedent (`grep -rl StorageRoot tests/`) and found
  `Sprint11AttachmentTests.cs` already solves exactly this: a per-test-class `ConfigurationBuilder`
  overriding `FileStorage:StorageRoot` to a `Path.GetTempPath()`-based directory, `IDisposable`
  cleanup. Mirrored it byte-for-byte in `StatementImportServiceTests` (test-layer only, zero
  production-code change) — both tests in the file now use `BuildProviderWithTempStorage` instead
  of `TestCompanyFactory.BuildProvider`. Checked every OTHER new Bank test file for the same
  exposure: `BankReconciliationServiceTests`/`BankReconciliationReportServiceTests`/
  `BankReconciliationRlsTests` all seed `StatementImport`/`StatementLine` DIRECTLY via DbContext
  (never call `IStatementImportService.ImportAsync`), so none of them touch file storage at all —
  no further changes needed.
  Gates: `dotnet build` 0/0 → `--filter "FullyQualifiedName~Bank"` **39/39** (was 38, +1 for the
  cross-period fact) → full suite `TEAS_TEST_PG`+`TEAS_REPO_ROOT` same shell: FIRST run hit 1
  failure, `TaxFilings.Pnd50FilingServiceTests.Pnd50_revenue_over_200m_...` — grepped
  `troubles-wiki.md` FIRST per protocol, found an EXACT match ("Full Accounting.Api.Tests run: a
  single, DIFFERENT test fails each run" — shared mutable `teas_test` state across ~575+ tests
  in one collection, not touched by this diff); confirmed via the prescribed remediation: the
  whole `Pnd50FilingServiceTests` class passed 7/7 in isolation, then a clean re-run of the FULL
  suite passed 100%: Domain.Tests 147/0/0, Api.Tests 735/0/8 skipped (identical 8) — **Total 882
  passed / 0 failed / 8 skipped = prior baseline 881 + 1, exactly.** No FE files touched this
  turn (grep-verified the report page only displays API values, no client recomputation) — FE
  gates not re-run per the coordinator's own "no FE changes expected" framing. `grep -rn "ম"`
  empty; no real samples staged. No commit.
