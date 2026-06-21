using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Composes the financial-statement supporting report PDF (งบแสดงฐานะการเงิน + งบกำไรขาดทุน) for a
/// fiscal year. FY-end is derived EXACTLY as <c>Pnd50FilingService.ComposeAsync</c> does
/// (company FiscalYearStartMonth → periodStart = year/startMonth/1, periodEnd = +12m −1d) so the
/// figures match the ภ.ง.ด.50 form. Reuses <see cref="IFinancialReportService"/> — no GL recompute.
/// Header (legal name + tax id) from CompanyProfiles ?? Companies, same as the ภ.ง.ด.50 PDF. Tenant-scoped
/// via the EF global filter. This is a management/supporting report from posted GL, NOT the audited DBD statement.
/// </summary>
public sealed class FinancialStatementPdfService(
    AccountingDbContext db,
    ITenantContext tenant,
    IFinancialReportService financialReport) : IFinancialStatementPdfService
{
    public async Task<byte[]> BuildAsync(int year, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");

        // FY-end derivation — verbatim Pnd50FilingService so the PDF's as-of/period match the form.
        var startMonth  = (int)c.FiscalYearStartMonth;
        var periodStart = new DateOnly(year, startMonth, 1);
        var periodEnd   = periodStart.AddMonths(12).AddDays(-1);

        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

        var bs = await financialReport.BalanceSheetAsync(periodEnd, ct);
        var pl = await financialReport.ProfitLossAsync(
            periodStart, periodEnd, businessUnitId: null, includeUnspecified: true, ct);

        var header = new FinancialStatementHeader(
            CompanyName: prof?.LegalName ?? c.NameTh,
            TaxId: prof?.TaxId ?? c.TaxId);

        return FinancialStatementPdf.Render(header, bs, pl);
    }
}
