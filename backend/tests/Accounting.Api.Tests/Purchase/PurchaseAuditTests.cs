using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Entities.Audit;
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
/// Sprint 13j-PURCH Phase A — every Purchase state transition appends exactly one
/// audit.activity_log row (module="purchase") in the same transaction as the
/// mutation it records. Mirrors the shipped Sales hooks (TaxInvoiceService.cs:223).
/// Real Postgres (teas_test); unique seed values via TestIds.* so the suite is
/// re-runnable on the shared DB (CLAUDE.md §8).
///
/// Coverage (≈12 transitions):
///  PurchaseOrder : Created · Approved · MarkedSent · Closed · Cancelled
///  VendorInvoice : Created · ClaimedPeriod · Posted
///  PaymentVoucher: Created · Approved · Posted (+ WhtCertificate "Generated" hook)
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseAuditTests
{
    private readonly PostgresFixture _fx;
    public PurchaseAuditTests(PostgresFixture fx) => _fx = fx;

    // AddInfrastructure registers IActivityRecorder + all three Purchase services
    // (and IPurchaseOrderService) exactly as production does — the most faithful
    // wiring for an audit-hook test (Sprint12 pattern).
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

    // ── audit-log assertion helper: exactly +1 matching row ────────────────────────
    private static async Task AssertOneLog(
        ServiceProvider sp, string entityType, long entityId, string action,
        string? fromStatus = null, string? toStatus = null)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rows = await db.Set<ActivityLog>().AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId
                     && a.ActivityType == action)
            .ToListAsync();
        rows.Should().HaveCount(1,
            $"exactly one '{action}' audit row is expected for {entityType} {entityId}");
        var row = rows[0];
        row.Module.Should().Be("purchase");
        // MetadataJson is System.Text.Json (default = space after colon, e.g.
        // {"fromStatus": "Draft", "toStatus": "Posted", "note": null}). Parse it
        // rather than substring-match so we are insensitive to formatting.
        using var meta = System.Text.Json.JsonDocument.Parse(row.MetadataJson!);
        if (fromStatus is not null)
            meta.RootElement.GetProperty("fromStatus").GetString().Should().Be(fromStatus);
        if (toStatus is not null)
            meta.RootElement.GetProperty("toStatus").GetString().Should().Be(toStatus);
    }

    // ── seed helpers ───────────────────────────────────────────────────────────────
    private async Task<long> NewVendor(ServiceProvider sp, CustomerType type = CustomerType.Corporate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายตรวจสอบ",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = type, IsForeign = false, VatRegistered = true,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private async Task<long> ExpenseAccount(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
    }

    private async Task<(int catId, long expAcct)> NewExpenseCategory(ServiceProvider sp)
    {
        var expAcct = await ExpenseAccount(sp);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบ", DefaultExpenseAccountId = expAcct,
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

    private static CreatePurchaseOrderRequest PoReq(long vendorId) =>
        new(new DateOnly(2026, 5, 1), null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "สินค้า", 10m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m, null)]);

    private static CreateVendorInvoiceRequest ViReq(long vendorId, int catId) =>
        new(
            DocDate: new DateOnly(2026, 5, 16),
            VendorId: vendorId,
            VendorTaxInvoiceNo: $"VTI-{TestIds.Suffix()[..6]}",
            // Batch-A ③ — claim period must be the CURRENT (open) Bangkok month; a past month is now closed.
            VendorTaxInvoiceDate: new Accounting.Application.Abstractions.SystemClock().TodayInBangkok(),
            VatClaimPeriod: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)]);

    private static CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, int? whtTypeId = null, decimal whtRate = 0m) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "x", Notes: null,
            Lines: [new PaymentVoucherLineInput(expAcct, "l", 1000m, null, 0m, false, whtTypeId, whtRate)]);

    // ════════════════════════ PurchaseOrder ════════════════════════════════════════

    [SkippableFact]
    public async Task Po_create_records_created_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(PoReq(vid), default);
        await AssertOneLog(sp, "PurchaseOrder", poId, "Created", toStatus: "Draft");
    }

    [SkippableFact]
    public async Task Po_approve_records_approved_draft_to_approved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        long poId;
        await using (var s = creator.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(PoReq(vid), default);

        await using var approver = Provider(userId: 2);   // SoD: different user approves
        await using (var s = approver.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .ApproveAsync(poId, default);
        await AssertOneLog(approver, "PurchaseOrder", poId, "Approved",
            fromStatus: "Draft", toStatus: "Approved");
    }

    [SkippableFact]
    public async Task Po_marksent_records_markedsent_approved_to_sent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        long poId;
        await using (var s = creator.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(PoReq(vid), default);
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            await svc.ApproveAsync(poId, default);
            await svc.MarkSentAsync(poId, default);
        }
        await AssertOneLog(approver, "PurchaseOrder", poId, "MarkedSent",
            fromStatus: "Approved", toStatus: "Sent");
    }

    [SkippableFact]
    public async Task Po_close_records_closed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        long poId;
        await using (var s = creator.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(PoReq(vid), default);
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            await svc.ApproveAsync(poId, default);
            await svc.CloseAsync(poId, default);
        }
        await AssertOneLog(approver, "PurchaseOrder", poId, "Closed", toStatus: "Closed");
    }

    [SkippableFact]
    public async Task Po_cancel_records_cancelled_with_reason_note()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        long poId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            poId = await svc.CreateDraftAsync(PoReq(vid), default);
            await svc.CancelAsync(poId, "ยกเลิกโครงการ", default);
        }
        await AssertOneLog(sp, "PurchaseOrder", poId, "Cancelled", toStatus: "Cancelled");

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.Set<ActivityLog>().AsNoTracking()
            .FirstAsync(a => a.EntityType == "PurchaseOrder" && a.EntityId == poId
                          && a.ActivityType == "Cancelled");
        row.MetadataJson.Should().Contain("ยกเลิกโครงการ");
    }

    // ════════════════════════ VendorInvoice ════════════════════════════════════════

    [SkippableFact]
    public async Task Vi_create_records_created_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, _) = await NewExpenseCategory(sp);
        long viId;
        await using (var s = sp.CreateAsyncScope())
            viId = await s.ServiceProvider.GetRequiredService<IVendorInvoiceService>()
                .CreateDraftAsync(ViReq(vid, catId), default);
        await AssertOneLog(sp, "VendorInvoice", viId, "Created", toStatus: "Draft");
    }

    [SkippableFact]
    public async Task Vi_setclaimperiod_records_claimedperiod_draft_to_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, _) = await NewExpenseCategory(sp);
        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await svc.CreateDraftAsync(ViReq(vid, catId), default);
            // Claim period = the VI's own (current Bangkok) month — always inside the ม.82/4
            // window [TI month .. +6]. Hardcoding a fixed period (was 202606) broke at the
            // month boundary once "today" passed that month.
            var vtiMonth = new SystemClock().TodayInBangkok();
            await svc.SetClaimPeriodAsync(viId, vtiMonth.Year * 100 + vtiMonth.Month, default);
        }
        await AssertOneLog(sp, "VendorInvoice", viId, "ClaimedPeriod",
            fromStatus: "Draft", toStatus: "Draft");
    }

    [SkippableFact]
    public async Task Vi_post_records_posted_draft_to_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, _) = await NewExpenseCategory(sp);
        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            viId = await svc.CreateDraftAsync(ViReq(vid, catId), default);
            db.SeedViAttachment(viId); await db.SaveChangesAsync();   // VI Post requires the vendor-TI file
            await svc.PostAsync(viId, default);
        }
        await AssertOneLog(sp, "VendorInvoice", viId, "Posted",
            fromStatus: "Draft", toStatus: "Posted");
    }

    // ════════════════════════ PaymentVoucher ═══════════════════════════════════════

    [SkippableFact]
    public async Task Pv_create_records_created_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);
        long pvId;
        await using (var s = sp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct), default);
        await AssertOneLog(sp, "PaymentVoucher", pvId, "Created", toStatus: "Draft");
    }

    [SkippableFact]
    public async Task Pv_approve_records_approved_draft_to_approved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        var (catId, expAcct) = await NewExpenseCategory(creator);
        long pvId;
        await using (var s = creator.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct), default);
        await using var approver = Provider(userId: 2);   // SoD
        await using (var s = approver.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .ApproveAsync(pvId, default);
        await AssertOneLog(approver, "PaymentVoucher", pvId, "Approved",
            fromStatus: "Draft", toStatus: "Approved");
    }

    [SkippableFact]
    public async Task Pv_post_records_posted_approved_to_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        var (catId, expAcct) = await NewExpenseCategory(creator);
        long pvId;
        await using (var s = creator.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct), default);
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            await svc.PostAsync(pvId, default);
        }
        await AssertOneLog(approver, "PaymentVoucher", pvId, "Posted",
            fromStatus: "Approved", toStatus: "Posted");
    }

    [SkippableFact]
    public async Task Pv_post_with_wht_records_certificate_generated_issued()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        var (catId, expAcct) = await NewExpenseCategory(creator);
        var whtTypeId = await NewWhtType(creator);
        long pvId;
        await using (var s = creator.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(PvReq(vid, catId, expAcct, whtTypeId, 0.03m), default);

        PaymentVoucherPostedResult posted;
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            posted = await svc.PostAsync(pvId, default);
        }
        posted.WhtCertificateId.Should().NotBeNull("a 50 ทวิ is auto-generated when WHT > 0");
        await AssertOneLog(approver, "WhtCertificate", posted.WhtCertificateId!.Value,
            "Generated", toStatus: "Issued");
    }
}
