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
/// cont.76 — advisory document completeness (computed on-read, POSTED docs only, NON-BLOCKING)
/// + the สินค้า/บริการ ProductType line snapshot round-trip. Real Postgres (teas_test); unique
/// seeds via TestIds.* so the suite re-runs on the shared DB (CLAUDE.md §8). SoD approve uses
/// a second provider (userId:2), mirroring PurchasePdfTests / PurchaseAuditTests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseCompletenessTests
{
    private readonly PostgresFixture _fx;
    public PurchaseCompletenessTests(PostgresFixture fx) => _fx = fx;

    private static readonly string StorageRoot =
        Path.Combine(Path.GetTempPath(), "teas-complete-" + Guid.NewGuid().ToString("N")[..8]);

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                ["FileStorage:StorageRoot"] = StorageRoot,
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // ── seed helpers ────────────────────────────────────────────────────────────────
    private async Task<long> NewVendor(ServiceProvider sp, bool vatRegistered)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบความสมบูรณ์",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = vatRegistered,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private async Task<(int catId, long expAcct)> NewExpenseCategory(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบความสมบูรณ์", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    private async Task<int> NewWhtType(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var w = new WhtType
        {
            CompanyId = 1, Code = TestIds.WhtTypeCode(), NameTh = "ค่าบริการทดสอบ",
            IncomeTypeCode = "2", FormType = WhtFormType.Pnd53, Rate = 0.03m,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
    }

    private CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, int? whtTypeId, decimal whtRate,
        long? viId = null, string? productType = null) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "จ่ายค่าบริการ", Notes: null,
            Lines: [new PaymentVoucherLineInput(
                expAcct, "ค่าบริการ", 1000m, null, 0m, true, whtTypeId, whtRate, productType)],
            VendorInvoiceId: viId);

    // Post a VI (requires the vendor-TI attachment at post). Returns the posted VI id.
    private async Task<long> PostVi(
        ServiceProvider sp, long vendorId, int catId, decimal amount, decimal vatRate,
        string? productType = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId,
            VendorTaxInvoiceNo: "VT-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok(), VatClaimPeriod: null,   // ③ current open period
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "vi line", amount, vatRate, productType)]),
            default);
        db.SeedViAttachment(id, category: AttachmentCategory.TaxInvoice);
        await db.SaveChangesAsync();
        await svc.PostAsync(id, default);
        return id;
    }

    private static async Task ApproveAndPost(ServiceProvider approverSp, long pvId)
    {
        await using var s = approverSp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        await svc.ApproveAsync(pvId, default);
        await svc.PostAsync(pvId, default);
    }

    private async Task SeedPvReceipt(ServiceProvider sp, long pvId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var suffix = TestIds.Suffix()[..8];
        db.Attachments.Add(new Accounting.Domain.Entities.Sys.Attachment
        {
            CompanyId = 1, ParentType = AttachmentParentType.PaymentVoucher, ParentId = pvId,
            Category = AttachmentCategory.Receipt, FileName = $"receipt-{suffix}.pdf",
            MimeType = "application/pdf", SizeBytes = 1024,
            StoragePath = $"test/1/pv/{pvId}/{suffix}.pdf",
            UploadedAt = DateTimeOffset.UtcNow, UploadedBy = 1,
        });
        await db.SaveChangesAsync();
    }

    private async Task<PaymentVoucherDetail> PvDetail(ServiceProvider sp, long pvId)
    {
        await using var s = sp.CreateAsyncScope();
        return (await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
            .GetDetailAsync(pvId, default))!;
    }

    private async Task<VendorInvoiceDetail> ViDetail(ServiceProvider sp, long viId)
    {
        await using var s = sp.CreateAsyncScope();
        return (await s.ServiceProvider.GetRequiredService<IVendorInvoiceService>()
            .GetDetailAsync(viId, default))!;
    }

    // ── 1. Posted PV, VAT vendor, no linked VI → MISSING_VI ─────────────────────────
    [SkippableFact]
    public async Task Posted_pv_vat_vendor_no_vi_flags_missing_vi()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m), default);
        await ApproveAndPost(sp2, pvId);

        var d = await PvDetail(sp, pvId);
        d.Completeness.Missing.Should().Contain("MISSING_VI");
        d.Completeness.IsComplete.Should().BeFalse();
    }

    // ── 2. Posted PV, non-VAT vendor, no VI → MISSING_VI NOT present ─────────────────
    [SkippableFact]
    public async Task Posted_pv_non_vat_vendor_does_not_flag_missing_vi()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp, vatRegistered: false);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m), default);
        await ApproveAndPost(sp2, pvId);

        var d = await PvDetail(sp, pvId);
        d.Completeness.Missing.Should().NotContain("MISSING_VI");
    }

    // ── 3. Posted PV, linked posted VI + Receipt, no WHT → complete ─────────────────
    [SkippableFact]
    public async Task Posted_pv_with_vi_and_receipt_no_wht_is_complete()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        var viId = await PostVi(sp, vid, catId, 1000m, 0m);   // total 1000
        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m, viId: viId), default);
        await ApproveAndPost(sp2, pvId);
        await SeedPvReceipt(sp, pvId);

        var d = await PvDetail(sp, pvId);
        d.Completeness.Missing.Should().BeEmpty();
        d.Completeness.IsComplete.Should().BeTrue();
    }

    // ── 4. Posted PV with WHT but cert deleted → MISSING_WHT_CERT ───────────────────
    [SkippableFact]
    public async Task Posted_pv_with_wht_but_no_cert_flags_missing_cert()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp, vatRegistered: false);   // non-VAT → no MISSING_VI noise
        var (catId, expAcct) = await NewExpenseCategory(sp);
        var whtTypeId = await NewWhtType(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, whtTypeId, 0.03m), default);
        await ApproveAndPost(sp2, pvId);   // auto-issues the 50ทวิ

        // Simulate the (near-vacuous) broken invariant: remove the auto-issued cert.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var certs = await db.WhtCertificates.Where(w => w.PaymentVoucherId == pvId).ToListAsync();
            db.WhtCertificates.RemoveRange(certs);
            await db.SaveChangesAsync();
        }

        var d = await PvDetail(sp, pvId);
        d.Completeness.Missing.Should().Contain("MISSING_WHT_CERT");
    }

    // ── 5. Posted VI without/with a TaxInvoice attachment → flag flips ──────────────
    [SkippableFact]
    public async Task Posted_vi_tax_invoice_file_flag_flips()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, _) = await NewExpenseCategory(sp);

        // Post the VI but satisfy the post-gate with a NON-TaxInvoice attachment, so the
        // category-specific completeness flag still fires.
        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            viId = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: new DateOnly(2026, 5, 16), VendorId: vid,
                VendorTaxInvoiceNo: "VT-" + TestIds.Suffix()[..6],
                VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok(), VatClaimPeriod: null,   // ③ current open period
                CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(catId, null, "vi line", 1000m, 0m)]), default);
            db.SeedViAttachment(viId, category: AttachmentCategory.Other);
            await db.SaveChangesAsync();
            await svc.PostAsync(viId, default);
        }

        (await ViDetail(sp, viId)).Completeness.Missing.Should().Contain("MISSING_TAX_INVOICE_FILE");

        // Add the TaxInvoice-category file → flag clears.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.SeedViAttachment(viId, category: AttachmentCategory.TaxInvoice);
            await db.SaveChangesAsync();
        }

        var cleared = await ViDetail(sp, viId);
        cleared.Completeness.Missing.Should().NotContain("MISSING_TAX_INVOICE_FILE");
        cleared.Completeness.IsComplete.Should().BeTrue();
    }

    // ── 6. DRAFT PV (VAT vendor, no VI) → not evaluated → complete ──────────────────
    [SkippableFact]
    public async Task Draft_pv_is_not_evaluated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m), default);

        var d = await PvDetail(sp, pvId);   // still Draft
        d.Completeness.IsComplete.Should().BeTrue();
        d.Completeness.Missing.Should().BeEmpty();
    }

    // ── 7. ProductType round-trips on draft PV + VI lines ───────────────────────────
    [SkippableFact]
    public async Task ProductType_round_trips_on_pv_and_vi_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m, productType: "SERVICE"), default);

        (await PvDetail(sp, pvId)).Lines[0].ProductType.Should().Be("SERVICE");

        long viId;
        await using (var s = sp.CreateAsyncScope())
            viId = await s.ServiceProvider.GetRequiredService<IVendorInvoiceService>()
                .CreateDraftAsync(new CreateVendorInvoiceRequest(
                    DocDate: new DateOnly(2026, 5, 16), VendorId: vid,
                    VendorTaxInvoiceNo: "VT-" + TestIds.Suffix()[..6],
                    VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok(), VatClaimPeriod: null,   // ③ current open period
                    CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                    Lines: [new VendorInvoiceLineInput(catId, null, "svc", 500m, 0m, "SERVICE")]),
                    default);

        (await ViDetail(sp, viId)).Lines[0].ProductType.Should().Be("SERVICE");
    }

    // ── bonus: an explicitly-invalid ProductType is rejected (default-GOOD otherwise) ─
    [SkippableFact]
    public async Task Invalid_product_type_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: false);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var act = () => svc.CreateDraftAsync(
            PvReq(vid, catId, expAcct, null, 0m, productType: "BOGUS"), default);
        (await act.Should().ThrowAsync<Accounting.Domain.Common.DomainException>())
            .Which.Code.Should().Be("pv.product_type_invalid");
    }

    // ── 8. PV→VI guided create pre-fills the VI from the PV + links it back ──────────
    [SkippableFact]
    public async Task Pv_to_vi_create_prefills_lines_and_links_back()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m, productType: "SERVICE"), default);

        long viId;
        await using (var s = sp.CreateAsyncScope())
            viId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateVendorInvoiceFromPvAsync(pvId, new CreateViFromPvRequest(
                    VendorTaxInvoiceNo: "VT-" + TestIds.Suffix()[..6],
                    VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok()), default);   // ③ current open period

        // PV now links the new VI; the VI is pre-filled from the PV (vendor, line + ProductType).
        (await PvDetail(sp, pvId)).VendorInvoiceId.Should().Be(viId);
        var vi = await ViDetail(sp, viId);
        vi.VendorId.Should().Be(vid);
        vi.Lines.Should().ContainSingle();
        vi.Lines[0].ProductType.Should().Be("SERVICE");
        vi.Lines[0].Amount.Should().Be(1000m);
    }

    // ── 9. PV→VI create is guarded: a second call conflicts (pv.vi_exists) ───────────
    [SkippableFact]
    public async Task Pv_to_vi_create_twice_throws_vi_exists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp, vatRegistered: true);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, null, 0m), default);

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateVendorInvoiceFromPvAsync(pvId, new CreateViFromPvRequest(
                    "VT-" + TestIds.Suffix()[..6], new Accounting.Application.Abstractions.SystemClock().TodayInBangkok()), default);   // ③ current open period

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var act = () => svc.CreateVendorInvoiceFromPvAsync(pvId, new CreateViFromPvRequest(
            "VT-" + TestIds.Suffix()[..6], new Accounting.Application.Abstractions.SystemClock().TodayInBangkok()), default);   // ③ current open period
        (await act.Should().ThrowAsync<Accounting.Domain.Common.DomainException>())
            .Which.Code.Should().Be("pv.vi_exists");
    }
}
