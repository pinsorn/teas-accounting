# Spec: AR/AP Sub-ledger suite — AR Aging · Customer Statement · Vendor Ledger

<!-- Living document. Worker updates the checklist as it works; a retry uses the
     SAME file and grows the Attempt log — never rewrite the spec for a retry. -->

**Scope class:** read-only reporting surface. **NO schema change, NO migration, NO changes to posting logic, NO RBAC seed.** Every permission this feature needs already exists and is granted (it reuses the perms that gate the existing tax-invoice / vendor-invoice read endpoints). Control accounts are resolved config-driven, never hardcoded. **If any requirement seems to need a migration or a new/ungranted permission → STOP and report to Fable; do NOT design one.**

**Capability map (Fable fills at dispatch):** footgun-zone (money + GL reconciliation). Suggested decomposition — (1) backend service+DTOs+endpoints+MCP tools, (2) backend tests incl. reconciliation, (3) frontend — sequential (they share files). Opus/Codex cross-review with a money/reconciliation lens; Haiku Tier-3 gate; Fable diff review + commit.

---

## Context / footguns

### The single most important fact
**Journal lines carry NO customer/vendor/party tag** (verified: `backend/src/Accounting.Domain/Entities/Ledger/JournalLine.cs` has `AccountId, DebitAmount, CreditAmount, Description, Reference, DimensionsJson, BusinessUnitId` — and nothing else identifying a party). **GL is not sub-ledgered by party.** Therefore:
- The AR/AP sub-ledger (per customer/vendor) can ONLY be sourced from **document tables**, not from GL.
- The GL control-account balance (1130 AR / 2110 AP) is the sum of posted journal **lines on that account**, with no way to split it by party inside the GL.
- **Reconciliation is a genuine two-source comparison:** document-derived subledger total vs GL control-account balance. They CAN differ (manual JEs straight to 1130/2110, opening-balance JEs, doc types the subledger doesn't model). That difference is REAL and must be **surfaced, never hidden** — this is the AR/AP analog of the `get_profit_loss` "silent untagged-BU filtering is a bug" lesson.

### Control accounts (config-driven — do NOT hardcode "1130"/"2110")
`backend/src/Accounting.Infrastructure/Ledger/GlAccountsOptions.cs`, bound from the `GlAccounts` config section, injected as `IOptions<GlAccountsOptions>` (registered in `Accounting.Infrastructure/DependencyInjection.cs:101`; already consumed by `GlPostingService`). Read `.ArAccount` (default `"1130"`) and `.ApAccount` (default `"2110"`) from it, then resolve the code → `ChartOfAccount.AccountId` for the caller's company (`CompanyId == tenant && AccountCode == code`) exactly as `GlPostingService.ResolveAccountIdAsync` does. If the account code is not in the tenant's CoA (possible for a minimal company), treat the control balance as 0 (there can be no postings to a non-existent account).

### What posts to the control accounts (enumerate — a missed source = a reconciliation difference)
Before writing the subledger movement queries, **grep `GlPostingService.cs` for every use of `_accounts.ArAccount` and `_accounts.ApAccount`** to enumerate the exact document types that debit/credit 1130 / 2110. Expected AR (1130): Tax Invoices (DR), Receipts (CR), Credit/Debit adjustment notes `TaxAdjustmentNote` (CN → CR, DN → DR). Expected AP (2110): Vendor Invoices (CR), Payment Vouchers (DR), plus any AP adjustment note. Include **all** of them in the ledger/reconciliation movements. If you find a posting source you cannot model read-only, do NOT drop it — report it to Fable (it becomes a documented reconciliation residual, not a silent gap).

### AP-side reference implementation (mirror its shape for AR aging)
- Service: `backend/src/Accounting.Infrastructure/Reports/ApAgingService.cs`; DTOs `.../Application/Reports/ApAgingDtos.cs` (`ApAgingRow(VendorId, VendorName, VendorTaxId, Current, Bucket31To60, Bucket61To90, BucketOver90, Total)`).
- Route: `GET /reports/ap-aging?asOf={date}&vendorId={long?}` in `backend/src/Accounting.Api/Endpoints/PurchaseOrderEndpoints.cs` (~L90), gated `purchase.purchase_order.read`.
- Computation: over `VendorInvoices` where `Status==Posted && SettlementStatus!="PAID"`; `Outstanding = TotalAmount − SettledAmount` (**current snapshot**, not as-of-date reconstructed); grouped by `VendorId`; **age bucket off `DocDate`**: `age = asOf.DayNumber − DocDate.DayNumber`; ≤30 Current, ≤60 31-60, ≤90 61-90, else Over90.
- FE: `frontend/app/(dashboard)/reports/ap-aging/page.tsx` (as-of `<input type=date>` default `bangkokToday()`, `VendorSelector`, buckets, `MascotGreeting` empty state). Hook `useApAgingReport(asOf, vendorId?)`. Type `ApAgingRow`/`ApAgingReport` in `frontend/lib/types.ts` (~L1036).

### Entities & sources (all queryable read-only, no schema change)
- `master/Customer.cs`: `CustomerId (long)`, `CustomerCode` (human), `NameTh/NameEn`, `TaxId`, `PaymentTermDays`. `master/Vendor.cs`: `VendorId`, `VendorCode`, `NameTh/NameEn`, `TaxId`.
- AR docs: `Sales/TaxInvoice.cs` (`CustomerId, TotalAmount, AmountPaid, PaymentStatus ∈ {UNPAID,PARTIAL,PAID}, DueDate, DocDate, Status`); `Sales/Receipt.cs` (`CustomerId, Amount, WhtAmount, CashReceived, DocDate, Status`) + `ReceiptApplication(TaxInvoiceId?, DeliveryOrderId?, BillingNoteId?, AppliedAmount)`; adjustment `TaxAdjustmentNote`.
- AP docs: `Purchase/VendorInvoice.cs` (`VendorId, TotalAmount, SettledAmount, SettlementStatus, DocDate, Status`) + `PaymentVoucherApplication(VendorInvoiceId, AppliedAmount)`.
- **WHT caution (verify, do not assume):** on an AR receipt with WHT, the cash + the WHT-withheld portion both clear 1130 (WHT lands in 1180, a *different* control account). For the subledger to tie to 1130, the amount treated as "paid/settled" per invoice MUST equal the full amount that cleared 1130 (cash + WHT), i.e. the receipt's full application, not cash-only. The controlled reconciliation test (below) is the arbiter — if it fails, the AmountPaid/AppliedAmount semantics are the cause; **report the exact gap to Fable, do not silently patch** (it may be a data-model question, not a report bug).

### Env / test footguns (respect in the test plan)
- **teas_test** fixture applies each SQL seed ONCE and must stay fixture-managed. `TEAS_TEST_PG` env dies between PowerShell calls — set it in the SAME invocation as `dotnet test`. Check skip-count vs baseline (a skipped test fakes green). `TEAS_REPO_ROOT` must be set same-shell for `RbacAuthMapTests`/`RbacMatrixTests` (subst-drive quirk).
- **Date/period footgun (critical for AGING tests):** seed 400 closes the prev month relative to CURRENT_DATE; posting into a closed past period fails. So do NOT create past-dated invoices to hit aging buckets. Instead post invoices dated **today (T)** and vary the query `asOf` (a pure param, no posting): asOf=T → Current, asOf=T+45 → 31-60, asOf=T+75 → 61-90, asOf=T+100 → Over90. All postings stay in the open current period.
- **co2 is load-bearing + polluted** (P&L ties to doc chapters; JEs immutable, no void). Every co2 probe is **READ-ONLY** — never post/seed into co2. co2/co3 raw-SQL seeds skip DefaultTaxCodes (0 tax_codes) — don't build reconciliation assertions that assume tax rows exist there.
- **No new backend endpoint needs a Next.js passthrough:** FE pages call via the `/api/proxy` BFF (`frontend/lib/api.ts`, token injected server-side). The troubles-wiki "307 to /login" trap is only for *new browser-hit* backend routes; these are BFF-proxied like every other report. Do NOT add `app/**/route.ts` or touch `middleware.ts`.
- **MCP null serialization (troubles-wiki):** the MCP SDK omits `null`-valued properties from JSON. In round-trip tests read nested/optional keys with `TryGetProperty` and treat key-absent == null; do NOT assert `JsonValueKind.Null`.
- **Thai glyph:** grep `ম` (Bengali) before commit.
- **git add:** `git status | grep '^??'` — new source files (new service/DTO/page files) must be explicitly added; `git add -u` misses them.

---

## Design — reconciliation model (the load-bearing part)

**One shared reconciliation block, computed API-side, exposed on every new endpoint/tool.** New DTO:
```csharp
public sealed record SubledgerReconciliation(
    string ControlAccountCode,        // "1130" (AR) / "2110" (AP), from GlAccountsOptions — NOT literal
    decimal ControlAccountBalance,    // signed GL balance on the control account, DocDate <= asOf
    decimal SubLedgerTotal,           // Σ document-derived net party balance, as-of asOf
    decimal Difference,               // ControlAccountBalance − SubLedgerTotal
    bool Balanced);                   // Difference == 0m
```
- **ControlAccountBalance** = over posted `JournalLines` on the resolved control `AccountId` with `entry.DocDate <= asOf`: AR = `Σ(Debit − Credit)` (1130 is DR-normal); AP = `Σ(Credit − Debit)` (2110 is CR-normal). Same posted-only rule as balance sheet/GL.
- **SubLedgerTotal** = Σ over all parties of that party's **net** balance as-of `asOf`, from the enumerated 1130/2110-posting document types (invoices increase, receipts/payments decrease, notes per sign). Computed by the SAME logic that produces each party's `ClosingBalance` in the statement/ledger — so statement closing balances across all parties sum to `SubLedgerTotal` by construction.
- **Difference / Balanced:** if `Difference != 0`, the endpoint STILL returns 200 with `Balanced=false` and the non-zero `Difference` — this is the "surface, never hide" requirement. `Difference` is the 1130/2110 activity the subledger cannot attribute to any party document (manual JEs, opening-balance JEs, unmodeled doc types). Zero tolerance = report it exactly, never round/clamp/suppress it.
- **Homes:** the block is returned on the AR aging report, the customer statement, AND the vendor ledger (AR block for AR features, AP block for vendor ledger). It is computed by shared private helpers `ArReconciliationAsync(asOf, ct)` / `ApReconciliationAsync(asOf, ct)` so all three surfaces report identical numbers for the same as-of date. **Reconciliation is company-wide, not per-party** (GL has no party dimension, so a single customer's balance cannot tie to a GL figure) — document this in the DTO/XML-doc so no one expects per-party GL tie-out.

**Aging vs net-balance (both well-defined, keep them distinct):** the aging **buckets** are invoice-centric (mirror AP: Σ over unpaid invoices), whereas `SubLedgerTotal` is the full net party balance (invoices − receipts ± notes, incl. on-account/unapplied receipts). They can differ by advances/notes; that is correct — each answers a different question. Do NOT force the aging total to equal `SubLedgerTotal`.

---

## Requirements (checklist)

### Backend

- [x] **S1. DTOs + service interface** — new file `backend/src/Accounting.Application/Reports/SubledgerDtos.cs` and interface `ISubledgerReportService`:
  - `SubledgerReconciliation` (above).
  - AR aging (mirror `ApAgingRow`): `ArAgingRow(long CustomerId, string CustomerCode, string CustomerName, string? CustomerTaxId, decimal Current, decimal Bucket31To60, decimal Bucket61To90, decimal BucketOver90, decimal Total)`; `ArAgingReport(DateOnly AsOfDate, int CompanyId, IReadOnlyList<ArAgingRow> Rows, ArAgingRow Totals, SubledgerReconciliation Reconciliation)`.
  - Customer statement: `CustomerStatementLine(DateOnly DocDate, string DocType, string DocNo, string? Description, decimal Debit, decimal Credit, decimal RunningBalance)`; `CustomerStatement(long CustomerId, string CustomerCode, string CustomerName, DateOnly FromDate, DateOnly ToDate, decimal OpeningBalance, IReadOnlyList<CustomerStatementLine> Lines, decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance, SubledgerReconciliation Reconciliation)`.
  - Vendor ledger (AP analog): `VendorLedgerLine(...)` same shape; `VendorLedger(long VendorId, string VendorCode, string VendorName, DateOnly FromDate, DateOnly ToDate, decimal OpeningBalance, IReadOnlyList<VendorLedgerLine> Lines, decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance, SubledgerReconciliation Reconciliation)`.
  - Interface methods: `Task<ArAgingReport> ArAgingAsync(DateOnly asOf, long? customerId, CancellationToken ct)`; `Task<CustomerStatement> CustomerStatementAsync(long customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)`; `Task<VendorLedger> VendorLedgerAsync(long vendorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)`. Statement/ledger throw `DomainException("customer.not_found" / "vendor.not_found", …)` → 404 when the party is unknown/other-tenant (EF global filter hides cross-tenant identically).
  - Acceptance: `dotnet build` green.

- [x] **S2. Service implementation** — new `backend/src/Accounting.Infrastructure/Reports/SubledgerReportService.cs`, registered in `DependencyInjection.cs` (mirror how `ApAgingService`/`IFinancialReportService` are registered). Inject `AccountingDbContext`, `ITenantContext`, `IOptions<GlAccountsOptions>`.
  - **Control-balance + reconciliation helpers** `ArReconciliationAsync(asOf, ct)` / `ApReconciliationAsync(asOf, ct)` per the model above.
  - **AR aging:** over `TaxInvoices` where `Status==Posted && PaymentStatus != "PAID"`, `Outstanding = TotalAmount − AmountPaid`, filter `customerId` if given, group by `CustomerId`, bucket by `age = asOf.DayNumber − DocDate.DayNumber` (mirror AP thresholds). `Totals` = column-wise sums. Attach `ArReconciliationAsync(asOf)`.
  - **Customer statement:** movements = enumerated AR-posting docs for the customer (Tax Invoices → Debit=TotalAmount; Receipts → Credit=amount that clears 1130; CN → Credit; DN → Debit). `OpeningBalance` = net of movements with `DocDate < fromDate`. `Lines` = movements with `fromDate <= DocDate <= toDate`, ordered by **`DocDate`, then a fixed DocType rank, then the source row id** (deterministic stable order — no ambiguity in the running balance). `RunningBalance` = opening + cumulative `(Debit − Credit)`. `ClosingBalance = OpeningBalance + Σ(Debit−Credit)`. Include unapplied/on-account receipts (they still credit 1130). Attach `ArReconciliationAsync(toDate)`.
  - **Vendor ledger (AP sign):** AP is CR-normal; present a positive "payable owed" balance. Vendor Invoice → Credit (increase payable); Payment Voucher → Debit (decrease); AP notes per sign. `RunningBalance` = opening + cumulative `(Credit − Debit)`; `ClosingBalance` mirrors. Order `DocDate`, DocType rank, source id. Attach `ApReconciliationAsync(toDate)`. **Document the Credit-minus-Debit orientation in an XML-doc comment** so a reviewer doesn't read it as an AR-style sign bug.
  - Acceptance: builds; behavior pinned by S5 tests.

- [x] **S3. REST endpoints** — add to `backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs` (`/reports` group), each `.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + <existing perm>)`:
  - `GET /reports/ar-aging?asOf={DateOnly?}&customerId={long?}` → `ArAgingAsync(asOf ?? today, customerId, ct)`; perm `Permissions.Sales.TaxInvoiceRead`. (`asOf` default = `DateOnly.FromDateTime(DateTime.UtcNow)` or the clock helper the group uses — match ap-aging's default.)
  - `GET /reports/customer-statement?customerId={long}&fromDate={DateOnly}&toDate={DateOnly}` → 400 if `fromDate > toDate`; else `CustomerStatementAsync`; perm `Permissions.Sales.TaxInvoiceRead`.
  - `GET /reports/vendor-ledger?vendorId={long}&fromDate={DateOnly}&toDate={DateOnly}` → 400 if `fromDate > toDate`; else `VendorLedgerAsync`; perm `Permissions.Purchase.VendorInvoiceRead`.
  - **RBAC:** these perms already exist and gate live endpoints (`Permissions.cs` `Sales.TaxInvoiceRead="sales.tax_invoice.read"`, `Purchase.VendorInvoiceRead="purchase.vendor_invoice.read"`) — **no new perm, no seed.** Every new route MUST carry `.RequireAuthorization` (else `RbacAuthMapTests` "no unprotected endpoint" gate fails). If you believe a NEW perm is required → STOP and escalate (a seed/migration is out of blast radius).

- [x] **S4. MCP tools** — add to `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` in the C1 report-tools region, thin wrappers over `ISubledgerReportService`, reusing EXISTING policy consts (`TaxInvoiceRead`, `VendorInvoiceRead`) and `IClock` for the aging default. **NO new `McpScopes` entry, NO frontend scope-list change** (these scopes already exist/are granted). Every id param description MUST state internal-id-vs-code explicitly (the `get_general_ledger` field-test lesson):
  - `get_ar_aging` `[Authorize(Policy = TaxInvoiceRead)]` — params `asOfDate?` ("omit for today"), `customerId?` ("internal customer id from list_customers, NOT the customer code; omit for all customers"). Desc notes it includes a company-wide reconciliation block (AR subledger vs GL control account 1130).
  - `get_customer_statement` `[Authorize(Policy = TaxInvoiceRead)]` — params `customerId` ("internal customer id from list_customers, NOT the customer code"), `fromDate`, `toDate` (inclusive). Desc: per-customer ledger with running balance + company-wide AR reconciliation.
  - `get_vendor_ledger` `[Authorize(Policy = VendorInvoiceRead)]` — params `vendorId` ("internal vendor id from list_vendors, NOT the vendor code"), `fromDate`, `toDate`. Desc: per-vendor ledger with running balance + company-wide AP reconciliation (vs GL control account 2110).
  - Acceptance: build green; tools appear in the MCP catalog (S6 smoke).

### Backend tests — new `backend/tests/Accounting.Api.Tests/Reports/SubledgerReportTests.cs` (service + endpoint level, `TestCompanyFactory`-isolated); MCP round-trips in the existing MCP read-test file

- [x] **S5. Reconciliation + subledger correctness (the money tests).** Use `TestCompanyFactory.CreateAsync`; all postings dated **today (T)** or future; vary `asOf` for aging.
  - **AR aging buckets:** post one unpaid tax invoice (customer X, amount A) dated T. Query `ArAgingAsync` at asOf ∈ {T, T+45, T+75, T+100}; assert A lands in {Current, Bucket31To60, Bucket61To90, BucketOver90} respectively; other buckets 0. `customerId` filter returns only X.
  - **Reconciliation balanced (baseline):** fresh company, post invoice A (DR 1130). `ArAgingAsync(T).Reconciliation` → `ControlAccountBalance==A`, `SubLedgerTotal==A`, `Difference==0`, `Balanced==true`. Then post a receipt fully clearing it → all three go to 0, `Balanced==true`.
  - **WHT tie-out (arbiter test):** post invoice 107 (100 + 7 VAT), post a receipt withholding 3 WHT (cash 104, WHT→1180). Assert customer net AR (statement `ClosingBalance` / reconciliation contribution) == 0 and `Balanced==true`. If this fails, the "paid/settled" amount excludes WHT → **report to Fable, do not patch.**
  - **Unattributable difference is SURFACED (the "never hide" test):** post a manual balanced JE that debits 1130 by D with no customer document. Assert the endpoint returns 200, `Difference == D` (or `−D` per sign), `Balanced == false` — the gap is reported, not dropped.
  - **Customer statement running balance:** post invoice 1000 (T) then receipt 300 (T+1). `CustomerStatementAsync(X, T, T+10)` → `OpeningBalance==0`, two lines ordered by date (invoice: Debit 1000, running 1000; receipt: Credit 300, running 700), `ClosingBalance==700`. Separately: invoice dated T, query from=T+5 → invoice is in `OpeningBalance` (==1000), excluded from `Lines`.
  - **Vendor ledger:** AP analog — vendor invoice increases payable (Credit), payment decreases (Debit), `RunningBalance` payable-positive; reconciliation vs 2110 balanced after a matched invoice+payment.
  - **Errors:** unknown/other-tenant `customerId`/`vendorId` → 404; `fromDate > toDate` → 400 (statement + ledger).
  - Acceptance: `dotnet test --filter "FullyQualifiedName~SubledgerReportTests"` (TEAS_TEST_PG same-shell) → all passed, 0 skipped.

- [x] **S6. MCP round-trip + RBAC.** In the MCP read-test file: invoke `get_ar_aging` / `get_customer_statement` / `get_vendor_ledger` through the in-process MCP harness (mirror existing `get_trial_balance` MCP tests); assert shapes incl. the `reconciliation` object (read keys with `TryGetProperty` — MCP null-omission footgun). Endpoint RBAC: a user lacking the perm → 403 on each of the three routes. `RbacAuthMapTests` + `RbacMatrixTests` green (TEAS_REPO_ROOT same-shell) — confirms every new route is perm-gated and no new catalog entry leaked.
  - Acceptance: targeted MCP + Rbac filters → passed, 0 skipped.

- [x] **S7. co2 reconciliation regression (READ-ONLY probe).** Against demo company co2 at default asOf, call the AR reconciliation (`get_ar_aging`) and AP reconciliation (`get_vendor_ledger` for any co2 vendor): assert the `reconciliation` block is PRESENT and **arithmetically consistent** (`Difference == ControlAccountBalance − SubLedgerTotal`) and the call returns 200. **Do NOT assert `Balanced==true`** — co2's ledger may carry manual/seed JEs on 1130/2110; a non-zero `Difference` is a finding to LOG for Fable, not a test failure. **No writes to co2.** Log the actual `ControlAccountBalance` / `SubLedgerTotal` / `Difference` in the Attempt log.

### Frontend (mirror `reports/ap-aging` + `reports/general-ledger` patterns)

- [x] **F1. Types + hooks** — `frontend/lib/types.ts`: `ArAgingRow/ArAgingReport`, `CustomerStatementLine/CustomerStatement`, `VendorLedgerLine/VendorLedger`, `SubledgerReconciliation` (camelCase). `frontend/lib/queries.ts`: `useArAgingReport(asOf, customerId?)` (param `asOf`, mirror `useApAgingReport`), `useCustomerStatement(customerId, fromDate, toDate)` (params `customerId/fromDate/toDate`; `enabled` only when customerId set), `useVendorLedger(vendorId, fromDate, toDate)`. **Match each hook's query-param names to the REST route exactly** (ar-aging → `asOf`; statement/ledger → `fromDate`/`toDate`).

- [x] **F2. AR Aging page** — new `frontend/app/(dashboard)/reports/ar-aging/page.tsx`, mirror `ap-aging/page.tsx` but with `CustomerSelector` (`frontend/components/ui/CustomerSelector.tsx`, exists). Buckets table + a **reconciliation panel** (Control 1130 balance | Subledger total | Difference | Balanced badge — badge RED / "ไม่กระทบยอด" when `!balanced`, showing the `difference`). `bangkokToday()` default as-of. `formatTHB`, `text-right tabular-nums`.

- [x] **F3. Customer Statement page** — new `frontend/app/(dashboard)/reports/customer-statement/page.tsx`: `CustomerSelector` + from/to `<input type=date>` (default first/last of current month via `bangkokMonthStart/End` from `frontend/lib/utils.ts`) + "แสดงรายงาน" trigger (query enabled only when a customer is chosen). Running-balance table: วันที่ | ประเภท | เลขที่ | คำอธิบาย | เดบิต | เครดิต | คงเหลือ; first row = ยอดยกมา (opening); `<tfoot>` totals + ยอดยกไป (closing). Reconciliation panel (as F2).

- [x] **F4. Vendor Ledger page** — new `frontend/app/(dashboard)/reports/vendor-ledger/page.tsx`: `VendorSelector` + from/to + trigger, same running-balance table (labelled for payables), reconciliation panel vs 2110.

- [x] **F5. Nav + i18n** — `frontend/components/app-shell/SidebarNav.tsx` `reports` section, three items (import lucide icons): `{ href:'/reports/ar-aging', key:'arAging', perm:'sales.tax_invoice.read' }`, `{ href:'/reports/customer-statement', key:'customerStatement', perm:'sales.tax_invoice.read' }`, `{ href:'/reports/vendor-ledger', key:'vendorLedger', perm:'purchase.vendor_invoice.read' }`. Add `nav.*` + `report.*` (bucket labels, reconciliation labels: controlAccount/subLedgerTotal/difference/balanced/notReconciled) keys to BOTH `messages/th.json` and `messages/en.json`; verify both parse.

## Verification gates

- `dotnet build` (full solution) → 0 errors. (MSB3027 lock → kill stray `testhost` PID, troubles-wiki.)
- `dotnet test --filter "FullyQualifiedName~SubledgerReportTests"` (TEAS_TEST_PG same-shell) → all passed, 0 skipped; skip-count == baseline.
- `dotnet test --filter "FullyQualifiedName~Rbac"` (TEAS_REPO_ROOT same-shell) → passed, 0 skipped (proves the 3 new routes are gated and no perm-catalog drift).
- MCP round-trip filter → passed. co2 probe (S7) → 200 + consistent reconciliation, values logged.
- `npx next build` → compiled, 0 type errors; `/reports/ar-aging`, `/reports/customer-statement`, `/reports/vendor-ledger` in the route manifest.
- Both `messages/*.json` parse as JSON. `grep -rn "ম"` over touched `*.ts/*.tsx/*.cs/*.json` (excl. bin/obj/node_modules/.next) → empty.
- `git status | grep '^??'` before commit — explicitly `git add` the new service/DTO/page/test files.

## Blast-radius cap

Max **18 files** (~6 new). Backend: `SubledgerDtos.cs` (new), `SubledgerReportService.cs` (new), `DependencyInjection.cs`, `ReportEndpoints.cs`, `TeasMcpTools.cs`, `SubledgerReportTests.cs` (new), + the existing MCP read-test file. Frontend: `types.ts`, `queries.ts`, 3 new `page.tsx`, `SidebarNav.tsx`, `messages/th.json`, `messages/en.json`.
- **Forbidden (hitting any = STOP + re-spec):** any migration or `SqlScripts/*.sql` change; any new/ungranted permission or RBAC seed; any edit to posting logic (`GlPostingService`, JE/immutability), to `JournalLine`/entities, or to the existing `ApAgingService`/ap-aging endpoint; any `McpScopes`/frontend scope-list change; any `middleware.ts`/`app/**/route.ts` passthrough. Reconciliation must resolve control accounts via `GlAccountsOptions` — no hardcoded "1130"/"2110" in the new code.
- Public-API surface: additive only (3 new read GET routes reusing existing perms + 3 new MCP tools reusing existing scopes). No change to any existing contract.

## Open questions — RULED by Fable 2026-07-08
1. **Company-wide reconciliation: CONFIRMED.** GL has no party dimension; per-party tie-out is impossible without schema change (forbidden). Whole-AR-vs-1130 block on all three surfaces satisfies the requirement. Keep the DTO/XML-doc note so nobody expects per-party GL tie-out.
2. **Aging basis: ACCEPT snapshot (mirror AP).** They coincide at the default asOf=today; document the historical-asOf divergence in the endpoint/tool description ("aging buckets use current settlement snapshot; historical asOf reconstructs dates, not payments"). Do NOT build transaction-based AR aging.
3. **WHT arbiter: surface/document only.** If S5 shows `AmountPaid` excludes the WHT-cleared portion, log the exact gap in the Attempt log and STOP that item — no deeper fix in this read-only scope. Fable escalates it as a separate data-model task.

## Attempt log
<!-- - <date> <worker>: <result / evidence> -->
- 2026-07-08 sonnet-implementer: BACKEND HALF (S1-S7) done, all gates green. F1-F5 (frontend)
  explicitly NOT started per dispatch.
  - **Recon first** (per dispatch instruction): grepped `GlPostingService.cs` for every
    `_accounts.ArAccount`/`_accounts.ApAccount` use — confirmed AR posting sources are exactly
    TaxInvoice (DR), Receipt (CR, via `ReceiptApplication.AppliedAmount` where `TaxInvoiceId`
    is set), TaxAdjustmentNote (CN→CR/DN→DR); AP sources are exactly VendorInvoice (CR) and
    PaymentVoucher (DR, only when `pv.VendorInvoiceId is not null` — confirmed
    `PaymentVoucherApplication` IS populated 1:1 at PV POST with `AppliedAmount == pv.Subtotal+Vat`,
    matching what GL actually debits). No AP-side adjustment-note type exists. No missed posting
    source found — enumeration matches spec exactly, nothing to report as an unmodeled residual.
  - **S1** `SubledgerDtos.cs` (new) + `ISubledgerReportService` — all record shapes verbatim
    from spec, with XML-doc notes on company-wide reconciliation + AP Credit-minus-Debit
    orientation.
  - **S2** `SubledgerReportService.cs` (new) — control accounts resolved via
    `IOptions<GlAccountsOptions>` (no hardcoded "1130"/"2110" in service code; only test
    *setup* code references the literal default codes, same convention as
    `GeneralLedgerReportTests.cs`). Missing control-account code → 0 balance (not thrown), per
    spec. Shared `ArMovementsAsync`/`ApMovementsAsync` private helpers feed BOTH the aging/
    statement/ledger projections AND the reconciliation `SubLedgerTotal` (party=null aggregates
    all parties) — so all three surfaces are guaranteed to report identical numbers for the same
    asOf, by construction, per spec §"Homes".
  - **S3** 3 REST endpoints in `ReportEndpoints.cs` — `ar-aging` (asOf/customerId, TaxInvoiceRead),
    `customer-statement` (customerId/fromDate/toDate, 400 if fromDate>toDate, TaxInvoiceRead),
    `vendor-ledger` (same shape, VendorInvoiceRead). 400 check lives at the endpoint (mirrors
    `general-ledger`'s existing pattern) since a plain `DomainException` defaults to 422, not 400.
  - **S4** 3 MCP tools in `TeasMcpTools.cs` — reused the ALREADY-DEFINED `TaxInvoiceRead`/
    `VendorInvoiceRead` policy consts (no new consts, no new `McpScopes` entry — both scopes
    were already granted/used by other tools). Added `"purchase.vendor_invoice.read"` to
    `McpReadExpansionTests.FullReadScopes` (1-line addition — it wasn't in the default test-key
    scope set before, needed for the new `get_vendor_ledger` tool's round-trip test).
  - **S5** new `SubledgerReportTests.cs` (11 tests, service-level TestCompanyFactory-isolated +
    2 endpoint-level RbacApiFactory tests for 400/403) — aging buckets across asOf∈{T,T+45,
    T+75,T+100}; reconciliation balanced baseline (invoice→balanced, full receipt→zero);
    **WHT tie-out arbiter: PASSED, no gap** (see finding below); unattributable manual-JE
    difference surfaced (`Balanced==false`, exact `Difference`, never hidden); customer
    statement running balance + opening/lines partition; vendor ledger running balance +
    AP reconciliation; 404 (unknown + cross-tenant customer/vendor); 400 (fromDate>toDate on
    both routes); 403 (all 3 routes, no perm).
  - **FOOTGUN FOUND mid-S5** (not in the original spec footgun list): `TaxInvoiceService`/
    `ReceiptService`/`VendorInvoiceService` ALWAYS re-pin `DocDate` to server `TodayInBangkok()`
    at POST, ignoring the request's `DocDate` entirely (§10 convention, same as `IJournalService`
    manual JVs) — so two documents posted within one test run can never land on genuinely
    different real dates. First test attempt failed on an exact-date assertion; fixed by
    restructuring the test to range the query around a single shared DocDate (mirrors
    `GeneralLedgerReportTests`'s existing "query range starts after the posting" trick) instead
    of trying to backdate/forward-date documents. **Appended to `troubles-wiki.md`** (confirmed
    repo-wide convention, not local to this feature — future workers WILL hit this).
  - **WHT arbiter finding (spec ruling 3): NO GAP — reconciliation balances.** Verified in code
    first (`ReceiptService.PostAsync`: `ti.AmountPaid += applied` where `applied` = sum of
    `ReceiptApplication.AppliedAmount`, which is the SAME value `GlPostingService.PostReceiptAsync`
    credits to 1130 — i.e. `AppliedAmount` already includes cash+WHT, confirmed independently
    against the existing `Sprint86ArWhtTests.Receipt_with_wht_posts_balanced_gl_and_cert_R` JE
    assertions: `CreditAmount == 10700m` [full applied] even though only 10400 was cash + 300
    WHT). `Wht_tie_out_arbiter_customer_net_ar_zero_after_withheld_receipt` test (invoice
    100+7%VAT=107, receipt applies 107 with 3 withheld) → `CustomerStatement.ClosingBalance==0`
    and `Reconciliation.Balanced==true`. **No escalation needed — this item does NOT stop.**
  - **S6** MCP round-trip (3 new tools invoked via in-process MCP harness, `reconciliation`
    object present + all 5 keys read via `TryGetProperty` per the null-omission footgun; unknown
    customerId → `IsError==true`) + scope-denial test (tools absent from catalog + call throws
    when key lacks the perm) added to `McpReadExpansionTests.cs`. Endpoint RBAC: `RbacAuthMapTests`
    + `RbacMatrixTests` green (41/41, 0 skipped) confirms all 3 new routes are perm-gated with
    zero catalog drift; a dedicated `No_perm_user_gets_403_on_all_three_subledger_routes`
    endpoint test in `SubledgerReportTests.cs` additionally pins the literal 403.
  - **S7** co2 read-only probes (2 tests: `get_ar_aging`, `get_vendor_ledger` for co2's first
    seeded vendor) — both assert `Difference == ControlAccountBalance − SubLedgerTotal` exactly
    (never `Balanced==true`, per Fable's ruling). **Actual co2 numbers logged** (captured via a
    temporary diagnostic assertion, reverted before finalizing — see below):
    - co2 AR (1130): `controlAccountBalance=0, subLedgerTotal=0, difference=0, balanced=true`.
    - co2 AP (2110, vendor id 1): `controlAccountBalance=0, subLedgerTotal=0, difference=0, balanced=true`.
    - **No finding to escalate** — co2 is trivially balanced on both sides (no outstanding
      AR/AP activity at the default asOf=today). No writes made to co2 at any point.
  - **Gates (evidence):**
    - `dotnet build backend/Accounting.sln` → 0 Warning(s), 0 Error(s) (checked after every S1-S4
      increment + once final).
    - `TEAS_TEST_PG=... dotnet test ... --filter "FullyQualifiedName~SubledgerReportTests"` →
      **11 passed, 0 skipped**.
    - `TEAS_TEST_PG=... TEAS_REPO_ROOT=... dotnet test ... --filter "FullyQualifiedName~Rbac"` →
      **41 passed, 0 skipped** (RbacAuthMapTests + RbacMatrixTests; `TEAS_REPO_ROOT` set to the
      repo root explicitly in the same shell invocation).
    - `TEAS_TEST_PG=... dotnet test ... --filter "FullyQualifiedName~McpReadExpansionTests"` →
      **16 passed, 0 skipped** (full file, incl. the 5 new subledger MCP tests + the 2 balance-
      sheet tests from the prior dispatch — no regression).
    - `grep -rn "ম"` over all 7 touched files → empty.
  - **Files touched (7, all named in the blast-radius cap, backend-only):**
    `SubledgerDtos.cs` (new), `SubledgerReportService.cs` (new), `DependencyInjection.cs`,
    `ReportEndpoints.cs`, `TeasMcpTools.cs`, `SubledgerReportTests.cs` (new — flagged `??` in
    `git status`), `McpReadExpansionTests.cs`. No forbidden item hit (no migration/SqlScripts,
    no `McpScopes`/frontend-scope-list change, no `middleware.ts`/`route.ts`, no edit to
    `GlPostingService`/`JournalLine`/`ApAgingService`/ap-aging endpoint).
  - **Spec deviations:** none from S1-S7's letter. One interpretive choice on S6's "Endpoint
    RBAC: a user lacking the perm → 403 on each of the three routes" — implemented as an
    explicit HTTP-level test in `SubledgerReportTests.cs` (co-located with the other endpoint-
    level 400 test, same RbacApiFactory harness) rather than in the MCP test file, since it's a
    REST-route-level assertion; the MCP-scope-denial equivalent for the 3 new *tools* was added
    separately in `McpReadExpansionTests.cs` for symmetry with the existing
    `Mcp_report_tools_are_denied_without_the_report_scope` pattern.

- 2026-07-08 sonnet-implementer: FRONTEND HALF (F1-F5) done, all gates green. Backend
  UNTOUCHED (verified via `git status --porcelain -- backend` before/after — byte-identical
  to the S1-S7 dispatch, 0 new changes) per the coordinator's explicit constraint (backend
  diff under Tier-2 review in parallel).
  - **F1** `types.ts`: `SubledgerReconciliation`, `ArAgingRow/Report`, `CustomerStatementLine/
    Statement`, `VendorLedgerLine/Ledger` (camelCase, matching the live JSON exactly — no
    naming collision with existing `ApAgingRow/Report`). `queries.ts`: `useArAgingReport(asOf,
    customerId?)` (param `asOf`, matches `ar-aging?asOf=`), `useCustomerStatement(customerId,
    fromDate, toDate)`, `useVendorLedger(vendorId, fromDate, toDate)` — both mirror
    `useGeneralLedger`'s `enabled: id != null && !!fromDate && !!toDate` gating exactly.
  - **F2** new `reports/ar-aging/page.tsx` — mirrors `ap-aging/page.tsx` (CSV export
    deliberately NOT copied — not in the F2 checklist item, out of scope) with `CustomerSelector`
    replacing `VendorSelector`, `MascotGreeting` empty state, `bangkokToday()` default.
  - **F3** new `reports/customer-statement/page.tsx` — mirrors `general-ledger/page.tsx`'s
    "applied filters only fire on แสดงรายงาน click" pattern exactly (customerId/from/to are
    draft state until the button copies them into applied state, avoiding a query firing on a
    half-picked customer or a half-typed date). `bangkokMonthStart()`/`bangkokMonthEnd()`
    defaults. Running-balance table + opening/closing rows.
  - **F4** new `reports/vendor-ledger/page.tsx` — same shape as F3 with `VendorSelector`/
    `useVendorLedger`, AP reconciliation panel (2110).
  - **F5** `SidebarNav.tsx`: 3 items added (`arAging`/`customerStatement` near
    `generalLedger`; `vendorLedger` near `apAging`), perms exactly as spec (`sales.tax_invoice
    .read` ×2, `purchase.vendor_invoice.read` ×1) — reused `FileText`/`Coins` icons already
    imported (no new lucide import needed). `messages/th.json` + `en.json`: added
    `nav.arAging/customerStatement/vendorLedger` + `report.arAgingTitle/
    customerStatementTitle/vendorLedgerTitle/current/bucket31To60/bucket61To90/bucketOver90/
    customer/taxId/clear/controlAccount/subLedgerTotal/difference/notReconciled/
    arAgingEmptyTitle/arAgingEmptySubtitle`. **Reused ~10 existing `report.*` keys** without
    duplicating (`from/to/debit/credit/docNo/description/openingBalance/closingBalance/
    runningBalance/totalRow/showReport/type`) plus `common.date` — the general-ledger/
    balance-sheet groundwork covered almost the entire statement/ledger table already.
    **Did NOT re-add `report.balanced`** (already exists from the balance-sheet dispatch,
    would have been a duplicate JSON key) — reused it as-is for the reconciliation badge's
    positive state; added `notReconciled` as the new key for the negative state's literal
    "ไม่กระทบยอด" wording the dispatch specified (distinct from the pre-existing
    `unbalanced`/"ไม่สมดุล!" trial-balance wording — a reconciliation gap and a trial-balance
    imbalance are different concepts and deserve different copy).
  - **Reconciliation panel**: implemented as a small local `ReconciliationPanel` function
    duplicated identically in all 3 page files (not a new shared component) — the blast-radius
    cap names only "3 new page.tsx", no new component file, and the panel is ~15 lines.
  - **Gates (evidence):**
    - `node -e "JSON.parse(...)"` on both `messages/*.json` → both valid.
    - `grep -rn "ম"` over all 8 touched frontend files/dirs → empty.
    - `npx next build` → **"Compiled successfully"**, 0 type errors, all 3 new routes present
      in the manifest: `/reports/ar-aging` (4.21 kB), `/reports/customer-statement` (3.81 kB),
      `/reports/vendor-ledger` (3.8 kB).
    - `git status --porcelain -- backend` → identical output before and after this dispatch
      (4 modified + 3 untracked, same as the S1-S7 commit's diff) — confirms zero backend
      touch.
  - **Files touched (8, all named in F1-F5, frontend-only):** `types.ts`, `queries.ts`, new
    `ar-aging/page.tsx`, new `customer-statement/page.tsx`, new `vendor-ledger/page.tsx`,
    `SidebarNav.tsx`, `messages/th.json`, `messages/en.json`. No `dotnet test` run (per
    dispatch instruction — a reviewer is using the backend). No `git commit`.
  - **Spec deviations:** F2's CSV export (present on the AP-aging template it mirrors) was
    intentionally NOT carried over — not requested by F2's checklist text, and exporting an
    AR-side reconciliation-bearing report wasn't scoped in this dispatch; flagging in case the
    coordinator wants parity with ap-aging in a follow-up. Table `docType` values (e.g.
    "TaxInvoice"/"Receipt"/"CreditNote"/"VendorInvoice"/"PaymentVoucher") are shown as the raw
    backend strings in the Type column, not translated to Thai labels — the checklist text
    didn't ask for a doc-type label map and the rest of the row (date/docNo/description) is
    enough to identify the movement; flagging as a possible small follow-up polish item.

- 2026-07-08 sonnet-implementer (Tier-2 review fix batch, 4 nits, backend-only): all 4
  applied verbatim, nothing else touched.
  1. **ar-aging REST default:** `ReportEndpoints.cs` `/reports/ar-aging` — changed
     `DateOnly.FromDateTime(DateTime.UtcNow)` → `DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))`,
     matching ap-aging's exact pattern (`PurchaseOrderEndpoints.cs` ~L94) and the MCP tool's
     `clock.TodayInBangkok()`. Fixes the 00:00–07:00 Bangkok "omitted asOf = yesterday" bug.
  2. **CN/DN coverage gap:** added
     `Customer_statement_shows_credit_note_as_credit_and_debit_note_as_debit_and_ar_stays_balanced`
     to `SubledgerReportTests.cs` — posts a tax invoice (1000+VAT), a Credit Note (subtotal 100)
     and a Debit Note (subtotal 50) against it (all dated today, via `ITaxAdjustmentNoteService`,
     mirroring `CnDnGlBalanceTests.cs`'s API usage). Asserts the statement's 3 lines in
     DocType-rank order (TaxInvoice→CreditNote→DebitNote), CN as Credit / DN as Debit with
     correct running balances, `ClosingBalance == tiTotal − cn.TotalAmount + dn.TotalAmount`,
     and `ArAgingAsync(...).Reconciliation` still `Difference==0m`/`Balanced==true` (CN/DN post
     balanced GL entries against 1130, so the subledger ties out by construction — exercises the
     previously-untested `ArMovementsAsync` TaxAdjustmentNote branch for the first time).
  3. **Weak filter test:** strengthened `Ar_aging_buckets_move_with_asOf_customer_filter_isolates`
     — added a SECOND customer in the SAME company (direct `db.Customers.Add`, mirrors
     `SeedVendor`'s pattern) with its own posted 2000-unit invoice, then asserted the
     `customerId`-filtered query both returns only the first customer's row AND
     `Totals.Total == total` (the original, unpolluted amount) — proves exclusion, not just
     that the first customer's row happens to be present.
  4. **Client-side sum:** `SubledgerReportService.ControlAccountBalanceAsync` — replaced
     `ToListAsync()` + in-memory `.Sum()` with a single server-side aggregation: the DR/CR
     sign selection moved into the LINQ `select` (ternary → SQL `CASE WHEN`), then one
     `SumAsync(ct)` over the projected `decimal`. Identical semantics preserved (posted only,
     `DocDate <= asOf`, same AR/AP sign convention); `SumAsync` on an empty sequence returns
     `0m`, same as the old empty-list `.Sum()`. No signature/behavior change.
  - **Gates (evidence):**
    - `dotnet build backend/Accounting.sln` → 0 Warning(s), 0 Error(s).
    - `TEAS_TEST_PG=... TEAS_REPO_ROOT=... dotnet test backend/Accounting.sln --filter
      "FullyQualifiedName~SubledgerReportTests"` → **12 passed, 0 skipped** (11 prior + 1 new
      CN/DN test).
    - `TEAS_TEST_PG=... TEAS_REPO_ROOT=... dotnet test backend/Accounting.sln --filter
      "FullyQualifiedName~McpReadExpansionTests"` → **16 passed, 0 skipped** (no regression).
    - `grep -rn "ম"` over the 3 touched files → empty.
  - **Files touched (3, all in scope):** `ReportEndpoints.cs`, `SubledgerReportService.cs`,
    `SubledgerReportTests.cs`. No frontend file, `GlPostingService`, entity, migration, or perm
    touched. No `git commit` run (per dispatch).
