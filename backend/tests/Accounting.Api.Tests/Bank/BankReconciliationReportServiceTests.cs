using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Bank;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B5.4, T10) — reconciliation report tie-out.
/// Dates are relative to <see cref="Today"/> (IClock) — never a hardcoded past period.
///
/// Corrected 2026-07-09 (Fable diff review): the ORIGINAL spec formula (and these tests) were
/// sign-flipped on deposits-in-transit/outstanding-payments — self-consistent with the buggy
/// code, so both the old T10 facts passed while asserting a MATHEMATICALLY WRONG tie-out
/// (circular: the test just proved the code matched itself, not reality). These facts are
/// rewritten from first principles — each scenario is worked out independently of the
/// implementation before asserting Difference == 0 (or its sign in the one non-zero case).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BankReconciliationReportServiceTests
{
    private readonly PostgresFixture _fx;
    public BankReconciliationReportServiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp, int BankAccountId, long GlCashAccountId)> SetupAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var bankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bankAccount = await db.BankAccounts.AsNoTracking().FirstAsync(b => b.BankAccountId == bankAccountId);
        return (co, sp, bankAccountId, bankAccount.GlCashAccountId);
    }

    private static async Task<long> SeedImportAsync(
        ServiceProvider sp, int companyId, int bankAccountId, DateOnly periodStart, DateOnly periodEnd, decimal closingBalance)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var import = new StatementImport
        {
            CompanyId = companyId, BankAccountId = bankAccountId, AdapterCode = "TEST",
            SourceFileName = "test.csv", PeriodStart = periodStart, PeriodEnd = periodEnd,
            OpeningBalance = 0m, ClosingBalance = closingBalance, LineCount = 0,
            Status = ImportStatus.Parsed, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = 1,
        };
        db.StatementImports.Add(import);
        await db.SaveChangesAsync();
        return import.StatementImportId;
    }

    private static async Task<long> SeedStatementLineAsync(
        ServiceProvider sp, int companyId, long importId, int bankAccountId,
        StatementDirection direction, decimal amount, DateOnly txnDate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = new StatementLine
        {
            CompanyId = companyId, StatementImportId = importId, BankAccountId = bankAccountId,
            LineNo = 1, TxnDate = txnDate, Direction = direction, Amount = amount, RunningBalance = amount,
            Channel = "TEST", TxnType = "TEST", Description = "test line", MatchStatus = MatchStatus.Unmatched,
        };
        db.StatementLines.Add(line);
        await db.SaveChangesAsync();
        return line.StatementLineId;
    }

    private static async Task<long> SeedPostedReceiptAsync(
        ServiceProvider sp, int companyId, int branchId, long customerId, decimal cashReceived, DateOnly docDate,
        PaymentMethod method = PaymentMethod.Transfer)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rc = new Receipt
        {
            CompanyId = companyId, BranchId = branchId, CustomerId = customerId,
            CustomerName = "ลูกค้าทดสอบ", CustomerAddress = "-",
            DocNo = "RCTEST" + Guid.NewGuid().ToString("N")[..8],
            DocDate = docDate, PaymentMethod = method,
            Amount = cashReceived, TotalAmount = cashReceived, TotalAmountThb = cashReceived,
            WhtAmount = 0m, CashReceived = cashReceived,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
        };
        db.Receipts.Add(rc);
        await db.SaveChangesAsync();
        return rc.ReceiptId;
    }

    /// <summary>VendorId/ExpenseCategoryId carry no DB-level FK on PaymentVoucher (verified in
    /// PaymentVoucherConfiguration.cs) — placeholder ids are safe for a directly-seeded row.</summary>
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

    /// <summary>Seeds a POSTED 2-line JE directly (mirrors YearEndClosingTests.AddPostedJe) — the
    /// report reads GL balance from JournalLines, decoupled from any Receipt/PV posting flow.
    /// <paramref name="bankDebit"/> true = Dr bank/Cr contra (a deposit hitting GL); false =
    /// Dr contra/Cr bank (a payment hitting GL).</summary>
    private static async Task SeedPostedJeAsync(
        ServiceProvider sp, int companyId, int branchId, DateOnly docDate,
        long glCashAccountId, long contraAccountId, decimal amount, bool bankDebit = true)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = new JournalEntry
        {
            CompanyId = companyId, BranchId = branchId,
            DocNo = "JVTEST" + Guid.NewGuid().ToString("N")[..12] + "X",
            PrefixCode = "JV", DocDate = docDate, PostingDate = docDate,
            Description = "Seed JE (report tie-out test)",
            TotalDebit = amount, TotalCredit = amount,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
            Lines = bankDebit
                ?
                [
                    new JournalLine { LineNo = 1, AccountId = glCashAccountId, DebitAmount = amount, CreditAmount = 0m },
                    new JournalLine { LineNo = 2, AccountId = contraAccountId, DebitAmount = 0m, CreditAmount = amount },
                ]
                :
                [
                    new JournalLine { LineNo = 1, AccountId = contraAccountId, DebitAmount = amount, CreditAmount = 0m },
                    new JournalLine { LineNo = 2, AccountId = glCashAccountId, DebitAmount = 0m, CreditAmount = amount },
                ],
        };
        db.JournalEntries.Add(je);
        await db.SaveChangesAsync();
    }

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code).Select(a => a.AccountId).FirstAsync();
    }

    // (4) Fully reconciled: one line, matched, GL and statement agree, nothing outstanding.
    [SkippableFact]
    public async Task FullyReconciled_no_reconciling_items_difference_is_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, glCashAccountId) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");

        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, 500.00m);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyIn, 500.00m, Today);
        var receiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 500.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 500.00m);

        await using var s = sp.CreateAsyncScope();
        var reconSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await reconSvc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptId, null), default);

        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.StatementClosingBalance.Should().Be(500.00m);
        report.GlBalance.Should().Be(500.00m);
        report.UnmatchedLines.Should().BeEmpty();
        report.DepositsInTransit.Should().BeEmpty();
        report.OutstandingPayments.Should().BeEmpty();
        report.Difference.Should().Be(0m);
    }

    // (1) Deposit-in-transit: Receipt A is posted AND matched to a statement line (so it's
    // reflected on BOTH sides). Receipt B is posted but has NO statement line at all (the bank
    // hasn't shown it yet) — it is already in GL but not yet on the statement, so GL is AHEAD
    // of the statement by exactly Receipt B's amount. Worked independently: statement=500 (only
    // A shows), GL=500+200=700 (both A and B posted), depositsInTransit=200 (B) ->
    // GL - deposits = 700-200 = 500 = statement. Difference must be 0.
    [SkippableFact]
    public async Task DepositInTransit_posted_but_not_on_statement_ties_out_to_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, glCashAccountId) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");

        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, 500.00m);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyIn, 500.00m, Today);
        var receiptA = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 500.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 500.00m);

        // Receipt B — posted (already in GL) but never appears on any statement line.
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 200.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 200.00m);

        await using var s = sp.CreateAsyncScope();
        var reconSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await reconSvc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptA, null), default);

        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.StatementClosingBalance.Should().Be(500.00m);
        report.GlBalance.Should().Be(700.00m, "both receipts (500 matched + 200 in-transit) are posted to GL");
        report.DepositsInTransit.Should().ContainSingle(x => x.Amount == 200.00m);
        report.DepositsInTransitTotal.Should().Be(200.00m);
        report.Difference.Should().Be(0m,
            "GL(700) - deposits-in-transit(200) = 500 = statement — worked independently of the implementation");
    }

    // (2) Outstanding payment: PV A is posted AND matched to a statement line. PV B is posted
    // but has NO statement line (the bank hasn't cleared it yet) — already in GL (decreasing
    // it) but not yet reflected on the statement, so the statement is HIGHER than GL by PV B's
    // amount. Worked independently: statement=-300 (only A shows), GL=-300-100=-400 (both
    // posted), outstanding=100 (B) -> GL + outstanding = -400+100 = -300 = statement.
    [SkippableFact]
    public async Task OutstandingPayment_posted_but_not_on_statement_ties_out_to_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, glCashAccountId) = await SetupAsync();
        var expenseAccountId = await AccountId(sp, "5100");

        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, -300.00m);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyOut, 300.00m, Today);
        var pvA = await SeedPostedPaymentVoucherAsync(sp, co.CompanyId, co.BranchId, 300.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, expenseAccountId, 300.00m, bankDebit: false);

        // PV B — posted (already in GL) but never appears on any statement line.
        await SeedPostedPaymentVoucherAsync(sp, co.CompanyId, co.BranchId, 100.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, expenseAccountId, 100.00m, bankDebit: false);

        await using var s = sp.CreateAsyncScope();
        var reconSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await reconSvc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(null, pvA), default);

        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.StatementClosingBalance.Should().Be(-300.00m);
        report.GlBalance.Should().Be(-400.00m, "both payments (300 matched + 100 outstanding) are posted to GL");
        report.OutstandingPayments.Should().ContainSingle(x => x.Amount == 100.00m);
        report.OutstandingPaymentsTotal.Should().Be(100.00m);
        report.Difference.Should().Be(0m,
            "GL(-400) + outstanding(100) = -300 = statement — worked independently of the implementation");
    }

    // (3) Unmatched bank-fee line: ON the statement, but NEVER posted to GL at all (no
    // Receipt/PV, no inline JE). Worked independently: statement=-20 (the fee), GL=0 (nothing
    // posted), unmatchedNet=-20 (a MoneyOut unmatched line contributes negatively) ->
    // GL + unmatchedNet = 0 + (-20) = -20 = statement.
    [SkippableFact]
    public async Task UnmatchedBankFeeLine_on_statement_no_JE_ties_out_to_zero_with_negative_unmatchedNet()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, _) = await SetupAsync();

        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, -20.00m);
        await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyOut, 20.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.GlBalance.Should().Be(0m, "the fee was never posted to GL");
        report.UnmatchedLines.Should().ContainSingle(x => x.Amount == -20.00m);
        report.UnmatchedLinesNet.Should().Be(-20.00m, "an unmatched MoneyOut line contributes negatively");
        report.UnmatchedLinesNet.Should().BeLessThan(0m);
        report.Difference.Should().Be(0m,
            "GL(0) + unmatchedNet(-20) = -20 = statement — worked independently of the implementation");
    }

    // Cross-period (Fable cross-review finding, 2026-07-09, BLOCKING): a reconciling item dated
    // BEFORE `from` must still be picked up — GL balance and statement balance are CUMULATIVE
    // (<= to only), so the three item queries must be too, or the item silently drops from both
    // the list and the Difference math while still being baked into the two balances. Same shape
    // as the unmatched-bank-fee scenario, but the fee is dated LAST month while the report window
    // is the CURRENT month (from = current month start — the FE's own default) to today.
    [SkippableFact]
    public async Task Item_dated_before_from_still_appears_and_ties_out_cumulatively()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, _) = await SetupAsync();
        var lastMonth = Today.AddMonths(-1);
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);

        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, lastMonth, lastMonth, -20.00m);
        await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyOut, 20.00m, lastMonth);

        await using var s = sp.CreateAsyncScope();
        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        // Report window = the CURRENT month, same as the FE's default filter — the fee is
        // dated BEFORE `from` and must still surface.
        var report = await reportSvc.GetAsync(bankAccountId, monthStart, Today, default);

        report.UnmatchedLines.Should().ContainSingle(x => x.Amount == -20.00m,
            "an item dated before `from` must still appear — the tie-out is cumulative, not windowed");
        report.Difference.Should().Be(0m,
            "GL(0) + unmatchedNet(-20) = -20 = statement, regardless of `from` — a lower-bounded " +
            "item query would have dropped this item and left Difference permanently nonzero");
    }

    // (5) Non-zero case: take the fully-reconciled scenario and alter the STATEMENT's own
    // closing balance to be too high by 50 (simulating a parse/import error, not a real
    // reconciling item) — Difference must be strictly positive (statement over-reports
    // relative to everything GL + reconciling items justify) and equal to exactly +50.
    [SkippableFact]
    public async Task Statement_balance_altered_produces_a_nonzero_difference_with_the_correct_sign()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, glCashAccountId) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");

        // Same as the fully-reconciled scenario, EXCEPT the statement's closing balance is
        // seeded 50 too high (550 instead of 500) — nothing else changes.
        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, 550.00m);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyIn, 500.00m, Today);
        var receiptId = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 500.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 500.00m);

        await using var s = sp.CreateAsyncScope();
        var reconSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await reconSvc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(receiptId, null), default);

        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.Difference.Should().Be(50.00m,
            "statement(550) - (GL(500) - deposits(0) + outstanding(0) + unmatchedNet(0)) = +50, " +
            "positive because the statement over-reports relative to GL + reconciling items");
        report.Difference.Should().BePositive();
    }

    // ── §1 design addendum (2026-07-10) — report SCOPING (T-scope-1..4). Codex review finding
    //    #1: the report previously counted EVERY unmatched company Receipt/PV as outstanding for
    //    whichever bank account was being reconciled, including cash-method docs. ──────────────

    /// <summary>T-scope-1 + T-scope-2 — the addendum's worked example, reproduced verbatim: RC1
    /// Transfer 100 (matched), RC2 Transfer 100 (in-transit, MUST be counted — T-scope-2 guards
    /// over-filtering), RC3 Cash 40 (posts 1110, unmatched, MUST NOT be counted — T-scope-1, the
    /// finding's failing scenario), PV1 Transfer 30 (outstanding), bank-fee MoneyOut 50
    /// (unmatched). GL_1120 = 100+100-30 = 170 (RC3 excluded, unaffected by the fix — it never
    /// posted to 1120). statement = 100-50 = 50. expected = 170-100+30-50 = 50; difference = 0.</summary>
    [SkippableFact]
    public async Task TScope1And2_cash_method_receipt_excluded_transfer_receipt_still_counted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, bankAccountId, glCashAccountId) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");
        var expenseAccountId = await AccountId(sp, "5100");
        var cashAccountId = await AccountId(sp, "1110");

        // RC1 — Transfer 100, matched to a statement line.
        var importId = await SeedImportAsync(sp, co.CompanyId, bankAccountId, Today, Today, 50.00m);
        var lineId = await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyIn, 100.00m, Today);
        var rc1 = await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 100.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 100.00m);

        // RC2 — Transfer 100, unmatched (deposit-in-transit; MUST be counted).
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 100.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, revenueAccountId, 100.00m);

        // RC3 — CASH 40, unmatched (posts 1110; MUST NOT be counted as a deposit for THIS
        // 1120-mapped bank account — the finding's failing scenario, T-scope-1).
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 40.00m, Today, PaymentMethod.Cash);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, cashAccountId, revenueAccountId, 40.00m);

        // PV1 — Transfer 30, unmatched (outstanding).
        var pv1 = await SeedPostedPaymentVoucherAsync(sp, co.CompanyId, co.BranchId, 30.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, glCashAccountId, expenseAccountId, 30.00m, bankDebit: false);
        _ = pv1;

        // Bank fee — statement-only, MoneyOut 50, unmatched.
        await SeedStatementLineAsync(sp, co.CompanyId, importId, bankAccountId, StatementDirection.MoneyOut, 50.00m, Today);

        await using var s = sp.CreateAsyncScope();
        var reconSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        await reconSvc.ConfirmMatchAsync(lineId, new ConfirmMatchRequest(rc1, null), default);

        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(bankAccountId, Today, Today, default);

        report.GlBalance.Should().Be(170.00m, "RC1(100)+RC2(100)-PV1(30) on 1120; RC3 hit 1110, never in this GL balance");
        report.StatementClosingBalance.Should().Be(50.00m, "100(RC1) - 50(fee)");
        report.DepositsInTransit.Should().ContainSingle(x => x.Amount == 100.00m, "RC2 (Transfer) must be counted");
        report.DepositsInTransitTotal.Should().Be(100.00m, "RC3 (Cash) must be excluded — T-scope-1");
        report.OutstandingPayments.Should().ContainSingle(x => x.Amount == 30.00m);
        report.OutstandingPaymentsTotal.Should().Be(30.00m);
        report.UnmatchedLinesNet.Should().Be(-50.00m);
        report.Difference.Should().Be(0m, "170 - 100 + 30 - 50 = 50 = statement — the addendum's re-derived worked example");
        report.DocReconciliationLimited.Should().BeFalse("this bank account IS the primary 1120-mapped account");
    }

    /// <summary>T-scope-3 — a bank account mapped to a NON-1120 sub-account: neither cash(1110)
    /// nor bank(1120) equals its own GlCashAccountId, so both attribution flags are false and the
    /// deposit/outstanding sets are always empty, regardless of what is posted; DocReconciliationLimited
    /// must flag this as a known v1 limitation, not a silent broken tie-out.</summary>
    [SkippableFact]
    public async Task TScope3_non_primary_bank_account_has_empty_sets_and_is_flagged_limited()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, _, primaryGlCashAccountId) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");
        // Reuse an existing seeded Asset account ("1170" Input VAT) as a stand-in non-primary
        // "sub-account" — any Asset account other than 1110/1120 exercises the same code path.
        var subAccountId = await AccountId(sp, "1170");

        await using var s0 = sp.CreateAsyncScope();
        var bankSvc = s0.ServiceProvider.GetRequiredService<IBankAccountService>();
        var subBankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank (sub)", "888-8-88888-8", null, null, subAccountId, "THB"), default);

        // A Transfer receipt posted to the PRIMARY 1120 account — must NOT leak into this
        // sub-account's deposits even though it is a Transfer (not Cash).
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 100.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, primaryGlCashAccountId, revenueAccountId, 100.00m);

        await using var s = sp.CreateAsyncScope();
        var reportSvc = s.ServiceProvider.GetRequiredService<IBankReconciliationReportService>();
        var report = await reportSvc.GetAsync(subBankAccountId, Today, Today, default);

        report.DepositsInTransit.Should().BeEmpty("neither cash(1110) nor bank(1120) equals this sub-account's own GlCashAccountId");
        report.OutstandingPayments.Should().BeEmpty();
        report.DocReconciliationLimited.Should().BeTrue("a non-1120-mapped account is a known v1 limitation, never a silent broken tie-out");
    }

    /// <summary>T-scope-4 coupling guard — the report's cash/bank codes must come from
    /// GlAccountsOptions (the SAME source GlPostingService reads), never a hardcoded "1110"/
    /// "1120" literal. Proven by injecting NON-default codes ("9110"/"9120") directly into a
    /// manually-constructed service instance: if the report ever hardcoded the default codes
    /// instead of reading _accounts, this test would see EMPTY sets (the hardcoded codes would
    /// resolve to the WRONG, unrelated default 1110/1120 accounts) instead of the expected
    /// Transfer-in/Cash-excluded split — failing loudly if posting's rule and the report's
    /// replica ever diverge.</summary>
    [SkippableFact]
    public async Task TScope4_coupling_guard_report_reads_codes_from_GlAccountsOptions_not_hardcoded()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, _, _) = await SetupAsync();
        var revenueAccountId = await AccountId(sp, "4000");

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var customCash = new ChartOfAccount
        {
            CompanyId = co.CompanyId, AccountCode = "9110", AccountNameTh = "เงินสด (ทดสอบ)",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        var customBank = new ChartOfAccount
        {
            CompanyId = co.CompanyId, AccountCode = "9120", AccountNameTh = "ธนาคาร (ทดสอบ)",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.AddRange(customCash, customBank);
        await db.SaveChangesAsync();

        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var customBankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank (custom-codes)", "777-7-77777-7", null, null, customBank.AccountId, "THB"), default);

        // Transfer -> should attribute to the CUSTOM bank code "9120"; Cash -> "9110".
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 100.00m, Today);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, customBank.AccountId, revenueAccountId, 100.00m);
        await SeedPostedReceiptAsync(sp, co.CompanyId, co.BranchId, co.CustomerId, 40.00m, Today, PaymentMethod.Cash);
        await SeedPostedJeAsync(sp, co.CompanyId, co.BranchId, Today, customCash.AccountId, revenueAccountId, 40.00m);

        var tenant = s.ServiceProvider.GetRequiredService<ITenantContext>();
        var customOptions = Options.Create(new GlAccountsOptions { CashAccount = "9110", BankAccount = "9120" });
        var reportSvc = new BankReconciliationReportService(db, tenant, customOptions);
        var report = await reportSvc.GetAsync(customBankAccountId, Today, Today, default);

        report.DepositsInTransit.Should().ContainSingle(x => x.Amount == 100.00m,
            "the Transfer receipt attributes to the INJECTED bank code 9120, proving the source is GlAccountsOptions");
        report.DepositsInTransitTotal.Should().Be(100.00m, "the Cash receipt (9110) must stay excluded under the injected codes too");
    }
}
