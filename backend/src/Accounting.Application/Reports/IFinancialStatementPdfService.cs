namespace Accounting.Application.Reports;

/// <summary>
/// Builds the financial-statement SUPPORTING report PDF (งบแสดงฐานะการเงิน + งบกำไรขาดทุน) for a fiscal
/// year (CE), to attach to / reference when filing ภ.ง.ด.50. Read-only: derives the same FY-end as
/// <c>Pnd50FilingService</c> from the company fiscal-year-start month and reuses
/// <see cref="IFinancialReportService"/> (BalanceSheetAsync + ProfitLossAsync) so the figures match the
/// ภ.ง.ด.50 form. NOT the audited DBD งบการเงิน — labeled as a management/supporting report.
/// </summary>
public interface IFinancialStatementPdfService
{
    Task<byte[]> BuildAsync(int year, CancellationToken ct);
}
