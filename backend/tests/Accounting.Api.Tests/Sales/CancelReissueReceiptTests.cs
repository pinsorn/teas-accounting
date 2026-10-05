using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Sales;

[Collection(nameof(PostgresCollection))]
public sealed class CancelReissueReceiptTests
{
    private readonly PostgresFixture _fx;
    public CancelReissueReceiptTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => CancelKit.Today;

    /// <summary>I4 — for every TI of the company: AmountPaid == Σ applied over POSTED receipts, PaymentStatus agrees.</summary>
    private static async Task AssertI4Async(ServiceProvider sp, int companyId)
    {
        await CancelKit.WithDbAsync(sp, async db =>
        {
            var tis = await db.TaxInvoices.AsNoTracking().Where(t => t.CompanyId == companyId && t.Status != DocumentStatus.Draft).ToListAsync();
            var apps = await db.ReceiptApplications.AsNoTracking()
                .Join(db.Receipts.Where(r => r.CompanyId == companyId && r.Status == DocumentStatus.Posted),
                    a => a.ReceiptId, r => r.ReceiptId, (a, r) => a)
                .Where(a => a.TaxInvoiceId != null).ToListAsync();
            foreach (var t in tis)
            {
                var paid = apps.Where(a => a.TaxInvoiceId == t.TaxInvoiceId).Sum(a => a.AppliedAmount);
                t.AmountPaid.Should().Be(paid, $"I4: TI {t.DocNo}");
                t.PaymentStatus.Should().Be(paid == 0m ? "UNPAID" : paid >= t.TotalAmount ? "PAID" : "PARTIAL", $"I4: TI {t.DocNo}");
            }
            return 0;
        });
    }

    private static async Task<string> BnStatusAsync(ServiceProvider sp, long bnId)
    {
        await using var s = sp.CreateAsyncScope();
        return (await s.ServiceProvider.GetRequiredService<IBillingNoteService>().GetAsync(bnId, default))!.Status;
    }

