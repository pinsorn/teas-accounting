using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
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
/// cont.79 — Business Unit wired through the purchase docs (PO/PV/VI). Real Postgres
/// (teas_test); unique seeds via TestIds.* so the suite re-runs on the shared DB (§8).
/// Covers: bu.required (company opt-in), bu.invalid (inactive/foreign), the doc-number
/// BU segment (PV = -PV-{BU}-{CATEGORY}-, VI = -VI-{BU}-), and the GL journal_line BU stamp.
///
/// IMPORTANT: RequiresBusinessUnit lives on shared company 1 — every test that flips it
/// ON restores it OFF in a finally (mirrors Sprint8BusinessUnitTests) so the toggle never
/// leaks into other tests on the shared DB.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseBusinessUnitTests
{
    private readonly PostgresFixture _fx;
    public PurchaseBusinessUnitTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // Short unique code (prefix + 6 hex ≈ 16.7M space, stable on shared teas_test).
    // Mirrors Sprint8BusinessUnitTests.NewCode — needed here because the PV doc-number
    // sub_prefix is the CONCAT "{BU}-{CATEGORY}" and number_sequences.sub_prefix is
    // varchar(20); NewCode("BU")+TestIds.ExpenseCategoryCode() (10+12) would
    // overflow it. BU/category codes are short in practice (ECOM/RENT).
    private static string NewCode(string prefix) =>
        (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    // ── seed helpers ────────────────────────────────────────────────────────────────
    private static async Task<int> CreateBu(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        return await svc.CreateAsync(new CreateBusinessUnitRequest(code, "หน่วย " + code, code, null), default);
    }

    private static async Task DeactivateBu(ServiceProvider sp, int buId)
    {
        await using var s = sp.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<IBusinessUnitService>().DeactivateAsync(buId, default);
    }

    private static async Task SetRequiresBu(ServiceProvider sp, bool value)
    {
        await using var s = sp.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(value, default);
    }

    private async Task<long> NewVendor(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบ BU",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = false,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private async Task<(int catId, long expAcct, string code)> NewExpenseCategory(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var code = NewCode("EX");   // short — fits the concat'd PV sub_prefix (≤20)
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = code,
            NameTh = "หมวดทดสอบ BU", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct, code);
    }

    private CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, int? buId) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "จ่ายค่าบริการ", Notes: null,
            Lines: [new PaymentVoucherLineInput(expAcct, "ค่าบริการ", 1000m, null, 0m, true, null, 0m)],
            BusinessUnitId: buId);

    private CreateVendorInvoiceRequest ViReq(long vendorId, int catId, int? buId) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId,
            VendorTaxInvoiceNo: "VT-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok(), VatClaimPeriod: null,   // ③ current open period
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "vi line", 1000m, 0m)],
            BusinessUnitId: buId);

    private static async Task<string> ApproveAndPostPv(ServiceProvider approverSp, long pvId)
    {
        await using var s = approverSp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        await svc.ApproveAsync(pvId, default);
        var res = await svc.PostAsync(pvId, default);
        return res.DocNo;
    }

    // ── 1. required when the company opted in (PV / VI / PO) → bu.required ───────────
    [SkippableFact]
    public async Task Flag_on_requires_bu_on_purchase_docs()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct, _) = await NewExpenseCategory(sp);
        try
        {
            await SetRequiresBu(sp, true);

            await using (var s = sp.CreateAsyncScope())
            {
                var pv = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
                (await ((Func<Task>)(() => pv.CreateDraftAsync(PvReq(vid, catId, expAcct, null), default)))
                    .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
            }
            await using (var s = sp.CreateAsyncScope())
            {
                var vi = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
                (await ((Func<Task>)(() => vi.CreateDraftAsync(ViReq(vid, catId, null), default)))
                    .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
            }
            await using (var s = sp.CreateAsyncScope())
            {
                var po = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
                var req = new CreatePurchaseOrderRequest(
                    new DateOnly(2026, 5, 16), null, vid, BusinessUnitId: null,
                    "THB", 1m, null, null,
                    [new PurchaseOrderLineInput(null, "po line", 1m, "ชิ้น", 1000m, 0m, null, null, 0m, null)]);
                (await ((Func<Task>)(() => po.CreateDraftAsync(req, default)))
                    .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
            }
        }
        finally
        {
            await SetRequiresBu(sp, false);   // shared company 1 — restore default
        }
    }

    // ── 2. inactive BU rejected → bu.invalid (PV / VI / PO) ──────────────────────────
    [SkippableFact]
    public async Task Inactive_bu_rejected_on_purchase_docs()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct, _) = await NewExpenseCategory(sp);
        var buId = await CreateBu(sp, NewCode("BU"));
        await DeactivateBu(sp, buId);

        await using (var s = sp.CreateAsyncScope())
        {
            var pv = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            (await ((Func<Task>)(() => pv.CreateDraftAsync(PvReq(vid, catId, expAcct, buId), default)))
                .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.invalid");
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var vi = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            (await ((Func<Task>)(() => vi.CreateDraftAsync(ViReq(vid, catId, buId), default)))
                .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.invalid");
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var po = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            var req = new CreatePurchaseOrderRequest(
                new DateOnly(2026, 5, 16), null, vid, BusinessUnitId: buId,
                "THB", 1m, null, null,
                [new PurchaseOrderLineInput(null, "po line", 1m, "ชิ้น", 1000m, 0m, null, null, 0m, null)]);
            (await ((Func<Task>)(() => po.CreateDraftAsync(req, default)))
                .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.invalid");
        }
    }

    // ── 3a. posted PV with BU + category → DocNo carries -PV-{BU}-{CATEGORY}- ─────────
    [SkippableFact]
    public async Task Posted_pv_docno_embeds_bu_and_category()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp);
        var (catId, expAcct, catCode) = await NewExpenseCategory(sp);
        var buCode = NewCode("BU");
        var buId = await CreateBu(sp, buCode);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, buId), default);
        var docNo = await ApproveAndPostPv(sp2, pvId);

        docNo.Should().Contain($"-PV-{buCode}-{catCode}-");
    }

    // ── 3b. posted VI with BU → DocNo carries -VI-{BU}- ──────────────────────────────
    [SkippableFact]
    public async Task Posted_vi_docno_embeds_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, _, _) = await NewExpenseCategory(sp);
        var buCode = NewCode("BU");
        var buId = await CreateBu(sp, buCode);

        string docNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var id = await svc.CreateDraftAsync(ViReq(vid, catId, buId), default);
            db.SeedViAttachment(id, category: AttachmentCategory.TaxInvoice);
            await db.SaveChangesAsync();
            docNo = (await svc.PostAsync(id, default)).DocNo;
        }

        docNo.Should().Contain($"-VI-{buCode}-");
    }

    // ── 4a. posted PV journal_lines carry the PV's BU ────────────────────────────────
    [SkippableFact]
    public async Task Posted_pv_stamps_bu_on_every_journal_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var vid = await NewVendor(sp);
        var (catId, expAcct, _) = await NewExpenseCategory(sp);
        var buId = await CreateBu(sp, NewCode("BU"));

        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, buId), default);
        var docNo = await ApproveAndPostPv(sp2, pvId);

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines).FirstAsync(j => j.Reference == docNo);
        je.Lines.Should().NotBeEmpty();
        je.Lines.Should().OnlyContain(l => l.BusinessUnitId == buId);
    }

    // ── 4b. posted VI journal_lines carry the VI's BU ────────────────────────────────
    [SkippableFact]
    public async Task Posted_vi_stamps_bu_on_every_journal_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, _, _) = await NewExpenseCategory(sp);
        var buId = await CreateBu(sp, NewCode("BU"));

        string docNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var id = await svc.CreateDraftAsync(ViReq(vid, catId, buId), default);
            db.SeedViAttachment(id, category: AttachmentCategory.TaxInvoice);
            await db.SaveChangesAsync();
            docNo = (await svc.PostAsync(id, default)).DocNo;
        }

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db2.JournalEntries.Include(j => j.Lines).FirstAsync(j => j.Reference == docNo);
        je.Lines.Should().NotBeEmpty();
        je.Lines.Should().OnlyContain(l => l.BusinessUnitId == buId);
    }
}
