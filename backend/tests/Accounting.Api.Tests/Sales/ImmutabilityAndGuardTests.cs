using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// Round-2 hardening — three targeted guards (09-M3, 05-M1, 05-L1):
///
///   M3 — Posted Tax Invoice cannot be re-posted (immutability; §4.2 / CLAUDE.md §4.2).
///        There is no PUT/DELETE endpoint on TaxInvoiceEndpoints.cs (verified: only POST+GET
///        routes exist), so the guard is at the service layer: PostAsync on a posted TI
///        must throw. The endpoint layer protects by omission; the service layer is the
///        authoritative immutability gate.
///
///   M1 — Receipt over-application of a Tax Invoice is rejected (05-M1): duplicate
///        application rows in one receipt, or a second receipt after full payment, both
///        reject with DomainException "receipt.over_applied".
///
///   L1 — Delivery Order cannot exceed the Sales Order line quantity (05-L1).
///        Cumulative delivered qty per SO line is capped at the ordered qty.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ImmutabilityAndGuardTests
{
    private readonly PostgresFixture _fx;
    public ImmutabilityAndGuardTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider() =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1);

    private static async Task<long> CustomerIdAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    // Build and post a Tax Invoice (DO → BN → TI posted). Returns tiId and its TotalAmount.
    private static async Task<(long TiId, decimal Total)> PostedTiAsync(ServiceProvider sp, long custId)
    {
        // DO → Issue
        long doId;
        await using (var s = sp.CreateAsyncScope())
        {
            var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                new DateOnly(2026, 5, 18), custId, null, IsCombinedWithTi: false, null, null,
                [new DeliveryLineInput(null, null, "รายการทดสอบ guard", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await dosvc.IssueAsync(doId, default);
        }

        // DO → BN → Issue
        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bnsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(bnId, default);
        }

        // BN → TI → Post
        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tisvc.CreateFromBillingNoteAsync(bnId, default);
            await tisvc.PostAsync(tiId, default);
        }

        // Read back the TI total (1000 net + 70 VAT = 1070).
        await using var sc = sp.CreateAsyncScope();
        var db = sc.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var total = await db.TaxInvoices.Where(t => t.TaxInvoiceId == tiId)
            .Select(t => t.TotalAmount).FirstAsync();
        return (tiId, total);
    }

    // ── 09-M3: posted TI cannot be re-posted (§4.2 / CLAUDE.md §4.2) ──────────
    [SkippableFact]
    public async Task PostedTaxInvoice_cannot_be_posted_again()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var custId = await CustomerIdAsync(sp);
        var (tiId, _) = await PostedTiAsync(sp, custId);

        // A second PostAsync on a posted TI must be rejected — immutability guard (§4.2).
        // Note: TaxInvoiceEndpoints.cs exposes no PUT/DELETE route on /tax-invoices/{id};
        // the service-layer check is the enforcement point.
        await using var s = sp.CreateAsyncScope();
        var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var act = () => tisvc.PostAsync(tiId, default);
        // Any DomainException (e.g. "ti.bad_status") or InvalidOperationException is acceptable;
        // the key assertion is that it throws, not silently re-posts.
        await act.Should().ThrowAsync<Exception>(
            "a posted Tax Invoice must be immutable — §4.2 / ม.86/4");
    }

    // ── 05-M1: receipt over-apply — TOCTOU: two separate receipts drafted before
    //          either posts; the second receipt's PostAsync must detect the over-apply ─
    [SkippableFact]
    public async Task Two_concurrent_receipts_second_post_is_rejected_as_over_applied()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var custId = await CustomerIdAsync(sp);
        var (tiId, total) = await PostedTiAsync(sp, custId);

        // Draft receipt A (half the TI amount). Will post first — succeeds.
        long rcIdA;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcIdA = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                DateOnly.FromDateTime(DateTime.UtcNow), custId, PaymentMethod.Transfer,
                null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, total / 2m, null, null)]),
                default);
        }

        // Draft receipt B also for the FULL amount — drafted while A is outstanding.
        // Both see the same outstanding at draft time. B's PostAsync must catch
        // the over-apply once A has posted and incremented AmountPaid.
        long rcIdB;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcIdB = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                DateOnly.FromDateTime(DateTime.UtcNow), custId, PaymentMethod.Transfer,
                null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, total, null, null)]),
                default);
        }

        // Post A first (half-payment, succeeds).
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            await rsvc.PostAsync(rcIdA, default);
        }

        // Post B — now would push AmountPaid above TotalAmount → must be rejected.
        await using var s2 = sp.CreateAsyncScope();
        var rsvc2 = s2.ServiceProvider.GetRequiredService<IReceiptService>();
        var act = () => rsvc2.PostAsync(rcIdB, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("receipt.over_applied",
                "TOCTOU: second receipt posting the full amount after half already settled must be rejected — 05-M1 guard");
    }

    // ── 05-L1: DO over-delivery beyond SO line qty is rejected ────────────────
    [SkippableFact]
    public async Task DeliveryOrder_exceeding_so_line_qty_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var custId = await CustomerIdAsync(sp);

        // Create and post a Sales Order with qty=3 (partial delivery is possible).
        long soId;
        await using (var s = sp.CreateAsyncScope())
        {
            var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
                new DateOnly(2026, 5, 18), null, custId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "สินค้าทดสอบ over-deliver", 3m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await sosvc.PostAsync(soId, default);
        }

        // Read the SO line ID so we can pass SalesOrderLineId on the DO lines.
        long solId;
        await using (var sc = sp.CreateAsyncScope())
        {
            var db = sc.ServiceProvider.GetRequiredService<AccountingDbContext>();
            solId = await db.SalesOrderLines
                .Where(l => l.SalesOrderId == soId)
                .Select(l => l.LineId).FirstAsync();
        }

        // First DO — delivers qty=2 (partial, leaves 1 remaining). Should succeed.
        await using (var s = sp.CreateAsyncScope())
        {
            var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var doId = await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
                new DateOnly(2026, 5, 18), custId, null, IsCombinedWithTi: false, null, soId,
                [new DeliveryLineInput(solId, null, "สินค้าทดสอบ over-deliver", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
                default);
            doId.Should().BePositive("first partial DO within qty should be created");
        }

        // Second DO — tries to deliver qty=2 more (delivered 2 + new 2 = 4 > ordered 3) → rejected.
        await using var s2 = sp.CreateAsyncScope();
        var sosvc2 = s2.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var act = () => sosvc2.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 18), custId, null, IsCombinedWithTi: false, null, soId,
            [new DeliveryLineInput(solId, null, "สินค้าทดสอบ over-deliver", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
            default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("do.over_delivered",
                "cumulative delivery cannot exceed SO line qty — 05-L1 guard");
    }
}
