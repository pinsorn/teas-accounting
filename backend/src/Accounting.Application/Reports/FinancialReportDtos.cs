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

// ── C-C Balance Sheet (งบแสดงฐานะการเงิน) — assets / liabilities / equity as-of date.
public sealed record BalanceSheetRow(string AccountCode, string AccountNameTh, decimal Balance);

public sealed record BalanceSheetSection(IReadOnlyList<BalanceSheetRow> Rows, decimal Total);

public sealed record BalanceSheetReport(
    DateOnly AsOfDate, int CompanyId,
    BalanceSheetSection Assets, BalanceSheetSection Liabilities, BalanceSheetSection Equity,
    decimal CurrentPeriodEarnings,          // cumulative un-closed Revenue − Expense up to as-of
    decimal LiabilitiesAndEquityTotal,      // Liabilities.Total + Equity.Total + CurrentPeriodEarnings
    bool Balanced,                          // Assets.Total == LiabilitiesAndEquityTotal
    string Note);

// ── General Ledger (บัญชีแยกประเภท) — per-account drill-down, opening/rows/closing ──
// NOTE (spec-vs-reality): spec's DTO literal said `int AccountId`, but
// ChartOfAccount.AccountId is `long` in this codebase (BIGINT PK) — kept `long`
// throughout (DTOs, service signature, query params) to match the real column
// instead of truncating/narrowing. Everything else matches the spec verbatim.
public sealed record GeneralLedgerReport(
    long AccountId, string AccountCode, string AccountNameTh, string AccountType,
    string NormalBalance, DateOnly FromDate, DateOnly ToDate,
    decimal OpeningBalance, IReadOnlyList<GeneralLedgerRow> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance);

public sealed record GeneralLedgerRow(
    long JournalId, DateOnly DocDate, string DocNo, string? Description,
    string? Reference, decimal Debit, decimal Credit, decimal RunningBalance);

/// <summary>Account-picker option for the GL report screen (active, non-header accounts).</summary>
public sealed record GeneralLedgerAccountOption(
    long AccountId, string AccountCode, string AccountNameTh, string NormalBalance);

public interface IFinancialReportService
{
    Task<TrialBalanceReport> TrialBalanceAsync(DateOnly asOfDate, bool includeInactive, CancellationToken ct);
    Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOfDate, CancellationToken ct);
    Task<ProfitLossReport> ProfitLossAsync(
        DateOnly from, DateOnly to, int? businessUnitId, bool includeUnspecified, CancellationToken ct);
    /// <summary><paramref name="groupBy"/> = "customer" | "business_unit".
    /// "product" throws <see cref="Accounting.Domain.Common.DomainException"/>
    /// code "report.product_unsupported" (Product master = Sprint 10).</summary>
    Task<SalesSummary> SalesSummaryAsync(
        DateOnly from, DateOnly to, string groupBy, CancellationToken ct);
    /// <summary>Throws <see cref="Accounting.Domain.Common.DomainException"/> code
    /// "gl_account.not_found" (→ 404) when <paramref name="accountId"/> doesn't exist or
    /// belongs to another tenant (EF global filter hides cross-tenant rows identically).</summary>
    Task<GeneralLedgerReport> GeneralLedgerAsync(
        long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    /// <summary>Active, non-header accounts for the GL report's account picker.</summary>
    Task<IReadOnlyList<GeneralLedgerAccountOption>> GeneralLedgerAccountsAsync(CancellationToken ct);
}
