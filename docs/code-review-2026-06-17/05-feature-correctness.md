# Feature / Functional Correctness Review — TEAS — 2026-06-17

Reviewer lens: feature / functional correctness & calculations. Read-only. Traced
the implemented business logic against the as-built spec (`docs/accounting-system-plan.md`
§4 + `docs/manual/api/*.md`) for: the sales chain, CN/DN, purchases + WHT, payroll,
GL double-entry, numerical rounding, and reports.

## Summary

Posture: **Strong.** The core calculation engines (line VAT, inclusive 7/107, WHT
gross-up, PIT ม.50(1), CIT ladder, payroll net, double-entry sign conventions) are
correct and well-documented. State machines across the sales/purchase chains enforce
valid transitions and block double-conversion. The one material gap is a **multi-currency
hole in GL posting** that silently misstates the financial statements for any
foreign-currency document; everything else is small.

Counts by severity:
- Critical: 1 (foreign-currency VAT flows into ภ.พ.30 / VAT registers uncconverted → wrong statutory return)
- High: 1 (multi-currency GL posting ignores exchange rate)
- Medium: 1 (receipt over-application via duplicate / concurrent applications)
- Low: 2 (delivery over-qty; payroll other-deductions latent)
- Informational / verified-correct: large (see final section)

**Root-cause note:** CRITICAL-1 and HIGH-1 share one root cause — foreign-currency
documents (`ExchangeRate ≠ 1`) are never converted to THB anywhere downstream of the
document row. The document stores a correct `TotalAmountThb`, but neither the GL nor the
tax registers read it. A single fix (convert-to-THB at the document→ledger/register
boundary, or hard-block non-THB at create) closes both. They are split because their blast
radius differs: HIGH-1 misstates internal financials; CRITICAL-1 misstates a return filed
with the Revenue Department.

**Scope note:** the statutory *filing-output* services
(`Pnd1FilingService`, `SsoFilingService`, `Pnd50/51FilingService`,
`WhtFilingService`) that emit the final RD figures were NOT traced line-by-line — payroll
*run* math and the VAT/WHT *aggregation* feeding them were. Tracing the filing services'
own arithmetic is a recommended follow-up.

---

## Findings

### CRITICAL-1 · Foreign-currency VAT/WHT enters the ภ.พ.30 VAT return and VAT registers unconverted → wrong statutory figures filed with the RD

- **Where:** `backend/src/Accounting.Infrastructure/Reports/VatReportService.cs`
  (`GetRegisterAsync` lines 23–72, `GetPnd30Async` lines 74–88);
  `backend/src/Accounting.Infrastructure/Reports/TaxSummaryService.cs`
  (WHT roll-up lines 70–96).
