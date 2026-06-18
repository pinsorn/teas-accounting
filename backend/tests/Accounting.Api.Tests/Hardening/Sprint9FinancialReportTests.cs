using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 9 Part A — financial reports. The Trial Balance Dr==Cr invariant is
/// the headline assertion: it surfaces any silent GL imbalance instantly.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint9FinancialReportTests
{
    private readonly PostgresFixture _fx;
    public Sprint9FinancialReportTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static async Task<int> CreateBu(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        return await svc.CreateAsync(new CreateBusinessUnitRequest(code, "BU " + code, code, null), default);
    }

    private static async Task PostTi(ServiceProvider sp, long cust, decimal price, int? bu)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", price, 0m, 1, "VAT7", 0.07m)],
            bu), default);
        await svc.PostAsync(id, default);
    }

    [SkippableFact]
    public async Task Trial_balance_is_always_balanced_Dr_equals_Cr()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 12345m, null);
        await PostTi(sp, cust, 6789m, null);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var tb = await svc.TrialBalanceAsync(new DateOnly(2026, 12, 31), false, default);

        tb.Totals.Debit.Should().Be(tb.Totals.Credit);
        tb.Totals.Balanced.Should().BeTrue("double-entry invariant — any imbalance is a GL bug");
        tb.Rows.Should().NotBeEmpty();
        // Per-account net = debit − credit.
        tb.Rows.Should().OnlyContain(r => r.Net == r.Debit - r.Credit);
    }

    [SkippableFact]
    public async Task Profit_loss_groups_by_bu_flat_revenue_minus_expense()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var bu = await CreateBu(sp, "PL" + Sfx());
        await PostTi(sp, cust, 10000m, bu);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        // Batch-A ① — the TI is server-pinned to today's period; query the CURRENT Bangkok month.
        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var pl = await svc.ProfitLossAsync(from, to, bu, false, default);

        var g = pl.Groups.Single(x => x.BusinessUnitId == bu);
        g.Revenue.Should().BeGreaterThan(0m);
        g.NetProfit.Should().Be(g.Revenue - g.Expense);
        pl.Note.Should().Contain("Phase 2");   // GP/COGS deferral disclosed
    }

    [SkippableFact]
    public async Task Sales_summary_by_customer_sums_posted_tis()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 5000m, null);

        // TI is server-pinned to today's Bangkok date (ม.86/4(7)); query the current Bangkok month.
        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var ss = await svc.SalesSummaryAsync(from, to, "customer", default);

        ss.Rows.Should().Contain(r => r.Subtotal > 0m && r.Total >= r.Subtotal);
        ss.Totals.Total.Should().Be(ss.Rows.Sum(r => r.Total));
    }

    // Sprint 9 R-Q2 rejected group_by=product "until Sprint 10". Sprint 10 A6
    // reversed that (Product master shipped) — see Sprint10ProductTests for the
    // product-grouping coverage. The still-valid invariant: an UNKNOWN group_by
    // is rejected.
    [SkippableFact]
    public async Task Sales_summary_rejects_unknown_group_by()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var act = () => svc.SalesSummaryAsync(
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "nonsense", default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("report.bad_group_by");
    }

    [SkippableFact]
    public async Task Wht_receivable_aging_has_buckets()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtReceivableReportService>();
        var aging = await svc.GetAgingAsync(default);
        aging.Buckets.Should().NotBeNull();
        (aging.Buckets.Current + aging.Buckets.Days30 + aging.Buckets.Days60
            + aging.Buckets.Days90Plus).Should().Be(aging.TotalOutstanding);
    }
}
