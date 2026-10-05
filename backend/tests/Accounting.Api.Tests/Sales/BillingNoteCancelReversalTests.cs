using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>cancel-reissue spec 3.4.3 (O3) for a VAT company's Invoice: it groups already-accrued Tax Invoices and
/// has NO journal, so cancel is status-only and must leave the linked TIs and the GL untouched.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class BillingNoteCancelReversalTests
{
    private readonly PostgresFixture _fx;
    public BillingNoteCancelReversalTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => CancelKit.Today;

    private static async Task<long> NewBnAsync(ServiceProvider sp, long custId, long tiId, bool issue)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var id = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today, Today.AddDays(30), custId, null, null, [tiId], "THB", 1m, null, null, []), default);
        if (issue) await svc.IssueAsync(id, default);
        return id;
    }

    [SkippableFact]
    public async Task Vat_bn_cancel_is_status_only_and_frees_the_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var bn = await NewBnAsync(sp, co.CustomerId, ti, issue: true);
        var jeBefore = await CancelKit.WithDbAsync(sp, db => db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId));

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBillingNoteService>().CancelAsync(bn, "SALE_CANCELLED", "ลูกค้ายกเลิก", default);

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.BillingNotes.AsNoTracking().FirstAsync(b => b.BillingNoteId == bn);
        row.Status.Should().Be(BillingNoteStatus.Cancelled);
        row.CancelReasonCode.Should().Be("SALE_CANCELLED");
        row.CancelledReason.Should().Be("ลูกค้ายกเลิก");
        row.CancelledAt.Should().NotBeNull();
        row.CancelledBy.Should().Be(1);
        row.ReversalJournalEntryId.Should().BeNull("a VAT invoice has no journal to reverse");
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId)).Should().Be(jeBefore, "no JE posted");
        var tiRow = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == ti);
        tiRow.Status.Should().Be(DocumentStatus.Posted);
        tiRow.AmountPaid.Should().Be(0m);
        var detail = await s2.ServiceProvider.GetRequiredService<IBillingNoteService>().GetAsync(bn, default);
        detail!.HasJournal.Should().BeFalse();
        detail.CancelReasonCode.Should().Be("SALE_CANCELLED");

        // the linked TI can be cancelled now (exit of ti.linked_to_billing_note)
        await s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(ti, "DUPLICATE", "x", default);
    }

    [SkippableFact]
    public async Task Vat_settled_bn_cancel_refused_until_receipt_cancelled()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var bn = await NewBnAsync(sp, co.CustomerId, ti, issue: true);
        var (rc, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, ti, 1070m);

        await using var s = sp.CreateAsyncScope();
        var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        (await bsvc.GetAsync(bn, default))!.Status.Should().Be("Settled");
        (await CancelKit.CodeOfAsync(() => bsvc.CancelAsync(bn, "ISSUED_IN_ERROR", "x", default))).Should().Be("billing_note.settled_cannot_cancel");
        (await CancelKit.CodeOfAsync(() => bsvc.CancelAsync(bn, "BUYER_DETAILS_ERROR", "x", default))).Should().Be("cancel.reason_code_invalid");

        await s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rc, "ISSUED_IN_ERROR", "x", default);
        (await bsvc.GetAsync(bn, default))!.Status.Should().Be("Issued");
        await bsvc.CancelAsync(bn, "ISSUED_IN_ERROR", "x", default);
        (await bsvc.GetAsync(bn, default))!.Status.Should().Be("Cancelled");
    }

    [SkippableFact]
    public async Task Draft_bn_cancel_needs_a_valid_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var bn = await NewBnAsync(sp, co.CustomerId, ti, issue: false);
        await using var s = sp.CreateAsyncScope();
        var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        (await CancelKit.CodeOfAsync(() => bsvc.CancelAsync(bn, "WRONG", "x", default))).Should().Be("cancel.reason_code_invalid");
        await bsvc.CancelAsync(bn, "DETAILS_ERROR", "x", default);
        (await bsvc.GetAsync(bn, default))!.Status.Should().Be("Cancelled");
    }
}
