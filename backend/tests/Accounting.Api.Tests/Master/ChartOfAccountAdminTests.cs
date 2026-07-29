using System.Reflection;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// Chart-of-accounts admin hardening (specs/manual-jv-and-coa-management.md §1 A1/A2, §4 INV-6/INV-7).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ChartOfAccountAdminTests
{
    private readonly PostgresFixture _fx;
    public ChartOfAccountAdminTests(PostgresFixture fx) => _fx = fx;

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp)> NewCompanyAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        return (co, TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId));
    }

    // ── A1a — CreatedAt / IsActive stamped on create ────────────────────────────────────

    [SkippableFact]
    public async Task CreateAsync_sets_CreatedAt_and_IsActive()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var before = DateTimeOffset.UtcNow.AddSeconds(-5);
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var id = await coa.CreateAsync(new CreateAccountRequest(
                "9700", "บัญชีทดสอบ", "Test account", AccountType.Asset, null, false, NormalBalance.Debit), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var row = await db.ChartOfAccounts.AsNoTracking().FirstAsync(a => a.AccountId == id);
            row.CreatedAt.Should().BeAfter(before, "CreatedAt must not be stamped 0001-01-01");
            row.IsActive.Should().BeTrue();
        }
    }

    // ── A1b — ParentId validation ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task CreateAsync_refuses_nonexistent_parent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var act = () => coa.CreateAsync(new CreateAccountRequest(
                "9701", "ลูก", null, AccountType.Asset, 999_999_999L, false, NormalBalance.Debit), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.parent_not_found");
        }
    }

    [SkippableFact]
    public async Task CreateAsync_refuses_non_header_parent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var nonHeaderParent = await db.ChartOfAccounts.Where(a => a.AccountCode == "1120")
                .Select(a => a.AccountId).FirstAsync();

            var act = () => coa.CreateAsync(new CreateAccountRequest(
                "9702", "ลูก", null, AccountType.Asset, nonHeaderParent, false, NormalBalance.Debit), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.parent_not_header");
        }
    }

    [SkippableFact]
    public async Task CreateAsync_accepts_a_valid_header_parent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var headerId = await coa.CreateAsync(new CreateAccountRequest(
                "9710", "หัวข้อ", null, AccountType.Asset, null, true, NormalBalance.Debit), default);
            var childId = await coa.CreateAsync(new CreateAccountRequest(
                "9711", "ลูก", null, AccountType.Asset, headerId, false, NormalBalance.Debit), default);
            childId.Should().BeGreaterThan(0);
        }
    }

    // ── A1c — header-flip refused when the account already carries posted lines ────────

    [SkippableFact]
    public async Task UpdateAsync_refuses_header_flip_on_account_with_posted_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var accountId = await coa.CreateAsync(new CreateAccountRequest(
                "9720", "มีการผ่านรายการ", null, AccountType.Asset, null, false, NormalBalance.Debit), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await db.ChartOfAccounts.Where(a => a.AccountCode == "1120")
                .Select(a => a.AccountId).FirstAsync();

            // Drive the account through a REAL posting — never seed journal_lines by hand.
            var journals = scope.ServiceProvider.GetRequiredService<IJournalService>();
            await journals.CreateAndPostManualAsync(new CreateManualJournalRequest(
                new SystemClock().TodayInBangkok(), "Posting for header-flip test", null,
                [
                    new ManualJournalLineInput(accountId, 10m, 0m, null, null),
                    new ManualJournalLineInput(bank, 0m, 10m, null, null),
                ]), default);

            var act = () => coa.UpdateAsync(accountId,
                new UpdateAccountRequest("มีการผ่านรายการ", null, true, true), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.has_postings");
        }
    }

    [SkippableFact]
    public async Task UpdateAsync_allows_header_flip_on_account_without_postings()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var accountId = await coa.CreateAsync(new CreateAccountRequest(
                "9721", "ไม่มีรายการ", null, AccountType.Asset, null, false, NormalBalance.Debit), default);

            var act = () => coa.UpdateAsync(accountId,
                new UpdateAccountRequest("ไม่มีรายการ", null, true, true), default);
            await act.Should().NotThrowAsync();
        }
    }

    // ── A1d — a GlAccountsOptions system account cannot be deactivated or header-flipped ──

    [SkippableFact]
    public async Task System_account_cannot_be_deactivated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var arId = await db.ChartOfAccounts.Where(a => a.AccountCode == "1130")
                .Select(a => a.AccountId).FirstAsync();

            var act = () => coa.UpdateAsync(arId,
                new UpdateAccountRequest("ลูกหนี้การค้า", "Accounts Receivable", false, false), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.system_account");
        }
    }

    [SkippableFact]
    public async Task System_account_cannot_be_header_flipped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var arId = await db.ChartOfAccounts.Where(a => a.AccountCode == "1130")
                .Select(a => a.AccountId).FirstAsync();

            var act = () => coa.UpdateAsync(arId,
                new UpdateAccountRequest("ลูกหนี้การค้า", "Accounts Receivable", true, true), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.system_account");
        }
    }

    [Fact]
    public void GlAccountsOptions_AllCodes_covers_every_mapped_string_property()
    {
        var stringProps = typeof(GlAccountsOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.PropertyType == typeof(string));
        new GlAccountsOptions().AllCodes().Count().Should().Be(stringProps,
            "AllCodes() must be kept in sync with every string property — pins drift when someone adds one");
    }

    [SkippableFact]
    public async Task Every_GlAccountsOptions_code_resolves_to_exactly_one_row_per_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlAccountsOptions>>().Value;
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            foreach (var code in options.AllCodes())
            {
                var count = await db.ChartOfAccounts.CountAsync(a => a.CompanyId == co.CompanyId && a.AccountCode == code);
                count.Should().Be(1, $"GL account code {code} must resolve to exactly one row for company {co.CompanyId}");
            }
        }
    }

    // ── C2 — the seed covers both paths (new company via DefaultChartOfAccounts, existing
    // companies via SqlScripts/631). teas_test is a long-lived, persisted DB — the LOWEST
    // company_ids predate this test session and prove the SQL-seed path (631) actually ran. ──

    [SkippableFact]
    public async Task Seeded_codes_2190_5500_4300_exist_for_every_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (newCo, sp) = await NewCompanyAsync();   // proves the DefaultChartOfAccounts (C2a) path
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // Lowest company_ids predate THIS test session (teas_test is long-lived and
            // accumulates companies across runs) — proves the SQL-seed path (631/C2b).
            var preExistingIds = await db.Companies.IgnoreQueryFilters()
                .OrderBy(c => c.CompanyId).Select(c => c.CompanyId).Take(5).ToListAsync();
            // The just-created company proves the DefaultChartOfAccounts path (C2a).
            var companyIds = preExistingIds.Append(newCo.CompanyId).Distinct().ToList();

            foreach (var companyId in companyIds)
            foreach (var code in new[] { "2190", "5500", "4300" })
            {
                var count = await db.ChartOfAccounts.IgnoreQueryFilters()
                    .CountAsync(a => a.CompanyId == companyId && a.AccountCode == code);
                count.Should().Be(1, $"company {companyId} must have account {code} (C2a for new companies, C2b/631 for existing ones)");
            }
        }
    }

    // ── INV-6 — CoA edits never corrupt history ──────────────────────────────────────────

    [Fact]
    public void UpdateAccountRequest_exposes_only_mutable_fields()
    {
        var names = typeof(UpdateAccountRequest).GetProperties().Select(p => p.Name).ToHashSet();
        names.Should().BeEquivalentTo(["AccountNameTh", "AccountNameEn", "IsHeader", "IsActive"],
            "account_code/account_type/normal_balance/parent_id must stay frozen after create");
    }

    [SkippableFact]
    public async Task No_account_delete_route_exists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        var source = factory.Services.GetRequiredService<EndpointDataSource>();

        var deletes = source.Endpoints.OfType<RouteEndpoint>()
            .Where(ep => (ep.RoutePattern.RawText ?? "").TrimStart('/').StartsWith("accounts", StringComparison.OrdinalIgnoreCase))
            .SelectMany(ep => (ep.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []))
            .Where(m => m == "DELETE")
            .ToList();

        deletes.Should().BeEmpty("deactivation is the only retire operation — no delete route may exist");
    }

    [SkippableFact]
    public async Task Duplicate_account_code_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            await coa.CreateAsync(new CreateAccountRequest(
                "9730", "ซ้ำ", null, AccountType.Asset, null, false, NormalBalance.Debit), default);
            var act = () => coa.CreateAsync(new CreateAccountRequest(
                "9730", "ซ้ำอีกครั้ง", null, AccountType.Asset, null, false, NormalBalance.Debit), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("coa.duplicate");
        }
    }

    // ── A2 / INV-6 — deactivation never unbalances the trial balance ───────────────────

    [SkippableFact]
    public async Task Deactivating_an_account_with_movement_keeps_the_trial_balance_balanced()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var accountId = await coa.CreateAsync(new CreateAccountRequest(
                "9740", "จะปิดใช้งาน", null, AccountType.Asset, null, false, NormalBalance.Debit), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await db.ChartOfAccounts.Where(a => a.AccountCode == "1120")
                .Select(a => a.AccountId).FirstAsync();

            var journals = scope.ServiceProvider.GetRequiredService<IJournalService>();
            var today = new SystemClock().TodayInBangkok();
            await journals.CreateAndPostManualAsync(new CreateManualJournalRequest(
                today, "Movement before deactivation", null,
                [
                    new ManualJournalLineInput(accountId, 250m, 0m, null, null),
                    new ManualJournalLineInput(bank, 0m, 250m, null, null),
                ]), default);

            await coa.UpdateAsync(accountId, new UpdateAccountRequest("จะปิดใช้งาน", null, false, false), default);

            var reports = scope.ServiceProvider.GetRequiredService<IFinancialReportService>();
            var tb = await reports.TrialBalanceAsync(today, includeInactive: false, default);
            tb.Rows.Should().Contain(r => r.AccountCode == "9740",
                "an inactive account that still carries movement must still appear");
            tb.Totals.Balanced.Should().BeTrue("deactivating an account with movement must never unbalance the report");
        }
    }
}