    private static async Task<ReceiptCancelResult> CancelRcAsync(ServiceProvider sp, long rcId, string code = "ISSUED_IN_ERROR")
    {
        await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rcId, code, "ทดสอบ", default);
    }

    // ── T7 — full unwind (I1, I4) ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Receipt_cancel_full_unwind()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var wht = await CancelKit.WhtSvcTypeIdAsync(sp, co.CompanyId);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, posted) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m,
            wht: [new ReceiptWhtLineInput(wht, 1000m)], certNo: "50T-7");
        posted.WhtAmount.Should().BeGreaterThan(0m);
        (await CancelKit.WithDbAsync(sp, db => db.WhtCertificates.CountAsync(w => w.ReceiptId == rcId && w.Direction == "R")))
            .Should().Be(1);
        await AssertI4Async(sp, co.CompanyId);
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId)))
            .PaymentStatus.Should().Be("PAID");

        var res = await CancelRcAsync(sp, rcId);
        res.Status.Should().Be("Voided");

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rc = await db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcId);
        rc.Status.Should().Be(DocumentStatus.Voided);
        rc.CancelReasonCode.Should().Be("ISSUED_IN_ERROR");
        rc.CancelReason.Should().Be("ทดสอบ");
        rc.CancelledAt.Should().NotBeNull();
        rc.CancelledBy.Should().Be(1);
        rc.ReversalJournalEntryId.Should().Be(res.ReversalJournalId);
        rc.JournalEntryId.Should().NotBeNull();

        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId);
        ti.AmountPaid.Should().Be(0m);
        ti.PaymentStatus.Should().Be("UNPAID");
        (await db.WhtCertificates.AsNoTracking().Where(w => w.ReceiptId == rcId && w.Direction == "R").ToListAsync())
            .Should().OnlyContain(w => w.Status == DocumentStatus.Voided);

        var orig = await db.JournalEntries.Include(j => j.Lines).AsNoTracking().SingleAsync(j => j.JournalId == rc.JournalEntryId!.Value);
        var rev = await db.JournalEntries.Include(j => j.Lines).AsNoTracking().SingleAsync(j => j.JournalId == res.ReversalJournalId);
        rev.ReversalOfId.Should().Be(orig.JournalId);
        rev.TotalDebit.Should().Be(orig.TotalCredit);
        rev.Reference.Should().Be(rc.DocNo);
        orig.Lines.Concat(rev.Lines).GroupBy(l => (l.AccountId, l.BusinessUnitId))
            .Should().OnlyContain(g => g.Sum(l => l.DebitAmount - l.CreditAmount) == 0m);
        await AssertI4Async(sp, co.CompanyId);

        (await db.ActivityLogs.AsNoTracking().AnyAsync(a => a.EntityType == "Receipt" && a.EntityId == rcId && a.ActivityType == "Cancelled"))
            .Should().BeTrue();
        // a voided receipt cannot be cancelled again
        (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rcId, "DUPLICATE", "x", default)))
            .Should().Be("rc.cannot_cancel_status");
        // the TI is payable again
        var (_, again) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m);
        again.Amount.Should().Be(1070m);
    }

    // ── T8 — cancel-and-reissue unchanged, incl. WHT (I3, I4, amount lock) ──────────────────────

    [SkippableFact]
    public async Task Receipt_cancel_and_reissue_unchanged_nets_to_original()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var wht = await CancelKit.WhtSvcTypeIdAsync(sp, co.CompanyId);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, posted) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m,
            wht: [new ReceiptWhtLineInput(wht, 1000m)], certNo: "50T-8");
        var cash = (await CancelKit.CashBankAccountIdsAsync(sp));

        ReceiptCancelResult res;
        await using (var s = sp.CreateAsyncScope())
            res = await s.ServiceProvider.GetRequiredService<IReceiptService>()
                .CancelAndReissueAsync(rcId, "PAYER_DETAILS_ERROR", "ชื่อผู้จ่ายผิด", default);
        var replId = res.ReplacementReceiptId!.Value;
        await AssertI4Async(sp, co.CompanyId);   // between cancel and replacement post the TI is unpaid again

        var orig = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcId));
        var draft = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking()
            .Include(r => r.Applications).Include(r => r.WhtLines).FirstAsync(r => r.ReceiptId == replId));
        draft.Status.Should().Be(DocumentStatus.Draft);
        draft.ReplacesReceiptId.Should().Be(rcId);
        draft.DocDate.Should().Be(orig.DocDate);
        draft.Amount.Should().Be(orig.Amount);
        draft.WhtAmount.Should().Be(orig.WhtAmount);
        draft.CashReceived.Should().Be(0m, "computed at post");
        draft.Applications.Should().ContainSingle(a => a.TaxInvoiceId == tiId && a.AppliedAmount == 1070m);
        draft.WhtLines.Should().HaveCount(1);

        // lock: applied amount / WHT base / cheque amount-ish fields are refused; editable fields pass
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var req = (await svc.GetDraftInputAsync(replId, default))!;
            (await CancelKit.CodeOfAsync(() => svc.UpdateDraftAsync(replId,
                req with { Applications = [new ReceiptApplicationInput(tiId, 500m)], WhtLines = null, WhtAmount = 0m }, default)))
                .Should().Be("replacement.locked_field");
            (await CancelKit.CodeOfAsync(() => svc.UpdateDraftAsync(replId,
                req with { WhtLines = [new ReceiptWhtLineInput(wht, 500m)] }, default)))
                .Should().Be("replacement.locked_field");
            await svc.UpdateDraftAsync(replId, req with { Notes = "แก้ไขผู้จ่าย", ChequeNo = null }, default);
            var after = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == replId));
            after.Notes.Should().Be("แก้ไขผู้จ่าย");
            after.Amount.Should().Be(1070m);

            var posted2 = await svc.PostAsync(replId, default);
            posted2.DocNo.Should().NotBe(orig.DocNo);
            posted2.CashReceived.Should().Be(posted.CashReceived);
        }

        var repl = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == replId));
        repl.Status.Should().Be(DocumentStatus.Posted);
        repl.JournalEntryId.Should().NotBeNull();
        await AssertI4Async(sp, co.CompanyId);
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId)))
            .PaymentStatus.Should().Be("PAID");

        // I3 — net GL equals the original per account; cash moved exactly once.
        var origNet = await CancelKit.NetAsync(sp, orig.JournalEntryId!.Value);
        var allNet = await CancelKit.NetAsync(sp, orig.JournalEntryId!.Value, res.ReversalJournalId, repl.JournalEntryId!.Value);
        allNet.Where(kv => kv.Value != 0m).Should().BeEquivalentTo(origNet.Where(kv => kv.Value != 0m));
        allNet.Where(kv => cash.Contains(kv.Key)).Sum(kv => kv.Value).Should().Be(posted.CashReceived);

        // certs: the original's rows are Voided, the replacement owns fresh Posted rows (shared cert no is legal, F32)
        var certs = await CancelKit.WithDbAsync(sp, db => db.WhtCertificates.AsNoTracking()
            .Where(w => w.Direction == "R" && (w.ReceiptId == rcId || w.ReceiptId == replId)).ToListAsync());
        certs.Where(c => c.ReceiptId == rcId).Should().OnlyContain(c => c.Status == DocumentStatus.Voided);
        certs.Where(c => c.ReceiptId == replId).Should().ContainSingle(c => c.Status == DocumentStatus.Posted && c.DocNo == "50T-8");

        // paper: reference line (no book number) and the voided original is always stamped
        await using var s2 = sp.CreateAsyncScope();
        var rsvc = s2.ServiceProvider.GetRequiredService<IReceiptService>();
        (await rsvc.BuildPaperAsync(replId, default)).Notes.Should().Contain("ยกเลิกและออกแทนฉบับเดิม").And.Contain(orig.DocNo!);
        var pOrig = await rsvc.BuildPaperAsync(rcId, default, copy: true);
        pOrig.Notes.Should().Contain("ออกใบแทนแล้ว").And.Contain(repl.DocNo!);
        pOrig.Watermark!.Text.Should().NotBe("สำเนา");
        var dOrig = await rsvc.GetDetailAsync(rcId, default);
        dOrig!.ReplacedById.Should().Be(replId);
        dOrig.CancelReasonCode.Should().Be("PAYER_DETAILS_ERROR");
        dOrig.ReversalJournalDocNo.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Replacement_receipt_lock_rechecked_at_post_and_discard_exit()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 500m);
        long replId;
        await using (var s = sp.CreateAsyncScope())
            replId = (await s.ServiceProvider.GetRequiredService<IReceiptService>()
                .CancelAndReissueAsync(rcId, "OTHER_PARTICULARS_ERROR", "x", default)).ReplacementReceiptId!.Value;

        await CancelKit.WithDbAsync(sp, async db =>
        {
            var r = await db.Receipts.FirstAsync(x => x.ReceiptId == replId);
            r.Amount = 400m; r.TotalAmount = 400m;
            await db.SaveChangesAsync();
            return 0;
        });
        await using var sc = sp.CreateAsyncScope();
        var svc = sc.ServiceProvider.GetRequiredService<IReceiptService>();
        var ex = await FluentActions.Awaiting(() => svc.PostAsync(replId, default)).Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("replacement.locked_field");

        // exit: discard, then reissue again, then post the fresh one
        await svc.DiscardReplacementAsync(replId, default);
        (await CancelKit.WithDbAsync(sp, db => db.Receipts.AnyAsync(r => r.ReceiptId == replId))).Should().BeFalse();
        (await CancelKit.CodeOfAsync(() => svc.DiscardReplacementAsync(rcId, default))).Should().Be("rc.delete_not_allowed");
        var again = await svc.ReissueAsync(rcId, default);
        (await CancelKit.CodeOfAsync(() => svc.ReissueAsync(rcId, default))).Should().Be("rc.replacement_exists");
        (await svc.PostAsync(again, default)).Amount.Should().Be(500m);
        await AssertI4Async(sp, co.CompanyId);
    }

    // ── T9 — BN settlement re-derived (I5): VAT link path and non-VAT direct path ───────────────

    [SkippableFact]
    public async Task Receipt_cancel_unsettles_bn_vat_link_path()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var a = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var b = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long bn;
        await using (var s = sp.CreateAsyncScope())
        {
            var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bn = await bsvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), co.CustomerId, null, null, [a, b], "THB", 1m, null, null, []), default);
            await bsvc.IssueAsync(bn, default);
        }
        var (rcA, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, a, 1070m);
        (await BnStatusAsync(sp, bn)).Should().Be("Issued", "only half collected");
        var (rcB, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, b, 1070m);
        (await BnStatusAsync(sp, bn)).Should().Be("Settled");

        // full -> cancel the second receipt: coverage broken, back to Issued
        await CancelRcAsync(sp, rcB);
        (await BnStatusAsync(sp, bn)).Should().Be("Issued");
        (await CancelKit.WithDbAsync(sp, db => db.BillingNotes.AsNoTracking().FirstAsync(x => x.BillingNoteId == bn))).SettledAt.Should().BeNull();
        await AssertI4Async(sp, co.CompanyId);

        // re-collect, settle again, then cancel the FIRST receipt instead
        await CancelKit.PostReceiptAsync(sp, co.CustomerId, b, 1070m);
        (await BnStatusAsync(sp, bn)).Should().Be("Settled");
        await CancelRcAsync(sp, rcA);
        (await BnStatusAsync(sp, bn)).Should().Be("Issued");
        (await CancelKit.WithDbAsync(sp, db => db.ActivityLogs.CountAsync(x => x.EntityType == "BillingNote" && x.EntityId == bn && x.ActivityType == "Unsettled")))
            .Should().Be(2);

        // a partially-paid Issued BN stays Issued when its partial receipt is cancelled
        var c = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long bn2;
        await using (var s = sp.CreateAsyncScope())
        {
            var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bn2 = await bsvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), co.CustomerId, null, null, [c], "THB", 1m, null, null, []), default);
            await bsvc.IssueAsync(bn2, default);
        }
        var (rcC, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, c, 500m);
        await CancelRcAsync(sp, rcC);
        (await BnStatusAsync(sp, bn2)).Should().Be("Issued");
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == c))).PaymentStatus.Should().Be("UNPAID");
        await AssertI4Async(sp, co.CompanyId);
    }

    [SkippableFact]
    public async Task Receipt_cancel_unsettles_bn_non_vat_direct_path()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = CancelKit.Sp(_fx, co);
        long bn;
        await using (var s = sp.CreateAsyncScope())
        {
            var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bn = await bsvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), co.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]), default);
            await bsvc.IssueAsync(bn, default);
        }
        async Task<long> Pay(decimal amt)
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
                Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(null, amt, null, bn)]), default);
            await svc.PostAsync(id, default);
            return id;
        }
        var r1 = await Pay(600m);
        (await BnStatusAsync(sp, bn)).Should().Be("Issued");
        var r2 = await Pay(400m);
        (await BnStatusAsync(sp, bn)).Should().Be("Settled");

        await CancelRcAsync(sp, r1);                       // paid drops to 400 < 1000
        (await BnStatusAsync(sp, bn)).Should().Be("Issued");
        // the earlier cancel left the other receipt posted: settle again with the remaining 600
        var r3 = await Pay(600m);
        (await BnStatusAsync(sp, bn)).Should().Be("Settled");
        await CancelRcAsync(sp, r2);                       // paid = 600 (r3) < 1000
        (await BnStatusAsync(sp, bn)).Should().Be("Issued");
        r3.Should().BeGreaterThan(0);
    }

    // ── T18 — bank-matched receipt refused, unmatch is the exit ─────────────────────────────────

    [SkippableFact]
    public async Task Receipt_bank_matched_refused_then_unmatch_exit()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, posted) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m);

        long lineId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bankAcct = await s.ServiceProvider.GetRequiredService<IBankAccountService>().CreateAsync(new CreateBankAccountRequest(
                "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var import = new StatementImport
            {
                CompanyId = co.CompanyId, BankAccountId = bankAcct, AdapterCode = "TEST", SourceFileName = "t.csv",
                PeriodStart = Today, PeriodEnd = Today, OpeningBalance = 0m, ClosingBalance = posted.CashReceived,
                LineCount = 1, Status = ImportStatus.Parsed, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = 1,
            };
            db.StatementImports.Add(import);
            await db.SaveChangesAsync();
            var line = new StatementLine
            {
                CompanyId = co.CompanyId, StatementImportId = import.StatementImportId, BankAccountId = bankAcct, LineNo = 1,
                TxnDate = Today, Direction = StatementDirection.MoneyIn, Amount = posted.CashReceived,
                RunningBalance = posted.CashReceived, Channel = "TEST", TxnType = "TEST", Description = "t",
                MatchStatus = MatchStatus.Unmatched,
            };
            db.StatementLines.Add(line);
            await db.SaveChangesAsync();
            lineId = line.StatementLineId;
            await s.ServiceProvider.GetRequiredService<IBankReconciliationService>()
                .ConfirmMatchAsync(lineId, new ConfirmMatchRequest(rcId, null), default);
        }

        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rcId, "ISSUED_IN_ERROR", "x", default)))
                .Should().Be("rc.bank_reconciled");
        (await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcId))).Status.Should().Be(DocumentStatus.Posted);

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBankReconciliationService>().UnmatchAsync(lineId, default);
        var res = await CancelRcAsync(sp, rcId);
        res.Status.Should().Be("Voided");
    }

    // ── T16 — concurrent TI cancel vs receipt post: exactly one wins, I4 holds ──────────────────

    [SkippableFact]
    public async Task Concurrent_cancel_vs_receipt_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        for (var round = 0; round < 3; round++)
        {
            var co = await CancelKit.VatCoAsync(_fx);
            await using var sp = CancelKit.Sp(_fx, co);
            var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
            long rcDraft;
            await using (var s = sp.CreateAsyncScope())
                rcDraft = await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                    Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                    [new ReceiptApplicationInput(tiId, 1070m)]), default);

            async Task<string> CancelTi()
            {
                await using var s = sp.CreateAsyncScope();
                var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
                return await CancelKit.CodeOfAsync(() => svc.CancelAsync(tiId, "DUPLICATE", "race", default));
            }
            async Task<string> PostRc()
            {
                await using var s = sp.CreateAsyncScope();
                var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
                return await CancelKit.CodeOfAsync(() => svc.PostAsync(rcDraft, default));
            }
            var results = await Task.WhenAll(Task.Run(CancelTi), Task.Run(PostRc));
            var ok = results.Count(r => r == "NO_EXCEPTION");
            ok.Should().Be(1, $"exactly one side wins: [{string.Join(", ", results)}]");
            var loser = results.Single(r => r != "NO_EXCEPTION");
            loser.Should().BeOneOf("ti.has_posted_receipts", "ti.locked_mismatch", "rc.locked_mismatch", "rc.ti_not_posted");

            var ti = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId));
            var rc = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcDraft));
            if (ti.Status == DocumentStatus.Voided) { rc.Status.Should().Be(DocumentStatus.Draft); ti.AmountPaid.Should().Be(0m); }
            else { ti.Status.Should().Be(DocumentStatus.Posted); rc.Status.Should().Be(DocumentStatus.Posted); ti.AmountPaid.Should().Be(1070m); }
            await AssertI4Async(sp, co.CompanyId);
        }
    }

    // ── T23 — paid TI with a wrong buyer name: the full supported chain (O11) ───────────────────

    [SkippableFact]
    public async Task Paid_ti_name_fix_full_chain()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, posted) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m);
        var origTi = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId));
        var origRc = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcId));

        // 1. cancel the receipt (the TI becomes cancellable)
        var cancelRc = await CancelRcAsync(sp, rcId);
        // 2. fix the buyer name in the master, cancel-and-reissue the TI
        await CancelKit.WithDbAsync(sp, async db =>
        {
            (await db.Customers.FirstAsync(c => c.CustomerId == co.CustomerId)).NameTh = "ชื่อที่ถูกต้อง จำกัด";
            await db.SaveChangesAsync();
            return 0;
        });
        TaxInvoiceCancelResult tiRes;
        await using (var s = sp.CreateAsyncScope())
            tiRes = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .CancelAndReissueAsync(tiId, "BUYER_DETAILS_ERROR", "ชื่อผู้ซื้อผิด", default);
        var replTi = tiRes.ReplacementTaxInvoiceId!.Value;

        // 2b. reissuing the receipt BEFORE the replacement TI is posted keeps the Voided TI and refuses to post (exit: post/discard)
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var early = await rsvc.ReissueAsync(rcId, default);
            (await CancelKit.CodeOfAsync(() => rsvc.PostAsync(early, default))).Should().Be("rc.ti_not_posted");
            await rsvc.DiscardReplacementAsync(early, default);
        }

        // 3. post the replacement TI
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().PostAsync(replTi, default);
        (await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replTi)))
            .CustomerName.Should().Be("ชื่อที่ถูกต้อง จำกัด");

        // 4. reissue the receipt: its application is RETARGETED to the replacement TI
        long replRc;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            replRc = await rsvc.ReissueAsync(rcId, default);
            var draft = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().Include(r => r.Applications).FirstAsync(r => r.ReceiptId == replRc));
            draft.Applications.Should().ContainSingle(a => a.TaxInvoiceId == replTi && a.AppliedAmount == 1070m);
            // 5. the lock compares AFTER mapping both sides: the retarget never trips it, a real change still does
            var req = (await rsvc.GetDraftInputAsync(replRc, default))!;
            await rsvc.UpdateDraftAsync(replRc, req with { Notes = "ใบเสร็จใหม่" }, default);
            (await CancelKit.CodeOfAsync(() => rsvc.UpdateDraftAsync(replRc,
                req with { Applications = [new ReceiptApplicationInput(replTi, 1000m)] }, default))).Should().Be("replacement.locked_field");
            await rsvc.PostAsync(replRc, default);
        }

        var rcNew = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().Include(r => r.Applications).FirstAsync(r => r.ReceiptId == replRc));
        rcNew.Status.Should().Be(DocumentStatus.Posted);
        rcNew.Applications.Should().ContainSingle(a => a.TaxInvoiceId == replTi);
        var tiNew = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replTi));
        tiNew.PaymentStatus.Should().Be("PAID");
        tiNew.AmountPaid.Should().Be(1070m);
        var tiOld = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId));
        tiOld.Status.Should().Be(DocumentStatus.Voided);
        tiOld.AmountPaid.Should().Be(0m);
        await AssertI4Async(sp, co.CompanyId);   // I4

        // I2 + I3: net GL over the whole chain == the originals alone, cash moved exactly once
        var origNet = await CancelKit.NetAsync(sp, origTi.JournalEntryId!.Value, origRc.JournalEntryId!.Value);
        var chain = await CancelKit.NetAsync(sp,
            origTi.JournalEntryId!.Value, origRc.JournalEntryId!.Value, cancelRc.ReversalJournalId, tiRes.ReversalJournalId,
            tiNew.JournalEntryId!.Value, rcNew.JournalEntryId!.Value);
        chain.Where(kv => kv.Value != 0m).Should().BeEquivalentTo(origNet.Where(kv => kv.Value != 0m));
        var cash = await CancelKit.CashBankAccountIdsAsync(sp);
        chain.Where(kv => cash.Contains(kv.Key)).Sum(kv => kv.Value).Should().Be(posted.CashReceived);
    }

    // ── T19 (receipt half) — 643 freezes a VOIDED receipt ───────────────────────────────────────

    [SkippableFact]
    public async Task Triggers_freeze_voided_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 500m);
        await CancelRcAsync(sp, rcId);

        async Task<string?> Sql(FormattableString sql)
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            try { await db.Database.ExecuteSqlInterpolatedAsync(sql); return null; }
            catch (Exception ex) { return (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState ?? ex.GetType().Name; }
        }
        (await Sql($"UPDATE sales.receipts SET amount = amount + 1 WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET doc_no = 'X-1' WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET cash_received = 1 WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET replaces_receipt_id = {rcId} WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET cancel_reason = 'changed' WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET cancelled_by = 99 WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET reversal_journal_entry_id = 1 WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET status = 'POSTED' WHERE receipt_id = {rcId}")).Should().Be("23514", "VOIDED to POSTED is illegal");
        (await Sql($"UPDATE sales.receipts SET status = 'DRAFT' WHERE receipt_id = {rcId}")).Should().Be("23514");
        (await Sql($"UPDATE sales.receipts SET notes = 'ok' WHERE receipt_id = {rcId}")).Should().BeNull("notes stay writable");
    }

    // ── post-time status re-checks (spec section 2) + period close + CN post ────────────────────

    [SkippableFact]
    public async Task Post_time_rechecks_refuse_cancelled_ti_note_and_bn()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);

        // receipt draft against a TI that is cancelled afterwards (a draft does not block the cancel)
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long rcDraft;
        await using (var s = sp.CreateAsyncScope())
            rcDraft = await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null, [new ReceiptApplicationInput(ti, 100m)]), default);
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(ti, "ISSUED_IN_ERROR", "x", default);
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(rcDraft, default)))
                .Should().Be("rc.ti_not_posted");
        // a new receipt cannot be drafted against the Voided TI either
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null, [new ReceiptApplicationInput(ti, 100m)]), default)))
                .Should().Be("rc.ti_not_posted");
        // and the receipt TI picker no longer offers it
        await using (var s = sp.CreateAsyncScope())
        {
            var page = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().ListAsync(
                new TaxInvoiceListQuery(null, null, co.CustomerId, null, null, 50, Unpaid: true), default);
            page.Items.Should().NotContain(i => i.TaxInvoiceId == ti);
        }

        // credit note drafted, then the TI cancelled: the note must not post (original no longer Posted)
        var ti2 = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long noteId;
        await using (var s = sp.CreateAsyncScope())
            noteId = await s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>().CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                TaxAdjustmentNoteType.Credit, Today, ti2, nameof(CreditNoteReasonCode.AmountError), "cn", 100m, 0.07m, "THB", 1m, null), default);
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(ti2, "ISSUED_IN_ERROR", "x", default);
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>().PostAsync(noteId, default)))
                .Should().Be("note.original_not_posted");

        // a receipt cannot post to a CANCELLED non-VAT invoice
        var non = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var spN = CancelKit.Sp(_fx, non);
        long bn;
        await using (var s = spN.CreateAsyncScope())
        {
            var bsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bn = await bsvc.CreateDraftAsync(new CreateBillingNoteRequest(Today, Today.AddDays(30), non.CustomerId, null, null, null,
                "THB", 1m, null, null, [new BillingLineInput(null, null, "line", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]), default);
            await bsvc.IssueAsync(bn, default);
        }
        long rcN;
        await using (var s = spN.CreateAsyncScope())
            rcN = await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                Today, non.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null, [new ReceiptApplicationInput(null, 500m, null, bn)]), default);
        await using (var s = spN.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBillingNoteService>().CancelAsync(bn, "DUPLICATE", "d", default);
        await using (var s = spN.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(rcN, default)))
                .Should().Be("rc.invoice_cancelled");
        // the cancelled invoice's DO/SO dedup guard is released (BN status != Cancelled), and SetWhtCert needs a Posted receipt
        await using (var s = spN.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().SetWhtCertAsync(rcN, "C-1", null, default)))
                .Should().Be("rc.not_posted");
    }

    [SkippableFact]
    public async Task Period_close_refuses_a_replacement_receipt_draft_but_not_a_plain_one()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co, new FixedClock(new DateTimeOffset(2031, 5, 10, 5, 0, 0, TimeSpan.Zero)));
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 300m);
        long repl;
        await using (var s = sp.CreateAsyncScope())
            repl = (await s.ServiceProvider.GetRequiredService<IReceiptService>()
                .CancelAndReissueAsync(rcId, "PAYER_DETAILS_ERROR", "x", default)).ReplacementReceiptId!.Value;

        await using (var s = sp.CreateAsyncScope())
        {
            var period = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
            (await CancelKit.CodeOfAsync(() => period.CloseAsync(2031, 5, null, default))).Should().Be("period.draft_present");
            await s.ServiceProvider.GetRequiredService<IReceiptService>().DiscardReplacementAsync(repl, default);
            // a PLAIN receipt draft is deliberately not checked (it has no delete path)
            await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                CancelKit.Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, 100m)]), default);
            (await period.CloseAsync(2031, 5, null, default)).Should().NotBeNull();
        }
    }

    // ── R1-F1 — receipt post vs a TI cancel that commits while the post is waiting on the row lock ──

    [SkippableFact]
    public async Task R1_F1_receipt_post_refused_when_ti_cancel_commits_first_under_lock()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        long rcDraft;
        await using (var s = sp.CreateAsyncScope())
            rcDraft = await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(new CreateReceiptRequest(
                Today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, 1070m)]), default);

        // A second connection holds the TI row lock. The cancel queues first, then the receipt post queues behind it;
        // releasing the lock makes the cancel commit before the post can take the lock.
        await using var raw = new NpgsqlConnection(_fx.ConnectionString);
        await raw.OpenAsync();
        await using var rawTx = await raw.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("SELECT tax_invoice_id FROM sales.tax_invoices WHERE tax_invoice_id = @id FOR UPDATE", raw, rawTx))
        {
            cmd.Parameters.AddWithValue("id", tiId);
            await cmd.ExecuteScalarAsync();
        }

        var cancelTask = Task.Run(async () =>
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await CancelKit.CodeOfAsync(() => svc.CancelAsync(tiId, "DUPLICATE", "r1", default));
        });
        await Task.Delay(800);
        var postTask = Task.Run(async () =>
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            return await CancelKit.CodeOfAsync(() => svc.PostAsync(rcDraft, default));
        });
        await Task.Delay(800);
        await rawTx.CommitAsync();

        (await cancelTask).Should().Be("NO_EXCEPTION");
        (await postTask).Should().Be("rc.ti_not_posted");
        var ti = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId));
        ti.Status.Should().Be(DocumentStatus.Voided);
        ti.AmountPaid.Should().Be(0m);
        (await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == rcDraft))).Status.Should().Be(DocumentStatus.Draft);
        await AssertI4Async(sp, co.CompanyId);
    }

    // ── R1-F2 — T23b: the TI is reissued under a different buyer; the replacement receipt ADOPTS that customer ──

    [SkippableFact]
    public async Task R1_F2_Paid_ti_reissued_to_another_customer_receipt_adopts_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var wht = await CancelKit.WhtSvcTypeIdAsync(sp, co.CompanyId);
        var custB = await CancelKit.WithDbAsync(sp, async db =>
        {
            var c = new Accounting.Domain.Entities.Master.Customer
            {
                CompanyId = co.CompanyId, CustomerCode = Accounting.TestKit.TestIds.CustomerCode(),
                CustomerType = CustomerType.Corporate, NameTh = "ผู้ซื้อที่ถูกต้อง จำกัด", TaxId = "0105556123453",
                BranchCode = "00000", VatRegistered = true, BillingAddress = "ที่อยู่ B", IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c;
        });

        var tiId = await CancelKit.PostTiAsync(sp, co.CustomerId);
        var (rcId, _) = await CancelKit.PostReceiptAsync(sp, co.CustomerId, tiId, 1070m,
            wht: [new ReceiptWhtLineInput(wht, 1000m)], certNo: "50T-F2");
        await CancelRcAsync(sp, rcId);

        long replTi;
        await using (var s = sp.CreateAsyncScope())
        {
            var tsvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            replTi = (await tsvc.CancelAndReissueAsync(tiId, "BUYER_DETAILS_ERROR", "ผิดผู้ซื้อ", default)).ReplacementTaxInvoiceId!.Value;
            var req = (await tsvc.GetDraftInputAsync(replTi, default))!;
            await tsvc.UpdateDraftAsync(replTi, req with { CustomerId = custB.CustomerId }, default);
            await tsvc.PostAsync(replTi, default);
        }

        long replRc;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            replRc = await rsvc.ReissueAsync(rcId, default);
            var draft = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == replRc));
            draft.CustomerId.Should().Be(custB.CustomerId, "the replacement adopts the retargeted TI's customer");
            draft.CustomerName.Should().Be("ผู้ซื้อที่ถูกต้อง จำกัด");
            await rsvc.PostAsync(replRc, default);
        }

        var rc = await CancelKit.WithDbAsync(sp, db => db.Receipts.AsNoTracking().FirstAsync(r => r.ReceiptId == replRc));
        rc.CustomerId.Should().Be(custB.CustomerId);
        var certs = await CancelKit.WithDbAsync(sp, db => db.WhtCertificates.AsNoTracking().Where(w => w.ReceiptId == replRc).ToListAsync());
        certs.Should().ContainSingle().Which.PayerName.Should().Be("ผู้ซื้อที่ถูกต้อง จำกัด");
        await AssertI4Async(sp, co.CompanyId);

        // AR subledger: A (the wrong buyer) nets to zero, B carries invoice + receipt and also nets to zero.
        await using var s2 = sp.CreateAsyncScope();
        var sub = s2.ServiceProvider.GetRequiredService<Accounting.Application.Reports.ISubledgerReportService>();
        var from = Today.AddDays(-1); var to = Today.AddDays(1);
        var stA = await sub.CustomerStatementAsync(co.CustomerId, from, to, default);
        stA.ClosingBalance.Should().Be(0m);
        stA.Lines.Should().Contain(l => l.DocType == "TaxInvoiceCancel");
        var stB = await sub.CustomerStatementAsync(custB.CustomerId, from, to, default);
        stB.ClosingBalance.Should().Be(0m);
        stB.Lines.Select(l => l.DocType).Should().Contain(["TaxInvoice", "Receipt"]);

        // a replacement receipt whose applied TI belongs to someone else is refused at post
        var tiC = await CancelKit.PostTiAsync(sp, custB.CustomerId);
        var (rcOk, _) = await CancelKit.PostReceiptAsync(sp, custB.CustomerId, tiC, 100m);
        await CancelRcAsync(sp, rcOk);
        long replBad;
        await using (var s = sp.CreateAsyncScope())
            replBad = await s.ServiceProvider.GetRequiredService<IReceiptService>().ReissueAsync(rcOk, default);
        await CancelKit.WithDbAsync(sp, async db =>
        {
            var r = await db.Receipts.FirstAsync(x => x.ReceiptId == replBad);
            r.CustomerId = co.CustomerId;   // out-of-band tamper: draft customer no longer matches its TI
            await db.SaveChangesAsync();
            return 0;
        });
        await using (var s = sp.CreateAsyncScope())
            (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(replBad, default)))
                .Should().Be("rc.customer_mismatch");
    }

    // ── R1-F3 — a Voided TI cannot be grouped into an Invoice ──────────────────────────────────

    [SkippableFact]
    public async Task R1_F3_voided_ti_cannot_be_grouped_into_a_billing_note()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co);
        var ti = await CancelKit.PostTiAsync(sp, co.CustomerId);
        await using var s = sp.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CancelAsync(ti, "DUPLICATE", "x", default);
        (await CancelKit.CodeOfAsync(() => s.ServiceProvider.GetRequiredService<IBillingNoteService>().CreateDraftAsync(
            new CreateBillingNoteRequest(Today, Today.AddDays(30), co.CustomerId, null, null, [ti], "THB", 1m, null, null, []), default)))
            .Should().Be("billing_note.ti_not_posted");
    }
}
