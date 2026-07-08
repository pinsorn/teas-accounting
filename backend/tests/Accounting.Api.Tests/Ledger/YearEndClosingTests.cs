using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Reports;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Ledger;

/// <summary>
/// Year-end closing (specs/year-end-closing.md). Service-level, TestCompanyFactory-isolated.
/// Date strategy (§E): the target fiscal year is derived from a controlled clock
/// (<c>Today.Year - 1</c> — never a hardcoded literal year), revenue/expense JournalEntry
/// rows are seeded POSTED directly via DbContext (JournalService.CreateDraftAsync forces
/// DocDate to clock-today, unusable for a target fiscal year), and the 12 AccountingPeriod
/// Closed rows are seeded directly (D6 — explicit rows only, not IsOpenAsync's implicit rule).
/// "No-perm user -> 403 on close-year/reopen-year" (E9) is already covered generically by
/// RbacCartesianTests (every role x every Perm-gated endpoint) — not duplicated here.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class YearEndClosingTests
{
    private readonly PostgresFixture _fx;
    public YearEndClosingTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private static List<(int Year, int Month)> FiscalMonths(int fiscalYear, int startMonth)
    {
        var start = new DateOnly(fiscalYear, startMonth, 1);
        return Enumerable.Range(0, 12).Select(i => start.AddMonths(i)).Select(d => (d.Year, d.Month)).ToList();
    }

    private static async Task<long> AccountId(AccountingDbContext db, string code) =>
        await db.ChartOfAccounts.Where(a => a.AccountCode == code).Select(a => a.AccountId).FirstAsync();

    private static void AddPostedJe(
        AccountingDbContext db, int companyId, int branchId, DateOnly docDate,
        (long AccountId, decimal Debit, decimal Credit)[] lines)
    {
        var je = new JournalEntry
        {
            CompanyId = companyId, BranchId = branchId,
            // Trailing "X" (not a hex digit) guarantees tax.v_number_gaps' `(\d+)$` regex never
            // matches this DocNo — a bare GUID-hex suffix can end in a long run of digits (e.g.
            // "...11443527012", 11 digits) which overflows that view's `::int` cast (22003) and,
            // once posted, is PERMANENT (JE immutability blocks both UPDATE of doc_no and DELETE
            // of a Posted row) — see troubles-wiki.md "Test-seeded DocNo poisons v_number_gaps".
            DocNo = "JVTEST" + Guid.NewGuid().ToString("N")[..12] + "X",
            PrefixCode = "JV", DocDate = docDate, PostingDate = docDate,
            Description = "Seed JE (year-end closing test)",
            TotalDebit = lines.Sum(l => l.Debit), TotalCredit = lines.Sum(l => l.Credit),
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
            Lines = lines.Select((l, i) => new JournalLine
            {
                LineNo = i + 1, AccountId = l.AccountId, DebitAmount = l.Debit, CreditAmount = l.Credit,
            }).ToList(),
        };
        db.JournalEntries.Add(je);
    }

    /// <summary>Fresh company + a seeded Revenue JV and Expense JV (both dated fiscalEnd) + all
    /// 12 fiscal months explicitly Closed. Default revenue/expense reproduce the spec's worked
    /// check (§C: NetProfit = 400,000).</summary>
    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp, int FiscalYear, DateOnly FiscalEnd)>
        SetupClosableYearAsync(short startMonth = 1, decimal revenue = 1_000_000m, decimal expense = 600_000m)
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var fy = Today.Year - 1;
        var fiscalEnd = new DateOnly(fy, startMonth, 1).AddYears(1).AddDays(-1);
        var months = FiscalMonths(fy, startMonth);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        if (startMonth != 1)
        {
            var c = await db.Companies.FirstAsync(x => x.CompanyId == co.CompanyId);
            c.FiscalYearStartMonth = startMonth;
        }

        var cash = await AccountId(db, "1110");
        var sales = await AccountId(db, "4000");
        var rent = await AccountId(db, "5100");

        if (revenue != 0m)
            AddPostedJe(db, co.CompanyId, co.BranchId, fiscalEnd, [(cash, revenue, 0m), (sales, 0m, revenue)]);
        if (expense != 0m)
            AddPostedJe(db, co.CompanyId, co.BranchId, fiscalEnd, [(rent, expense, 0m), (cash, 0m, expense)]);

        foreach (var (y, m) in months)
            db.AccountingPeriods.Add(new AccountingPeriod
            {
                CompanyId = co.CompanyId, Year = y, Month = (short)m,
                Status = PeriodStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedBy = 1,
            });

        await db.SaveChangesAsync();

        return (co, sp, fy, fiscalEnd);
    }

    // ── E1 — happy path ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task E1_happy_path_posts_closing_je_and_sweeps_pl_accounts()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp, fy, fiscalEnd) = await SetupClosableYearAsync();

        await using var s = sp.CreateAsyncScope();
        var result = await s.ServiceProvider.GetRequiredService<IYearCloseService>()
            .CloseAsync(fy, "test close", default);

        result.NetProfit.Should().Be(400_000m);
        result.ClosingJournalId.Should().NotBeNull();
        result.FiscalEndDate.Should().Be(fiscalEnd);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.FirstAsync(j => j.JournalId == result.ClosingJournalId);
        je.IsClosingEntry.Should().BeTrue();
        je.DocDate.Should().Be(fiscalEnd);
        je.TotalDebit.Should().Be(je.TotalCredit);

        var reportSvc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var re = await AccountId(db, "3300");
        (await reportSvc.GeneralLedgerAsync(re, fiscalEnd, fiscalEnd, default))
            .ClosingBalance.Should().Be(400_000m, "3300 receives the swept net profit");

        var sales = await AccountId(db, "4000");
        (await reportSvc.GeneralLedgerAsync(sales, fiscalEnd, fiscalEnd, default))
            .ClosingBalance.Should().Be(0m, "revenue account swept to zero");

        var rent = await AccountId(db, "5100");
        (await reportSvc.GeneralLedgerAsync(rent, fiscalEnd, fiscalEnd, default))
            .ClosingBalance.Should().Be(0m, "expense account swept to zero");

        var row = await db.FiscalYearCloses.FirstAsync(x => x.FiscalYear == fy && x.CompanyId == co.CompanyId);
        row.NetProfit.Should().Be(400_000m);
        row.ClosingJournalId.Should().Be(result.ClosingJournalId);
    }

    // ── E2 — P&L / CIT / tax-summary correctness AFTER close (the footgun) ─

    [SkippableFact]
    public async Task E2_profit_loss_cit_and_tax_summary_unchanged_after_close()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, fiscalEnd) = await SetupClosableYearAsync();
        var fiscalStart = new DateOnly(fy, 1, 1);

        await using (var s1 = sp.CreateAsyncScope())
        {
            var before = await s1.ServiceProvider.GetRequiredService<IFinancialReportService>()
                .ProfitLossAsync(fiscalStart, fiscalEnd, null, true, default);
            before.Totals.Revenue.Should().Be(1_000_000m);
            before.Totals.Expense.Should().Be(600_000m);
            before.Totals.NetProfit.Should().Be(400_000m);
        }

        await using (var s2 = sp.CreateAsyncScope())
            await s2.ServiceProvider.GetRequiredService<IYearCloseService>().CloseAsync(fy, null, default);

        await using var s3 = sp.CreateAsyncScope();
        var after = await s3.ServiceProvider.GetRequiredService<IFinancialReportService>()
            .ProfitLossAsync(fiscalStart, fiscalEnd, null, true, default);
        after.Totals.Revenue.Should().Be(1_000_000m, "C1 — ProfitLossAsync excludes closing entries");
        after.Totals.Expense.Should().Be(600_000m);
        after.Totals.NetProfit.Should().Be(400_000m, "must NOT read zero after the sweep");

        var taxAfter = await s3.ServiceProvider.GetRequiredService<ITaxSummaryService>().GetAsync(fy, default);
        taxAfter.Totals.NetProfit.Should().Be(400_000m, "C3 — TaxSummaryService excludes closing entries");

        var citAfter = await s3.ServiceProvider.GetRequiredService<ICitYearDataService>()
            .ExpenseByAccountAsync(fy, default);
        citAfter.Sum(r => r.Amount).Should().Be(600_000m, "C2 — CIT expense-by-account excludes closing entries");
    }

    // ── E3 — balance sheet + trial balance AFTER close ──────────────────────

    [SkippableFact]
    public async Task E3_balance_sheet_and_trial_balance_after_close()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, fiscalEnd) = await SetupClosableYearAsync();

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().CloseAsync(fy, null, default);

        await using var s2 = sp.CreateAsyncScope();
        var reportSvc = s2.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var bs = await reportSvc.BalanceSheetAsync(fiscalEnd, default);
        bs.CurrentPeriodEarnings.Should().Be(0m);
        bs.Equity.Rows.Should().Contain(r => r.AccountCode == "3300" && r.Balance == 400_000m);
        bs.Balanced.Should().BeTrue();

        var tb = await reportSvc.TrialBalanceAsync(fiscalEnd, false, default);
        // TrialBalanceAsync sums RAW (gross) Dr/Cr across all posted lines ever touching the
        // account — the original activity's gross debit/credit stays on the books; the sweep
        // adds an offsetting line so the account's NET (Dr-Cr) reads zero, not its gross totals.
        tb.Rows.Where(r => r.AccountCode is "4000" or "5100")
            .Should().OnlyContain(r => r.Net == 0m, "revenue/expense net to zero after the sweep (gross Dr/Cr unaffected)");
        tb.Rows.Should().Contain(r => r.AccountCode == "3300" && r.Debit == 0m && r.Credit == 400_000m);
        tb.Totals.Balanced.Should().BeTrue();
    }

    // ── E4 — reject: periods not all closed ─────────────────────────────────

    [SkippableFact]
    public async Task E4_rejects_when_periods_not_all_closed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var fy = Today.Year - 1;
        var months = FiscalMonths(fy, 1);

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // Close only 11 of 12 months — leave December open.
            foreach (var (y, m) in months.Where(x => x.Month != 12))
                db.AccountingPeriods.Add(new AccountingPeriod
                {
                    CompanyId = co.CompanyId, Year = y, Month = (short)m,
                    Status = PeriodStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedBy = 1,
                });
            await db.SaveChangesAsync();
        }

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IYearCloseService>();
        var act = () => svc.CloseAsync(fy, null, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Code.Should().Be("year.periods_not_closed");
        ex.Message.Should().Contain($"{fy}-12");

        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db2.JournalEntries.CountAsync(j => j.IsClosingEntry)).Should().Be(0);
        (await db2.FiscalYearCloses.CountAsync()).Should().Be(0);
    }

    // ── E5 — reject: double close ────────────────────────────────────────────

    [SkippableFact]
    public async Task E5_rejects_double_close()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, _) = await SetupClosableYearAsync();

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().CloseAsync(fy, null, default);

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IYearCloseService>();
        var act = () => svc.CloseAsync(fy, null, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("year.already_closed");

        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.FiscalYearCloses.CountAsync(x => x.FiscalYear == fy && x.ReversedAt == null)).Should().Be(1);
        (await db.JournalEntries.CountAsync(j => j.IsClosingEntry)).Should().Be(1);
    }

    // ── E6 — reopen ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task E6_reopen_reverses_close_and_allows_reclose()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, fiscalEnd) = await SetupClosableYearAsync();
        var fiscalStart = new DateOnly(fy, 1, 1);

        long closingJournalId;
        await using (var s = sp.CreateAsyncScope())
            closingJournalId = (await s.ServiceProvider.GetRequiredService<IYearCloseService>()
                .CloseAsync(fy, null, default)).ClosingJournalId!.Value;

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().ReopenAsync(fy, default);

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var row = await db.FiscalYearCloses.FirstAsync(x => x.FiscalYear == fy);
        row.ReversedAt.Should().NotBeNull();
        row.ReversingJournalId.Should().NotBeNull();

        var reversingJe = await db.JournalEntries.FirstAsync(j => j.JournalId == row.ReversingJournalId);
        reversingJe.ReversalOfId.Should().Be(closingJournalId);
        reversingJe.IsClosingEntry.Should().BeTrue();

        var reportSvc = s2.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var re = await AccountId(db, "3300");
        (await reportSvc.GeneralLedgerAsync(re, fiscalEnd, fiscalEnd, default))
            .ClosingBalance.Should().Be(0m, "the reversal swaps Dr/Cr, zeroing 3300 back out");

        var pl = await reportSvc.ProfitLossAsync(fiscalStart, fiscalEnd, null, true, default);
        pl.Totals.NetProfit.Should().Be(400_000m, "P&L still excludes both closing and reversal entries");

        // Filtered-unique slot freed by ReversedAt — a fresh close succeeds.
        await using var s3 = sp.CreateAsyncScope();
        var result2 = await s3.ServiceProvider.GetRequiredService<IYearCloseService>()
            .CloseAsync(fy, "re-close", default);
        result2.ClosingJournalId.Should().NotBeNull();
    }

    // ── E7 — zero-activity year ──────────────────────────────────────────────

    [SkippableFact]
    public async Task E7_zero_activity_year_posts_no_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, _) = await SetupClosableYearAsync(revenue: 0m, expense: 0m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IYearCloseService>();
        var result = await svc.CloseAsync(fy, null, default);

        result.ClosingJournalId.Should().BeNull();
        result.NetProfit.Should().Be(0m);

        var status = await svc.GetStatusAsync(fy, default);
        status.IsClosed.Should().BeTrue();
        status.ClosingJournalId.Should().BeNull();
    }

    // ── E8 — non-January fiscal year ─────────────────────────────────────────

    [SkippableFact]
    public async Task E8_non_january_fiscal_year_closes_correctly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, fiscalEnd) = await SetupClosableYearAsync(startMonth: 4);

        fiscalEnd.Should().Be(new DateOnly(fy + 1, 3, 31), "Apr(N)-Mar(N+1) fiscal year");

        await using var s = sp.CreateAsyncScope();
        var result = await s.ServiceProvider.GetRequiredService<IYearCloseService>()
            .CloseAsync(fy, null, default);
        result.NetProfit.Should().Be(400_000m);
        result.FiscalEndDate.Should().Be(fiscalEnd);
        result.ClosingJournalId.Should().NotBeNull();

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.FirstAsync(j => j.JournalId == result.ClosingJournalId);
        je.DocDate.Should().Be(fiscalEnd);
    }

    // ── E9 — RLS / tenant isolation ──────────────────────────────────────────

    [SkippableFact]
    public async Task E9_rls_and_tenant_isolation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (coA, spA, fyA, _) = await SetupClosableYearAsync();
        var (coB, spB, _, _) = await SetupClosableYearAsync();

        long closingJeA;
        await using (var s = spA.CreateAsyncScope())
            closingJeA = (await s.ServiceProvider.GetRequiredService<IYearCloseService>()
                .CloseAsync(fyA, null, default)).ClosingJournalId!.Value;

        // App layer — company B's own tenant-scoped provider must not see A's close via the
        // EF global query filter (no cross-tenant leak on year-status reads).
        await using (var s = spB.CreateAsyncScope())
        {
            var statusB = await s.ServiceProvider.GetRequiredService<IYearCloseService>()
                .GetStatusAsync(fyA, default);
            statusB.IsClosed.Should().BeFalse("company B has no close record for A's fiscal year");
        }

        // DB layer — RLS under a NOBYPASSRLS role (teas_test's `accounting` login bypasses RLS
        // and would be a false-green; same trick as SuperAdminTenantScopeRlsTests).
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        async Task ExecAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        async Task<long> ScalarAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        await ExecAsync(
            "GRANT USAGE ON SCHEMA gl TO pg_database_owner; " +
            "GRANT SELECT ON gl.fiscal_year_closes TO pg_database_owner; " +
            "GRANT SELECT ON gl.journal_entries TO pg_database_owner;");
        try
        {
            await ExecAsync("SET ROLE pg_database_owner");
            await ExecAsync($"SELECT set_config('app.company_id', '{coB.CompanyId}', false)");

            (await ScalarAsync(
                $"SELECT count(*) FROM gl.fiscal_year_closes WHERE fiscal_year = {fyA} AND company_id = {coA.CompanyId}"))
                .Should().Be(0, "company B pinned in session must not see company A's fiscal_year_closes row");
            (await ScalarAsync($"SELECT count(*) FROM gl.journal_entries WHERE journal_id = {closingJeA}"))
                .Should().Be(0, "company B pinned in session must not see company A's closing JE");
        }
        finally
        {
            await ExecAsync("RESET ROLE");
        }
    }

    // ── E11 — Tier-2 BLOCKING regression: second year's close sweeps ONLY its own year ──

    [SkippableFact]
    public async Task E11_second_year_close_sweeps_only_its_own_year()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // FY N: revenue 1,000,000 / expense 600,000 -> NetProfit 400,000 (SetupClosableYearAsync defaults).
        var (co, sp, fyN, _) = await SetupClosableYearAsync();

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().CloseAsync(fyN, null, default);

        var fyN1 = fyN + 1;
        var fiscalEndN1 = new DateOnly(fyN1, 12, 31);

        // FY N+1 activity — deliberately DIFFERENT amounts (300k/100k) so a bug that re-sweeps
        // FY N's 1,000,000/600,000 would produce a visibly wrong NetProfit, not a coincidental match.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var cash = await AccountId(db, "1110");
            var sales = await AccountId(db, "4000");
            var rent = await AccountId(db, "5100");

            AddPostedJe(db, co.CompanyId, co.BranchId, fiscalEndN1, [(cash, 300_000m, 0m), (sales, 0m, 300_000m)]);
            AddPostedJe(db, co.CompanyId, co.BranchId, fiscalEndN1, [(rent, 100_000m, 0m), (cash, 0m, 100_000m)]);

            foreach (var (y, m) in FiscalMonths(fyN1, 1))
                db.AccountingPeriods.Add(new AccountingPeriod
                {
                    CompanyId = co.CompanyId, Year = y, Month = (short)m,
                    Status = PeriodStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedBy = 1,
                });
            await db.SaveChangesAsync();
        }

        await using var s2 = sp.CreateAsyncScope();
        var result2 = await s2.ServiceProvider.GetRequiredService<IYearCloseService>()
            .CloseAsync(fyN1, null, default);

        result2.NetProfit.Should().Be(200_000m,
            "year-2 NetProfit must be year-2-ONLY (300k-100k=200k) — a re-sweep of FY N would combine to 600k");

        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var reportSvc = s2.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var sales2 = await AccountId(db2, "4000");
        (await reportSvc.GeneralLedgerAsync(sales2, fiscalEndN1, fiscalEndN1, default))
            .ClosingBalance.Should().Be(0m, "revenue account nets to zero as-of end of N+1 — no phantom balance");

        var rent2 = await AccountId(db2, "5100");
        (await reportSvc.GeneralLedgerAsync(rent2, fiscalEndN1, fiscalEndN1, default))
            .ClosingBalance.Should().Be(0m, "expense account nets to zero as-of end of N+1");

        var re = await AccountId(db2, "3300");
        (await reportSvc.GeneralLedgerAsync(re, fiscalEndN1, fiscalEndN1, default))
            .ClosingBalance.Should().Be(600_000m, "RE = sum of both years' NetProfit (400k + 200k), not overstated");

        (await db2.FiscalYearCloses.CountAsync(x => x.CompanyId == co.CompanyId && x.ReversedAt == null))
            .Should().Be(2, "both FY N and FY N+1 have their own active close record");
    }

    // ── E12 — Tier-2 non-blocking regression: double reopen guard ───────────

    [SkippableFact]
    public async Task E12_double_reopen_fails_cleanly_with_exactly_one_reversal()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp, fy, _) = await SetupClosableYearAsync();

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().CloseAsync(fy, null, default);

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IYearCloseService>().ReopenAsync(fy, default);

        // Second reopen — must fail cleanly (year.not_closed), not post a second reversing JE
        // (which would swing 3300 to -NetProfit).
        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IYearCloseService>();
        var act = () => svc.ReopenAsync(fy, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("year.not_closed");

        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.CountAsync(j => j.IsClosingEntry && j.ReversalOfId != null))
            .Should().Be(1, "exactly ONE reversing JE must exist even after a second reopen attempt");
    }
}
