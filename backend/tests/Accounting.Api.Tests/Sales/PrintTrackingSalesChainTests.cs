using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// cont.69 Phase 4 (D8) — universal original/copy print tracking extended to the
/// non-fiscal sales chain (Quotation / SalesOrder / DeliveryOrder / Invoice).
/// First original print stamps OriginalPrintedAt + WasReprint=false; a second
/// original print returns WasReprint=true (FE downgrades it to สำเนา). Real Postgres.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PrintTrackingSalesChainTests
{
    private readonly PostgresFixture _fx;
    public PrintTrackingSalesChainTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // §4.6 per-company-vat-mode — VAT mode comes from companies.vat_registered;
            // company 1 is seeded VAT-registered, so no config flag is needed.
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    // A standalone Draft Quotation is enough to exercise print tracking.
    private static async Task<long> CreateQuotationAsync(ServiceProvider sp, long cust)
    {
        await using var s = sp.CreateAsyncScope();
        var qsvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var date = new DateOnly(2026, 5, 18);
        var line = new ChainLineInput(null, "งานทดสอบ พิมพ์", 1m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m);
        return await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            date, date.AddDays(30), cust, null, "THB", 1m, null, null, [line]), default);
    }

    [SkippableFact]
    public async Task First_original_print_stamps_then_reprint_downgrades()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var qId = await CreateQuotationAsync(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var print = s.ServiceProvider.GetRequiredService<IPrintTrackingService>();

        // 1st original — fresh, so WasReprint=false + OriginalPrintedAt set.
        var first = await print.MarkPrintedAsync(PrintDocType.Quotation, qId, isCopy: false, default);
        first.Should().NotBeNull();
        first!.WasReprint.Should().BeFalse();
        first.OriginalPrintedAt.Should().NotBeNull();
        first.PrintCount.Should().Be(1);

        // 2nd original — original already exists, so WasReprint=true (FE → สำเนา).
        var second = await print.MarkPrintedAsync(PrintDocType.Quotation, qId, isCopy: false, default);
        second.Should().NotBeNull();
        second!.WasReprint.Should().BeTrue();
        second.PrintCount.Should().Be(2);

        // The stamped timestamp must NOT change on reprint (immutable first-print).
        second.OriginalPrintedAt.Should().Be(first.OriginalPrintedAt);
    }

    [SkippableFact]
    public async Task Explicit_copy_does_not_stamp_original()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var qId = await CreateQuotationAsync(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var print = s.ServiceProvider.GetRequiredService<IPrintTrackingService>();

        // Printing a copy first never stamps OriginalPrintedAt.
        var r = await print.MarkPrintedAsync(PrintDocType.Quotation, qId, isCopy: true, default);
        r.Should().NotBeNull();
        r!.WasReprint.Should().BeFalse();
        r.OriginalPrintedAt.Should().BeNull();
        r.PrintCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task Unknown_id_returns_null()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var print = s.ServiceProvider.GetRequiredService<IPrintTrackingService>();

        var r = await print.MarkPrintedAsync(PrintDocType.DeliveryOrder, 999_999_999, isCopy: false, default);
        r.Should().BeNull();
    }
}
