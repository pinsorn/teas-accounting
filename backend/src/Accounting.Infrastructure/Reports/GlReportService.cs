using Accounting.Application.Reports;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Aggregates posted journal lines into Trial Balance and P&amp;L.
/// Only POSTED journals contribute. Tenant filter applies via DbContext.
/// </summary>
public sealed class GlReportService : IGlReportService
{
    private readonly AccountingDbContext _db;
    public GlReportService(AccountingDbContext db) => _db = db;

    public async Task<TrialBalance> GetTrialBalanceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromDate = from;
        var toDate = to;
        var rows = await (
            from j in _db.JournalEntries
            where j.Status == DocumentStatus.Posted && j.DocDate >= fromDate && j.DocDate <= toDate
            from l in j.Lines
            join a in _db.ChartOfAccounts on l.AccountId equals a.AccountId
            group new { l, a } by new { a.AccountCode, a.AccountNameTh, a.AccountType, a.NormalBalance }
            into g
            orderby g.Key.AccountCode
            select new TrialBalanceRow(
                g.Key.AccountCode,
                g.Key.AccountNameTh,
                g.Key.AccountType,
                g.Sum(x => x.l.DebitAmount),
                g.Sum(x => x.l.CreditAmount),
                g.Sum(x => x.l.DebitAmount) - g.Sum(x => x.l.CreditAmount))
        ).ToListAsync(ct);

        return new TrialBalance(
            From: fromDate, To: toDate, Rows: rows,
            DebitGrandTotal:  rows.Sum(r => r.DebitTotal),
            CreditGrandTotal: rows.Sum(r => r.CreditTotal));
    }

    public async Task<ProfitLoss> GetProfitLossAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var tb = await GetTrialBalanceAsync(from, to, ct);

        var revenue = tb.Rows
            .Where(r => r.AccountType == AccountType.Revenue)
            .Select(r => new ProfitLossRow(r.AccountCode, r.AccountNameTh, -r.Balance))   // revenue normal = CR → flip
            .ToList();
        var expense = tb.Rows
            .Where(r => r.AccountType == AccountType.Expense)
            .Select(r => new ProfitLossRow(r.AccountCode, r.AccountNameTh, r.Balance))
            .ToList();

        var totalRevenue = revenue.Sum(r => r.Amount);
        var totalExpense = expense.Sum(r => r.Amount);

        return new ProfitLoss(
            From: from, To: to,
            Revenue: revenue, Expense: expense,
            TotalRevenue: totalRevenue,
            TotalExpense: totalExpense,
            NetProfit:    totalRevenue - totalExpense);
    }
}
