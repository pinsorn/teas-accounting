using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Purchase;

/// <summary>
/// Guards the WHT-certificate form routing through the real PV-post path (the only path that reaches
/// PaymentVoucherService line ~341). The cert form is the payee-kind default (Individual → ภ.ง.ด.3,
/// Corporate → ภ.ง.ด.53) UNLESS the chosen WHT income type is a ม.70 foreign type (FormType=Pnd54),
/// which routes to ภ.ง.ด.54 regardless of payee kind — because ม.70 status is captured by the income
/// type, not the vendor flag (a foreign co. with a Thai PE still files on ภ.ง.ด.53). Also pins that the
/// user-chosen rate (15% vs a 10% DTA rate) flows through to the certificate. A hand-inserted cert can't
/// guard this — only posting a PV exercises the routing.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WhtFormRoutingTests
{
    private readonly PostgresFixture _fx;
    public WhtFormRoutingTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task<long> NewVendor(ServiceProvider sp, CustomerType type, bool isForeign)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้รับเงินทดสอบ",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = type, IsForeign = isForeign, VatRegistered = true,
            CountryCode = isForeign ? "US" : null,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private static async Task<(int catId, long expAcct)> NewExpenseCategory(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบ routing", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    private static async Task<int> NewWhtType(ServiceProvider sp, WhtFormType form, decimal rate, string incomeCode)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var w = new WhtType
        {
            CompanyId = 1, Code = TestIds.WhtTypeCode(), NameTh = "ประเภทเงินได้ทดสอบ",
            IncomeTypeCode = incomeCode, FormType = form, Rate = rate,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
    }

    private static CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, int whtTypeId, decimal whtRate, string payerMode) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "จ่ายค่าบริการทดสอบ routing", Notes: null,
            Lines: [new(expAcct, "ค่าบริการ", 1000m, null, 0m, true, whtTypeId, whtRate)],
            WhtPayerMode: payerMode);

    private async Task<WhtCertificate> PostAndGetCert(
        long vendorId, WhtFormType whtTypeForm, decimal rate, string incomeCode, string payerMode)
    {
        await using var creator = Provider(userId: 1);
        var (catId, expAcct) = await NewExpenseCategory(creator);
        var whtTypeId = await NewWhtType(creator, whtTypeForm, rate, incomeCode);

        long pvId;
        await using (var s = creator.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vendorId, catId, expAcct, whtTypeId, rate, payerMode), default);

        await using var approver = Provider(userId: 2);   // SoD: a different user approves + posts
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            await svc.PostAsync(pvId, default);            // auto-issues the WHT certificate
        }

        await using var read = Provider();
        await using var rs = read.CreateAsyncScope();
        var db = rs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtCertificates.AsNoTracking().FirstAsync(c => c.PaymentVoucherId == pvId);
    }

    [SkippableFact]
    public async Task Individual_payee_routes_to_pnd3()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp, CustomerType.Individual, isForeign: false);
        var cert = await PostAndGetCert(vid, WhtFormType.Pnd3, 0.05m, "5", "DEDUCT");
        cert.FormType.Should().Be(WhtFormType.Pnd3);
    }

    [SkippableFact]
    public async Task Domestic_corporate_payee_routes_to_pnd53()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp, CustomerType.Corporate, isForeign: false);
        var cert = await PostAndGetCert(vid, WhtFormType.Pnd53, 0.03m, "2", "DEDUCT");
        cert.FormType.Should().Be(WhtFormType.Pnd53);
    }

    // The fix: a ม.70 (Pnd54) WHT type routes to ภ.ง.ด.54 even though the payee is Corporate — and the
    // user-chosen rate (15% flat vs a 10% DTA rate) flows through to the cert.
    [SkippableTheory]
    [InlineData(0.15)]
    [InlineData(0.10)]
    public async Task Foreign_ma70_payee_routes_to_pnd54_with_chosen_rate(double rate)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp, CustomerType.Corporate, isForeign: true);
        var cert = await PostAndGetCert(vid, WhtFormType.Pnd54, (decimal)rate, "8", "DEDUCT");
        cert.FormType.Should().Be(WhtFormType.Pnd54);
        cert.WhtRate.Should().Be((decimal)rate);   // DEDUCT → effective rate == chosen rate
    }
}
