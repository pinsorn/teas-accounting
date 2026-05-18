namespace Accounting.Application.Reports;

// Sprint 9 names are *Report to avoid colliding with the pre-existing unused
// GlReportDtos TrialBalance/ProfitLoss scaffold (range-based, no BU, not
// endpoint-wired) — left intact, no breaking change. See Report-Backend14.

// ── A1 Trial Balance (as-of, normal_balance, balanced invariant) ───────────
public sealed record TrialBalanceReportRow(
    string AccountCode, string AccountNameTh, string AccountType,
    string NormalBalance, decimal Debit, decimal Credit, decimal Net);

public sealed record TrialBalanceTotals(decimal Debit, decimal Credit, bool Balanced);

public sealed record TrialBalanceReport(
    DateOnly AsOfDate, int CompanyId,
    IReadOnlyList<TrialBalanceReportRow> Rows, TrialBalanceTotals Totals);

// ── A2 P&L by BU (R-Q1a: flat Revenue − Expense = NetProfit; no GP/COGS) ────
public sealed record ProfitLossGroup(
    int? BusinessUnitId, string? BusinessUnitCode, string GroupName,
    decimal Revenue, decimal Expense, decimal NetProfit);

public sealed record ProfitLossReport(
    DateOnly From, DateOnly To,
    IReadOnlyList<ProfitLossGroup> Groups,
    ProfitLossGroup Totals,
    string Note);

// ── A3 Sales Summary (R-Q2: customer | business_unit only) ─────────────────
public sealed record SalesSummaryRow(
    string Dimension, string Label, int DocCount,
    decimal Subtotal, decimal Vat, decimal Total);

public sealed record SalesSummary(
    DateOnly From, DateOnly To, string GroupBy,
    IReadOnlyList<SalesSummaryRow> Rows,
    SalesSummaryRow Totals);

public interface IFinancialReportService
{
    Task<TrialBalanceReport> TrialBalanceAsync(DateOnly asOfDate, bool includeInactive, CancellationToken ct);
    Task<ProfitLossReport> ProfitLossAsync(
        DateOnly from, DateOnly to, int? businessUnitId, bool includeUnspecified, CancellationToken ct);
    /// <summary><paramref name="groupBy"/> = "customer" | "business_unit".
    /// "product" throws <see cref="Accounting.Domain.Common.DomainException"/>
    /// code "report.product_unsupported" (Product master = Sprint 10).</summary>
    Task<SalesSummary> SalesSummaryAsync(
        DateOnly from, DateOnly to, string groupBy, CancellationToken ct);
}
