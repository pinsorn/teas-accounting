using Accounting.Api.Tests.Fixtures;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// WP1.5a (F20, D7 — specs/fix-purchase-ux-findings-2026-07-14.md) — CompanyService.CreateAsync
/// now auto-seeds the 19 recommended expense categories (DefaultExpenseCategories helper),
/// each resolved to a real CoA account from the SAME company's just-seeded DefaultChartOfAccounts.
/// A freshly onboarded company must never hit F20 (a category savable with NULL default account
/// that 422s vi/pv.expense_account_missing the first time a document line uses it).
/// TestCompanyFactory.CreateAsync already routes through the real ICompanyService.CreateAsync —
/// this test just asserts what that seeding left behind.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyCreateExpenseCategorySeedTests
{
    private readonly PostgresFixture _fx;
    public CompanyCreateExpenseCategorySeedTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CreateCompany_SeedsExpenseCategoriesWithAccounts()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var cats = await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.CompanyId == co.CompanyId).ToListAsync();

        cats.Should().HaveCount(19, "the 19-code recommended set (mirrors 430_seed_expense_categories_full.sql)");
        cats.Should().OnlyContain(c => c.DefaultExpenseAccountId != null,
            "F20 — a category with a NULL default account 422s the first document line that uses it");

        // Spot-check the account remap onto DefaultChartOfAccounts' coarser codes (the granular
        // 62xxx chart only the demo company gets does not exist for a real onboarded tenant).
        // Resolve the target account ids from the in-memory `cats` list FIRST — embedding a
        // List<T>.First(...) call inside an EF query lambda fails query translation.
        var rentAccountId = cats.First(c => c.CategoryCode == "RENT").DefaultExpenseAccountId;
        var rentAccountCode = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountId == rentAccountId)
            .Select(a => a.AccountCode).FirstAsync();
        rentAccountCode.Should().Be("5100");

        var capexAccountId = cats.First(c => c.CategoryCode == "CAPEX").DefaultExpenseAccountId;
        var capexAccountCode = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountId == capexAccountId)
            .Select(a => a.AccountCode).FirstAsync();
        capexAccountCode.Should().Be("1610");

        var cogs = cats.First(c => c.CategoryCode == "COGS");
        cogs.IsCogs.Should().BeTrue();
        // No dedicated COGS account exists in DefaultChartOfAccounts — falls back to 5200
        // (same universal fallback as 623_backfill_expense_category_accounts.sql).
        var cogsAccountCode = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountId == cogs.DefaultExpenseAccountId)
            .Select(a => a.AccountCode).FirstAsync();
        cogsAccountCode.Should().Be("5200");
    }
}
