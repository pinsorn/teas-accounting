using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B4.5) — matching engine (T5), inline JE +
/// period-close (T6), unmatch rules (T7), and the concurrent-confirm race guard. Dates are
/// ALWAYS relative to <see cref="Today"/> (IClock) — never a hardcoded past period (a fixed
/// past month fails period.closed on a fresh teas_test). Receipts/PVs are seeded POSTED
/// directly via DbContext (never the manual POST paths, which force DocDate=today).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BankReconciliationServiceTests
{
    private readonly PostgresFixture _fx;
    public BankReconciliationServiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp, int BankAccountId)> SetupAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var bankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);
        return (co, sp, bankAccountId);
    }

    private static async Task<long> SeedStatementLineAsync(
        ServiceProvider sp, int companyId, int bankAccountId,
        StatementDirection direction, decimal amount, DateOnly txnDate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var import = new StatementImport
        {
            CompanyId = companyId, BankAccountId = bankAccountId, AdapterCode = "TEST",
            SourceFileName = "test.csv", PeriodStart = txnDate, PeriodEnd = txnDate,
            OpeningBalance = 0m, ClosingBalance = amount, LineCount = 1,
            Status = ImportStatus.Parsed, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = 1,
        };
        db.StatementImports.Add(import);
        await db.SaveChangesAsync();

        var line = new StatementLine
        {
            CompanyId = companyId, StatementImportId = import.StatementImportId, BankAccountId = bankAccountId,
            LineNo = 1, TxnDate = txnDate, Direction = direction, Amount = amount, RunningBalance = amount,
            Channel = "TEST", TxnType = "TEST", Description = "test line", MatchStatus = MatchStatus.Unmatched,
        };
        db.StatementLines.Add(line);
        await db.SaveChangesAsync();
        return line.StatementLineId;
    }

    private static async Task<long> SeedPostedReceiptAsync(
        ServiceProvider sp, int companyId, int branchId, long customerId, decimal cashReceived, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rc = new Receipt
        {
            CompanyId = companyId, BranchId = branchId, CustomerId = customerId,
            CustomerName = "ลูกค้าทดสอบ", CustomerAddress = "-",
            DocNo = "RCTEST" + Guid.NewGuid().ToString("N")[..8],
            DocDate = docDate, PaymentMethod = PaymentMethod.Transfer,
            Amount = cashReceived, TotalAmount = cashReceived, TotalAmountThb = cashReceived,
            WhtAmount = 0m, CashReceived = cashReceived,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
        };
        db.Receipts.Add(rc);
        await db.SaveChangesAsync();
        return rc.ReceiptId;
    }

    /// <summary>VendorId/ExpenseCategoryId carry no DB-level FK on PaymentVoucher (verified in
    /// PaymentVoucherConfiguration.cs — no HasOne/HasForeignKey for either) — placeholder ids
    /// are safe for a directly-seeded test row.</summary>
    private static async Task<long> SeedPostedPaymentVoucherAsync(
        ServiceProvider sp, int companyId, int branchId, decimal totalPaid, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = new PaymentVoucher
        {
            CompanyId = companyId, BranchId = branchId, SubPrefix = "TEST",
            DocDate = docDate, PostingDate = docDate,
            VendorId = 1, ExpenseCategoryId = 1, VendorName = "ผู้ขายทดสอบ",
            VendorType = CustomerType.Corporate, PaymentMethod = PaymentMethod.Transfer,
            SubtotalAmount = totalPaid, VatAmount = 0m, WhtAmount = 0m,
            TotalPaid = totalPaid, TotalAmountThb = totalPaid,
            DocNo = "PVTEST" + Guid.NewGuid().ToString("N")[..8],
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
        };
        db.PaymentVouchers.Add(pv);
        await db.SaveChangesAsync();
        return pv.PaymentVoucherId;
    }

    private static async Task<long> AccountId(AccountingDbContext db, string code) =>
        await db.ChartOfAccounts.Where(a => a.AccountCode == code).Select(a => a.AccountId).FirstAsync();

    // ── T5 — matching suggest/confirm ──────────────────────────────────────

    [SkippableFact]
    public async Task Suggest_and_confirm_a_MoneyIn_line_against_a_posted_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 1234.56m, Today);
        var receiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 1234.56m, Today.AddDays(-2));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();

        var suggestions = await svc.SuggestAsync(lineId, default);
        suggestions.Should().ContainSingle(x => x.DocType == "Receipt" && x.DocId == receiptId);

        await svc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptId, null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Matched);
        line.MatchedReceiptId.Should().Be(receiptId);
        line.MatchedAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Suggest_and_confirm_a_MoneyOut_line_against_a_posted_payment_voucher()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyOut, 500.00m, Today);
        var pvId = await SeedPostedPaymentVoucherAsync(sp, co.CompanyId, co.BranchId, 500.00m, Today.AddDays(3));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();

        var suggestions = await svc.SuggestAsync(lineId, default);
        suggestions.Should().ContainSingle(x => x.DocType == "PaymentVoucher" && x.DocId == pvId);

        await svc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(null, pvId), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Matched);
        line.MatchedPaymentVoucherId.Should().Be(pvId);
    }

    [SkippableFact]
    public async Task Suggest_excludes_a_doc_already_matched_to_another_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var receiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 777.00m, Today);
        var line1 = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 777.00m, Today);
        var line2 = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 777.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await svc.ConfirmMatchAsync(line1, new ConfirmMatchRequest(receiptId, null), default);

        var suggestionsForLine2 = await svc.SuggestAsync(line2, default);
        suggestionsForLine2.Should().BeEmpty("the receipt is already matched to line1");

        var act = () => svc.ConfirmMatchAsync(line2, new ConfirmMatchRequest(receiptId, null), default);
        await act.Should().ThrowAsync<DomainException>().Where(e => e.Code == "bank.doc_already_matched");
    }

    [SkippableFact]
    public async Task Suggest_excludes_amount_off_by_one_satang()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 1000.00m, Today);
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 1000.01m, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        var suggestions = await svc.SuggestAsync(lineId, default);

        suggestions.Should().BeEmpty("D4 amount matching is EXACT to the satang, no tolerance");
    }

    [SkippableFact]
    public async Task Suggest_excludes_a_doc_dated_eight_days_away_window_boundary()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 900.00m, Today);
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 900.00m, Today.AddDays(8));
        var withinWindowReceiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 900.00m, Today.AddDays(-7));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        var suggestions = await svc.SuggestAsync(lineId, default);

        // The +8-day doc is excluded; the exactly -7-day doc (window boundary, inclusive) is kept.
        suggestions.Select(x => x.DocId).Should().BeEquivalentTo([withinWindowReceiptId]);
    }

    // ── T6 — inline JE respects period-close ───────────────────────────────

    [SkippableFact]
    public async Task CreateJournal_in_an_open_period_posts_a_balanced_JE_and_appears_in_profit_loss()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        // Today is always the current Bangkok month -> OPEN with no explicit period row.
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 3.04m, Today);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var revenueAccountId = await AccountId(db, "4000");   // stand-in "interest income" contra
        var pl = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var plBefore = await pl.ProfitLossAsync(monthStart, Today, null, true, default);

        var result = await svc.CreateJournalAsync(
            lineId, new CreateInlineJournalRequest(revenueAccountId, "Bank interest"), default);

        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Posted);
        line.PostedJournalId.Should().Be(result.JournalId);

        var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == result.JournalId);
        je.IsClosingEntry.Should().BeFalse();
        je.DocDate.Should().Be(Today);
        je.TotalDebit.Should().Be(3.04m);
        je.TotalCredit.Should().Be(3.04m);

        var plAfter = await pl.ProfitLossAsync(monthStart, Today, null, true, default);
        (plAfter.Totals.Revenue - plBefore.Totals.Revenue).Should().Be(3.04m,
            "the inline JE must appear in P&L — proves IsClosingEntry=false, not hidden like a year-end sweep");
    }

    [SkippableFact]
    public async Task CreateJournal_with_a_header_contra_account_is_rejected_and_posts_nothing()
    {
        // Opus Tier-2 fix (2026-07-09) — journal_lines.account_id has no DB-level FK, so a
        // header (non-postable) contra account must be rejected in the SERVICE, not the DB.
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 20.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // DefaultChartOfAccounts seeds every row with IsHeader=false — insert one directly to
        // get a header account to reject against.
        var header = new Accounting.Domain.Entities.Master.ChartOfAccount
        {
            CompanyId = co.CompanyId, AccountCode = "9000-HDR", AccountNameTh = "หมวดทดสอบ",
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
            IsHeader = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.Add(header);
        await db.SaveChangesAsync();

        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        var act = () => svc.CreateJournalAsync(
            lineId, new CreateInlineJournalRequest(header.AccountId, null), default);

        await act.Should().ThrowAsync<DomainException>().Where(e => e.Code == "bank.contra_account_is_header");

        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Unmatched);
        line.PostedJournalId.Should().BeNull();
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId && j.TotalDebit == 20.00m))
            .Should().Be(0, "a rejected header contra account must post nothing");
    }

    [SkippableFact]
    public async Task CreateJournal_in_a_closed_period_is_rejected_and_posts_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        // A prior month with NO explicit AccountingPeriod row is CLOSED by default
        // (PeriodCloseService.IsOpenAsync — only the CURRENT Bangkok month is open when missing).
        var closedDate = Today.AddMonths(-1);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyOut, 50.00m, closedDate);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseAccountId = await AccountId(db, "5100");
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();

        var act = () => svc.CreateJournalAsync(
            lineId, new CreateInlineJournalRequest(expenseAccountId, "Bank charge"), default);

        await act.Should().ThrowAsync<DomainException>().Where(e => e.Code == "period.closed");

        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Unmatched);
        line.PostedJournalId.Should().BeNull();
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId && j.DocDate == closedDate))
            .Should().Be(0, "a rejected EnsureOpenAsync must post nothing");
    }

    // ── T7 — unmatch rules ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task Unmatch_a_Matched_line_returns_cleanly_to_Unmatched()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 250.00m, Today);
        var receiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 250.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await svc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptId, null), default);

        await svc.UnmatchAsync(lineId, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Unmatched);
        line.MatchedReceiptId.Should().BeNull();
        line.MatchedAt.Should().BeNull();
    }

    [SkippableFact]
    public async Task Unmatch_a_Posted_JE_backed_line_is_rejected_and_the_JE_remains()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 10.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var revenueAccountId = await AccountId(db, "4000");
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        var result = await svc.CreateJournalAsync(lineId, new CreateInlineJournalRequest(revenueAccountId, null), default);

        var act = () => svc.UnmatchAsync(lineId, default);
        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Code == "bank.line_posted" && e.Message.Contains(result.JournalId.ToString()));

        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchStatus.Should().Be(MatchStatus.Posted, "unmatch must be blocked, not silently succeed");
        (await db.JournalEntries.AnyAsync(j => j.JournalId == result.JournalId)).Should().BeTrue("the JE remains — no void exists");
    }

    // ── Concurrent-confirm race guard (coordinator watch item) ──────────────

    [SkippableFact]
    public async Task Confirm_race_second_caller_on_an_already_Matched_line_is_rejected_not_silently_overwritten()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId) = await SetupAsync();
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, bankAccountId, StatementDirection.MoneyIn, 400.00m, Today);
        var receiptA = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 400.00m, Today);
        var receiptB = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 400.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();

        // "User A confirms" wins the WHERE match_status='Unmatched' conditional update.
        await svc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptA, null), default);

        // "User B confirms a DIFFERENT doc on the SAME line" — the exact race the coordinator
        // named. The conditional claim now affects 0 rows (status is already Matched) and must
        // fail cleanly rather than silently overwrite A's link with B's doc.
        var act = () => svc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptB, null), default);
        await act.Should().ThrowAsync<DomainException>().Where(e => e.Code == "bank.line_not_unmatched");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.StatementLines.AsNoTracking().FirstAsync(x => x.StatementLineId == lineId);
        line.MatchedReceiptId.Should().Be(receiptA, "the first confirm's link must survive, never silently clobbered");
    }
}
