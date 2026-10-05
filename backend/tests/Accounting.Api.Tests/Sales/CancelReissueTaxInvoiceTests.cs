using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>Shared helpers for the cancel-reissue test classes. Every money fact is produced by posting through
/// the REAL services (never by seeding a target state).</summary>
internal static class CancelKit
{
    public static DateOnly Today => new SystemClock().TodayInBangkok();

    public static ServiceProvider Sp(PostgresFixture fx, TestCompanyFactory.SeededCompany co, IClock? clock = null) =>
        TestCompanyFactory.BuildProvider(fx.ConnectionString, co.CompanyId, co.BranchId, 1, clock);

    public static Task<TestCompanyFactory.SeededCompany> VatCoAsync(PostgresFixture fx) =>
        TestCompanyFactory.CreateAsync(fx.ConnectionString, vatRegistered: true);

    public static CreateTaxInvoiceRequest TiReq(long custId, decimal price, string desc, decimal qty = 1m) =>
        new(Today, custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, desc, qty, 1, "ชิ้น", price, 0m, 1, "VAT7", 0.07m)]);

    public static async Task<long> DraftTiAsync(ServiceProvider sp, long custId, decimal price = 1000m, string desc = "สินค้า")
    {
        await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
            .CreateDraftAsync(TiReq(custId, price, desc), default);
    }

    public static async Task<long> PostTiAsync(ServiceProvider sp, long custId, decimal price = 1000m, string desc = "สินค้า")
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(TiReq(custId, price, desc), default);
        await svc.PostAsync(id, default);
        return id;
    }

    public static async Task<(long Id, ReceiptPostedResult Result)> PostReceiptAsync(
        ServiceProvider sp, long custId, long tiId, decimal amount,
        PaymentMethod method = PaymentMethod.Cash, IReadOnlyList<ReceiptWhtLineInput>? wht = null, string? certNo = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
            Today, custId, method, null, null, null, "THB", 1m, null,
            Applications: [new ReceiptApplicationInput(tiId, amount)],
            CustomerWhtCertNo: certNo, CustomerWhtCertDate: certNo is null ? null : Today, WhtLines: wht), default);
        return (id, await svc.PostAsync(id, default));
    }

    /// <summary>Per account Σ(Dr - Cr) over the given journals.</summary>
    public static async Task<Dictionary<long, decimal>> NetAsync(ServiceProvider sp, params long[] jeIds)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return (await db.JournalLines.AsNoTracking().Where(l => jeIds.Contains(l.JournalId)).ToListAsync())
            .GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.DebitAmount - l.CreditAmount));
    }

    public static async Task<T> WithDbAsync<T>(ServiceProvider sp, Func<AccountingDbContext, Task<T>> f)
    {
        await using var s = sp.CreateAsyncScope();
        return await f(s.ServiceProvider.GetRequiredService<AccountingDbContext>());
    }

    public static async Task<string> CodeOfAsync(Func<Task> act)
    {
        try { await act(); }
        catch (DomainException ex) { return ex.Code; }
        return "NO_EXCEPTION";
    }

    public static async Task<long[]> CashBankAccountIdsAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var o = s.ServiceProvider.GetRequiredService<IOptions<GlAccountsOptions>>().Value;
        return await db.ChartOfAccounts.Where(a => a.AccountCode == o.CashAccount || a.AccountCode == o.BankAccount)
            .Select(a => a.AccountId).ToArrayAsync();
    }

    /// <summary>Runs one UPDATE with the header immutability trigger switched off inside a transaction. Used ONLY to turn a
    /// freshly posted document into the legacy shape production data has (no stored posting-JE id). The cancel itself is
    /// always driven through the real service afterwards.</summary>
    public static async Task MakeLegacyAsync(ServiceProvider sp, string table, string trigger, string idColumn, long id, string setClause)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
