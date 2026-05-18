using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 B3 — ม.82/6 proportional input-VAT claim ratio. When a company has
/// BOTH taxable and exempt sales, shared-purpose input VAT is claimable only at
/// ratio = taxable / total monthly sales. Computed on-demand (not stored) so the
/// formula stays transparent and recalculates if historical data changes.
/// Tenant-scoped via the DbContext global query filter.
/// </summary>
public sealed class ProportionalInputVatService(AccountingDbContext db)
    : IProportionalInputVatService
{
    public async Task<MonthlyClaimRatio> ComputeAsync(int period, CancellationToken ct)
    {
        var (from, to) = TaxFilingPeriod.MonthRange(period);
        var s = await SalesCategorizer.ComputeAsync(db, from, to, ct);

        // Zero-rated counts as taxable for claim purposes (still a VAT business).
        var taxable = s.TaxableSubtotal + s.ZeroRatedSubtotal;
        var exempt  = s.ExemptSubtotal;
        var total   = taxable + exempt;

        return new MonthlyClaimRatio(
            YearMonth:    period,
            TaxableSales: taxable,
            ExemptSales:  exempt,
            TotalSales:   total,
            ClaimRatio:   total > 0m ? decimal.Round(taxable / total, 6) : 1.0m,
            ApplicableTo: "shared-purpose input VAT only");
    }
}

/// <summary>yyyymm helpers shared by the Part B/C tax-filing services.</summary>
internal static class TaxFilingPeriod
{
    public static (DateOnly from, DateOnly to) MonthRange(int period)
    {
        var (y, m) = (period / 100, period % 100);
        if (m is < 1 or > 12 || y < 2000 || y > 9999)
            throw new DomainException("tax_filing.bad_period",
                $"Period '{period}' must be yyyymm (e.g. 202605).");
        return (new DateOnly(y, m, 1), new DateOnly(y, m, DateTime.DaysInMonth(y, m)));
    }

    /// <summary>ภ.พ.30 / ภ.ง.ด. due = 15th (VAT) — caller passes the day.</summary>
    public static DateOnly DueDate(int period, int day)
    {
        var (y, m) = (period / 100, period % 100);
        var next = new DateOnly(y, m, 1).AddMonths(1);
        return new DateOnly(next.Year, next.Month, day);
    }
}
