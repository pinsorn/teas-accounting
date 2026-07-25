using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// F-5 (Opus Tier-2 review, specs/fix-purchase-ux-findings-2026-07-14.md "Residual
/// follow-ups — WP1") — the VAT/WHT rate InclusiveBetween(0,1) bound previously lived only in
/// the DTO FluentValidators (REST endpoint layer). Internal callers that build a
/// CreateVendorInvoiceRequest/CreatePaymentVoucherRequest in-code and call the service directly
/// (CreateFromPurchaseOrderAsync, CreateVendorInvoiceFromPvAsync, CreateFromVendorInvoiceAsync)
/// bypass that bound entirely. These tests call the SERVICE layer directly (same shape as an
/// internal caller / a crafted API-key call) to prove the bound now lives at the one seam every
/// path funnels through: VendorInvoiceService.BuildLinesAsync and PaymentVoucherService's line
/// loop in CreateDraftAsync.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseRateBoundTests
{
    private readonly PostgresFixture _fx;
    public PurchaseRateBoundTests(PostgresFixture fx) => _fx = fx;

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
            CompanyId = companyId, VendorCode = TestIds.VendorCode("RB"),
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายทดสอบ F-5",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode("RB"),
            NameTh = "หมวดทดสอบ F-5", DefaultExpenseAccountId = expenseId,
            DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(vendor);
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (vendor.VendorId, cat.CategoryId);
    }

    [SkippableFact]
    public async Task VendorInvoice_CreateDraft_RejectsOutOfRangeVatRate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();

        // Crafted service-layer call (the shape of an internal caller like
        // CreateFromPurchaseOrderAsync) — the DTO FluentValidator never runs here, so this is
        // the only line of defence against a 700%-type raw rate.
        var act = () => svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 7.0m)]), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("vi.vat_rate_out_of_range");
    }

    [SkippableFact]
    public async Task VendorInvoice_CreateDraft_AcceptsNormalVatRate()
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
        vi.VatAmount.Should().Be(70m, "a rate already in [0,1] must behave identically to before");
    }

    [SkippableFact]
    public async Task PaymentVoucher_CreateDraft_RejectsOutOfRangeWhtRate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();

        // Same in-code-bypass shape as VI's — mirrors what CreateFromVendorInvoiceAsync builds
        // from an unbound CreatePvFromViRequest.WhtRate before calling CreateDraftAsync directly.
        var act = () => svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            DocDate: TodayBkk, VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null, BankAccountId: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Description: null, Notes: null,
            Lines: [new PaymentVoucherLineInput(
                null, "line", 1000m, null, 0m, false, null, WhtRate: 3.0m)]), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("pv.wht_rate_out_of_range");
    }

    [SkippableFact]
    public async Task PaymentVoucher_CreateDraft_AcceptsNormalWhtRate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // B1(a) (specs/fix-army-findings-2026-07-22.md) — CreateDraftAsync now rejects a
        // WhtRate>0 line with no resolvable Income-Type; this test is about the RATE bound
        // (already in [0,1] behaves unchanged), an orthogonal concern, so give it a real,
        // onboarding-seeded WHT type (SeedVendorAndCategoryAsync's category has no
        // DefaultWhtTypeId) rather than relying on the now-removed implicit-null pass.
        var whtTypeId = await db.WhtTypes.Where(w => w.CompanyId == co.CompanyId && w.Code == "SVC")
            .Select(w => (int?)w.WhtTypeId).FirstAsync();

        var id = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            DocDate: TodayBkk, VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null, BankAccountId: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Description: null, Notes: null,
            Lines: [new PaymentVoucherLineInput(
                null, "line", 1000m, null, 0m, false, whtTypeId, WhtRate: 0.03m)]), default);

        var pv = await db.PaymentVouchers.Include(p => p.Lines).AsNoTracking()
            .FirstAsync(p => p.PaymentVoucherId == id);
        pv.Lines.Should().ContainSingle(l => l.WhtAmount == 30m,
            "a rate already in [0,1] must behave identically to before");
    }
}
