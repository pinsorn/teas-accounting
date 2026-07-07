using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// General Ledger (บัญชีแยกประเภท) — per-account drill-down + JE detail. Service-level,
/// TestCompanyFactory-isolated (spec mandate) so opening/rows/closing math is exact and
/// never polluted by company 1's shared history. Manual JVs are pinned to TODAY server-side
/// (JournalService §10), so date ranges are built around "today", never a hardcoded past
/// period (fresh-teas_test period-close footgun).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class GeneralLedgerReportTests
{
    private readonly PostgresFixture _fx;
    public GeneralLedgerReportTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<TestCompanyFactory.SeededCompany> NewCompany() =>
        await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

    private static async Task<long> AccountId(
        Accounting.Infrastructure.Persistence.AccountingDbContext db, string code) =>
        await db.ChartOfAccounts.Where(a => a.AccountCode == code).Select(a => a.AccountId).FirstAsync();

    private static async Task<long> PostJv(
        ServiceProvider sp, long drAccount, long crAccount, decimal amount, bool post = true)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IJournalService>();
        var id = await svc.CreateDraftAsync(new CreateJournalRequest(
            Today, Today, "GL test JV", "REF-" + amount, "THB", 1m,
            [
                new JournalLineInput(drAccount, amount, 0m, "dr line", "dr-ref", null),
                new JournalLineInput(crAccount, 0m, amount, "cr line", "cr-ref", null),
            ]), default);
        if (post)
        {
            await using var s2 = sp.CreateAsyncScope();
            await s2.ServiceProvider.GetRequiredService<IJournalService>().PostAsync(id, default);
        }
        return id;
    }

    // ── Opening balance ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Opening_balance_reflects_posted_je_before_range_and_rows_exclude_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        long cash, ap;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cash = await AccountId(db, "1110");
            ap = await AccountId(db, "2110");
        }

        await PostJv(sp, cash, ap, 1000m); // DocDate = today

        await using var s3 = sp.CreateAsyncScope();
        var svc = s3.ServiceProvider.GetRequiredService<IFinancialReportService>();
        // Range starts AFTER today — the JV posted above is "before range" → opening only.
        var report = await svc.GeneralLedgerAsync(cash, Today.AddDays(1), Today.AddDays(30), default);

        report.OpeningBalance.Should().Be(1000m, "Cash is DR-normal: dr(1000) - cr(0) = 1000");
        report.Rows.Should().BeEmpty("the JV's DocDate (today) is before fromDate");
        report.ClosingBalance.Should().Be(1000m, "no movement in range → closing == opening");
    }

    // ── Running balance (DR-normal + CR-normal, same two JVs) ──────────────

    [SkippableFact]
    public async Task Running_balance_walks_correctly_for_debit_and_credit_normal_accounts()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        long cash, ap;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cash = await AccountId(db, "1110");
            ap = await AccountId(db, "2110");
        }

        await PostJv(sp, cash, ap, 500m); // Cash Dr 500 / AP Cr 500
        await PostJv(sp, ap, cash, 200m); // AP Dr 200 / Cash Cr 200

        await using var s3 = sp.CreateAsyncScope();
        var svc = s3.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var from = Today; var to = Today.AddDays(5);

        // Cash — DR-normal: signed(dr,cr) = dr - cr.
        var cashReport = await svc.GeneralLedgerAsync(cash, from, to, default);
        cashReport.OpeningBalance.Should().Be(0m);
        cashReport.Rows.Should().HaveCount(2);
        cashReport.Rows[0].RunningBalance.Should().Be(500m, "0 + (500-0)");
        cashReport.Rows[1].RunningBalance.Should().Be(300m, "500 + (0-200)");
        cashReport.TotalDebit.Should().Be(500m);
        cashReport.TotalCredit.Should().Be(200m);
        cashReport.ClosingBalance.Should().Be(300m, "0 + (500-200)");

        // AP — CR-normal: signed(dr,cr) = cr - dr (mirror check).
        var apReport = await svc.GeneralLedgerAsync(ap, from, to, default);
        apReport.OpeningBalance.Should().Be(0m);
        apReport.Rows.Should().HaveCount(2);
        apReport.Rows[0].RunningBalance.Should().Be(500m, "0 + (500-0) cr-dr");
        apReport.Rows[1].RunningBalance.Should().Be(300m, "500 + (0-200) cr-dr");
        apReport.ClosingBalance.Should().Be(300m, "0 + (500-200)");
    }

    // ── Draft excluded ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Draft_je_is_excluded_from_opening_and_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        long cash, ap;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cash = await AccountId(db, "1110");
            ap = await AccountId(db, "2110");
        }

        await PostJv(sp, cash, ap, 999m, post: false); // DRAFT only — never posted.

        await using var s3 = sp.CreateAsyncScope();
        var svc = s3.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var report = await svc.GeneralLedgerAsync(cash, Today.AddDays(-0), Today.AddDays(10), default);

        report.OpeningBalance.Should().Be(0m, "draft JEs never contribute to opening");
        report.Rows.Should().BeEmpty("draft JEs never appear as rows");
    }

    // ── Two lines, same account, one JE → two rows ─────────────────────────

    [SkippableFact]
    public async Task Two_lines_on_same_account_in_one_je_produce_two_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        long cash, ap;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cash = await AccountId(db, "1110");
            ap = await AccountId(db, "2110");
        }

        long jId;
        await using (var s = sp.CreateAsyncScope())
        {
            var jsvc = s.ServiceProvider.GetRequiredService<IJournalService>();
            jId = await jsvc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Two lines same account", null, "THB", 1m,
                [
                    new JournalLineInput(cash, 300m, 0m, "line 1", null, null),
                    new JournalLineInput(cash, 200m, 0m, "line 2", null, null),
                    new JournalLineInput(ap, 0m, 500m, "balancing", null, null),
                ]), default);
        }
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IJournalService>().PostAsync(jId, default);

        await using var s3 = sp.CreateAsyncScope();
        var svc = s3.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var report = await svc.GeneralLedgerAsync(cash, Today, Today.AddDays(1), default);

        report.Rows.Should().HaveCount(2, "one JE with 2 lines on the same account = 2 rows");
        report.Rows.Select(r => r.JournalId).Should().AllBeEquivalentTo(jId);
        report.Rows[0].RunningBalance.Should().Be(300m);
        report.Rows[1].RunningBalance.Should().Be(500m);
    }

    // ── fromDate > toDate is an endpoint-level (400) concern — see the HTTP test file. ──

    // ── Unknown / other-tenant accountId → not-found ───────────────────────

    [SkippableFact]
    public async Task Unknown_account_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var act = () => svc.GeneralLedgerAsync(999_999_999L, Today, Today.AddDays(1), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("gl_account.not_found");
    }

    [SkippableFact]
    public async Task Other_tenant_account_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await NewCompany();
        var coB = await NewCompany();
        var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        long cashB;
        await using (var s = spB.CreateAsyncScope())
            cashB = await AccountId(s.ServiceProvider.GetRequiredService<AccountingDbContext>(), "1110");

        var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coA.CompanyId, coA.BranchId);
        await using var sA = spA.CreateAsyncScope();
        var svc = sA.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var act = () => svc.GeneralLedgerAsync(cashB, Today, Today.AddDays(1), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("gl_account.not_found",
            "EF global query filter hides company B's account from company A — indistinguishable from unknown");
    }

    // ── Account picker ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Account_picker_lists_only_active_non_header_accounts_ordered_by_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();

        var options = await svc.GeneralLedgerAccountsAsync(default);

        options.Should().NotBeEmpty();
        options.Should().BeInAscendingOrder(o => o.AccountCode);
        options.Should().Contain(o => o.AccountCode == "1110" && o.NormalBalance == "DR");
        options.Should().Contain(o => o.AccountCode == "2110" && o.NormalBalance == "CR");
    }

    // ── JE detail ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Je_detail_totals_equal_and_lines_carry_account_code_and_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await NewCompany();
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        long cash, ap;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cash = await AccountId(db, "1110");
            ap = await AccountId(db, "2110");
        }

        var jId = await PostJv(sp, cash, ap, 750m);

        await using var s3 = sp.CreateAsyncScope();
        var jsvc = s3.ServiceProvider.GetRequiredService<IJournalService>();
        var detail = await jsvc.GetDetailAsync(jId, default);

        detail.TotalDebit.Should().Be(750m);
        detail.TotalCredit.Should().Be(750m);
        detail.Status.Should().Be("Posted");
        detail.Lines.Should().HaveCount(2);
        detail.Lines.Should().Contain(l => l.AccountId == cash && l.AccountCode == "1110" && l.AccountNameTh == "เงินสด");
        detail.Lines.Should().Contain(l => l.AccountId == ap && l.AccountCode == "2110");
    }

    [SkippableFact]
    public async Task Je_detail_wrong_tenant_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await NewCompany();
        var coB = await NewCompany();

        var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        long cashB, apB;
        await using (var s = spB.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            cashB = await AccountId(db, "1110");
            apB = await AccountId(db, "2110");
        }
        var jIdB = await PostJv(spB, cashB, apB, 100m);

        var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coA.CompanyId, coA.BranchId);
        await using var sA = spA.CreateAsyncScope();
        var jsvc = sA.ServiceProvider.GetRequiredService<IJournalService>();

        var act = () => jsvc.GetDetailAsync(jIdB, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.not_found");
    }
}