- **Confidence:** [Confirmed]
- **Evidence — the VAT register sums the document-currency VAT field directly, no FX:**
  ```csharp
  // GetRegisterAsync: rows projected straight from t.TaxAmount / v.VatAmount (doc currency)
  .Select(t => new SalesVatRegisterRow(..., t.SubtotalAmount, t.TaxAmount, t.TotalAmount))
  ...
  var outputVat = sales.Sum(s => s.TaxAmount);          // foreign VAT summed as-is
  var inputVat  = purchaseRows.Sum(p => p.RecoverableVat);
  return new VatRegisterPeriod(..., NetVatPayable: outputVat - inputVat);
  ```
  `GetPnd30Async` then feeds these straight into the ภ.พ.30 summary
  (`Sales`, `OutputVat`, `Purchase`, `InputVat`, `NetVatPayable/Refundable`). There is no
  `× ExchangeRate` and no read of `TotalAmountThb` anywhere in the file. Same for WHT in
  `TaxSummaryService` (`g.Sum(x => x.WhtAmount)` — the certificate's document-currency WHT).
- **Reachability:** identical to HIGH-1 — every document validator accepts any 3-letter
  `CurrencyCode` and any `ExchangeRate > 0` (e.g. `TaxInvoiceDtos.cs` 123–124), so a USD
  Tax Invoice at rate 35 records `TaxAmount` in USD and contributes that USD figure to the
  month's `OutputVat`.
- **Expected vs actual:** the ภ.พ.30 output/input VAT and the VAT registers (ม.87) must be
  in THB — they are filed with สรรพากร. Expected: VAT amounts converted to THB
  (`TaxAmount × ExchangeRate`, or read the stored `TotalAmountThb`-equivalent VAT) before
  aggregation. Actual: foreign VAT magnitudes are summed verbatim, so the filed return is
  wrong for any company that issues/receives a foreign-currency document. This is a §4
  compliance concern (a wrong VAT return), which is why it ranks above HIGH-1.
- **Fix:** convert VAT (and WHT) to THB at the register/aggregation boundary — either store
  a THB VAT amount on the document at post and sum that, or multiply by the document's
  `ExchangeRate` in the register query. If multi-currency is deferred, the HIGH-1
  THB-only create guard also neutralizes this (no foreign doc can exist to mis-aggregate).
- **Caveat:** if in practice every company is THB-only today (no foreign documents have ever
  been created), the live exposure is latent rather than active — but the path is open and
  unguarded, so it remains Critical pending a guard or conversion.

### HIGH-1 · Foreign-currency documents post foreign-magnitude amounts into the GL (financial statements misstated by the FX factor)

- **Where:** `backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs`
  (every `Post*Async` + `BuildAndPostAsync` lines 346–383); consumed by
  `backend/src/Accounting.Infrastructure/Reports/FinancialReportService.cs`.
- **Confidence:** [Confirmed]
- **Evidence — GL posts the document-currency figures, never the `*Thb` figures:**

  `GlPostingService.PostTaxInvoiceAsync`:
  ```csharp
  var net = ti.SubtotalAmount;      // document currency
  var vat = ti.TaxAmount;           // document currency
  var gross = ti.TotalAmount;       // document currency
  ... new() { ... DebitAmount = gross ... }
  ```
  `BuildAndPostAsync` creates the entry with **no** `CurrencyCode`/`ExchangeRate`,
  so they take the entity defaults (`CurrencyCode = "THB"`, `ExchangeRate = 1m` in
  `JournalEntry.cs` lines 25–26):
  ```csharp
  var je = new JournalEntry
  {
      CompanyId = companyId, BranchId = branchId, PrefixCode = JvPrefix,
      DocDate = docDate, ... TotalDebit = totalD, TotalCredit = totalC, Lines = lines,
  };   // CurrencyCode/ExchangeRate never set → "THB"/1
  ```
  A grep of `Accounting.Infrastructure/Ledger` shows the only reads of
  `ExchangeRate`/`CurrencyCode` are in the *manual* `JournalService` — the auto-posting
  service never reads them and never reads the computed `TotalAmountThb` that each
  document stores (`Math.Round(total * req.ExchangeRate, 4, …)`).
- **Reachability:** every create-request validator permits a non-THB currency and a
  non-unit rate — e.g. `TaxInvoiceDtos.cs` lines 123–124:
  ```csharp
  RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);   // any ISO code, not "THB"
  RuleFor(x => x.ExchangeRate).GreaterThan(0);          // not constrained to 1
  ```
  Same pattern in VendorInvoice / PaymentVoucher / Receipt / TaxAdjustmentNote /
  Quotation/SO/DO / BillingNote DTOs. So a USD invoice at `ExchangeRate = 35` is fully
  acceptable.
- **Expected vs actual:** GL/trial-balance/BS/P&L must be in functional currency (THB,
  CLAUDE.md §5). Expected: post `amount × ExchangeRate` (THB) to the ledger, or hard-block
  non-THB at create. Actual: posts the raw foreign amount tagged as THB → for any
  `ExchangeRate ≠ 1` the GL, Trial Balance, Balance Sheet, and P&L are wrong by the FX
  factor, while the document itself stays internally consistent and balanced (so it passes
  the `totalD == totalC` guard and looks fine on its own PDF). The spec is silent on
  multi-currency (§8 does not list it as out-of-scope), so this reads as a half-built
  feature, not an intentional exclusion.
- **Fix (pick one):**
  1. In `GlPostingService`, convert each posted amount to THB
     (`Math.Round(amount * doc.ExchangeRate, 2/4, AwayFromZero)`) and set
     `JournalEntry.CurrencyCode = doc.CurrencyCode` / `ExchangeRate` for traceability; OR
  2. If multi-currency is genuinely not in scope, add a THB-only guard
     (`CurrencyCode == "THB" && ExchangeRate == 1m`) to the document validators so the
     foreign path is unreachable, and record the exclusion in the spec §8.
  Option 1 is the real fix; option 2 closes the hole cheaply if FX is deferred.

### MEDIUM-1 · Receipt can over-apply a single Tax Invoice (duplicate app rows / concurrent receipts) → `AmountPaid > TotalAmount`

- **Where:** `backend/src/Accounting.Infrastructure/Sales/ReceiptService.cs`
  draft validation lines 89–105 and post application lines 320–325;
  validator `backend/src/Accounting.Application/Sales/ReceiptDtos.cs` (no Distinct guard).
- **Confidence:** [Confirmed]
- **Evidence — per-application check reads the same un-incremented outstanding:**
  ```csharp
  var outstanding = ti.TotalAmount - ti.AmountPaid;        // read per app row
  if (app.AppliedAmount > outstanding)
      throw new DomainException("rc.overpaid", ...);
  ```
  At post, each application increments unconditionally with no re-check:
  ```csharp
  foreach (var app in rc.Applications.Where(a => a.TaxInvoiceId.HasValue))
  {
      var ti = await _db.TaxInvoices.First(...);
      ti.AmountPaid += app.AppliedAmount;
      ti.PaymentStatus = ti.AmountPaid >= ti.TotalAmount ? "PAID" : "PARTIAL";
  }
  ```
  The validator (`ReceiptDtos.cs`) enforces "exactly one of {TI, DO, BN} per application"
  but has **no** rule preventing two application rows that both target the same
  `TaxInvoiceId`. Two rows of 100 against a TI with outstanding 100 each pass the draft
  check (both see outstanding = 100) and post to `AmountPaid = 200 > TotalAmount = 100`.
  The same root cause yields a cross-receipt TOCTOU: two receipts drafted before either
  posts both read the same `outstanding`, and neither re-checks at post.
- **Expected vs actual:** Σ applied to a TI must never exceed its outstanding. Actual: a
  single TI can be over-settled. Caps at Medium because the GL stays balanced
  (cash debit = Σ applied credits; the over-credit just over-relieves AR / mis-flags
  `PaymentStatus = PAID`) — it is an AR / payment-status integrity bug, not a ledger
  imbalance. Note the sibling AP path (`PaymentVoucherService.PostAsync` lines 409–414)
  *does* re-check `applied > outstanding + 0.01m` at post against the stored
  `vi.SettledAmount` and uses optimistic concurrency — apply the same pattern here.
- **Fix:** (a) validator: reject duplicate `TaxInvoiceId` across applications; (b) at post,
  aggregate per TI and re-assert `ti.AmountPaid + Σapplied <= ti.TotalAmount + 0.01m`
  inside the transaction, leaning on the TI `Version` (optimistic concurrency) to stop the
  cross-receipt race.

### LOW-1 · Delivery Order can over-deliver a Sales Order line (no qty cap)

- **Where:** `backend/src/Accounting.Infrastructure/Sales/SalesOrderDeliveryServices.cs`
  lines 121–125.
- **Confidence:** [Confirmed]
- **Evidence:**
  ```csharp
  if (l.SalesOrderLineId is { } solId)
  {
      var sol = so.Lines.FirstOrDefault(x => x.LineId == solId);
      if (sol is not null) sol.DeliveredQuantity += l.Quantity;   // no <= sol.Quantity cap
  }
  ```
  Auto-close uses `DeliveredQuantity >= Quantity`, so over-delivery still closes correctly;
  with no inventory in scope (spec §8) there is no stock impact.
- **Fix:** clamp / reject `DeliveredQuantity + l.Quantity > sol.Quantity` if delivered-qty
  accuracy ever matters. Otherwise leave as informational.

### LOW-2 · Payroll non-zero "other deductions" is unhandled (latent, fails loud — not a silent error)

- **Where:** `backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs`
  `PostPayrollRunAsync` lines 311–344; `Payslip.OtherDeductions` is always set to `0m` by
  `PayrollRunService` today.
- **Confidence:** [Confirmed]
- **Evidence:** the payroll JV credits PIT + SSO(both halves) + net-wages and debits
  salary + employer-SSO. It balances only because `OtherDeductions = 0`. A non-zero
  ΣOtherDeductions would unbalance the JV; `BuildAndPostAsync` then throws `gl.unbalanced`.
  The code comment says as much. So this is a guarded latent gap (fails loudly at post,
  never posts wrong numbers), not a live bug.
- **Fix:** wire an other-deductions liability account before exposing `OtherDeductions`.

---

## Verified CORRECT

**Line & document VAT (sales chain).**
`TaxInvoiceService.BuildLine` (`TaxInvoiceService.cs` lines 350–392): exclusive
`vat = round(net × rate, 2, AwayFromZero)`; inclusive `vat = round(total × rate/(1+rate),
2)` then `net = total − vat`. Quantity × price rounded to 4dp, discount applied before VAT.
Doc-level VAT/subtotal/total are summed from the **per-line rounded** figures
(`lines.Sum(l => l.TaxAmount)` etc., lines 217–221) — the RD-correct per-line method, no
premature roll-up rounding. The same `ChainMath.Line` is shared by Quotation / SO / DO
(`QuotationChainServices.cs` lines 16–28). VAT rate is **derived from company master data**,
not trusted from caller input (the documented §4.6 / ม.80 fix).

**GL double-entry integrity.** `BuildAndPostAsync` (`GlPostingService.cs` lines 356–360)
rejects any entry where `totalD != totalC || totalD == 0`. The manual journal path is also
safe: `JournalEntry.MarkPosted` (`JournalEntry.cs` lines 53–66) throws `je.unbalanced`
unless `IsBalanced` (`TotalDebit == TotalCredit && > 0`). TI post (AR=gross / Sales=net /
OutputVAT=vat), Receipt (cash + WHT-receivable vs AR / non-VAT Sales), Vendor Invoice
(expense + input-VAT vs AP=Σ(amount+vat)), Payment Voucher (AP-settle or expense+input-VAT,
WHT-payable, cash), CN/DN (sign by note type), and Payroll all tie out by construction.

**CN / DN (ม.86/9 / ม.86/10).** `TaxAdjustmentNoteService.cs` lines 77–112: VAT rate derived
from master data **and** the original TI (carries VAT only if the original did), not from
caller `req.TaxRate`; links to the posted original; correct GL sign in
`PostTaxAdjustmentNoteAsync` (CN: Dr SalesReturn + Dr OutputVAT, Cr AR; DN: inverse).
(Minor modelling note, not a defect: CN/DN take a single `AdjustmentSubtotal` with one flat
rate — a partial adjustment of a mixed taxable/exempt original cannot be apportioned
line-by-line. Acceptable given the single-figure adjustment model.)

**WHT base / amount.** `WhtPayerModes.Compute` (`WhtPayerModes.cs` lines 39–61): DEDUCT
`tax = r·net`; GROSS_UP_FOREVER `income = net/(1−r)`, `tax = r·income` (correct closed form
of infinite tax-on-tax); GROSS_UP_ONCE `income = net·(1+r)`. All 2dp AwayFromZero. PV issues
one 50ทวิ per income-type group with an effective blended rate
(`PaymentVoucherService.cs` lines 323–369), and `totalPaid` correctly differs between
deduct (`subtotal+vat−wht`) and self-withhold (`subtotal+vat`).

**Purchases / AP.** `VendorInvoiceService`: ม.82/4 claim-window enforcement (TI month ..+6,
incl. closed-period redirect), recoverable / non-recoverable VAT split in `RollUp`
(lines 164–171), AP total = subtotal + all VAT (matches `TotalAmount`), snapshot-at-draft
of recoverable flags, PO loose-match auto-close (`PoSettlement.Evaluate`). PV→VI settlement
re-checks outstanding with a 0.01 tolerance and uses optimistic concurrency.

**Payroll → net, SSO, PIT.** `Payslip.ComputeNet` = gross − PIT − employee-SSO − other
(employer SSO is a company cost, not deducted). `SsoContribution.Monthly`
(`PayrollMath.cs`) clamps wage to [floor, ceiling] then `round(wage×rate, 2)` (ม.33 cap,
config-driven). `ThaiPitCalculator` implements ม.42ทวิ standard expense, the progressive
band walk, and the ม.50(1) projected-annual monthly-withholding method (months-remaining =
13 − period-month; YTD from prior **posted** runs in the same calendar year), never
negative. SSO PIT allowance capped (`min(ssoEmp×12, MaxAllowanceForPit)`). Employer SSO
mirrors employee (ม.46). Run totals roll up from payslips; payroll JV balances (proven:
gross + ssoEmployer = pit + (ssoEmp+ssoEmployer) + net when other = 0).

**CIT (ภ.ง.ด.50/51).** `CitCalculator.cs`: statutory order is correct and pinned —
`taxableBeforeLoss = profit + signed adjustments`; loss applied capped at the positive base
(remainder rolls forward); progressive `TaxOnProfit`; credits (ภ.ง.ด.51 prepay + WHT
suffered) reduce the **tax**; `CitPayable` floored at 0. ม.67ทวิ method-A half-year prepay
(`× 0.50`, less H1 WHT, floored). ม.67ตรี under-estimate penalty (25% tolerance, 20% of
shortfall).

**State machines / chain linkage / anti-double-convert.** Quotation Draft→Sent→Accepted,
convert-to-SO blocked unless Accepted and blocked if `ConvertedToSoId` already set
(`QuotationChainServices.cs` 222–231); cancel blocked after conversion. SO must be Posted
before a DO; DO Draft→Issued→Delivered→TI, with `do.ti_exists` blocking a second TI
(`SalesOrderDeliveryServices.cs` 286–287). Each forward doc stamps its predecessor id
(QuotationId / SalesOrderId / DeliveryOrderId / BillingNoteId / OriginalTaxInvoiceId), and
doc numbers are allocated only on the post/issue step (gap-free).

**Reports — sign conventions & aggregation.** `FinancialReportService`: Trial Balance sums
posted Dr/Cr per account and asserts `td == tc`. Balance Sheet — Asset = Dr−Cr,
Liability/Equity = Cr−Dr, CurrentPeriodEarnings = ΣRevenue(Cr−Dr) − ΣExpense(Dr−Cr), and
asserts `Assets == Liab + Equity + Earnings`. P&L — Revenue = Cr−Dr, Expense = Dr−Cr, by BU
with a correct TOTAL. Sales Summary groups posted TIs by customer / BU / product. All use
Posted documents only and the tenant query filter. (These inherit HIGH-1: correct in THB,
wrong for any foreign-currency source document, because they read raw GL amounts.)

**VAT registers + ภ.พ.30 (`VatReportService`).** Logic is otherwise correct: output VAT from
posted TIs + CN/DN with **correct sign flips** (CN reduces, DN increases — lines 40–43);
input VAT sourced from Vendor Invoices by **ม.82/4 `VatClaimPeriod`** (not doc_date, not the
PV) with non-recoverable-only VIs excluded (`VatAmount > 0`); `NetVatPayable = output − input`
floored correctly into payable / refundable. `TaxSummaryService` correctly single-sources the
ภ.พ.30 logic, splits WHT by Direction ('P' withheld-and-remit vs 'R' suffered) and FormType,
and computes Revenue = Cr−Dr / Expense = Dr−Cr from GL. The **only** defect here is the
currency one — see CRITICAL-1 (VAT) and HIGH-1 (GL-sourced revenue/expense): all amounts are
summed in document currency with no THB conversion.

**Immutability / period control.** Posting refuses into a closed period
(`_period.EnsureOpenAsync`) across TI / Receipt / CN-DN / VI / PV / payroll; doc numbers
assigned only at post; posted entities transition through `MarkPosted` guards. Consistent
with CLAUDE.md §4.2 / §4.3.
