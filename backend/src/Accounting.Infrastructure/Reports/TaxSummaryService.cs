using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// 2026-06-13 — monthly tax summary dashboard. Aggregates, per calendar month of a year:
/// revenue/expense (GL), VAT (reuses <see cref="IVatReportService"/> so the ภ.พ.30 logic —
/// incl. VI vat_claim_period — stays single-sourced), and WHT paid/received from
/// <c>wht_certificates</c>. Tenant scoping is the DbContext global query filter; Posted only.
/// </summary>
public sealed class TaxSummaryService(
    AccountingDbContext db, ITenantContext tenant, IVatReportService vat) : ITaxSummaryService
{
    public async Task<TaxSummaryReport> GetAsync(int year, CancellationToken ct, int? businessUnitId = null)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // ── Revenue / Expense from GL, all 12 months in one grouped query ──────────
        // GlPostingService snapshots the document BU onto every journal_line (Sprint 8),
        // so a BU filter on the line is exact for revenue/expense.
        // Half-open calendar-year range instead of `DocDate.Year == year`. EF translates the
        // `.Year` form to `date_part('year', doc_date)::int = @year`, and that `::int` (Postgres
        // dtoi4, double->int4) overflows int4 / throws 22003 on any out-of-int4 date_part result —
        // GET /reports/tax-summary returned 500. A range predicate emits plain `doc_date >= ...
        // AND doc_date < ...` (no date_part, no cast), is sargable, and keeps output identical.
        var yearStart = new DateOnly(year, 1, 1);
        var nextYearStart = new DateOnly(year + 1, 1, 1);
        var glRows = await (
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            join a in db.ChartOfAccounts.AsNoTracking() on l.AccountId equals a.AccountId
            where j.Status == DocumentStatus.Posted
                  && j.DocDate >= yearStart && j.DocDate < nextYearStart
                  && (a.AccountType == AccountType.Revenue || a.AccountType == AccountType.Expense)
                  && (businessUnitId == null || l.BusinessUnitId == businessUnitId)
            group new { l.DebitAmount, l.CreditAmount, a.AccountType } by j.DocDate.Month into g
            select new
            {
                Month = g.Key,
                Revenue = g.Where(x => x.AccountType == AccountType.Revenue)
                           .Sum(x => x.CreditAmount - x.DebitAmount),
                Expense = g.Where(x => x.AccountType == AccountType.Expense)
                           .Sum(x => x.DebitAmount - x.CreditAmount),
            })
            .ToListAsync(ct);
        var gl = glRows.ToDictionary(r => r.Month, r => (r.Revenue, r.Expense));

        // ── WHT from the certificate register (one query for the year) ─────────────
        // Direction 'P' = we withheld + remit (ภ.ง.ด.3/53/54/1); 'R' = customer withheld
        // from us (ภ.ง.ด.50 credit). Grouped by CertDate month. The cert carries no BU of
        // its own, so the BU lens resolves it via the source PV (P) / Receipt (R) header BU.
        // Same half-open-range rewrite as glRows above — avoid `date_part('year', cert_date)::int`
        // (dtoi4 int4-overflow → 22003). Keeps the per-year scope identical.
        var certQuery = db.WhtCertificates.AsNoTracking()
            .Where(w => w.CertDate >= yearStart && w.CertDate < nextYearStart);
        if (businessUnitId is { } whtBu)
        {
            certQuery = certQuery.Where(w =>
                (w.PaymentVoucherId != null && db.PaymentVouchers
                    .Any(p => p.PaymentVoucherId == w.PaymentVoucherId && p.BusinessUnitId == whtBu))
             || (w.ReceiptId != null && db.Receipts
                    .Any(r => r.ReceiptId == w.ReceiptId && r.BusinessUnitId == whtBu)));
        }
        var whtRows = await certQuery
            .GroupBy(w => new { w.CertDate.Month, w.Direction, w.FormType })
            .Select(g => new { g.Key.Month, g.Key.Direction, g.Key.FormType, Wht = g.Sum(x => x.WhtAmount) })
            .ToListAsync(ct);

        // ── VAT: reuse the ภ.พ.30 service per month (DRY; respects claim-period) ───
        var months = new List<TaxSummaryMonth>(12);
        for (var m = 1; m <= 12; m++)
        {
            var pnd30 = await vat.GetPnd30Async(year, m, ct, businessUnitId);
            var (rev, exp) = gl.TryGetValue(m, out var v) ? v : (0m, 0m);

            decimal Paid(WhtFormType f) => whtRows
                .Where(r => r.Month == m && r.Direction == "P" && r.FormType == f)
                .Sum(r => r.Wht);
            var pnd3  = Paid(WhtFormType.Pnd3);
            var pnd53 = Paid(WhtFormType.Pnd53);
            var pnd54 = Paid(WhtFormType.Pnd54);
            var pnd1  = Paid(WhtFormType.Pnd1);
            var received = whtRows.Where(r => r.Month == m && r.Direction == "R").Sum(r => r.Wht);

            months.Add(new TaxSummaryMonth(
                Month: m, Revenue: rev, Expense: exp, NetProfit: rev - exp,
                OutputVat: pnd30.OutputVat, InputVat: pnd30.InputVat,
                VatPayable: pnd30.NetVatPayable, VatRefundable: pnd30.NetVatRefundable,
                WhtPaidPnd3: pnd3, WhtPaidPnd53: pnd53, WhtPaidPnd54: pnd54, WhtPaidPnd1: pnd1,
                WhtPaidTotal: pnd3 + pnd53 + pnd54 + pnd1, WhtReceived: received));
        }

        var totals = new TaxSummaryMonth(
            Month: 0,
            Revenue: months.Sum(x => x.Revenue), Expense: months.Sum(x => x.Expense),
            NetProfit: months.Sum(x => x.NetProfit),
            OutputVat: months.Sum(x => x.OutputVat), InputVat: months.Sum(x => x.InputVat),
            VatPayable: months.Sum(x => x.VatPayable), VatRefundable: months.Sum(x => x.VatRefundable),
            WhtPaidPnd3: months.Sum(x => x.WhtPaidPnd3), WhtPaidPnd53: months.Sum(x => x.WhtPaidPnd53),
            WhtPaidPnd54: months.Sum(x => x.WhtPaidPnd54), WhtPaidPnd1: months.Sum(x => x.WhtPaidPnd1),
            WhtPaidTotal: months.Sum(x => x.WhtPaidTotal), WhtReceived: months.Sum(x => x.WhtReceived));

        return new TaxSummaryReport(year, months, totals);
    }
}
