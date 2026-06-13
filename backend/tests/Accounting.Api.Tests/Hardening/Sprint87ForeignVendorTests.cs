using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 8.7 — foreign vendor / self-withhold. Real Postgres. GL must stay
/// balanced (self-withhold: Dr Expense gross = Cr Bank full + Cr WHT-Payable;
/// receipt-only VI: Dr Expense w/ VAT lumped = Cr AP).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint87ForeignVendorTests
{
    private readonly PostgresFixture _fx;
    public Sprint87ForeignVendorTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<long> CreateVendor(
        ServiceProvider sp, bool foreign, bool vatD, bool vatReg = true)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorService>();
        return await svc.CreateAsync(new CreateVendorRequest(
            "FV-" + Sfx(), CustomerType.Corporate, "Foreign Vendor", null,
            null, null, null, vatReg, null, null, null, null, 30, "THB", null,
            IsForeign: foreign, HasThaiVatDReg: vatD,
            CountryCode: foreign ? "US" : null), default);
    }

    private static async Task<(int catId, long expAcct)> SeedCategory(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts.Where(a => a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var c = new Accounting.Domain.Entities.Sys.ExpenseCategory
        {
            CompanyId = 1, CategoryCode = "S87" + Guid.NewGuid().ToString("N")[..5],
            NameTh = "หมวด S8.7", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(c);
        await db.SaveChangesAsync();
        return (c.CategoryId, expAcct);
    }

    private static async Task<int?> SvcWhtTypeId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
            .Select(w => (int?)w.WhtTypeId).FirstOrDefaultAsync();
    }

    private static async Task<long> CreatePv(
        ServiceProvider sp, long vendorId, int catId, long expAcct,
        decimal amount, decimal vatRate, decimal whtRate, bool? selfWithhold,
        string? payerMode = null)
    {
        var whtTypeId = whtRate > 0m ? await SvcWhtTypeId(sp) : null;
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        return await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            new DateOnly(2026, 5, 16), vendorId, catId, PaymentMethod.Transfer,
            null, null, null, "THB", 1m, "s87", null,
            [new PaymentVoucherLineInput(expAcct, "svc", amount, null, vatRate, true, whtTypeId, whtRate)],
            null, selfWithhold, null, payerMode), default);
    }

    private static async Task ApproveAndPost(ServiceProvider approver, long pvId)
    {
        await using var s = approver.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        await svc.ApproveAsync(pvId, default);
        await svc.PostAsync(pvId, default);
    }

    [SkippableFact]
    public async Task Foreign_no_vatd_pv_auto_self_withhold_and_pnd36()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var v = await CreateVendor(sp, foreign: true, vatD: false);
        var (cat, exp) = await SeedCategory(sp);

        // No self_withhold passed → auto-true (foreign no VAT-D). 3500, no VAT, 15%.
        // 2026-06-12 wht-grossup: auto self-withhold defaults to ออกให้ตลอดไป —
        // income = 3500/0.85 = 4117.65, tax = 617.65 (was flat 525 = under-remitted).
        var pvId = await CreatePv(sp, v, cat, exp, 3500m, 0m, 0.15m, selfWithhold: null);

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
            pv.SelfWithholdMode.Should().BeTrue();
            pv.WhtPayerMode.Should().Be("GROSS_UP_FOREVER");
            pv.RequiresPnd36ReverseCharge.Should().BeTrue();
            pv.TotalPaid.Should().Be(3500m);   // full amount (no WHT deducted)
            pv.WhtAmount.Should().Be(617.65m); // 3500·0.15/0.85
        }

        await ApproveAndPost(sp2, pvId);
        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv2 = await db2.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        var je = await db2.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == pv2.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.Lines.Sum(l => l.DebitAmount).Should().Be(4117.65m); // expense 3500 + wht 617.65
        je.Lines.Should().Contain(l => l.CreditAmount == 3500m);   // bank full
        je.Lines.Should().Contain(l => l.CreditAmount == 617.65m); // WHT payable (grossed)

        // 50ทวิ: income carries the absorbed tax; condition = (2) ออกให้ตลอดไป.
        var cert = await db2.WhtCertificates.FirstAsync(w => w.PaymentVoucherId == pvId);
        cert.IncomeAmount.Should().Be(4117.65m);
        cert.WhtAmount.Should().Be(617.65m);
        cert.WhtCondition.Should().Be(2);
    }

    [SkippableFact]
    public async Task Domestic_manual_self_withhold_gross_up()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var v = await CreateVendor(sp, foreign: false, vatD: false);
        var (cat, exp) = await SeedCategory(sp);

        // Legacy selfWithholdMode:true (no mode) → GROSS_UP_FOREVER:
        // 10,000 @3% → income 10,309.28, tax 309.28 (effective 3.0928%).
        var pvId = await CreatePv(sp, v, cat, exp, 10000m, 0.07m, 0.03m, selfWithhold: true);
        await ApproveAndPost(sp2, pvId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.TotalPaid.Should().Be(10700m);   // subtotal + vat (full)
        pv.WhtPayerMode.Should().Be("GROSS_UP_FOREVER");
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == pv.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.Lines.Should().Contain(l => l.CreditAmount == 10700m);  // bank
        je.Lines.Should().Contain(l => l.CreditAmount == 309.28m); // WHT payable (grossed)
        je.Lines.Where(l => l.DebitAmount > 0).Sum(l => l.DebitAmount)
            .Should().Be(11009.28m);   // expense 10000 + input vat 700 + wht 309.28
    }

    [SkippableFact]
    public async Task Gross_up_once_uses_single_iteration_and_condition_3()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var v = await CreateVendor(sp, foreign: false, vatD: false);
        var (cat, exp) = await SeedCategory(sp);

        // ออกให้ครั้งเดียว: income = 10,000·1.03 = 10,300, tax = 309.
        var pvId = await CreatePv(sp, v, cat, exp, 10000m, 0m, 0.03m,
            selfWithhold: null, payerMode: "GROSS_UP_ONCE");
        await ApproveAndPost(sp2, pvId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.SelfWithholdMode.Should().BeTrue();
        pv.WhtPayerMode.Should().Be("GROSS_UP_ONCE");
        pv.TotalPaid.Should().Be(10000m);
        pv.WhtAmount.Should().Be(309m);
        var cert = await db.WhtCertificates.FirstAsync(w => w.PaymentVoucherId == pvId);
        cert.IncomeAmount.Should().Be(10300m);
        cert.WhtAmount.Should().Be(309m);
        cert.WhtCondition.Should().Be(3);
    }

    [SkippableFact]
    public async Task Normal_deduct_pv_keeps_condition_1_and_flat_math()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var v = await CreateVendor(sp, foreign: false, vatD: false);
        var (cat, exp) = await SeedCategory(sp);

        var pvId = await CreatePv(sp, v, cat, exp, 10000m, 0m, 0.03m, selfWithhold: null);
        await ApproveAndPost(sp2, pvId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.WhtPayerMode.Should().Be("DEDUCT");
        pv.SelfWithholdMode.Should().BeFalse();
        pv.WhtAmount.Should().Be(300m);
        pv.TotalPaid.Should().Be(9700m);   // WHT netted off the payment
        var cert = await db.WhtCertificates.FirstAsync(w => w.PaymentVoucherId == pvId);
        cert.IncomeAmount.Should().Be(10000m);
        cert.WhtCondition.Should().Be(1);
    }

    [SkippableFact]
    public void Payer_mode_contradicting_selfwithhold_is_rejected()
    {
        var v = new CreatePaymentVoucherValidator();
        var req = new CreatePaymentVoucherRequest(
            new DateOnly(2026, 5, 16), 1, 1, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null, null,
            [new PaymentVoucherLineInput(1, "x", 100m, null, 0m, true, null, 0.03m)],
            null, SelfWithholdMode: false, null, WhtPayerMode: "GROSS_UP_FOREVER");
        var r = v.Validate(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("contradicts"));
    }

    [SkippableFact]
    public async Task Self_withhold_with_vendor_invoice_is_rejected_by_validator()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var v = new CreatePaymentVoucherValidator();
        var req = new CreatePaymentVoucherRequest(
            new DateOnly(2026, 5, 16), 1, 1, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null, null,
            [new PaymentVoucherLineInput(1, "x", 100m, null, 0m, true, null, 0m)],
            VendorInvoiceId: 5, SelfWithholdMode: true);
        var r = v.Validate(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("VI-linked"));
    }

    [SkippableFact]
    public async Task Vatd_without_foreign_violates_check()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.Vendors.Add(new Vendor
        {
            CompanyId = 1, VendorCode = "BAD-" + Sfx(),
            VendorType = CustomerType.Corporate, NameTh = "bad",
            VatRegistered = true, IsForeign = false, HasThaiVatDReg = true,
        });
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>(
            "ck_vendors_vatd_foreign: VAT-D requires a foreign vendor");
    }

    [SkippableFact]
    public async Task Non_vat_registered_vendor_rejects_a_vat_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        // Domestic, NOT VAT-registered → ม.82/5: no input VAT may be charged on the purchase.
        var v = await CreateVendor(sp, foreign: false, vatD: false, vatReg: false);
        var (cat, exp) = await SeedCategory(sp);

        var act = async () => await CreatePv(sp, v, cat, exp, 1_000m, 0.07m, 0m, selfWithhold: null);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.vendor_not_vat_registered");
    }

    [SkippableFact]
    public async Task Non_vat_registered_vendor_allows_a_zero_vat_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var v = await CreateVendor(sp, foreign: false, vatD: false, vatReg: false);
        var (cat, exp) = await SeedCategory(sp);

        // vatRate 0 is fine — the non-VAT vendor purchase just carries no input VAT.
        var pvId = await CreatePv(sp, v, cat, exp, 1_000m, 0m, 0m, selfWithhold: null);
        pvId.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Receipt_only_vi_lumps_vat_into_expense()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        // Domestic non-VAT vendor → HasInputVat auto-false → VAT lumps into expense.
        var v = await CreateVendor(sp, foreign: false, vatD: false, vatReg: false);
        var (cat, _) = await SeedCategory(sp);

        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                new DateOnly(2026, 5, 16), v, "VT-" + Sfx(),
                new DateOnly(2026, 5, 10), null, "THB", 1m, null,
                [new VendorInvoiceLineInput(cat, null, "x", 1000m, 0.07m)]), default);
            var dbInner = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var vi = await dbInner.VendorInvoices.FirstAsync(x => x.VendorInvoiceId == viId);
            vi.HasInputVat.Should().BeFalse("non-VAT vendor → no claimable input VAT");
            dbInner.SeedViAttachment(viId); await dbInner.SaveChangesAsync();   // VI Post requires the vendor-TI file
            await svc.PostAsync(viId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var posted = await db.VendorInvoices.FirstAsync(x => x.VendorInvoiceId == viId);
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(1070m); // expense gross = AP gross
        je.Lines.Should().NotContain(l => l.Description!.Contains("Input VAT"));
    }
}
