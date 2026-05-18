using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Computes <see cref="RevenueThresholdStatus"/> from rolling-12-month posted-TI
/// revenue. Tenant scoping is the DbContext global query filter (no manual
/// company_id). Pure read; safe to call on every dashboard load.
/// </summary>
public sealed class VatThresholdService(
    AccountingDbContext db, IClock clock, IOptions<VatModeOptions> vat)
    : IVatThresholdService
{
    private const decimal Approaching = 1_500_000m;
    private const decimal Exceeded    = 1_800_000m;

    public async Task<RevenueThresholdStatus> CheckAsync(CancellationToken ct)
    {
        if (vat.Value.VatMode) return RevenueThresholdStatus.NotApplicable;

        var cutoff = clock.UtcNow.AddYears(-1);
        var revenue = await db.TaxInvoices
            .Where(ti => ti.Status == DocumentStatus.Posted
                         && ti.PostedAt != null
                         && ti.PostedAt >= cutoff)
            .SumAsync(ti => (decimal?)ti.TotalAmountThb, ct) ?? 0m;

        return revenue switch
        {
            >= Exceeded    => RevenueThresholdStatus.Exceeded,
            >= Approaching => RevenueThresholdStatus.Approaching,
            _              => RevenueThresholdStatus.Ok,
        };
    }
}
