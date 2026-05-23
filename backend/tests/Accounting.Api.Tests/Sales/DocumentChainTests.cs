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
/// cont.69 Phase 3 (D7) — the unified full-chain resolver
/// <see cref="IDocumentCrossRefService.GetChainAsync"/>. Builds a complete VAT chain
///   Quotation → Sales Order → Delivery Order → Invoice (BillingNote) → Tax Invoice → Receipt
/// through the real services, then asserts the resolver returns EVERY node whether the
/// anchor is the Quotation (walk DOWN) or the Receipt (walk UP then DOWN). Real Postgres.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DocumentChainTests
{
    private readonly PostgresFixture _fx;
    public DocumentChainTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["Tax:VatMode"] = "true",
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

    private readonly record struct ChainIds(
        long QuotationId, long SalesOrderId, long DeliveryOrderId,
        long InvoiceId, long TaxInvoiceId, long ReceiptId);

    // Build the full VAT chain through the real services.
    private static async Task<ChainIds> BuildChainAsync(ServiceProvider sp, long cust)
    {
        long qId, soId, doId, invId, tiId, rcId;
        var date = new DateOnly(2026, 5, 18);
        var line = new ChainLineInput(null, "งานทดสอบ chain", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m);

        await using (var s = sp.CreateAsyncScope())
        {
            var qsvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
                date, date.AddDays(30), cust, null, "THB", 1m, null, null, [line]), default);
            await qsvc.SendAsync(qId, default);
            await qsvc.AcceptAsync(qId, default);
            soId = await qsvc.ConvertToSalesOrderAsync(qId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            await sosvc.PostAsync(soId, default);
            doId = await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
                date, cust, null, IsCombinedWithTi: false, null, soId,
                [new DeliveryLineInput(null, null, "งานทดสอบ chain", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            await dosvc.IssueAsync(doId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var bnsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(invId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tisvc.CreateFromBillingNoteAsync(invId, default);
            await tisvc.PostAsync(tiId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                new DateOnly(2026, 5, 18), cust, Accounting.Domain.Enums.PaymentMethod.Transfer,
                null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, 1070m, null, null)]),
                default);
            await rsvc.PostAsync(rcId, default);
        }

        return new ChainIds(qId, soId, doId, invId, tiId, rcId);
    }

    private static void AssertFullChain(DocumentChainDto? chain, ChainIds ids)
    {
        chain.Should().NotBeNull();
        chain!.Quotation!.Id.Should().Be(ids.QuotationId);
        chain.SalesOrder!.Id.Should().Be(ids.SalesOrderId);
        chain.DeliveryOrders.Select(n => n.Id).Should().Contain(ids.DeliveryOrderId);
        chain.Invoices.Select(n => n.Id).Should().Contain(ids.InvoiceId);
        chain.TaxInvoices.Select(n => n.Id).Should().Contain(ids.TaxInvoiceId);
        chain.Receipts.Select(n => n.Id).Should().Contain(ids.ReceiptId);
    }

    [SkippableFact]
    public async Task GetChain_from_quotation_returns_full_chain_down_to_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ids = await BuildChainAsync(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IDocumentCrossRefService>();
        var chain = await svc.GetChainAsync("quotation", ids.QuotationId, default);

        AssertFullChain(chain, ids);
    }

    [SkippableFact]
    public async Task GetChain_from_receipt_returns_full_chain_up_to_quotation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ids = await BuildChainAsync(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IDocumentCrossRefService>();
        var chain = await svc.GetChainAsync("receipt", ids.ReceiptId, default);

        AssertFullChain(chain, ids);
    }
}
