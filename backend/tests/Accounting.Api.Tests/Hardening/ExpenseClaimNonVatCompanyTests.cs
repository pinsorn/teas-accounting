using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Expense;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// F-B (specs/fix-purchase-nonvat-ux.md) — a non-VAT-registered company must never book
/// recoverable input VAT on an expense claim, even when the client explicitly sends
/// isRecoverableVat=true + vatRate&gt;0 (an MCP/API-key caller, or a claim drafted while the
/// company was still VAT-registered). Mirrors VendorInvoiceNonVatCompanyTests: server-forced in
/// ExpenseClaimService.BuildLinesAsync (create/update) and re-asserted in PayAsync (defence for a
/// claim approved before the company flipped non-VAT, or drafted before this guard existed). GL
/// posting code itself is UNTOUCHED — only the input it reads (line IsRecoverableVat) changes.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExpenseClaimNonVatCompanyTests
{
    private readonly PostgresFixture _fx;
    public ExpenseClaimNonVatCompanyTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private static async Task<long> SeedEmployeeAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var e = new Employee
        {
            CompanyId = companyId,
            EmployeeCode = "EMP-" + TestIds.Suffix(),
            FirstNameTh = "สมหญิง", LastNameTh = "ทดสอบ",
            NationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(),
            HireDate = Today.AddYears(-1),
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e.EmployeeId;
    }

    private static async Task<(int CategoryId, long AccountId)> SeedCategoryAsync(
        ServiceProvider sp, int companyId, string accountCode, string nameTh)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var acct = new ChartOfAccount
        {
            CompanyId = companyId, AccountCode = accountCode, AccountNameTh = nameTh,
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.Add(acct);
        await db.SaveChangesAsync();

        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = nameTh, DefaultExpenseAccountId = acct.AccountId,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, acct.AccountId);
    }

    [SkippableFact]
    public async Task NonVatCompany_ForcesNonRecoverable_OnCreateAndPay_NoInputVatLine()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, acctId) = await SeedCategoryAsync(sp, co.CompanyId, "6210", "ค่าใช้จ่ายทดสอบ (non-VAT)");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Even an explicit isRecoverableVat=true + vatRate=7% from the caller must be overridden.
        var id = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
            employeeId, Today, "non-VAT co claim", null,
            [new ExpenseClaimLineInput(catId, null, "ค่าน้ำมัน", Today, 1000m, null, 0.07m, true)]), default);

        var detail = await svc.GetDetailAsync(id, default);
        detail!.Lines.Should().ContainSingle().Which.IsRecoverableVat.Should().BeFalse(
            "a non-VAT company must never book recoverable input VAT");
        detail.VatAmount.Should().Be(70m, "the VAT the employee actually paid is still real money — folded into cost, not dropped");
        detail.TotalAmount.Should().Be(1070m);

        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);
        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default);
        paid.TotalAmount.Should().Be(1070m);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == paid.JournalEntryId);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.TotalDebit.Should().Be(1070m);

        je.Lines.Should().ContainSingle(l => l.AccountId == acctId)
            .Which.DebitAmount.Should().Be(1070m, "net 1000 + non-recoverable VAT 70 folded into the expense debit");

        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().NotContain("1170", "no Input VAT (1170) debit on a non-VAT company");
    }

    // Regression — a VAT-registered company is unaffected by the guard.
    [SkippableFact]
    public async Task VatRegisteredCompany_StillBooksRecoverableVat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, acctId) = await SeedCategoryAsync(sp, co.CompanyId, "6220", "ค่าใช้จ่ายทดสอบ (VAT)");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var id = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
            employeeId, Today, "VAT co claim", null,
            [new ExpenseClaimLineInput(catId, null, "ค่าน้ำมัน", Today, 1000m, null, 0.07m, true)]), default);

        var detail = await svc.GetDetailAsync(id, default);
        detail!.Lines.Should().ContainSingle().Which.IsRecoverableVat.Should().BeTrue();

        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);
        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == paid.JournalEntryId);
        var inputVatAcctId = await db.ChartOfAccounts.Where(a => a.CompanyId == co.CompanyId && a.AccountCode == "1170")
            .Select(a => a.AccountId).FirstAsync();
        je.Lines.Should().ContainSingle(l => l.AccountId == inputVatAcctId)
            .Which.DebitAmount.Should().Be(70m);
        je.Lines.Should().ContainSingle(l => l.AccountId == acctId)
            .Which.DebitAmount.Should().Be(1000m);
    }

    // Defence-in-depth — PayAsync's re-guard: a claim approved while the company WAS
    // VAT-registered, then the company flips to non-VAT before Pay, must still never post 1170.
    // EXERCISES the real CreateDraft -> Submit -> Approve -> Pay transition (not a seeded row).
    [SkippableFact]
    public async Task CompanyFlipsNonVat_AfterApprove_PayStillForcesNonRecoverable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, acctId) = await SeedCategoryAsync(sp, co.CompanyId, "6230", "ค่าใช้จ่ายทดสอบ (flip)");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var id = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
            employeeId, Today, "flip co claim", null,
            [new ExpenseClaimLineInput(catId, null, "ค่าน้ำมัน", Today, 1000m, null, 0.07m, true)]), default);
        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);

        // Company flips to non-VAT AFTER approve (direct DB write — simulates a /settings/companies
        // edit between approval and payment; the claim's already-persisted line still says recoverable).
        var company = await db.Companies.FirstAsync(c => c.CompanyId == co.CompanyId);
        company.VatRegistered = false;
        await db.SaveChangesAsync();

        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == paid.JournalEntryId);
        je.Lines.Should().ContainSingle(l => l.AccountId == acctId)
            .Which.DebitAmount.Should().Be(1070m, "the stale recoverable flag must not survive to posting");

        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().NotContain("1170");
    }
}
