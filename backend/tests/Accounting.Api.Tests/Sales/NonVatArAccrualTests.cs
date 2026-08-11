using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-breakit-r1-ledger-integrity.md WP-1 (C6) — a non-VAT company's Invoice
/// (BillingNote) is its ONLY sales document (ม.86/4 blocks the Tax Invoice), so Issue
/// must accrue Dr 1130 AR / Cr 4000 Sales exactly like a VAT company's Tax Invoice does,
/// and the receipt must then CLEAR that AR instead of re-recognizing revenue. T1-T7
/// (spec §6), each proving one of I1-I8 (spec §4). Real Postgres, fresh company per test
/// (never company 1/co2/co3 — co2/co3 are live Repttown data, see troubles-wiki).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NonVatArAccrualTests
{
    private readonly PostgresFixture _fx;
    public NonVatArAccrualTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    private Task<TestCompanyFactory.SeededCompany> NonVatCompanyAsync() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    // Creates a Draft Invoice with one line and Issues it. Non-VAT company → the backstop
    // zeroes VAT regardless of the requested tax code/rate (mirrors the existing
    // NonVatBillingTests convention), so TotalAmount == unitPrice.
    private static async Task<(long BnId, string DocNo, decimal Total)> CreateAndIssueBnAsync(
        ServiceProvider sp, long customerId, decimal unitPrice)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var id = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today, Today.AddDays(30), customerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "line", 1m, "ชิ้น", unitPrice, 0m, 1, "VAT7", 0.07m)]),
            default);
        await svc.IssueAsync(id, default);
        var detail = await svc.GetAsync(id, default);
        return (id, detail!.DocNo!, detail.TotalAmount);
    }

    private static async Task<Accounting.Application.Sales.ReceiptPostedResult> PostReceiptForBnAsync(
        ServiceProvider sp, long customerId, long bnId, decimal appliedAmount)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
            Today, customerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, appliedAmount, null, bnId)]), default);
        return await svc.PostAsync(id, default);
    }

    // ── T1 — Issue posts exactly one Dr AR / Cr Sales JE, TB balanced (I1, I3) ──────────

    [SkippableFact]
    public async Task T1_issue_posts_ar_sales_accrual_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var ar = await AccountId(sp, "1130");
        var sales = await AccountId(sp, "4000");

        var (bnId, docNo, total) = await CreateAndIssueBnAsync(sp, c.CustomerId, 1000m);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);
        bn.JournalEntryId.Should().NotBeNull("Issue must stamp the accrual JE onto the BN");

        var je = await db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == bn.JournalEntryId!.Value);
        je.Reference.Should().Be(docNo);
        je.TotalDebit.Should().Be(je.TotalCredit, "TB must balance");
        je.Lines.Should().ContainSingle(l => l.AccountId == ar && l.DebitAmount == total);
        je.Lines.Should().ContainSingle(l => l.AccountId == sales && l.CreditAmount == total);
    }

    // ── T2 — receipt clears AR, does not re-recognize revenue (I1, I2, I3, I4) ─────────

    [SkippableFact]
    public async Task T2_receipt_clears_ar_without_doubling_revenue()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var ar = await AccountId(sp, "1130");
        var sales = await AccountId(sp, "4000");

        var (bnId, _, total) = await CreateAndIssueBnAsync(sp, c.CustomerId, 1000m);
        var res = await PostReceiptForBnAsync(sp, c.CustomerId, bnId, total);

        res.CashReceived.Should().Be(total, "I4 — cash debited by exactly the applied amount");

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);
        var issueJeId = bn.JournalEntryId!.Value;
        var receiptJe = await db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Reference == res.DocNo);

        // I2 — 1130's net movement across BOTH JEs for this sale is exactly 0.00.
        var arLines = await db.JournalLines
            .Where(l => (l.JournalId == issueJeId || l.JournalId == receiptJe.JournalId) && l.AccountId == ar)
            .ToListAsync();
        arLines.Sum(l => l.DebitAmount - l.CreditAmount).Should().Be(0m);

        // The receipt settles AR (Cr AR), it does NOT credit Sales again.
        receiptJe.Lines.Should().Contain(l => l.AccountId == ar && l.CreditAmount == total);
        receiptJe.Lines.Should().NotContain(l => l.AccountId == sales);

        // I1 — total 4000 credit across both JEs for this sale is exactly the invoice
        // total, never doubled. Fresh company → no other postings touch 4000.
        var salesCredit = await db.JournalLines
            .Where(l => l.AccountId == sales)
            .SumAsync(l => l.CreditAmount);
        salesCredit.Should().Be(total);
    }

    // ── T3 — AR aging ties out across unpaid/part-paid/settled (I5) ────────────────────

    [SkippableFact]
    public async Task T3_ar_aging_ties_out_across_unpaid_partpaid_settled()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);

        var (unpaidId, _, unpaidTotal) = await CreateAndIssueBnAsync(sp, c.CustomerId, 1000m);
        var (partId, _, partTotal) = await CreateAndIssueBnAsync(sp, c.CustomerId, 2000m);
        await PostReceiptForBnAsync(sp, c.CustomerId, partId, 1000m);
        var (settledId, _, settledTotal) = await CreateAndIssueBnAsync(sp, c.CustomerId, 1500m);
        await PostReceiptForBnAsync(sp, c.CustomerId, settledId, settledTotal);

        var expectedOutstanding = unpaidTotal + (partTotal - 1000m) + 0m;   // 2000m

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        var aging = await svc.ArAgingAsync(Today, null, default);
        aging.Reconciliation.Difference.Should().Be(0m,
            "the release's primary regression detector — a gap here means a consumer was missed");
        aging.Reconciliation.Balanced.Should().BeTrue();
        aging.Rows.Should().ContainSingle(r => r.CustomerId == c.CustomerId)
            .Which.Total.Should().Be(expectedOutstanding);

        var statement = await svc.CustomerStatementAsync(c.CustomerId, Today, Today, default);
        statement.ClosingBalance.Should().Be(expectedOutstanding);
    }

    // ── T4 — VAT company's books do not move (I6) ───────────────────────────────────────

    [SkippableFact]
    public async Task T4_vat_company_billing_note_issue_posts_no_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        await using var s0 = sp.CreateAsyncScope();
        var before = await s0.ServiceProvider.GetRequiredService<ISubledgerReportService>()
            .ArAgingAsync(Today, null, default);
        var jeCountBefore = await s0.ServiceProvider.GetRequiredService<AccountingDbContext>()
            .JournalEntries.CountAsync();

        long bnId;
        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), c.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await svc.IssueAsync(bnId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);
        bn.JournalEntryId.Should().BeNull("a VAT company's BN never posts — its BN groups already-accrued TIs");
        (await db.JournalEntries.CountAsync()).Should().Be(jeCountBefore, "no JE at all — byte-identical books");

        var after = await s2.ServiceProvider.GetRequiredService<ISubledgerReportService>()
            .ArAgingAsync(Today, null, default);
        after.Reconciliation.ControlAccountBalance.Should().Be(before.Reconciliation.ControlAccountBalance);
        after.Reconciliation.SubLedgerTotal.Should().Be(before.Reconciliation.SubLedgerTotal);
        after.Rows.Should().BeEquivalentTo(before.Rows);
    }

    // ── T5 — a receipt cannot apply to an already-invoiced DO (I7) ─────────────────────

    [SkippableFact]
    public async Task T5_receipt_to_already_invoiced_do_is_refused_receipt_to_invoice_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;

        long doId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var dosvc = s0.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                Today, cust, null, IsCombinedWithTi: false, null, null,
                [new DeliveryLineInput(null, null, "ส่งของ", 1m, "ชิ้น", 800m, 0m, 1, "VAT7", 0m)]),
                default);
            await dosvc.IssueAsync(doId, default);
        }

        long bnId; string bnDocNo;
        await using (var s1 = sp.CreateAsyncScope())
        {
            var bnsvc = s1.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnsvc.IssueAsync(bnId, default);
            var detail = await bnsvc.GetAsync(bnId, default);
            bnDocNo = detail!.DocNo!;
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var rsvc = s2.ServiceProvider.GetRequiredService<IReceiptService>();
            var act = () => rsvc.CreateDraftAsync(new CreateReceiptRequest(
                Today, cust, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(null, 800m, doId)]), default);
            var ex = await act.Should().ThrowAsync<DomainException>();
            ex.Which.Code.Should().Be("rc.do_already_invoiced");
            ex.Which.Message.Should().Contain(bnDocNo,
                "Ham's decision (spec §8 #4) — the message must name the existing invoice");
        }

        await using var s3 = sp.CreateAsyncScope();
        var rsvc2 = s3.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc2.CreateDraftAsync(new CreateReceiptRequest(
            Today, cust, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, 800m, null, bnId)]), default);
        var res = await rsvc2.PostAsync(rcId, default);
        res.Amount.Should().Be(800m, "a receipt against the INVOICE (not the DO) succeeds");
    }

    // ── T6 — transition window: a pre-fix Invoice (no JournalEntryId) still recognizes
    //    revenue at the receipt, exactly once (I8) ──────────────────────────────────────

    [SkippableFact]
    public async Task T6_legacy_prefix_invoice_receipt_still_credits_sales_not_ar()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;
        var salesAcct = await AccountId(sp, "4000");
        var arAcct = await AccountId(sp, "1130");

        // Simulate a BN issued BEFORE this fix shipped: Issued, DocNo allocated, but
        // JournalEntryId left null (the pre-WP-1 shape — IssueAsync never posted a JE).
        long bnId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bn = new BillingNote
            {
                CompanyId = c.CompanyId, BranchId = c.BranchId,
                DocNo = "LEGACY-" + TestIds.Suffix()[..8],
                Status = BillingNoteStatus.Issued,
                DocDate = Today, DueDate = Today,
                CustomerId = cust, CustomerName = "ลูกค้าทดสอบ จำกัด",
                TotalAmount = 600m, SubtotalAmount = 600m, VatAmount = 0m,
                IssuedAt = DateTimeOffset.UtcNow,
                // JournalEntryId intentionally left null.
            };
            db.BillingNotes.Add(bn);
            await db.SaveChangesAsync();
            bnId = bn.BillingNoteId;
        }

        await using var s1 = sp.CreateAsyncScope();
        var rsvc = s1.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            Today, cust, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, 600m, null, bnId)]), default);
        var res = await rsvc.PostAsync(rcId, default);

        var db2 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db2.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Reference == res.DocNo);
        je.Lines.Should().Contain(l => l.AccountId == salesAcct && l.CreditAmount == 600m,
            "a pre-fix Invoice never accrued — the receipt is still its revenue-recognition point");
        je.Lines.Should().NotContain(l => l.AccountId == arAcct, "1130 stays untouched — no AR ever existed for it");
    }

    // ── T7 — end-to-end: total Sales credit for one sale equals the invoice total,
    //    driven through the real issue→receipt transition (I1) ────────────────────────

    [SkippableFact]
    public async Task T7_issue_then_receipt_credits_sales_exactly_once_for_the_total()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var sales = await AccountId(sp, "4000");

        var (bnId, _, total) = await CreateAndIssueBnAsync(sp, c.CustomerId, 1234.56m);
        await PostReceiptForBnAsync(sp, c.CustomerId, bnId, total);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // Fresh company, one sale — no other document can have touched 4000.
        var totalSalesCredit = await db.JournalLines.Where(l => l.AccountId == sales).SumAsync(l => l.CreditAmount);
        totalSalesCredit.Should().Be(total, "revenue recognised exactly once — at issue, never at the receipt too");
    }

    // ── WP-1 checklist — Issue gates on the period; a closed-period issue consumes no
    //    invoice number ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Closed_period_issue_returns_period_closed_and_consumes_no_invoice_number()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long bnId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc = s0.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), c.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "line", 1m, "ชิ้น", 900m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s1 = sp.CreateAsyncScope())
        {
            var period = s1.ServiceProvider.GetRequiredService<IPeriodCloseService>();
            await period.CloseAsync(Today.Year, Today.Month, "T-close for WP-1 test", default);
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var act = () => svc.IssueAsync(bnId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("period.closed");
        }

        await using (var s3 = sp.CreateAsyncScope())
        {
            var db = s3.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bn = await db.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);
            bn.Status.Should().Be(BillingNoteStatus.Draft, "the failed issue must roll back the status flip");
            bn.DocNo.Should().BeNull("and must not consume an invoice number");
        }

        // Reopen and issue for real — proves the earlier failed attempt burned NO number:
        // the successful DocNo must still be the FIRST one (…-0001), not …-0002.
        await using (var s4 = sp.CreateAsyncScope())
        {
            var period = s4.ServiceProvider.GetRequiredService<IPeriodCloseService>();
            await period.ReopenAsync(Today.Year, Today.Month, "T-reopen for WP-1 test", default);
        }
        await using (var s5 = sp.CreateAsyncScope())
        {
            var svc = s5.ServiceProvider.GetRequiredService<IBillingNoteService>();
            await svc.IssueAsync(bnId, default);
            var detail = await svc.GetAsync(bnId, default);
            detail!.DocNo.Should().EndWith("-0001", "no number was consumed by the earlier failed attempt");
            detail.JournalEntryId.Should().NotBeNull();
        }
    }

    // ── WP-1 checklist — a posted (accrued) Invoice cannot be cancelled ────────────────

    [SkippableFact]
    public async Task Cancel_on_accrued_invoice_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var (bnId, _, _) = await CreateAndIssueBnAsync(sp, c.CustomerId, 500m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var act = () => svc.CancelAsync(bnId, "test cancel", default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("billing_note.cannot_cancel_posted");
    }

    // ── Cross-period AR reconciliation — Repttown's real pattern: invoice issued in
    //    month N, its receipt settling in month N+1. `ar-aging` reconciled AS OF end-of-N
    //    must tie out (I5), because as of that date only the accrual has happened — the
    //    next month's receipt must not appear to have settled anything in that as-of view.
    //
    //    DocDate can't be varied through BillingNoteService/ReceiptService (both pin it to
    //    server-today — troubles-wiki "Test asserts an exact past/future DocDate"), and a
    //    POSTED JournalEntry's doc_date is a DB-trigger-guarded critical field
    //    (020_journal_immutability.sql fn_enforce_je_immutability — UPDATE only, confirmed
    //    by reading the trigger, not assumed), so an already-posted JE's date can never be
    //    shifted afterwards. Instead: INSERT the invoice directly with DocDate = month N
    //    (mirrors the T6 pattern above — sales.billing_notes has no immutability trigger at
    //    all), then call the REAL production GlPostingService.PostBillingNoteAsync against
    //    that already-dated row — an INSERT-time post, never an UPDATE, so the JE trigger
    //    never engages. The receipt then posts through the ordinary, unmodified
    //    ReceiptService flow (dated server-today), which is naturally in the NEXT calendar
    //    month relative to month N's last day — exactly the real-world pattern, no manual
    //    JE construction needed on that side. ────────────────────────────────────────────

    [SkippableFact]
    public async Task Ar_reconciliation_ties_out_across_month_boundary_invoice_then_next_month_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        const decimal total = 900m;

        var monthNEnd = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);   // last day of month N

        long bnId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var gl = s0.ServiceProvider.GetRequiredService<IGlPostingService>();
            var bn = new BillingNote
            {
                CompanyId = c.CompanyId, BranchId = c.BranchId,
                DocNo = "CP-" + TestIds.Suffix()[..8],
                Status = BillingNoteStatus.Issued,
                DocDate = monthNEnd, DueDate = monthNEnd,
                CustomerId = c.CustomerId, CustomerName = "ลูกค้าทดสอบ จำกัด",
                TotalAmount = total, SubtotalAmount = total, VatAmount = 0m,
                IssuedAt = DateTimeOffset.UtcNow,
            };
            db.BillingNotes.Add(bn);
            await db.SaveChangesAsync();   // INSERT — bn.BillingNoteId assigned
            bnId = bn.BillingNoteId;

            // The exact production method WP-1 added, posting the accrual JE dated at the
            // BN's own DocDate (month N). Fresh INSERT into gl.journal_entries — the
            // immutability trigger only guards UPDATE on an already-POSTED row, never INSERT.
            bn.JournalEntryId = await gl.PostBillingNoteAsync(bnId, default);
            await db.SaveChangesAsync();
        }

        // Real, unmodified ReceiptService flow — DocDate pins to server-today, which is
        // always chronologically after monthNEnd (month N+1 relative to the invoice).
        await PostReceiptForBnAsync(sp, c.CustomerId, bnId, total);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
        var aging = await svc.ArAgingAsync(monthNEnd, null, default);

        // As of end-of-N: the accrual JE (dated month N) is on the books; the receipt's
        // settlement JE (dated month N+1) is excluded by ControlAccountBalanceAsync's own
        // `j.DocDate <= asOf` filter, and by ArReconciliationAsync's `m.DocDate <= asOf`
        // filter over ArMovementsAsync's per-movement dates — so BOTH sides of the tie-out
        // must still show the invoice as fully outstanding, not settled.
        aging.Reconciliation.ControlAccountBalance.Should().Be(total,
            "as of end-of-N only the accrual JE (dated month N) has posted to 1130 — the " +
            "receipt's JE is dated month N+1, after this as-of date");
        aging.Reconciliation.SubLedgerTotal.Should().Be(total,
            "the sub-ledger side, filtered to movements on/before asOf, must agree");
        aging.Reconciliation.Difference.Should().Be(0m);
        aging.Reconciliation.Balanced.Should().BeTrue();
    }
}
