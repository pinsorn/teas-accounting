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

    /// <summary>
    /// R2/WP-6 — pnd50/pnd51 threw an unmapped ArgumentOutOfRangeException (500) for a
    /// nonsense CE year (year&lt;=0, year&gt;=9999) while this class's own MonthRange (above)
    /// already returns a clean 422 for the analogous period case. Same convention, same code
    /// family (tax_filing.bad_*).
    /// Bound: mirrors MonthRange's y&gt;9999 ceiling, tightened by one to y&gt;=9999 — 9999 is
    /// the classic "impossible year" placeholder (no real filing is ever for CE 9999), so
    /// rejecting it and anything above closes the exact hole in the bug report. Deliberately
    /// NOT tightened further to a "near future" ceiling: the Pnd50/CIT integration-test suite
    /// (FreshJeYearAsync, CitExpenseByAccountTests.cs:112, plus fixed years 3098/3099 in
    /// Pnd50FilingServiceTests.cs/Pnd51FilingServiceTests.cs) deliberately files far-future
    /// years up to ~7499 as a "definitely fake" sentinel to dodge collisions on the shared,
    /// never-reset teas_test DB — a lower ceiling would 422 those tests. Floor of 2000 mirrors
    /// MonthRange's own y&lt;2000 check; kept loose so a legitimate late filing for an old year
    /// is never refused (the R1 lesson: a guard that turns a wrong answer into an impossible
    /// one has broken a real capability).
    /// </summary>
    public static void EnsureYear(int year)
    {
        if (year is < 2000 or >= 9999)
            throw new DomainException("tax_filing.bad_year",
                $"Year '{year}' must be a CE year (e.g. 2026).");
    }
}
