using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-breakit-r1-ledger-integrity.md WP-2 (C6 backfill, §3.2.5 — REDESIGNED) —
/// INonVatArBackfillService. T8-T11 (spec §6, corrected to the redesign: no entry is EVER
/// dated inside a closed period; a closed-year correction dates at TODAY, never at the true
/// historical event date — the stale "true event dates" wording in the original §6 table row
/// predates the 2026-07-31 redesign and is superseded by §3.2.5/I13b). Real Postgres, fresh
/// company per test (never company 1/co2/co3 — co2/co3 are live Repttown data).
///
/// Fixtures simulate "pre-WP-1" invoices (Issued/Settled, JournalEntryId == null) via direct
/// entity insert — mirrors NonVatArAccrualTests.cs's T6 pattern (BillingNoteService always
/// pins DocDate to server-today, so a historical DocDate can only be constructed this way).
///
/// "Closed" (Ham's ruling, 2026-08-11): a PRIOR fiscal year, period — a pure
/// fiscal-year-number comparison, no FiscalYearClose row needed (that table under-reports
/// closure for a company, like Repttown, that never ran the in-app year-close even though its
/// prior-year returns are already filed). A current-fiscal-year invoice can STILL need its
/// post date bumped to today if its own issue MONTH was independently closed via the ordinary
/// monthly close (IPeriodCloseService) — simulated below via the real CloseAsync, since that's
/// the actual production code path for closing a month, unlike the FiscalYearClose insert this
/// file used before Ham's fix (which bypassed YearCloseService's real flow entirely).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NonVatArBackfillTests
{
    private readonly PostgresFixture _fx;
    public NonVatArBackfillTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    private Task<TestCompanyFactory.SeededCompany> NonVatCompanyAsync() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);

    // FIX 3 (Opus review) — genuinely simulate "the process died between invoice 1 and
    // invoice 2" for T10's crash-atomicity proof. Wraps the REAL GlPostingService and lets the
    // FIRST PostManualEntryAsync(ManualJvLine[]) call through unchanged (so invoice 1 posts and
    // its own transaction commits for real). LOW 2 (Opus round 2) — the SECOND call also lets
    // the real post happen FIRST (invoice 2's transaction genuinely gets a fresh JE row), THEN
    // throws — simulating a crash AFTER the JE posts but BEFORE the caller's stamp+commit, the
    // window where an "orphan JE" (posted, but never stamped onto JournalEntryId — invisible to
    // the idempotency key, so resumable logic could double-post it) could in principle survive.
    // It doesn't, because the whole invoice-2 transaction (JE included) rolls back via the
    // `await using tx` in NonVatArBackfillService.ApplyAsync — T10 asserts that directly. Every
    // other member just forwards to the inner service (mechanical pass-through, not behavior
    // under test).
    private sealed class FailSecondManualPostGl(IGlPostingService inner) : IGlPostingService
    {
        private int _manualPostCount;

        public Task<long> PostTaxInvoiceAsync(long taxInvoiceId, CancellationToken ct) =>
            inner.PostTaxInvoiceAsync(taxInvoiceId, ct);
        public Task<long> PostBillingNoteAsync(long billingNoteId, CancellationToken ct) =>
            inner.PostBillingNoteAsync(billingNoteId, ct);
        public Task<long> PostReceiptAsync(long receiptId, CancellationToken ct) =>
            inner.PostReceiptAsync(receiptId, ct);
        public Task<long> PostPaymentVoucherAsync(long paymentVoucherId, CancellationToken ct) =>
            inner.PostPaymentVoucherAsync(paymentVoucherId, ct);
        public Task<long> PostVendorInvoiceAsync(long vendorInvoiceId, CancellationToken ct) =>
            inner.PostVendorInvoiceAsync(vendorInvoiceId, ct);
        public Task<long> PostExpenseClaimAsync(long expenseClaimId, CancellationToken ct) =>
            inner.PostExpenseClaimAsync(expenseClaimId, ct);
        public Task<long> PostTaxAdjustmentNoteAsync(long noteId, CancellationToken ct) =>
            inner.PostTaxAdjustmentNoteAsync(noteId, ct);
        public Task<long> PostPayrollRunAsync(long payrollRunId, CancellationToken ct) =>
            inner.PostPayrollRunAsync(payrollRunId, ct);
        public Task<long> PostClosingEntryAsync(
            int companyId, int branchId, DateOnly docDate, string description, bool isClosingEntry,
            long? reversalOfId, IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines,
            CancellationToken ct) =>
            inner.PostClosingEntryAsync(companyId, branchId, docDate, description, isClosingEntry, reversalOfId, lines, ct);
        public Task<long> PostManualEntryAsync(
            int companyId, int branchId, DateOnly docDate, string description, string? reference,
            IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines, CancellationToken ct) =>
            inner.PostManualEntryAsync(companyId, branchId, docDate, description, reference, lines, ct);

        public async Task<long> PostManualEntryAsync(
            int companyId, int branchId, DateOnly docDate, string description, string? reference,
            IReadOnlyList<ManualJvLine> lines, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _manualPostCount) == 2)
            {
                // LOW 2 — let the REAL post happen first (a fresh JE row genuinely exists,
                // inside invoice 2's still-open transaction), then throw. Proves the "orphan
                // JE" window is safe: the transaction never commits, so the JE never survives.
                await inner.PostManualEntryAsync(companyId, branchId, docDate, description, reference, lines, ct);
                throw new InvalidOperationException(
                    "Simulated crash after invoice 2's post, before the stamp commits (T10 orphan-JE proof).");
            }
            return await inner.PostManualEntryAsync(companyId, branchId, docDate, description, reference, lines, ct);
        }
    }

    // Mirrors TestCompanyFactory.BuildProvider exactly, except IGlPostingService resolves to
    // the decorator above (last registration in a ServiceCollection wins for GetRequiredService).
    private static ServiceProvider BuildProviderWithFailingSecondPost(
        string connectionString, int companyId, int branchId)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(cfg);
        services.AddSingleton<ITenantContext>(new StubTenant
        { CompanyId = companyId, BranchId = branchId, UserId = 1, IsSuperAdmin = false });
        services.AddScoped<IGlPostingService>(sp => new FailSecondManualPostGl(
            new GlPostingService(
                sp.GetRequiredService<AccountingDbContext>(),
                sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<INumberSequenceService>(),
                sp.GetRequiredService<IOptions<GlAccountsOptions>>())));
        return services.BuildServiceProvider();
    }

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    // Simulates a pre-WP-1 invoice: Issued (or later), JournalEntryId left null.
    private static async Task<long> CreateLegacyBnAsync(
        ServiceProvider sp, TestCompanyFactory.SeededCompany co, DateOnly docDate, decimal total)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = new BillingNote
        {
            CompanyId = co.CompanyId, BranchId = co.BranchId,
            DocNo = "LEGACY-" + TestIds.Suffix()[..8],
            Status = BillingNoteStatus.Issued,
            DocDate = docDate, DueDate = docDate,
            CustomerId = co.CustomerId, CustomerName = "ลูกค้าทดสอบ จำกัด",
            TotalAmount = total, SubtotalAmount = total, VatAmount = 0m,
            IssuedAt = DateTimeOffset.UtcNow,
        };
        db.BillingNotes.Add(bn);
        await db.SaveChangesAsync();
        return bn.BillingNoteId;
    }

    // Closes a specific calendar month via the REAL IPeriodCloseService (not a hand-inserted
    // row) — this is the production code path NonVatArBackfillService now checks per-invoice
    // for the "current FY, own issue month closed" case.
    private static async Task CloseMonthAsync(ServiceProvider sp, int year, int month)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
        await svc.CloseAsync(year, month, "T-close for WP-2 test", default);
    }

    // Explicitly marks a PAST month Open. PeriodCloseService.IsOpenAsync's own default rule
    // (verified while chasing a test failure, not assumed) is "OPEN only for the CURRENT
    // Bangkok month when no row exists — every OTHER missing month, past or future, is
    // CLOSED". So a genuinely-open PAST month (the "current FY, own issue month still open"
    // case) needs an explicit row; it is not the common case for historical data.
    private static async Task OpenPastMonthAsync(ServiceProvider sp, int companyId, int year, int month)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            CompanyId = companyId, Year = year, Month = (short)month, Status = PeriodStatus.Open,
        });
        await db.SaveChangesAsync();
    }

    // Settles a legacy BN via the REAL ReceiptService (credits Sales — the legacy BN never
    // accrued, so its receipt is still its revenue-recognition point, per WP-1 T6). Note:
    // settling does NOT stamp BillingNote.JournalEntryId — that field means "the ACCRUAL JE",
    // which a legacy invoice never got; the receipt's own JE is separate (Reference == its DocNo).
    private static async Task<string> SettleAsync(ServiceProvider sp, long customerId, long bnId, decimal amount)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
            Today, customerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, amount, null, bnId)]), default);
        var res = await svc.PostAsync(id, default);
        return res.DocNo;
    }

    private static async Task<JournalEntry> JeAsync(ServiceProvider sp, long journalId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.JournalEntries.Include(j => j.Lines).AsNoTracking()
            .FirstAsync(j => j.JournalId == journalId);
    }

    private static async Task<JournalEntry> JeByReferenceAsync(ServiceProvider sp, string reference)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.JournalEntries.Include(j => j.Lines).AsNoTracking()
            .FirstAsync(j => j.Reference == reference);
    }

    private static async Task<long?> JournalEntryIdOfAsync(ServiceProvider sp, long bnId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.BillingNotes.AsNoTracking()
            .Where(b => b.BillingNoteId == bnId).Select(b => b.JournalEntryId).FirstAsync();
    }

    private static async Task<string?> DocNoOfAsync(ServiceProvider sp, long bnId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.BillingNotes.AsNoTracking()
            .Where(b => b.BillingNoteId == bnId).Select(b => b.DocNo).FirstAsync();
    }

    // ── T8 — apply posts balanced correcting entries dated per §3.2.5's rule, and never
    //    touches an existing JE (I9, I11, I13b) ─────────────────────────────────────────

    [SkippableFact]
    public async Task T8_apply_posts_balanced_entries_dated_correctly_and_leaves_existing_je_untouched()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);

        // Four cases in one fixture — every branch NonVatArBackfillService.BuildPlanAsync takes.
        var priorFyDate = new DateOnly(Today.Year - 1, 6, 15);          // closed: PRIOR fiscal year
        var openMonthDate = Today.AddMonths(-2);                        // current FY, own month still open
        var closedMonthDate = Today.AddMonths(-1);                      // current FY, own month gets closed below
        var settledDate = Today;

        var priorFyInvId = await CreateLegacyBnAsync(sp, co, priorFyDate, 1000m);
        var openMonthInvId = await CreateLegacyBnAsync(sp, co, openMonthDate, 800m);
        var closedMonthInvId = await CreateLegacyBnAsync(sp, co, closedMonthDate, 650m);
        var settledInvId = await CreateLegacyBnAsync(sp, co, settledDate, 500m);
        var settleReceiptDocNo = await SettleAsync(sp, co.CustomerId, settledInvId, 500m);   // fully pays it → Settled

        // PeriodCloseService.IsOpenAsync's default is "OPEN only for the CURRENT Bangkok
        // month; every OTHER missing month, past or future, is CLOSED" — so a genuinely-open
        // PAST month needs an explicit row (openMonthDate's case); no action needed to make
        // closedMonthDate closed, it already is by that same default — but close it via the
        // REAL service anyway so this exercises the actual production close path, not the default.
        await OpenPastMonthAsync(sp, co.CompanyId, openMonthDate.Year, openMonthDate.Month);
        await CloseMonthAsync(sp, closedMonthDate.Year, closedMonthDate.Month);

        var preExistingJeBefore = await JeByReferenceAsync(sp, settleReceiptDocNo);   // the receipt's own JE

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
        var result = await svc.ApplyAsync(default);

        result.Posted.Should().Be(3, "3 outstanding invoices — the settled one gets nothing (I9)");

        var ar = await AccountId(sp, "1130");
        var raAcct = await AccountId(sp, "3300");
        var sales = await AccountId(sp, "4000");

        // Case 1 — prior fiscal year → Cr Retained Earnings, dated TODAY.
        var priorFyJe = await JeAsync(sp, (await JournalEntryIdOfAsync(sp, priorFyInvId))!.Value);
        priorFyJe.DocDate.Should().Be(Today,
            "I13b — a closed (prior-FY) correction dates at TODAY, never inside that year");
        priorFyJe.TotalDebit.Should().Be(priorFyJe.TotalCredit).And.Be(1000m);
        priorFyJe.Lines.Should().Contain(l => l.AccountId == ar && l.DebitAmount == 1000m);
        priorFyJe.Lines.Should().Contain(l => l.AccountId == raAcct && l.CreditAmount == 1000m);

        // Case 2 — current fiscal year, own month still open → Cr Revenue, dated at issue.
        var openMonthJe = await JeAsync(sp, (await JournalEntryIdOfAsync(sp, openMonthInvId))!.Value);
        openMonthJe.DocDate.Should().Be(openMonthDate,
            "current FY + open issue month → dated at its OWN issue date, not today");
        openMonthJe.TotalDebit.Should().Be(openMonthJe.TotalCredit).And.Be(800m);
        openMonthJe.Lines.Should().Contain(l => l.AccountId == ar && l.DebitAmount == 800m);
        openMonthJe.Lines.Should().Contain(l => l.AccountId == sales && l.CreditAmount == 800m);

        // Case 3 — current fiscal year, own month CLOSED → still Cr Revenue (same fiscal
        // year's P&L), but dated TODAY (I13b — never into the closed month).
        var closedMonthJe = await JeAsync(sp, (await JournalEntryIdOfAsync(sp, closedMonthInvId))!.Value);
        closedMonthJe.DocDate.Should().Be(Today,
            "current FY but the issue MONTH is closed → date moves to today, credit side does not");
        closedMonthJe.TotalDebit.Should().Be(closedMonthJe.TotalCredit).And.Be(650m);
        closedMonthJe.Lines.Should().Contain(l => l.AccountId == ar && l.DebitAmount == 650m);
        closedMonthJe.Lines.Should().Contain(l => l.AccountId == sales && l.CreditAmount == 650m,
            "still THIS fiscal year's revenue — only the date moved, not the credit side");

        // I11 — the pre-existing JE (settled invoice's receipt) is byte-identical; the
        // settled invoice itself gets no correction (I9 — its JournalEntryId, meaning
        // "the accrual JE", was never set and stays null: settling never sets it, and this
        // backfill correctly excludes a zero-outstanding invoice from its plan).
        var preExistingJeAfter = await JeByReferenceAsync(sp, settleReceiptDocNo);
        preExistingJeAfter.TotalDebit.Should().Be(preExistingJeBefore.TotalDebit);
        preExistingJeAfter.TotalCredit.Should().Be(preExistingJeBefore.TotalCredit);
        preExistingJeAfter.DocDate.Should().Be(preExistingJeBefore.DocDate);
        preExistingJeAfter.Version.Should().Be(preExistingJeBefore.Version, "never modified — same Version");
        (await JournalEntryIdOfAsync(sp, settledInvId)).Should().BeNull(
            "a settled sale is untouched (I9) — its JournalEntryId was never the accrual field's job here");
    }

    // ── T9 — Σ Dr 1130 == Σ outstanding, credit split Revenue/RetainedEarnings, cash
    //    untouched (I9, I10) ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T9_dr_ar_equals_outstanding_and_credit_side_splits_and_cash_is_untouched()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);

        var closedInvId = await CreateLegacyBnAsync(sp, co, new DateOnly(Today.Year - 3, 4, 1), 1200m);
        var openInvId = await CreateLegacyBnAsync(sp, co, Today.AddMonths(-1), 700m);

        async Task<decimal> CashBalanceAsync(string code)
        {
            await using var s0 = sp.CreateAsyncScope();
            var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var acctId = await db0.ChartOfAccounts.Where(a => a.AccountCode == code)
                .Select(a => a.AccountId).FirstAsync();
            return await db0.JournalLines.Where(l => l.AccountId == acctId)
                .SumAsync(l => l.DebitAmount - l.CreditAmount);
        }

        var cashBefore = await CashBalanceAsync("1110");
        var bankBefore = await CashBalanceAsync("1120");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
        var result = await svc.ApplyAsync(default);

        result.GrandTotalOutstanding.Should().Be(1900m, "I9 — Σ Dr 1130 == Σ outstanding (1200+700)");

        var raGroup = result.ByFiscalYear.Should().ContainSingle(g => g.CreditSide == "RetainedEarnings").Which;
        raGroup.OutstandingTotal.Should().Be(1200m);
        raGroup.InvoiceCount.Should().Be(1);

        var revGroup = result.ByFiscalYear.Should().ContainSingle(g => g.CreditSide == "Revenue").Which;
        revGroup.OutstandingTotal.Should().Be(700m);
        revGroup.InvoiceCount.Should().Be(1);

        var closedJeId = (await JournalEntryIdOfAsync(sp, closedInvId))!.Value;
        var openJeId = (await JournalEntryIdOfAsync(sp, openInvId))!.Value;
        var ar = await AccountId(sp, "1130");
        var closedJe = await JeAsync(sp, closedJeId);
        var openJe = await JeAsync(sp, openJeId);
        (closedJe.Lines.Single(l => l.AccountId == ar).DebitAmount
            + openJe.Lines.Single(l => l.AccountId == ar).DebitAmount).Should().Be(1900m);

        (await CashBalanceAsync("1110")).Should().Be(cashBefore, "I10 — the backfill touches no cash account");
        (await CashBalanceAsync("1120")).Should().Be(bankBefore, "I10 — nor bank");
    }

    // ── T10 — idempotent and resumable, INCLUDING a genuine mid-run crash (I12) ────────────
    //    Opus review (2026-08-12) — the previous version of this test ran apply to completion,
    //    added an invoice, and re-ran: that proves incremental pickup, never crash-atomicity of
    //    the one-tx-per-invoice loop. This version actually aborts between invoice 1 and
    //    invoice 2 (a decorated IGlPostingService throws on the SECOND PostManualEntryAsync
    //    call, before it reaches the real poster) and proves invoice 1 survives intact while
    //    invoice 2 is untouched, then that resume completes it with exactly one JE each.

    [SkippableFact]
    public async Task T10_apply_is_idempotent_and_resumable_across_a_genuine_mid_run_crash()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);
        // BuildPlanAsync orders candidates by BillingNoteId — invA (created first) is always
        // processed before invB within one ApplyAsync call.
        var invAId = await CreateLegacyBnAsync(sp, co, Today.AddMonths(-1), 300m);
        var invBId = await CreateLegacyBnAsync(sp, co, Today, 150m);

        var invBDocNo = await DocNoOfAsync(sp, invBId);

        await using (var failingSp = BuildProviderWithFailingSecondPost(_fx.ConnectionString, co.CompanyId, co.BranchId))
        {
            var svcFailing = failingSp.GetRequiredService<INonVatArBackfillService>();
            var act = () => svcFailing.ApplyAsync(default);
            await act.Should().ThrowAsync<InvalidOperationException>(
                "the simulated crash after invoice 2's post, before its stamp commits");
        }

        // Invoice 1 committed for real before the simulated crash; invoice 2's transaction
        // never committed (rolled back when the decorator threw).
        (await JournalEntryIdOfAsync(sp, invAId)).Should().NotBeNull("invoice 1 committed before the simulated crash");
        (await JournalEntryIdOfAsync(sp, invBId)).Should().BeNull("invoice 2's transaction never committed");
        var jeAId = (await JournalEntryIdOfAsync(sp, invAId))!.Value;

        // LOW 2 (Opus round 2) — the decorator let invoice 2's REAL JE post before throwing, so
        // this is the genuine "orphan JE" window (a posted-but-never-stamped correcting JE,
        // invisible to the JournalEntryId-null idempotency key, that could double-post on
        // resume). It must not survive: invoice 2's whole transaction — JE included — rolled
        // back with the simulated crash.
        await using (var sOrphan = sp.CreateAsyncScope())
        {
            var dbOrphan = sOrphan.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await dbOrphan.JournalEntries.CountAsync(j => j.Reference == invBDocNo)).Should().Be(0,
                "no orphan JE survives for invoice 2 — its transaction rolled back in full");
        }

        // Resume with a NORMAL (undecorated) apply — must complete invoice 2 ONLY, never
        // re-touch invoice 1.
        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
            var resume = await svc.ApplyAsync(default);
            resume.Posted.Should().Be(1, "resumes with exactly invoice 2");
            resume.AlreadyDone.Should().Be(1, "invoice 1 — still correctly counted, never re-touched");
        }

        var jeBId = (await JournalEntryIdOfAsync(sp, invBId))!.Value;
        jeAId.Should().NotBe(jeBId, "two distinct corrections, never merged or duplicated");
        (await JournalEntryIdOfAsync(sp, invAId)).Should().Be(jeAId,
            "invoice 1's JE is exactly what the aborted run committed — untouched by the resume");

        await using (var s2 = sp.CreateAsyncScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.JournalEntries.CountAsync(j => j.Description.StartsWith("AR Backfill "))).Should().Be(2,
                "exactly one JE per invoice — no duplicates anywhere");
        }

        // Idempotency proper: run apply a THIRD time with nothing new — must post 0 and report
        // the prior work as alreadyDone (I12's other half — a true no-op, not just resumable).
        await using var s3 = sp.CreateAsyncScope();
        var svc3 = s3.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
        var noop = await svc3.ApplyAsync(default);
        noop.Posted.Should().Be(0, "idempotent — nothing left to do");
        noop.AlreadyDone.Should().Be(2);
        noop.ResumedFrom.Should().Be(2);
    }

    // ── FIX 1 (Opus review, HIGH) — apply refuses when today's own period is closed, and
    //    preview surfaces it as a blocker rather than silently planning an un-appliable run ──

    [SkippableFact]
    public async Task Apply_refuses_when_current_period_is_closed_preview_flags_it_as_blocker_posts_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);
        await CreateLegacyBnAsync(sp, co, Today.AddMonths(-1), 300m);   // non-empty plan

        await CloseMonthAsync(sp, Today.Year, Today.Month);   // close TODAY's own period

        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
            var preview = await svc.PreviewAsync(default);
            preview.ByFiscalYear.Should().NotBeEmpty("preview still returns the plan — it just flags the blocker");
            preview.Blockers.Should().Contain(b => b.Contains("closed"),
                "the closed current period must be surfaced BEFORE anyone tries apply and gets a surprise");
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
            var act = () => svc.ApplyAsync(default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("period.closed");
        }

        await using var s3 = sp.CreateAsyncScope();
        var db = s3.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.CountAsync()).Should().Be(0, "the preflight refuses BEFORE any posting");
        (await db.BillingNotes.CountAsync(b => b.JournalEntryId != null)).Should().Be(0);
    }

    // ── T11 — preview writes nothing (I13) ──────────────────────────────────────────────

    [SkippableFact]
    public async Task T11_preview_writes_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);
        await CreateLegacyBnAsync(sp, co, Today.AddMonths(-1), 600m);

        async Task<(int Je, int Lines, int Stamped)> SnapshotAsync()
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            return (
                await db.JournalEntries.CountAsync(),
                await db.JournalLines.CountAsync(),
                await db.BillingNotes.CountAsync(b => b.JournalEntryId != null));
        }

        var before = await SnapshotAsync();

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
        var result = await svc.PreviewAsync(default);

        result.ByFiscalYear.Should().NotBeEmpty("the plan must be non-empty for this to be a meaningful proof");
        result.GrandTotalOutstanding.Should().Be(600m);
        result.Posted.Should().Be(0);

        var after = await SnapshotAsync();
        after.Should().Be(before, "mode=preview must write ZERO rows — I13");
    }

    // ── Checklist — VAT-company refusal ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task Vat_company_is_refused_on_both_preview_and_apply()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<INonVatArBackfillService>();

        var preview = () => svc.PreviewAsync(default);
        (await preview.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("backfill.vat_company");
        var apply = () => svc.ApplyAsync(default);
        (await apply.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("backfill.vat_company");
    }

    // ── Checklist — missing retained-earnings account stops with a clear error, invents
    //    nothing, and posts NOTHING (not even the invoices that didn't need it) ──────────

    [SkippableFact]
    public async Task Missing_retained_earnings_account_stops_with_clear_error_and_posts_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);
        await CreateLegacyBnAsync(sp, co, new DateOnly(Today.Year - 4, 3, 1), 400m);   // prior FY → needs 3300

        // Rename the seeded 3300 account away so the code-based lookup fails.
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var acct = await db0.ChartOfAccounts.FirstAsync(a => a.AccountCode == "3300");
            acct.AccountCode = "3300-REMOVED-" + TestIds.Suffix()[..6];
            await db0.SaveChangesAsync();
        }

        // LOW item (Opus review) — this must be visible in the ACCOUNTANT-FACING PREVIEW too,
        // not only when someone actually runs apply and gets surprised.
        await using (var sPreview = sp.CreateAsyncScope())
        {
            var svcPreview = sPreview.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
            var preview = await svcPreview.PreviewAsync(default);
            preview.Blockers.Should().Contain(b => b.Contains("3300"),
                "preview must probe GL accounts too — a missing 3300 must not only explode at apply");
        }

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<INonVatArBackfillService>();
        var act = () => svc.ApplyAsync(default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("backfill.account_missing");

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db2.JournalEntries.CountAsync()).Should().Be(0, "stop before posting anything — never invent the account");
    }

    // ── LOW 3 (Opus round 2, "the one I care most about") — pins the Version concurrency
    //    MECHANISM directly, not just today's ApplyAsync behaviour. This repo has already been
    //    bitten once by a silently-inert concurrency token (PV's, named at
    //    ExpenseClaimService.cs:32-35) — a guard that regresses with NO test failing until the
    //    real incident. If a future edit swaps the tracked re-read in ApplyAsync's loop
    //    (currently line ~245) for AsNoTracking or ExecuteUpdateAsync, this test still catches
    //    it, because it exercises Version++ + SaveChangesAsync directly against two independent
    //    DbContexts on the SAME row — it does not go through ApplyAsync at all. ─────────────────

    [SkippableFact]
    public async Task Version_concurrency_token_actually_fires_on_a_stale_write()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NonVatCompanyAsync();
        await using var sp = Provider(co.CompanyId, co.BranchId);
        var bnId = await CreateLegacyBnAsync(sp, co, Today, 100m);

        // Context A: tracked-read (exactly what ApplyAsync's per-invoice loop does).
        await using var scopeA = sp.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bnA = await dbA.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);

        // Context B: a DIFFERENT context updates the SAME row and bumps Version first —
        // simulates a concurrent writer that committed while A was still holding its own read.
        await using (var scopeB = sp.CreateAsyncScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bnB = await dbB.BillingNotes.FirstAsync(b => b.BillingNoteId == bnId);
            bnB.Version++;
            bnB.JournalEntryId = 999_999_999L;   // arbitrary — only needs to force a real UPDATE
            await dbB.SaveChangesAsync();
        }

        // Context A's WHERE clause still carries the ORIGINAL version it read at the top of
        // this test — a losing writer's Version++ + SaveChangesAsync must throw
        // DbUpdateConcurrencyException. This is the raw EF mechanism NonVatArBackfillService
        // .SaveGuardedAsync wraps; SaveGuardedAsync itself is private, so this test exercises
        // the same public building blocks (Version++ then SaveChangesAsync) it depends on.
        bnA.Version++;
        bnA.JournalEntryId = 111_111_111L;
        var act = () => dbA.SaveChangesAsync(default);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