#pragma warning disable EF1002 // table / trigger / column names are test literals
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE sales.{table} DISABLE TRIGGER {trigger}");
        await db.Database.ExecuteSqlRawAsync($"UPDATE sales.{table} SET {setClause} WHERE {idColumn} = {id}");
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE sales.{table} ENABLE TRIGGER {trigger}");
#pragma warning restore EF1002
        await tx.CommitAsync();
    }

    public static async Task<int> WhtSvcTypeIdAsync(ServiceProvider sp, int companyId) =>
        await WithDbAsync(sp, db => db.WhtTypes.Where(w => w.CompanyId == companyId && w.Code == "SVC")
            .Select(w => w.WhtTypeId).FirstAsync());
}

[Collection(nameof(PostgresCollection))]
public sealed class CancelReissueTaxInvoiceTests
{
    private readonly PostgresFixture _fx;
    public CancelReissueTaxInvoiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => CancelKit.Today;

    // ── T1 — exact mirror, no cash (I1, I6) ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ti_cancel_posts_exact_mirror_and_touches_no_cash()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);

        TaxInvoiceCancelResult res;
        await using (var s = sp.CreateAsyncScope())
            res = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .CancelAsync(tiId, "ISSUED_IN_ERROR", "keyed twice", default);
        res.Status.Should().Be("Voided");
        res.ReplacementTaxInvoiceId.Should().BeNull();

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId);
        ti.Status.Should().Be(DocumentStatus.Voided);
        ti.CancelReasonCode.Should().Be("ISSUED_IN_ERROR");
        ti.CancelReason.Should().Be("keyed twice");
        ti.CancelledAt.Should().NotBeNull();
        ti.CancelledBy.Should().Be(1);
        ti.ReversalJournalEntryId.Should().Be(res.ReversalJournalId);
        ti.JournalEntryId.Should().NotBeNull("new posts stamp their JE id");

        var orig = await db.JournalEntries.Include(j => j.Lines).AsNoTracking().SingleAsync(j => j.JournalId == ti.JournalEntryId!.Value);
        var rev = await db.JournalEntries.Include(j => j.Lines).AsNoTracking().SingleAsync(j => j.JournalId == res.ReversalJournalId);
        orig.Reference.Should().Be(ti.DocNo);
        rev.ReversalOfId.Should().Be(orig.JournalId);
        rev.Status.Should().Be(DocumentStatus.Posted);
        rev.TotalDebit.Should().Be(rev.TotalCredit).And.Be(orig.TotalCredit);
        // I1 — per (account, BU) the pair nets to zero.
        orig.Lines.Concat(rev.Lines).GroupBy(l => (l.AccountId, l.BusinessUnitId))
            .Should().OnlyContain(g => g.Sum(l => l.DebitAmount - l.CreditAmount) == 0m);
        orig.Lines.Select(l => l.AccountId).Should().BeEquivalentTo(rev.Lines.Select(l => l.AccountId));

        // I6 — no cash/bank leg, and no receipt cash moved.
        var cashBank = await CancelKit.CashBankAccountIdsAsync(sp);
        rev.Lines.Should().NotContain(l => cashBank.Contains(l.AccountId));
        (await db.Receipts.AsNoTracking().Where(r => r.CompanyId == co.CompanyId).SumAsync(r => r.CashReceived)).Should().Be(0m);

        (await db.ActivityLogs.AsNoTracking().AnyAsync(a => a.EntityType == "TaxInvoice" && a.EntityId == tiId
                && a.ActivityType == "Cancelled")).Should().BeTrue();

        var detail = await s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>().GetDetailAsync(tiId, default);
        detail!.ReversalJournalDocNo.Should().Be(rev.DocNo);
        detail.CancelReasonCode.Should().Be("ISSUED_IN_ERROR");
        detail.Status.Should().Be("Voided");
    }

    // ── T2 — cancel-and-reissue, unchanged, same open month (I2, I8) ────────────────────────────

    [SkippableFact]
    public async Task Ti_cancel_and_reissue_unchanged_nets_to_original()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "เดิม");

        TaxInvoiceCancelResult res;
        await using (var s = sp.CreateAsyncScope())
            res = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .CancelAndReissueAsync(tiId, "ITEM_DESCRIPTION_ERROR", "คำอธิบายผิด", default);
        var replId = res.ReplacementTaxInvoiceId!.Value;

        var orig = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId));
        var draft = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == replId));
        draft.Status.Should().Be(DocumentStatus.Draft);
        draft.ReplacesTaxInvoiceId.Should().Be(tiId);
        draft.DocDate.Should().Be(orig.DocDate);
        draft.TotalAmount.Should().Be(orig.TotalAmount);
        draft.DocNo.Should().BeNull();

        // Edit only the description, then post.
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var req = (await svc.GetDraftInputAsync(replId, default))!;
            await svc.UpdateDraftAsync(replId, req with { Lines = [req.Lines[0] with { DescriptionTh = "แก้ไขแล้ว" }] }, default);
            var posted = await svc.PostAsync(replId, default);
            posted.DocNo.Should().NotBe(orig.DocNo).And.StartWith(orig.DocNo![..7], "same MM-YYYY sequence");
        }

        var repl = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == replId));
        repl.Status.Should().Be(DocumentStatus.Posted);
        repl.DocDate.Should().Be(orig.DocDate);
        repl.Lines.Single().DescriptionTh.Should().Be("แก้ไขแล้ว");
        repl.JournalEntryId.Should().NotBeNull();

        // I2 — net GL per account over {original, reversal, replacement} equals the original alone.
        var origNet = await CancelKit.NetAsync(sp, orig.JournalEntryId!.Value);
        var allNet = await CancelKit.NetAsync(sp, orig.JournalEntryId!.Value, res.ReversalJournalId, repl.JournalEntryId!.Value);
        allNet.Where(kv => kv.Value != 0m).Should().BeEquivalentTo(origNet.Where(kv => kv.Value != 0m));

        await using var s3 = sp.CreateAsyncScope();
        var tsvc = s3.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var dRepl = await tsvc.GetDetailAsync(replId, default);
        dRepl!.ReplacesId.Should().Be(tiId);
        dRepl.ReplacesDocNo.Should().Be(orig.DocNo);
        var dOrig = await tsvc.GetDetailAsync(tiId, default);
        dOrig!.ReplacedById.Should().Be(replId);
        dOrig.ReplacedByStatus.Should().Be("Posted");
        dOrig.ReplacedByDocNo.Should().Be(repl.DocNo);

        // Paper: reference lines + the voided original always carries the cancelled watermark, even as a copy.
        var pRepl = await tsvc.BuildPaperAsync(replId, default);
        pRepl.Notes.Should().Contain("ยกเลิกและออกแทนฉบับเดิม").And.Contain($"เลขที่ {orig.DocNo}");
        pRepl.Notes.Should().NotContain("เล่มที่", "no book number was ever written");
        var pOrig = await tsvc.BuildPaperAsync(tiId, default, copy: true);
        pOrig.Notes.Should().Contain("ออกใบแทนแล้ว").And.Contain(repl.DocNo!);
        pOrig.Watermark!.Text.Should().NotBe("สำเนา", "Voided wins over copy");
    }

    // ── T3 — every guard ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ti_cancel_guards()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        async Task<string> Cancel(long id, string code = "ISSUED_IN_ERROR", string reason = "x")
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await CancelKit.CodeOfAsync(() => svc.CancelAsync(id, code, reason, default));
        }

        // Draft
        (await Cancel(await CancelKit.DraftTiAsync(sp, co.CustomerId))).Should().Be("ti.cannot_cancel_status");

        // Bad / wrong-set reason code, empty reason, over-long reason
        var t1 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        (await Cancel(t1, "NOPE")).Should().Be("cancel.reason_code_invalid");
        (await Cancel(t1, "BUYER_DETAILS_ERROR")).Should().Be("cancel.reason_code_invalid", "reissue codes are not valid for a standalone cancel");
        (await Cancel(t1, reason: "  ")).Should().Be("validation.reason_required");
        (await Cancel(t1, reason: new string('x', 501))).Should().Be("validation.reason_too_long");

        // Already cancelled
        (await Cancel(t1)).Should().Be("NO_EXCEPTION", "a good cancel does not throw");
    }

    [SkippableFact]
    public async Task Ti_cancel_refuses_receipt_note_billing_note_etax_and_double_cancel()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        async Task<string> Cancel(long id)
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await CancelKit.CodeOfAsync(() => svc.CancelAsync(id, "ISSUED_IN_ERROR", "x", default));
        }

        // posted receipt
        var tiRc = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiRc, 100m);
        (await Cancel(tiRc)).Should().Be("ti.has_posted_receipts");

        // posted credit note
        var tiCn = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await using (var s = sp.CreateAsyncScope())
        {
            var notes = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var noteId = await notes.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                TaxAdjustmentNoteType.Credit, Today, tiCn, nameof(CreditNoteReasonCode.AmountError),
                "cn", 100m, 0.07m, "THB", 1m, null), default);
            await notes.PostAsync(noteId, default);
        }
        (await Cancel(tiCn)).Should().Be("ti.has_adjustment_notes");

        // live billing note (draft counts), exit = cancel it
        var tiBn = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long bnId;
        await using (var s = sp.CreateAsyncScope())
            bnId = await s.ServiceProvider.GetRequiredService<IBillingNoteService>().CreateDraftAsync(
                new CreateBillingNoteRequest(Today, Today.AddDays(30), co.CustomerId, null, null, [tiBn], "THB", 1m, null, null, []), default);
        (await Cancel(tiBn)).Should().Be("ti.linked_to_billing_note");

        // e-Tax submitted
        var tiEt = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await CancelKit.WithDbAsync(sp, async db =>
        {
            var t = await db.TaxInvoices.FirstAsync(x => x.TaxInvoiceId == tiEt);
            t.ETaxSubmittedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return 0;
        });
        (await Cancel(tiEt)).Should().Be("ti.etax_submitted");

        // double cancel
        var tiTwice = await CancelKit.PostTiAsync(sp, co.CustomerId);
        (await Cancel(tiTwice)).Should().Be("NO_EXCEPTION");
        (await Cancel(tiTwice)).Should().Be("ti.cannot_cancel_status");
        bnId.Should().BeGreaterThan(0);
    }

    // ── T4 — every guard has an exit ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ti_guard_exits()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        async Task Cancel(long id)
        {
            await using var s = sp.CreateAsyncScope();
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(id, "DUPLICATE", "dup", default);
        }

        // cancel the receipt, then the TI succeeds
        var t1 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rc1, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, t1, 500m);
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rc1, "ISSUED_IN_ERROR", "r", default);
        await Cancel(t1);
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == t1)))
            .Status.Should().Be(DocumentStatus.Voided);

        // cancel the billing note, then the TI succeeds (VAT BN has no JE: status only)
        var t2 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long bn;
        await using (var s = sp.CreateAsyncScope())
        {
            var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bn = await bsvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), co.CustomerId, null, null, [t2], "THB", 1m, null, null, []), default);
            await bsvc.IssueAsync(bn, default);
            await bsvc.CancelAsync(bn, "DUPLICATE", "bn", default);
        }
        await Cancel(t2);

        // discard the replacement, then reissue succeeds again
        var t3 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long repl1, repl2;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            repl1 = (await svc.CancelAndReissueAsync(t3, "OTHER_PARTICULARS_ERROR", "x", default)).ReplacementTaxInvoiceId!.Value;
            (await CancelKit.CodeOfAsync(() => svc.ReissueAsync(t3, default))).Should().Be("ti.replacement_exists");
            await svc.DiscardReplacementAsync(repl1, default);
            (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AnyAsync(t => t.TaxInvoiceId == repl1))).Should().BeFalse();
            repl2 = await svc.ReissueAsync(t3, default);
            repl2.Should().NotBe(repl1);
            // discard is only for replacement drafts
            (await CancelKit.CodeOfAsync(() => svc.DiscardReplacementAsync(t3, default))).Should().Be("ti.delete_not_allowed");
            var plain = await CancelKit.DraftTiAsync(sp, co.CustomerId);
            (await CancelKit.CodeOfAsync(() => svc.DiscardReplacementAsync(plain, default))).Should().Be("ti.delete_not_allowed");
        }
        // an un-cancelled TI cannot be reissued
        var t4 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().ReissueAsync(t4, default)))
                .Should().Be("ti.not_cancelled");
    }

    // ── T5 — replacement amount lock at UpdateDraft (I10) ───────────────────────────────────────

    [SkippableFact]
    public async Task Replacement_ti_amount_lock()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "เดิม");
        long replId;
        await using (var s = sp.CreateAsyncScope())
            replId = (await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .CancelAndReissueAsync(tiId, "BUYER_DETAILS_ERROR", "x", default)).ReplacementTaxInvoiceId!.Value;

        await using var sc = sp.CreateAsyncScope();
        var svc = sc.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var req = (await svc.GetDraftInputAsync(replId, default))!;
        async Task<string> Upd(CreateTaxInvoiceRequest r) => await CancelKit.CodeOfAsync(() => svc.UpdateDraftAsync(replId, r, default));

        (await Upd(req with { Lines = [req.Lines[0] with { Quantity = 2m }] })).Should().Be("replacement.locked_field");
        (await Upd(req with { Lines = [req.Lines[0] with { UnitPrice = 999m }] })).Should().Be("replacement.locked_field");
        (await Upd(req with { Lines = [req.Lines[0] with { DiscountPercent = 10m }] })).Should().Be("replacement.locked_field");
        (await Upd(req with { Lines = [req.Lines[0] with { TaxRate = 0m }] })).Should().Be("replacement.locked_field");
        (await Upd(req with { Lines = [req.Lines[0], req.Lines[0]] })).Should().Be("replacement.locked_field");
        (await Upd(req with { BusinessUnitId = 999999 })).Should().Be("replacement.locked_field");
        (await Upd(req with { QuotationId = 424242 })).Should().Be("replacement.locked_field");
        (await Upd(req with { IsTaxInclusive = true })).Should().Be("replacement.locked_field");
        // nothing above changed the stored draft
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replId)))
            .TotalAmount.Should().Be(1070m);

        // description / uom / notes / payment terms / customer ARE editable
        Customer other;
        other = await CancelKit.WithDbAsync(sp, async db =>
        {
            var c = new Customer
            {
                CompanyId = co.CompanyId, CustomerCode = Accounting.TestKit.TestIds.CustomerCode(),
                CustomerType = CustomerType.Corporate, NameTh = "ลูกค้าแก้ไข จำกัด", TaxId = "0105556123453",
                BranchCode = "00000", VatRegistered = true, BillingAddress = "ที่อยู่ใหม่", IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c;
        });
        await svc.UpdateDraftAsync(replId, req with
        {
            CustomerId = other.CustomerId, Notes = "หมายเหตุใหม่", PaymentTerms = "30 วัน",
            Lines = [req.Lines[0] with { DescriptionTh = "ใหม่", UomText = "กล่อง" }],
        }, default);
        var after = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == replId));
        after.CustomerName.Should().Be("ลูกค้าแก้ไข จำกัด");
        after.CustomerAddress.Should().Be("ที่อยู่ใหม่");
        after.Notes.Should().Be("หมายเหตุใหม่");
        after.Lines.Single().DescriptionTh.Should().Be("ใหม่");
        after.Lines.Single().UomText.Should().Be("กล่อง");
        after.Lines.Single().TaxAmount.Should().Be(70m, "tax is never recomputed");
        after.TotalAmount.Should().Be(1070m);
        after.DocDate.Should().Be(Today);
    }

    // ── T6 — the lock is re-checked at post (I10) ───────────────────────────────────────────────

    [SkippableFact]
    public async Task Replacement_amount_lock_rechecked_at_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "เดิม");
        long replId;
        await using (var s = sp.CreateAsyncScope())
            replId = (await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .CancelAndReissueAsync(tiId, "BUYER_DETAILS_ERROR", "x", default)).ReplacementTaxInvoiceId!.Value;

        // out-of-band edit of the draft (what a path that skips UpdateDraft could do)
        await CancelKit.WithDbAsync(sp, async db =>
        {
            var t = await db.TaxInvoices.Include(x => x.Lines).FirstAsync(x => x.TaxInvoiceId == replId);
            t.TaxAmount += 1m; t.TotalAmount += 1m;
            t.Lines.Single().Quantity = 2m;
            await db.SaveChangesAsync();
            return 0;
        });
        await using var sc = sp.CreateAsyncScope();
        var svc = sc.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var ex = await FluentActions.Awaiting(() => svc.PostAsync(replId, default)).Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("replacement.locked_field");
        ex.Which.Message.Should().Contain("TaxAmount").And.Contain("Lines[1].Quantity");
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replId)))
            .Status.Should().Be(DocumentStatus.Draft, "the refused post rolled back");
    }

    // ── T10 — closed original month: number in March, JEs in April (§8.2, I2, I8) ───────────────

    private static readonly DateTimeOffset Mar15 = new(2031, 3, 15, 5, 0, 0, TimeSpan.Zero);   // 12:00 Bangkok
    private static readonly DateTimeOffset Apr06 = new(2031, 4, 6, 5, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public Task Cross_month_closed_ti_reissue_implicitly_closed() => CrossMonthAsync(explicitClose: false);

    [SkippableFact]
    public Task Cross_month_closed_ti_reissue_explicit_closed_row() => CrossMonthAsync(explicitClose: true);

    private async Task CrossMonthAsync(bool explicitClose)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        long tiA, tiB;
        await using (var spMar = CancelKit.Sp(_fx, co, new FixedClock(Mar15)))
        {
            tiA = await CancelKit.PostTiAsync(spMar, co.CustomerId, 1000m, "มีนาคม A");
            tiB = await CancelKit.PostTiAsync(spMar, co.CustomerId, 2000m, "มีนาคม B");
            if (explicitClose)
                await using (var s = spMar.CreateAsyncScope())
                    await s.ServiceProvider.GetRequiredService<IPeriodCloseService>().CloseAsync(2031, 3, "t10", default);
        }

        await using var spApr = CancelKit.Sp(_fx, co, new FixedClock(Apr06));
        var a = await CancelKit.WithDbAsync(spApr, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiA));
        var b = await CancelKit.WithDbAsync(spApr, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiB));
        a.DocDate.Should().Be(new DateOnly(2031, 3, 15));
        a.DocNo.Should().Be("03-2031-TI-0001");
        b.DocNo.Should().Be("03-2031-TI-0002");


        TaxInvoiceCancelResult res;
        long replId;
        await using (var s = spApr.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            res = await svc.CancelAndReissueAsync(tiA, "BUYER_DETAILS_ERROR", "ชื่อผู้ซื้อผิด", default);
            replId = res.ReplacementTaxInvoiceId!.Value;
            res.GlDate.Should().Be(new DateOnly(2031, 4, 6), "March is closed, so the reversal is dated today");
            var draft = await CancelKit.WithDbAsync(spApr, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replId));
            draft.DocDate.Should().Be(new DateOnly(2031, 3, 15), "the draft keeps the original date even in a closed month");
            // the draft is still editable (UpdateDraft never touches DocDate / the period)
            var req = (await svc.GetDraftInputAsync(replId, default))!;
            await svc.UpdateDraftAsync(replId, req with { Lines = [req.Lines[0] with { DescriptionTh = "แก้ชื่อ" }] }, default);
            var posted = await svc.PostAsync(replId, default);
            posted.DocNo.Should().Be("03-2031-TI-0003", "appended after March's current max, voided -0001 stays used");
        }

        var repl = await CancelKit.WithDbAsync(spApr, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replId));
        repl.DocDate.Should().Be(new DateOnly(2031, 3, 15));
        repl.TaxPointDate.Should().Be(new DateOnly(2031, 3, 15));
        var jes = await CancelKit.WithDbAsync(spApr, db => db.JournalEntries.AsNoTracking()
            .Where(j => j.JournalId == a.JournalEntryId || j.JournalId == res.ReversalJournalId || j.JournalId == repl.JournalEntryId)
            .ToDictionaryAsync(j => j.JournalId, j => j.DocDate));
        jes[a.JournalEntryId!.Value].Should().Be(new DateOnly(2031, 3, 15));
        jes[res.ReversalJournalId].Should().Be(new DateOnly(2031, 4, 6));
        jes[repl.JournalEntryId!.Value].Should().Be(new DateOnly(2031, 4, 6), "replacement JE is dated by the GL-date rule");

        var origNet = await CancelKit.NetAsync(spApr, a.JournalEntryId!.Value);
        var allNet = await CancelKit.NetAsync(spApr, a.JournalEntryId!.Value, res.ReversalJournalId, repl.JournalEntryId!.Value);
        allNet.Where(kv => kv.Value != 0m).Should().BeEquivalentTo(origNet.Where(kv => kv.Value != 0m));

        // The number gap view never reports the voided -0001 (and 0002/0003 are contiguous).
        var gaps = await CancelKit.WithDbAsync(spApr, db => db.Database
            .SqlQuery<int>($"SELECT missing_seq_no AS \"Value\" FROM tax.v_number_gaps WHERE company_id = {co.CompanyId} AND series LIKE '03-2031-TI'")
            .ToListAsync());
        gaps.Should().BeEmpty();
    }

    // ── T15 — the number-gap view ignores a voided TI ───────────────────────────────────────────

    [SkippableFact]
    public async Task Number_gap_view_ignores_voided_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var t1 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var t2 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var t3 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(t2, "DUPLICATE", "dup", default);
        var gaps = await CancelKit.WithDbAsync(sp, db => db.Database
            .SqlQuery<int>($"SELECT missing_seq_no AS \"Value\" FROM tax.v_number_gaps WHERE company_id = {co.CompanyId} AND series LIKE '%-TI'")
            .ToListAsync());
        gaps.Should().BeEmpty("the cancelled number stays issued, it is not a gap");
        (t1 < t2 && t2 < t3).Should().BeTrue();
    }

    // ── T17 — two concurrent cancel-and-reissue calls: one replacement (race, unique index) ─────

    [SkippableFact]
    public async Task Concurrent_double_reissue()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);

        async Task<string> Run()
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await CancelKit.CodeOfAsync(() => svc.CancelAndReissueAsync(tiId, "OTHER_PARTICULARS_ERROR", "race", default));
        }
        var results = await Task.WhenAll(Task.Run(Run), Task.Run(Run));
        results.Count(r => r == "NO_EXCEPTION").Should().Be(1, string.Join(",", results));
        results.Single(r => r != "NO_EXCEPTION").Should().BeOneOf("ti.cannot_cancel_status", "ti.locked_mismatch", "ti.replacement_exists");

        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.CountAsync(t => t.ReplacesTaxInvoiceId == tiId))).Should().Be(1);
        // a second explicit reissue is refused too
        await using var s2 = sp.CreateAsyncScope();
        (await CancelKit.CodeOfAsync(() => s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>().ReissueAsync(tiId, default)))
            .Should().Be("ti.replacement_exists");
    }

    // ── T19 (TI half) — 643 freezes a VOIDED row ────────────────────────────────────────────────

    [SkippableFact]
    public async Task Triggers_freeze_voided_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(tiId, "DUPLICATE", "dup", default);

        async Task<string?> Sql(FormattableString sql)
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            try { await db.Database.ExecuteSqlInterpolatedAsync(sql); return null; }
            catch (Exception ex) { return (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState ?? ex.GetType().Name; }
        }
        (await Sql($"UPDATE sales.tax_invoices SET subtotal_amount = subtotal_amount + 1 WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET doc_no = 'X-1' WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET replaces_tax_invoice_id = {tiId} WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET cancel_reason = 'changed' WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET cancel_reason_code = 'X' WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET cancelled_at = now() WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET reversal_journal_entry_id = 1 WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.tax_invoices SET status = 'POSTED' WHERE tax_invoice_id = {tiId}")).Should().Be("23514", "VOIDED to POSTED is illegal");
        (await Sql($"UPDATE sales.tax_invoices SET status = 'DRAFT' WHERE tax_invoice_id = {tiId}")).Should().Be("23514");
        // fields outside the frozen list stay writable (payment_status / amount_paid are not legal-document fields)
        (await Sql($"UPDATE sales.tax_invoices SET payment_status = 'UNPAID' WHERE tax_invoice_id = {tiId}")).Should().BeNull();
    }

    // ── legacy documents (production data has no stored posting-JE id): lookup by Reference + Description ─────

    [SkippableFact]
    public async Task Legacy_ti_without_stored_je_id_is_found_by_reference_or_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);

        // found by lookup: the reversal mirrors the original, and the cancel backfills the id
        var t1 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var origJe = (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == t1))).JournalEntryId!.Value;
        await CancelKit.MakeLegacyAsync(sp, "tax_invoices", "trg_ti_immutable", "tax_invoice_id", t1, "journal_entry_id = NULL");
        TaxInvoiceCancelResult res;
        await using (var s = sp.CreateAsyncScope())
            res = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(t1, "ISSUED_IN_ERROR", "legacy", default);
        var after = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == t1));
        after.JournalEntryId.Should().Be(origJe, "the cancel backfills the resolved id");
        (await CancelKit.WithDbAsync(sp, db => db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == res.ReversalJournalId)))
            .ReversalOfId.Should().Be(origJe);

        // not found: the document number no longer matches any posting journal
        var t2 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await CancelKit.MakeLegacyAsync(sp, "tax_invoices", "trg_ti_immutable", "tax_invoice_id", t2, "journal_entry_id = NULL, doc_no = 'LEGACY-NOJE-1'");
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(t2, "ISSUED_IN_ERROR", "x", default)))
                .Should().Be("cancel.journal_not_found");

        // ambiguous: a second journal with the same reference and description exists
        var t3 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var t3Row = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(x => x.TaxInvoiceId == t3));
        var (ar, sales) = await CancelKit.WithDbAsync(sp, async db => (
            await db.ChartOfAccounts.Where(a => a.AccountCode == "1130").Select(a => a.AccountId).FirstAsync(),
            await db.ChartOfAccounts.Where(a => a.AccountCode == "4000").Select(a => a.AccountId).FirstAsync()));
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IGlPostingService>().PostManualEntryAsync(
                co.CompanyId, co.BranchId, Today, "TI " + t3Row.DocNo, t3Row.DocNo,
                [(ar, 10m, 0m), (sales, 0m, 10m)], default);
        await CancelKit.MakeLegacyAsync(sp, "tax_invoices", "trg_ti_immutable", "tax_invoice_id", t3, "journal_entry_id = NULL");
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(t3, "ISSUED_IN_ERROR", "x", default)))
                .Should().Be("cancel.journal_ambiguous");
    }

    [SkippableFact]
    public async Task Legacy_receipt_without_stored_je_id_cancels_by_lookup()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rc, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, ti, 400m);
        var origJe = (await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rc))).JournalEntryId!.Value;
        await CancelKit.MakeLegacyAsync(sp, "receipts", "tg_receipts_immutable_after_post", "receipt_id", rc, "journal_entry_id = NULL");

        ReceiptCancelResult res;
        await using (var s = sp.CreateAsyncScope())
            res = await s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rc, "ISSUED_IN_ERROR", "legacy", default);
        (await CancelKit.WithDbAsync(sp, db => db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == res.ReversalJournalId)))
            .ReversalOfId.Should().Be(origJe);
        (await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rc))).JournalEntryId.Should().Be(origJe);
    }
}
