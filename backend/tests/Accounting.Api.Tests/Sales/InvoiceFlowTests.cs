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

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// cont.69 Phase 1 — the linear invoice flow:
///   VAT:     DO → Invoice (BillingNote) → Tax Invoice → Receipt(apply TI)
///   non-VAT: DO → Invoice (BillingNote) → Receipt(apply Invoice)
/// Verifies (a) DO mark-delivered no longer auto-creates a TI; (b) DO→Invoice copies
/// lines + DeliveryOrderId; (c) VAT Invoice→TI copies lines + BillingNoteId;
/// (d) non-VAT Invoice→TI throws ti.non_vat_blocked; (e) a non-VAT receipt applied to
/// an Invoice posts revenue to Sales 4000 (cash basis), not AR. Real Postgres.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InvoiceFlowTests
{
    private readonly PostgresFixture _fx;
    public InvoiceFlowTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(bool vatMode)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["Tax:VatMode"] = vatMode ? "true" : "false",
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

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    // Create an Issued DeliveryOrder with a single line. taxRate drives VAT vs non-VAT.
    private static async Task<long> IssuedDoAsync(
        ServiceProvider sp, long cust, decimal qty, decimal price, decimal taxRate)
    {
        await using var s = sp.CreateAsyncScope();
        var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        var doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            new DateOnly(2026, 5, 18), cust, null, IsCombinedWithTi: false, null, null,
            [new DeliveryLineInput(null, null, "งานทดสอบ", qty, "ชิ้น", price, 0m, 1, "VAT7", taxRate)]),
            default);
        await dosvc.IssueAsync(doId, default);
        return doId;
    }

    // ── (a) mark-delivered does NOT create a TI ─────────────────────────────
    [SkippableFact]
    public async Task MarkDelivered_does_not_create_tax_invoice()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(vatMode: true);
        var cust = await CustomerId(sp);
        var doId = await IssuedDoAsync(sp, cust, 1m, 1000m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        await dosvc.MarkDeliveredAsync(doId, default);

        var det = await dosvc.GetAsync(doId, default);
        det!.Status.Should().Be("Delivered");
        det.TaxInvoiceId.Should().BeNull("cont.69 — mark-delivered is status-only");
    }

    // ── (b) DO → Invoice copies lines + DeliveryOrderId ─────────────────────
    [SkippableFact]
    public async Task Do_to_invoice_copies_lines_and_source_link()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(vatMode: true);
        var cust = await CustomerId(sp);
        var doId = await IssuedDoAsync(sp, cust, 2m, 1500m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var bnsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db.BillingNotes.Include(b => b.Lines)
            .FirstAsync(b => b.BillingNoteId == invId);
        bn.Status.ToString().Should().Be("Draft");
        bn.DeliveryOrderId.Should().Be(doId);
        bn.CustomerId.Should().Be(cust);
        bn.Lines.Should().HaveCount(1);
        bn.Lines.Single().DescriptionTh.Should().Be("งานทดสอบ");
        bn.Lines.Single().Quantity.Should().Be(2m);
        bn.SubtotalAmount.Should().Be(3000m);
    }

    // ── (c) VAT Invoice → TI copies lines + BillingNoteId ───────────────────
    [SkippableFact]
    public async Task Vat_invoice_to_tax_invoice_copies_lines_and_source_link()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(vatMode: true);
        var cust = await CustomerId(sp);
        var doId = await IssuedDoAsync(sp, cust, 3m, 200m, 0.07m);

        long invId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var bnsvc = s0.ServiceProvider.GetRequiredService<IBillingNoteService>();
            invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(invId, default);
        }

        await using var s = sp.CreateAsyncScope();
        var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await tisvc.CreateFromBillingNoteAsync(invId, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.Include(t => t.Lines)
            .FirstAsync(t => t.TaxInvoiceId == tiId);
        ti.BillingNoteId.Should().Be(invId);
        ti.CustomerId.Should().Be(cust);
        ti.Lines.Should().HaveCount(1);
        ti.Lines.Single().DescriptionTh.Should().Be("งานทดสอบ");
        ti.Lines.Single().Quantity.Should().Be(3m);
    }

    // ── (d) non-VAT Invoice → TI rejected (ม.86/4) ──────────────────────────
    [SkippableFact]
    public async Task NonVat_invoice_to_tax_invoice_is_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(vatMode: false);
        var cust = await CustomerId(sp);
        var doId = await IssuedDoAsync(sp, cust, 1m, 800m, 0m);

        long invId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var bnsvc = s0.ServiceProvider.GetRequiredService<IBillingNoteService>();
            invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(invId, default);
        }

        await using var s = sp.CreateAsyncScope();
        var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var act = () => tisvc.CreateFromBillingNoteAsync(invId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("ti.non_vat_blocked");
    }

    // ── (e) non-VAT receipt applied to an Invoice → Cr Sales 4000 ───────────
    [SkippableFact]
    public async Task NonVat_receipt_applied_to_invoice_recognizes_revenue_to_sales()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(vatMode: false);
        var cust = await CustomerId(sp);
        var salesAcct = await AccountId(sp, "4000");
        var doId = await IssuedDoAsync(sp, cust, 1m, 750m, 0m);

        long invId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var bnsvc = s0.ServiceProvider.GetRequiredService<IBillingNoteService>();
            invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(invId, default);
        }

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            new DateOnly(2026, 5, 18), cust, Accounting.Domain.Enums.PaymentMethod.Transfer,
            null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, 750m, null, invId)]),
            default);
        var res = await rsvc.PostAsync(rcId, default);

        res.Amount.Should().Be(750m);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(750m);
        // Revenue must land on Sales 4000 (cash basis), not AR.
        je.Lines.Should().Contain(l => l.AccountId == salesAcct && l.CreditAmount == 750m);
        var ar = await AccountId(sp, "1130");
        je.Lines.Should().NotContain(l => l.AccountId == ar);

        // Receipt detail derives its line items from the applied Invoice.
        var detail = await rsvc.GetDetailAsync(rcId, default);
        detail!.Lines.Should().NotBeNull();
        detail.Lines!.Should().Contain(l => l.DescriptionTh == "งานทดสอบ");
    }
}
