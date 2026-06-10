using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// Phase C-C — real Balance Sheet (งบแสดงฐานะการเงิน, locked decision #3).
/// Headline assertion: Σ assets == Σ liabilities + equity + current-period earnings —
/// the double-entry invariant restated as-of a date. Cross-checked against the
/// Trial Balance at the same date.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BalanceSheetTests
{
    private readonly PostgresFixture _fx;
    public BalanceSheetTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task PostTi(ServiceProvider sp, decimal price)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cust = await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", price, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
    }

    [SkippableFact]
    public async Task Balance_sheet_balances_and_classifies_by_account_type()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await PostTi(sp, 4_321m); // guarantee at least one AR/VAT/revenue posting

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var bs = await svc.BalanceSheetAsync(new DateOnly(2100, 12, 31), default);

        bs.Balanced.Should().BeTrue("Σ assets == Σ liabilities + equity + current earnings");
        bs.Assets.Total.Should().Be(bs.LiabilitiesAndEquityTotal);
        bs.LiabilitiesAndEquityTotal.Should()
            .Be(bs.Liabilities.Total + bs.Equity.Total + bs.CurrentPeriodEarnings);

        bs.Assets.Rows.Should().NotBeEmpty();
        bs.Assets.Rows.Should().OnlyContain(r => r.Balance != 0m, "zero-balance accounts hidden");
        // (NotContain: liability/equity sections may legitimately be empty on the test DB.)
        bs.Liabilities.Rows.Should().NotContain(r => r.Balance == 0m);
        bs.Equity.Rows.Should().NotContain(r => r.Balance == 0m);

        bs.Assets.Rows.Should().BeInAscendingOrder(r => r.AccountCode);
        bs.Assets.Total.Should().Be(bs.Assets.Rows.Sum(r => r.Balance));
        bs.Note.Should().Contain("Phase 2"); // single-line earnings deferral disclosed
    }

    [SkippableFact]
    public async Task Balance_sheet_assets_total_cross_checks_trial_balance_at_same_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await PostTi(sp, 1_234m);

        var asOf = new DateOnly(2100, 12, 31);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var bs = await svc.BalanceSheetAsync(asOf, default);
        var tb = await svc.TrialBalanceAsync(asOf, includeInactive: true, default);

        // Asset balance = Dr − Cr = TB net; the BS assets total must equal the TB Σ net
        // of ASSET accounts at the same date (same Posted-journal universe).
        bs.Assets.Total.Should().Be(
            tb.Rows.Where(r => r.AccountType == "ASSET").Sum(r => r.Net));
        // Liability/Equity flip the sign (Cr − Dr = −net).
        bs.Liabilities.Total.Should().Be(
            -tb.Rows.Where(r => r.AccountType == "LIABILITY").Sum(r => r.Net));
        bs.Equity.Total.Should().Be(
            -tb.Rows.Where(r => r.AccountType == "EQUITY").Sum(r => r.Net));
    }
}
