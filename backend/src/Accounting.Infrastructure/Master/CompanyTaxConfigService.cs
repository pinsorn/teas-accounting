using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Master;

/// <summary>
/// Per-company tax config read from master.companies (per-company-vat-mode spec).
/// Scoped: the row is fetched once per request and cached. Non-VAT doc labels come
/// from <see cref="VatModeOptions"/> (instance-level, cosmetic only).
/// </summary>
public sealed class CompanyTaxConfigService(
    AccountingDbContext db, ITenantContext tenant, IOptions<VatModeOptions> labels)
    : ICompanyTaxConfigService
{
    private CompanyTaxConfig? _cached;

    public async Task<CompanyTaxConfig> GetAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;

        var row = await db.Companies.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new { c.VatRegistered, c.VatRate, c.Pnd30SubmissionMode })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Company {tenant.CompanyId} not found while resolving tax config.");

        return _cached = new CompanyTaxConfig(
            row.VatRegistered, row.VatRate, row.Pnd30SubmissionMode,
            labels.Value.NonVatDocLabelTh, labels.Value.NonVatDocLabelEn);
    }
}
