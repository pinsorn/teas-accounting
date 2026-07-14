using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
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
/// WP1.2 (F27, D1 — specs/fix-purchase-ux-findings-2026-07-14.md) — a non-VAT company must
/// never book recoverable input VAT, even when the vendor IS VAT-registered and the caller
/// explicitly requests HasInputVat=true (an MCP/API-key caller, say). Server-forced in
/// VendorInvoiceService.CreateDraftAsync and re-asserted in PostAsync (defence for a draft that
/// predates the guard). GL posting code itself is UNTOUCHED — only the inputs it reads
/// (HasInputVat, line IsRecoverableVat) change.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class VendorInvoiceNonVatCompanyTests
{
    private readonly PostgresFixture _fx;
    public VendorInvoiceNonVatCompanyTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateOnly TodayBkk = new SystemClock().TodayInBangkok();

    private static async Task<(long vendorId, int categoryId)> SeedVendorAndCategoryAsync(
        string connectionString, int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(connectionString, companyId, branchId);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseId = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();

        var vendor = new Vendor
        {
            CompanyId = companyId, VendorCode = TestIds.VendorCode("NV"),
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายจด VAT ทดสอบ",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode("NV"),
            NameTh = "หมวดทดสอบ", DefaultExpenseAccountId = expenseId,
            DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(vendor);
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (vendor.VendorId, cat.CategoryId);
    }

    [SkippableFact]
    public async Task NonVatCompany_ForcesNonRecoverable_OnCreateAndPost()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Even an explicit HasInputVat=true from the caller must be overridden (D1).
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)],
            HasInputVat: true), default);

        var vi = await db.VendorInvoices.Include(v => v.Lines).AsNoTracking()
            .FirstAsync(v => v.VendorInvoiceId == id);
        vi.HasInputVat.Should().BeFalse("a non-VAT company must never book recoverable input VAT (D1)");
        vi.Lines.Should().OnlyContain(l => !l.IsRecoverableVat);
        vi.NonRecoverableVatAmount.Should().Be(70m);
        vi.VatAmount.Should().Be(0m, "the 70 VAT must land in NonRecoverableVatAmount, not VatAmount");

        var posted = await svc.PostAsync(id, default);
        posted.VatAmount.Should().Be(0m);

        var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.TotalDebit.Should().Be(1070m); // Dr Expense 1000+70 (cost) / Cr AP 1070 — no Input VAT line.

        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().NotContain("1170", "no Input VAT (1170) debit on a non-VAT company");
    }

    // F-2 (Opus Tier-2 review) — BuildLinesAsync re-snapshots IsRecoverableVat from the
    // category (true here) on every UpdateDraftAsync call, so editing a non-VAT company's
    // draft must re-apply the guard there too, or the header goes inconsistent until PostAsync
    // self-heals it. EXERCISES the real UpdateDraftAsync transition (not a seeded row).
    [SkippableFact]
    public async Task NonVatCompany_UpdateDraft_KeepsHeaderNonRecoverable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var tiNo = "VTI-" + TestIds.Suffix()[..6];
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: tiNo,
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)]), default);

        // Real edit (amount 1000 -> 2000), NOT a no-op — proves UpdateDraftAsync's own guard
        // fires, not just a leftover value from CreateDraftAsync.
        await svc.UpdateDraftAsync(id, new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: tiNo,
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 2000m, 0.07m)]), default);

        var vi = await db.VendorInvoices.Include(v => v.Lines).AsNoTracking()
            .FirstAsync(v => v.VendorInvoiceId == id);
        vi.HasInputVat.Should().BeFalse("editing a non-VAT company's draft must not resurrect recoverable VAT");
        vi.Lines.Should().OnlyContain(l => !l.IsRecoverableVat,
            "BuildLinesAsync re-snapshots IsRecoverableVat=true from the category on every edit " +
            "unless UpdateDraftAsync re-applies the WP1.2 guard");
        vi.NonRecoverableVatAmount.Should().Be(140m); // 2000 * 0.07
        vi.VatAmount.Should().Be(0m, "the header must not go inconsistent between edit and post");
    }

    // Regression — a VAT-registered company is unaffected by the WP1.2 guard.
    [SkippableFact]
    public async Task VatRegisteredCompany_StillBooksRecoverableVat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)]), default);

        var vi = await db.VendorInvoices.Include(v => v.Lines).AsNoTracking()
            .FirstAsync(v => v.VendorInvoiceId == id);
        vi.HasInputVat.Should().BeTrue();
        vi.Lines.Should().OnlyContain(l => l.IsRecoverableVat);
        vi.VatAmount.Should().Be(70m);
    }
}
