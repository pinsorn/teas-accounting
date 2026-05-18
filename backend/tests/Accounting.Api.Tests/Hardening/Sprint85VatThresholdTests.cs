using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 8.5 — ม.85/1 VAT-registration threshold. Each case uses a unique
/// company id so the rolling-12-month sum is isolated from other suites'
/// Tax Invoices (tenant query filter scopes the sum).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint85VatThresholdTests
{
    private readonly PostgresFixture _fx;
    public Sprint85VatThresholdTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, bool vatMode)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                ["Tax:VatMode"] = vatMode ? "true" : "false",
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        return services
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task SeedPostedRevenue(
        ServiceProvider sp, int companyId, decimal totalThb)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.TaxInvoices.Add(new TaxInvoice
        {
            CompanyId = companyId, BranchId = 1,
            DocDate = new DateOnly(2026, 5, 16), TaxPointDate = new DateOnly(2026, 5, 16),
            SupplierTaxId = "0000000000000", SupplierBranchCode = "00000",
            SupplierBranchName = "สำนักงานใหญ่", SupplierName = "T", SupplierAddress = "A",
            CustomerName = "C", CustomerAddress = "A",
            CurrencyCode = "THB", ExchangeRate = 1m,
            TotalAmount = totalThb, TotalAmountThb = totalThb,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow,
            DocNo = $"05-2026-TI-{TestIds.Suffix()[..6]}",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<RevenueThresholdStatus> Check(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<IVatThresholdService>()
            .CheckAsync(default);
    }

    // teas_test persists across runs — a fixed companyId would accumulate
    // seeded revenue across sessions and tip the band. Use a per-run-unique id;
    // the tenant query filter scopes the threshold sum to exactly this company.
    private static int NewCompanyId() => Random.Shared.Next(1_000_000, 2_000_000_000);

    [SkippableFact]
    public async Task Vat_registered_company_is_not_applicable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var cid = NewCompanyId();
        await using var sp = Provider(companyId: cid, vatMode: true);
        await SeedPostedRevenue(sp, cid, 5_000_000m); // ignored — already VAT
        (await Check(sp)).Should().Be(RevenueThresholdStatus.NotApplicable);
    }

    [SkippableFact]
    public async Task Below_1_5M_is_ok()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var cid = NewCompanyId();
        await using var sp = Provider(companyId: cid, vatMode: false);
        await SeedPostedRevenue(sp, cid, 1_000_000m);
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Ok);
    }

    [SkippableFact]
    public async Task Between_1_5M_and_1_8M_is_approaching()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var cid = NewCompanyId();
        await using var sp = Provider(companyId: cid, vatMode: false);
        await SeedPostedRevenue(sp, cid, 1_600_000m);
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Approaching);
    }

    [SkippableFact]
    public async Task At_or_above_1_8M_is_exceeded()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var cid = NewCompanyId();
        await using var sp = Provider(companyId: cid, vatMode: false);
        await SeedPostedRevenue(sp, cid, 1_200_000m);
        await SeedPostedRevenue(sp, cid, 700_000m); // cumulative 1.9M
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Exceeded);
    }
}
