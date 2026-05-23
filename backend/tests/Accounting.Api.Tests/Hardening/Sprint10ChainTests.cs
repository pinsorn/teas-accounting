using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
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
/// Sprint 10 Part B — Q→SO→DO→TI chain: conversion, BU cascade, partial
/// delivery + SO auto-close, Pattern X (combined auto-TI) and Pattern Y.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint10ChainTests
{
    private readonly PostgresFixture _fx;
    public Sprint10ChainTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
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

    private static ChainLineInput Line(decimal qty, decimal price) =>
        new(null, "line", qty, "ชิ้น", price, 0m, 1, "VAT7", 0.07m);

    [SkippableFact]
    public async Task Quotation_to_so_to_do_combined_creates_linked_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await using var s = sp.CreateAsyncScope();
        var qsvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            new DateOnly(2026, 5, 16), new DateOnly(2026, 6, 16), cust, null,
            "THB", 1m, null, null, [Line(2m, 1000m)]), default);

        // Lifecycle guards: cannot convert before Accepted.
        var early = () => qsvc.ConvertToSalesOrderAsync(qId, default);
        (await early.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("quotation.not_accepted");

        await qsvc.SendAsync(qId, default);
        await qsvc.AcceptAsync(qId, default);
        var soId = await qsvc.ConvertToSalesOrderAsync(qId, default);

        var qDet = await qsvc.GetAsync(qId, default);
        qDet!.ConvertedToSoId.Should().Be(soId);
        qDet.DocNo.Should().NotBeNull("Q number allocated on Send");

        await sosvc.PostAsync(soId, default);
        var soDet = await sosvc.GetAsync(soId, default);
        soDet!.DocNo.Should().NotBeNull("SO number allocated on Post");
        var soLineId = await SoLineId(s, soId);

        // cont.69 Phase 1 — IsCombinedWithTi no longer auto-creates a TI on delivery.
        // MarkDelivered is now a STATUS CHANGE ONLY (drops the combined-TI auto path,
        // fixes the non-VAT 422). The TI is issued manually from the Invoice step.
        // Link the DO line to the SO line so delivered-qty (2 of 2) closes the SO.
        var doId = await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 17), cust, null, IsCombinedWithTi: true, null, soId,
            [new DeliveryLineInput(soLineId, null, "line", 2m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        await dosvc.IssueAsync(doId, default);
        var doIssued = await dosvc.GetAsync(doId, default);
        doIssued!.DocNo.Should().NotBeNull("DO number allocated on Issue");
        doIssued.TaxInvoiceId.Should().BeNull("TI is no longer created on the DO");

        await dosvc.MarkDeliveredAsync(doId, default);
        var doDet = await dosvc.GetAsync(doId, default);
        doDet!.Status.Should().Be("Delivered");
        doDet.TaxInvoiceId.Should().BeNull("cont.69 — mark-delivered no longer auto-creates a TI");

        // SO fully delivered (2 of 2) → Closed.
        (await sosvc.GetAsync(soId, default))!.Status.Should().Be("Closed");
    }

    [SkippableFact]
    public async Task Partial_delivery_keeps_so_open_until_fully_delivered()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await using var s = sp.CreateAsyncScope();
        var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();

        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            new DateOnly(2026, 5, 16), null, cust, null, "THB", 1m, null, null,
            [Line(10m, 100m)]), default);
        await sosvc.PostAsync(soId, default);
        var soLineId = await SoLineId(s, soId);

        // Deliver 4 of 10 → SO still Posted.
        await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 17), cust, null, false, null, soId,
            [new DeliveryLineInput(soLineId, null, "line", 4m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m)]),
            default);
        (await sosvc.GetAsync(soId, default))!.Status.Should().Be("Posted");

        // Deliver remaining 6 → SO Closed.
        await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 18), cust, null, false, null, soId,
            [new DeliveryLineInput(soLineId, null, "line", 6m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m)]),
            default);
        (await sosvc.GetAsync(soId, default))!.Status.Should().Be("Closed");
    }

    [SkippableFact]
    public async Task Pattern_y_creates_ti_from_posted_do()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await using var s = sp.CreateAsyncScope();
        var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();

        var doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 17), cust, null, IsCombinedWithTi: false, null, null,
            [new DeliveryLineInput(null, null, "line", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
            default);
        // Sprint 13h P9 — Pattern Y needs Delivered state before manual TI creation.
        await dosvc.IssueAsync(doId, default);
        await dosvc.MarkDeliveredAsync(doId, default);
        (await dosvc.GetAsync(doId, default))!.TaxInvoiceId
            .Should().BeNull("not combined — no auto TI");

        var tiId = await dosvc.CreateTaxInvoiceAsync(doId, default);
        (await dosvc.GetAsync(doId, default))!.TaxInvoiceId.Should().Be(tiId);

        // Second attempt rejected (TI already exists).
        var again = () => dosvc.CreateTaxInvoiceAsync(doId, default);
        (await again.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("do.ti_exists");
    }

    private static async Task<long> SoLineId(IServiceScope s, long soId)
    {
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.SalesOrderLines.Where(l => l.SalesOrderId == soId)
            .Select(l => l.LineId).FirstAsync();
    }
}
