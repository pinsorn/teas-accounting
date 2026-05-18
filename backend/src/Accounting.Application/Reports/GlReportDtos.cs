using Accounting.Domain.Enums;

namespace Accounting.Application.Reports;

public sealed record TrialBalanceRow(
    string  AccountCode,
    string  AccountNameTh,
    AccountType AccountType,
    decimal DebitTotal,
    decimal CreditTotal,
    decimal Balance);   // DR-positive, CR-negative

public sealed record TrialBalance(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal DebitGrandTotal,
    decimal CreditGrandTotal);

public sealed record ProfitLossRow(
    string AccountCode,
    string AccountNameTh,
    decimal Amount);

public sealed record ProfitLoss(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ProfitLossRow> Revenue,
    IReadOnlyList<ProfitLossRow> Expense,
    decimal TotalRevenue,
    decimal TotalExpense,
    decimal NetProfit);

public interface IGlReportService
{
    Task<TrialBalance> GetTrialBalanceAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<ProfitLoss>   GetProfitLossAsync (DateOnly from, DateOnly to, CancellationToken ct);
}
