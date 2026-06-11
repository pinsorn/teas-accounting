using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 8.5 — ม.85/1 VAT-registration threshold. Each case uses its OWN company
/// (per-company-vat-mode spec §4.6: the VAT switch is companies.vat_registered, read
/// by CompanyTaxConfigService — the old in-memory Tax:VatMode config is a no-op) so
/// the rolling-12-month sum is isolated from other suites' Tax Invoices (tenant
/// query filter scopes the sum). teas_test persists across runs — a fresh company
/// per test also prevents seeded revenue accumulating across sessions.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint85VatThresholdTests
{
    private readonly PostgresFixture _fx;
    public Sprint85VatThresholdTests(PostgresFixture fx) => _fx = fx;

    private static async Task SeedPostedRevenue(
        ServiceProvider sp, TestCompanyFactory.SeededCompany c, decimal totalThb)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.TaxInvoices.Add(new TaxInvoice
        {
            CompanyId = c.CompanyId, BranchId = c.BranchId,
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

    private async Task<(TestCompanyFactory.SeededCompany c, ServiceProvider sp)> CompanyAsync(bool vatRegistered)
    {
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered);
        return (c, TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId));
    }

    [SkippableFact]
    public async Task Vat_registered_company_is_not_applicable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (c, sp) = await CompanyAsync(vatRegistered: true);
        await using var _ = sp;
        await SeedPostedRevenue(sp, c, 5_000_000m); // ignored — already VAT
        (await Check(sp)).Should().Be(RevenueThresholdStatus.NotApplicable);
    }

    [SkippableFact]
    public async Task Below_1_5M_is_ok()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (c, sp) = await CompanyAsync(vatRegistered: false);
        await using var _ = sp;
        await SeedPostedRevenue(sp, c, 1_000_000m);
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Ok);
    }

    [SkippableFact]
    public async Task Between_1_5M_and_1_8M_is_approaching()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (c, sp) = await CompanyAsync(vatRegistered: false);
        await using var _ = sp;
        await SeedPostedRevenue(sp, c, 1_600_000m);
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Approaching);
    }

    [SkippableFact]
    public async Task At_or_above_1_8M_is_exceeded()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (c, sp) = await CompanyAsync(vatRegistered: false);
        await using var _ = sp;
        await SeedPostedRevenue(sp, c, 1_200_000m);
        await SeedPostedRevenue(sp, c, 700_000m); // cumulative 1.9M
        (await Check(sp)).Should().Be(RevenueThresholdStatus.Exceeded);
    }
}
