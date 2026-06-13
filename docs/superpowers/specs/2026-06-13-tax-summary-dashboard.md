# Tax Summary Dashboard — monthly tax overview (Ham 2026-06-13)

## Problem
Tax data is scattered across 5–6 report pages (ภ.พ.30, ภ.ง.ด.3/53/54, wht-receivable,
profit-loss, CIT). No single page answers "per month: revenue, expense, VAT paid/refund,
WHT withheld/remitted, WHT credit". Ham asked for one summary with dashboard + detail +
visualization.

## Scope (v1)
One page `/reports/tax-summary?year=YYYY`. A 12-row monthly table + year totals + charts,
with drill-down links to the existing detail report for each cell's source.

### Data per month (all Posted docs only; tenant = DbContext global filter)
| Field | Source |
|---|---|
| Revenue | GL: `AccountType.Revenue` credit−debit, by `JournalEntry.DocDate` month |
| Expense | GL: `AccountType.Expense` debit−credit |
| NetProfit | Revenue − Expense |
| OutputVat / InputVat / VatPayable / VatRefundable | `IVatReportService.GetPnd30Async(year, month)` — reuse (respects VI vat_claim_period; DRY) |
| WhtPaidPnd3 / Pnd53 / Pnd54 / Pnd1 / Total | `wht_certificates` Direction='P', group by `CertDate` month + FormType, Σ WhtAmount |
| WhtReceived | `wht_certificates` Direction='R', by month — ภ.ง.ด.50 tax credit |

### Totals row = year sums of every column.

## API
`GET /reports/tax-summary?year=YYYY` → `TaxSummaryReport`.
Perm: `report.profit_loss.read` (financial-report tier; same as P&L). Year defaults to current
Asia/Bangkok year. openapi documented.

## DTOs (Application/Reports/TaxSummaryDtos.cs)
```
TaxSummaryReport(int Year, IReadOnlyList<TaxSummaryMonth> Months, TaxSummaryMonth Totals)
TaxSummaryMonth(int Month, decimal Revenue, decimal Expense, decimal NetProfit,
    decimal OutputVat, decimal InputVat, decimal VatPayable, decimal VatRefundable,
    decimal WhtPaidPnd3, decimal WhtPaidPnd53, decimal WhtPaidPnd54, decimal WhtPaidPnd1,
    decimal WhtPaidTotal, decimal WhtReceived)
```
Totals reuses TaxSummaryMonth with Month=0.

## FE
`app/(dashboard)/reports/tax-summary/page.tsx`:
- Year selector (default current year).
- KPI cards (year): รายได้รวม · รายจ่ายรวม · กำไรสุทธิ · VAT นำส่งสุทธิ · WHT นำส่งรวม · WHT เครดิต.
- Visualization: revenue-vs-expense bars per month + a VAT/WHT line/area — lightweight inline SVG
  (no new chart dep; matches existing teas-orange tokens).
- Monthly table (12 rows + total). Each VAT cell links `/reports/pnd30?...`; WHT links
  `/tax-filings`; WHT-received links `/reports/wht-receivable`.
- nav entry under "รายงาน"; i18n th/en parity.

## Out of scope (v1)
ภ.พ.36 reverse-charge line (net-zero VAT, already in the VAT register); CIT estimate (own
`/tax-filings/cit` dashboard); BU filtering; PDF export. Note these on the page as "ดูเพิ่มเติม" links.

## Gates
Domain n/a (no pure engine) · Api ≥ baseline +tests (`TaxSummaryTests`: GL months, VAT reuse,
WHT P/R split, year totals) pass 2× · tsc 0 · i18n parity · visual gate.
