using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Sprint 9 Part A — Trial Balance, P&amp;L by BU, Sales Summary. Tenant scoping
/// is the DbContext global query filter. All reads use Posted documents only.
/// </summary>
public sealed class FinancialReportService(AccountingDbContext db, ITenantContext tenant)
    : IFinancialReportService
{
    private sealed record PlRow(int? Bu, AccountType Type, decimal Dr, decimal Cr);

    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<TrialBalanceReport> TrialBalanceAsync(
        DateOnly asOfDate, bool includeInactive, CancellationToken ct)
    {
        EnsureAuth();

        // Per-account Dr/Cr over Posted journals on/before the as-of date.
        var sums = await (
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            where j.Status == DocumentStatus.Posted && j.DocDate <= asOfDate
            group l by l.AccountId into g
            select new { AccountId = g.Key,
                          Debit = g.Sum(x => x.DebitAmount),
                          Credit = g.Sum(x => x.CreditAmount) })
            .ToDictionaryAsync(x => x.AccountId, x => x, ct);

        var accounts = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.AccountCode)
            .Select(a => new { a.AccountId, a.AccountCode, a.AccountNameTh,
                               a.AccountType, a.NormalBalance })
            .ToListAsync(ct);

        var rows = new List<TrialBalanceReportRow>();
        decimal td = 0m, tc = 0m;
        foreach (var a in accounts)
        {
            var s = sums.GetValueOrDefault(a.AccountId);
            var dr = s?.Debit ?? 0m;
            var cr = s?.Credit ?? 0m;
            td += dr; tc += cr;
            rows.Add(new TrialBalanceReportRow(
                a.AccountCode, a.AccountNameTh,
                a.AccountType.ToString().ToUpperInvariant(),
                a.NormalBalance == NormalBalance.Debit ? "DR" : "CR",
                dr, cr, dr - cr));
        }

        return new TrialBalanceReport(asOfDate, tenant.CompanyId, rows,
            new TrialBalanceTotals(td, tc, td == tc));
    }

    /// <summary>
    /// C-C — งบแสดงฐานะการเงิน as-of date (locked decision #3; feeds ภ.ง.ด.50 + DBD).
    /// Same query shape as <see cref="TrialBalanceAsync"/>: Posted journals on/before the
    /// as-of date, grouped per account. Asset = Dr−Cr; Liability/Equity = Cr−Dr;
    /// CurrentPeriodEarnings = Σ Revenue(Cr−Dr) − Σ Expense(Dr−Cr) cumulative (period-close
    /// journals already in GL net themselves out, so this is correct whether or not closes ran).
    /// Zero-balance rows are dropped; rows ordered by AccountCode.
    /// </summary>
    public async Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOfDate, CancellationToken ct)
    {
        EnsureAuth();

        var sums = await (
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            where j.Status == DocumentStatus.Posted && j.DocDate <= asOfDate
            group l by l.AccountId into g
            select new { AccountId = g.Key,
                          Debit = g.Sum(x => x.DebitAmount),
                          Credit = g.Sum(x => x.CreditAmount) })
            .ToDictionaryAsync(x => x.AccountId, x => x, ct);

        // All accounts (active or not) — an inactive account with a balance must
        // still appear or the sheet won't balance; zero rows are dropped below.
        var accounts = await db.ChartOfAccounts.AsNoTracking()
            .OrderBy(a => a.AccountCode)
            .Select(a => new { a.AccountId, a.AccountCode, a.AccountNameTh, a.AccountType })
            .ToListAsync(ct);

        var assets = new List<BalanceSheetRow>();
        var liabilities = new List<BalanceSheetRow>();
        var equity = new List<BalanceSheetRow>();
        decimal assetTotal = 0m, liabTotal = 0m, equityTotal = 0m, earnings = 0m;

        static void Add(List<BalanceSheetRow> rows, string code, string name, decimal bal)
        {
            if (bal != 0m) rows.Add(new BalanceSheetRow(code, name, bal));
        }

        foreach (var a in accounts)
        {
            var s = sums.GetValueOrDefault(a.AccountId);
            if (s is null) continue;
            switch (a.AccountType)
            {
                case AccountType.Asset:
                    var ab = s.Debit - s.Credit;
                    assetTotal += ab;
                    Add(assets, a.AccountCode, a.AccountNameTh, ab);
                    break;
                case AccountType.Liability:
                    var lb = s.Credit - s.Debit;
                    liabTotal += lb;
                    Add(liabilities, a.AccountCode, a.AccountNameTh, lb);
                    break;
                case AccountType.Equity:
                    var eb = s.Credit - s.Debit;
                    equityTotal += eb;
                    Add(equity, a.AccountCode, a.AccountNameTh, eb);
                    break;
                case AccountType.Revenue:
                    earnings += s.Credit - s.Debit;
                    break;
                case AccountType.Expense:
                    earnings -= s.Debit - s.Credit;
                    break;
            }
        }

        var liabAndEquity = liabTotal + equityTotal + earnings;
        return new BalanceSheetReport(
            asOfDate, tenant.CompanyId,
            new BalanceSheetSection(assets, assetTotal),
            new BalanceSheetSection(liabilities, liabTotal),
            new BalanceSheetSection(equity, equityTotal),
            earnings, liabAndEquity, assetTotal == liabAndEquity,
            "Current-period earnings (กำไร(ขาดทุน)สะสมยังไม่ปิดงวด) appear as a single " +
            "computed line — cumulative Revenue − Expense up to the as-of date — not " +
            "per-account, until period-close maturity (Phase 2).");
    }

    public async Task<ProfitLossReport> ProfitLossAsync(
        DateOnly fromDate, DateOnly toDate, int? businessUnitId,
        bool includeUnspecified, CancellationToken ct)
    {
        EnsureAuth();

        var raw = await db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking(), l => l.JournalId, j => j.JournalId,
                  (l, j) => new { l, j })
            .Join(db.ChartOfAccounts.AsNoTracking(), x => x.l.AccountId, a => a.AccountId,
                  (x, a) => new { x.l, x.j, a })
            .Where(x => x.j.Status == DocumentStatus.Posted
                        && x.j.DocDate >= fromDate && x.j.DocDate <= toDate
                        && (x.a.AccountType == AccountType.Revenue
                            || x.a.AccountType == AccountType.Expense))
            .Select(x => new PlRow(x.l.BusinessUnitId, x.a.AccountType,
                                    x.l.DebitAmount, x.l.CreditAmount))
            .ToListAsync(ct);

        var rows = businessUnitId is { } bu
            ? raw.Where(r => r.Bu == bu).ToList()
            : includeUnspecified ? raw : raw.Where(r => r.Bu != null).ToList();

        var buCodes = await db.BusinessUnits.AsNoTracking()
            .ToDictionaryAsync(b => b.BusinessUnitId, b => b.Code, ct);

        ProfitLossGroup Build(int? buId, IEnumerable<PlRow> ls, string name)
        {
            decimal rev = 0m, exp = 0m;
            foreach (var r in ls)
            {
                if (r.Type == AccountType.Revenue) rev += r.Cr - r.Dr;
                else exp += r.Dr - r.Cr;
            }
            return new ProfitLossGroup(
                buId, buId is { } x ? buCodes.GetValueOrDefault(x) : null,
                name, rev, exp, rev - exp);
        }

        var groups = rows.GroupBy(r => r.Bu)
            .OrderBy(g => g.Key ?? int.MaxValue)
            .Select(g => Build(g.Key,
                g, g.Key is { } x ? buCodes.GetValueOrDefault(x) ?? "?" : "(ไม่ระบุ BU)"))
            .ToList();
        var totals = Build(null, rows, "TOTAL");

        return new ProfitLossReport(fromDate, toDate, groups, totals,
            "P&L is flat Revenue − Expense by BU. COGS / gross-profit / " +
            "operating-expense breakdown is deferred to Phase 2 (CoA " +
            "account_subtype classification — see plan.md §23.2). The API will " +
            "extend additively (cogs/gross_profit/operating_expense) with no " +
            "breaking change.");
    }

    public async Task<SalesSummary> SalesSummaryAsync(
        DateOnly from, DateOnly to, string groupBy, CancellationToken ct)
    {
        EnsureAuth();
        if (groupBy is not ("customer" or "business_unit" or "product"))
            throw new DomainException("report.bad_group_by",
                "group_by must be 'customer' | 'business_unit' | 'product'.");

        // Sprint 10 A6 — by-product is line-level (join TI lines → products;
        // lines with no product → "(no product)"). Re-enabled (Sprint 9 R-Q2).
        if (groupBy == "product")
        {
            var lines = await db.TaxInvoiceLines.AsNoTracking()
                .Join(db.TaxInvoices.AsNoTracking(),
                      l => l.TaxInvoiceId, t => t.TaxInvoiceId, (l, t) => new { l, t })
                .Where(x => x.t.Status == DocumentStatus.Posted
                         && x.t.DocDate >= from && x.t.DocDate <= to)
                .Select(x => new { x.l.ProductId, x.l.TaxInvoiceId,
                                   x.l.LineAmount, x.l.TaxAmount, x.l.TotalAmount })
                .ToListAsync(ct);
            var names = await db.Products.AsNoTracking()
                .ToDictionaryAsync(p => p.ProductId,
                    p => p.ProductCode + " · " + p.NameTh, ct);
            var pRows = lines.GroupBy(x => x.ProductId)
                .OrderBy(g => g.Key ?? long.MaxValue)
                .Select(g => new SalesSummaryRow(
                    "product",
                    g.Key is { } pid ? names.GetValueOrDefault(pid) ?? "?" : "(no product)",
                    g.Select(x => x.TaxInvoiceId).Distinct().Count(),
                    g.Sum(x => x.LineAmount), g.Sum(x => x.TaxAmount),
                    g.Sum(x => x.TotalAmount))).ToList();
            var pTotals = new SalesSummaryRow("product", "TOTAL",
                lines.Select(x => x.TaxInvoiceId).Distinct().Count(),
                lines.Sum(x => x.LineAmount), lines.Sum(x => x.TaxAmount),
                lines.Sum(x => x.TotalAmount));
            return new SalesSummary(from, to, groupBy, pRows, pTotals);
        }

        var tis = await db.TaxInvoices.AsNoTracking()
            .Where(t => t.Status == DocumentStatus.Posted
                        && t.DocDate >= from && t.DocDate <= to)
            .Select(t => new { t.CustomerId, t.CustomerName, t.BusinessUnitId,
                               t.SubtotalAmount, t.TaxAmount, t.TotalAmount })
            .ToListAsync(ct);

        List<SalesSummaryRow> rows;
        if (groupBy == "customer")
        {
            rows = tis.GroupBy(t => new { t.CustomerId, t.CustomerName })
                .OrderBy(g => g.Key.CustomerName)
                .Select(g => new SalesSummaryRow(
                    "customer", g.Key.CustomerName, g.Count(),
                    g.Sum(x => x.SubtotalAmount), g.Sum(x => x.TaxAmount),
                    g.Sum(x => x.TotalAmount))).ToList();
        }
        else
        {
            var buCodes = await db.BusinessUnits.AsNoTracking()
                .ToDictionaryAsync(b => b.BusinessUnitId, b => b.Code, ct);
            rows = tis.GroupBy(t => t.BusinessUnitId)
                .OrderBy(g => g.Key ?? int.MaxValue)
                .Select(g => new SalesSummaryRow(
                    "business_unit",
                    g.Key is { } b ? buCodes.GetValueOrDefault(b) ?? "?" : "(ไม่ระบุ BU)",
                    g.Count(), g.Sum(x => x.SubtotalAmount),
                    g.Sum(x => x.TaxAmount), g.Sum(x => x.TotalAmount))).ToList();
        }

        var totals = new SalesSummaryRow(groupBy, "TOTAL", tis.Count,
            tis.Sum(x => x.SubtotalAmount), tis.Sum(x => x.TaxAmount),
            tis.Sum(x => x.TotalAmount));
        return new SalesSummary(from, to, groupBy, rows, totals);
    }
}
