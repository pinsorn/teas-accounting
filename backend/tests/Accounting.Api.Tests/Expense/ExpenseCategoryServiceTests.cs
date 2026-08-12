using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Expense;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Expense;

/// <summary>
/// R1/C5 (WP-4, specs/fix-breakit-r1-ledger-integrity.md §3.3) — T17: a poisoned expense
/// category (bad DefaultExpenseAccountId, e.g. co7's category 78) must be refused at CREATE
/// and must be FIXABLE afterwards — corrected via PUT or deactivated via PUT (I20).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExpenseCategoryServiceTests
{
    private readonly PostgresFixture _fx;
    public ExpenseCategoryServiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp)> SetupAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        return (co, sp);
    }

    private static async Task<long> AccountIdByCode(AccountingDbContext db, int companyId, string code) =>
        await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();

    /// <summary>R1/C5 amendment (§3.3, Opus Tier-2 MEDIUM, FIX 3) — DefaultExpenseAccountId is
    /// now REQUIRED on UpdateExpenseCategoryRequest (non-nullable long), closing the footgun
    /// where a partial `{"nameTh":"x","isActive":false}` PUT silently nulled a category's default
    /// account with zero validation (the old `if (req.DefaultExpenseAccountId is {} acctId)` gate
    /// skipped EnsureDefaultAccountAsync entirely when the field was omitted). No live DB needed
    /// — pure validator unit test, does not skip on the Postgres hold.</summary>
    [Fact]
    public void Update_validator_rejects_a_missing_or_zero_DefaultExpenseAccountId()
    {
        var req = new UpdateExpenseCategoryRequest(
            "ชื่อปกติ", null, null, 0, null, true, null, false, false, null, true);
        new UpdateExpenseCategoryValidator().Validate(req).IsValid.Should().BeFalse(
            "a JSON PUT that omits defaultExpenseAccountId deserializes a non-nullable long as 0 — " +
            "the validator must catch that before it reaches the service and nulls/corrupts the record");
    }

    [SkippableFact]
    public async Task Create_with_a_wrong_type_default_account_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var inputVatAcctId = await AccountIdByCode(db, co.CompanyId, "1170"); // Input VAT, Asset, non-capex

        var svc = s.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var act = () => svc.CreateAsync(new CreateExpenseCategoryRequest(
            "T17BAD" + TestIds.Suffix().ToUpperInvariant(), "หมวดผิดประเภท", null, null,
            inputVatAcctId, null, true, null, false, false, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code
            .Should().Be("expense_category.default_account_invalid");
    }

    /// <summary>R1/C5 amendment ROUND 3 (§3.3, Opus Tier-2) — THE denylist test. Without it,
    /// Sys.ExpenseCatManage could point a CAPEX category's default at Bank/InputVat (both
    /// AccountType.Asset — the type check alone allows them for an IsCapex category) and every
    /// ordinary employee's subsequent claim on that category would pass the claim-line rule (the
    /// account now IS the blessed default) and post Dr Bank / Cr Bank — the same P0 the whole WP
    /// exists to close, one permission level up. Covers CREATE.</summary>
    [SkippableTheory]
    [InlineData("1120")] // Bank
    [InlineData("1170")] // Input VAT
    public async Task Create_of_a_CAPEX_category_pointing_at_a_cash_bank_or_VAT_role_account_is_rejected(string roleCode)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var roleAcctId = await AccountIdByCode(db, co.CompanyId, roleCode);

        var svc = s.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var act = () => svc.CreateAsync(new CreateExpenseCategoryRequest(
            "R3BAD" + TestIds.Suffix().ToUpperInvariant(), "หมวด capex พิษ", null, null,
            roleAcctId, null, true, null, /* IsCapex */ true, false, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code
            .Should().Be("expense_category.default_account_invalid",
                $"account {roleCode} is a cash/bank/AR/VAT role — never a legitimate capex default, even under IsCapex");
    }

    /// <summary>Same regression as above — covers UPDATE (repointing an EXISTING valid capex
    /// category at a cash/bank/VAT role account).</summary>
    [SkippableTheory]
    [InlineData("1120")] // Bank
    [InlineData("1170")] // Input VAT
    public async Task Update_of_a_CAPEX_category_to_point_at_a_cash_bank_or_VAT_role_account_is_rejected(string roleCode)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s0 = sp.CreateAsyncScope();
        var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var fixedAssetAcctId = await AccountIdByCode(db0, co.CompanyId, "1610"); // legitimate capex account
        var roleAcctId = await AccountIdByCode(db0, co.CompanyId, roleCode);

        var svc0 = s0.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var id = await svc0.CreateAsync(new CreateExpenseCategoryRequest(
            "R3OK" + TestIds.Suffix().ToUpperInvariant(), "หมวด capex ปกติ", null, null,
            fixedAssetAcctId, null, true, null, /* IsCapex */ true, false, null), default);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var act = () => svc.UpdateAsync(id, new UpdateExpenseCategoryRequest(
            "หมวด capex ปกติ", null, null, roleAcctId, null, true, null,
            /* IsCapex */ true, false, null, true), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code
            .Should().Be("expense_category.default_account_invalid");
    }

    [SkippableFact]
    public async Task Update_can_repoint_a_poisoned_default_account_to_a_valid_one()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s0 = sp.CreateAsyncScope();
        var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bankAcctId = await AccountIdByCode(db0, co.CompanyId, "1120"); // Bank — the poison
        var expenseAcctId = await AccountIdByCode(db0, co.CompanyId, "5200"); // valid fallback expense acct

        // Seeded directly (bypasses CreateAsync's new guard) — simulates a category poisoned
        // BEFORE this fix shipped, exactly like co7's category 78.
        var poisoned = new ExpenseCategory
        {
            CompanyId = co.CompanyId, CategoryCode = "T17POISONED",
            NameTh = "หมวดพิษ", DefaultExpenseAccountId = bankAcctId,
            DefaultIsRecoverableVat = true,
        };
        db0.ExpenseCategories.Add(poisoned);
        await db0.SaveChangesAsync();

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        await svc.UpdateAsync(poisoned.CategoryId, new UpdateExpenseCategoryRequest(
            "หมวดแก้ไขแล้ว", null, null, expenseAcctId, null, true, null, false, false, null, true), default);

        var updated = (await svc.ListAsync(default)).Single(c => c.CategoryId == poisoned.CategoryId);
        updated.DefaultExpenseAccountId.Should().Be(expenseAcctId);
        updated.NameTh.Should().Be("หมวดแก้ไขแล้ว");
        updated.IsActive.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Update_repointing_to_ANOTHER_wrong_type_account_is_still_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s0 = sp.CreateAsyncScope();
        var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseAcctId = await AccountIdByCode(db0, co.CompanyId, "5200");
        var revenueAcctId = await AccountIdByCode(db0, co.CompanyId, "4000");

        var svc0 = s0.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var id = await svc0.CreateAsync(new CreateExpenseCategoryRequest(
            "T17OK", "หมวดปกติ", null, null, expenseAcctId, null, true, null, false, false, null), default);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var act = () => svc.UpdateAsync(id, new UpdateExpenseCategoryRequest(
            "หมวดปกติ", null, null, revenueAcctId, null, true, null, false, false, null, true), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code
            .Should().Be("expense_category.default_account_invalid");
    }

    [SkippableFact]
    public async Task Update_can_deactivate_a_category_and_a_new_claim_against_it_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        await using var s0 = sp.CreateAsyncScope();
        var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseAcctId = await AccountIdByCode(db0, co.CompanyId, "5200");
        var employee = new Employee
        {
            CompanyId = co.CompanyId, EmployeeCode = "EMP-" + TestIds.Suffix(),
            FirstNameTh = "สมชาย", LastNameTh = "ทดสอบ",
            NationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(),
            HireDate = Today.AddYears(-1),
        };
        db0.Employees.Add(employee);
        await db0.SaveChangesAsync();

        var svc0 = s0.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        var catId = await svc0.CreateAsync(new CreateExpenseCategoryRequest(
            "T17DEACT", "หมวดจะปิด", null, null, expenseAcctId, null, true, null, false, false, null), default);

        await using var s1 = sp.CreateAsyncScope();
        var svc1 = s1.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        await svc1.UpdateAsync(catId, new UpdateExpenseCategoryRequest(
            "หมวดจะปิด", null, null, expenseAcctId, null, true, null, false, false, null, false), default);

        await using var s2 = sp.CreateAsyncScope();
        var claimSvc = s2.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var act = () => claimSvc.CreateDraftAsync(new CreateExpenseClaimRequest(
            employee.EmployeeId, Today, "claim on deactivated category", null,
            [new ExpenseClaimLineInput(catId, null, "x", Today, 10m, null, 0m, true)]), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code
            .Should().Be("expense_claim.expense_category_inactive");
    }
}
