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
/// Sprint 13j-PURCH Phase C — PO + PV PDF builders now render via the shared
/// PaperDocument mirror (Pdf.PaperDocumentPdf.Render). These tests prove the
/// builders produce a valid (>1 KB) PDF across Draft/Approved/Posted ×
/// single-line/multi-line × (PV) with-WHT/without-WHT, and that the original/copy
/// watermark switch does not throw. Real Postgres (teas_test); unique seeds via
/// TestIds.* so the suite re-runs on the shared DB (CLAUDE.md §8). The SoD approve
/// uses a second provider (userId:2), mirroring PurchaseAuditTests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchasePdfTests
{
    private readonly PostgresFixture _fx;
    public PurchasePdfTests(PostgresFixture fx) => _fx = fx;

    // Program.cs sets the QuestPDF Community license at API startup; these tests
    // exercise the builder directly (no host), so set it once here too.
    static PurchasePdfTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    // %PDF magic + a comfortable floor; QuestPDF output is well over 1 KB.
    private static void AssertPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(1024);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

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

    // ── seed helpers (mirror PurchaseAuditTests) ───────────────────────────────────
    private async Task<long> NewVendor(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบ PDF",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
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
            NameTh = "หมวดทดสอบ PDF", DefaultExpenseAccountId = expAcct,
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
            CompanyId = 1, Code = TestIds.WhtTypeCode(), NameTh = "ค่าบริการทดสอบ PDF",
            IncomeTypeCode = "2", FormType = WhtFormType.Pnd53, Rate = 0.03m,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
    }

    private static CreatePurchaseOrderRequest PoReq(long vendorId, bool multiLine)
    {
        var lines = new List<PurchaseOrderLineInput>
        {
            new(null, "สินค้า A", 10m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m, null),
        };
        if (multiLine)
        {
            lines.Add(new(null, "สินค้า B", 3m, "กล่อง", 250m, 5m, 2, "VAT7", 0.07m, "หมายเหตุบรรทัด"));
            lines.Add(new(null, "บริการ C", 1m, "งาน", 1500m, 0m, 3, "VAT7", 0.07m, null));
        }
        return new(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), vendorId, null,
            "THB", 1m, "หมายเหตุใบสั่งซื้อ", null, lines);
    }

    private static CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, bool multiLine, int? whtTypeId, decimal whtRate)
    {
        // (ExpenseAccountId, Description, Amount, TaxCodeId(int?), VatRate, IsRecoverableVat, WhtTypeId, WhtRate)
        var lines = new List<PaymentVoucherLineInput>
        {
            new(expAcct, "ค่าบริการ 1", 1000m, null, 0.07m, true, whtTypeId, whtRate),
        };
        if (multiLine)
        {
            lines.Add(new(expAcct, "ค่าบริการ 2", 500m, null, 0.07m, true, whtTypeId, whtRate));
            lines.Add(new(expAcct, "ค่าใช้จ่ายอื่น", 250m, null, 0m, false, null, 0m));
        }
        return new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "จ่ายค่าบริการทดสอบ", Notes: "บันทึกภายใน", Lines: lines);
    }

    // ════════════════════════ Purchase Order ═══════════════════════════════════════

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Po_pdf_renders_through_draft_approved(bool multiLine)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);

        long poId;
        await using (var s = creator.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            poId = await svc.CreateDraftAsync(PoReq(vid, multiLine), default);
            // Draft render (original + copy watermark).
            AssertPdf(await svc.BuildPdfAsync(poId, default, copy: false));
            AssertPdf(await svc.BuildPdfAsync(poId, default, copy: true));
        }

        // Approved render (SoD: a different user approves).
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            await svc.ApproveAsync(poId, default);
            AssertPdf(await svc.BuildPdfAsync(poId, default, copy: false));
        }
    }

    // ════════════════════════ Payment Voucher ══════════════════════════════════════

    [SkippableTheory]
    [InlineData(false, false)] // single-line, no WHT
    [InlineData(true, false)]  // multi-line, no WHT
    [InlineData(false, true)]  // single-line, with WHT
    [InlineData(true, true)]   // multi-line, with WHT
    public async Task Pv_pdf_renders_through_draft_approved_posted(bool multiLine, bool withWht)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        var (catId, expAcct) = await NewExpenseCategory(creator);
        int? whtTypeId = withWht ? await NewWhtType(creator) : null;
        var whtRate = withWht ? 0.03m : 0m;

        long pvId;
        await using (var s = creator.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct, multiLine, whtTypeId, whtRate), default);
            AssertPdf(await svc.BuildPdfAsync(pvId, default, copy: false));
            AssertPdf(await svc.BuildPdfAsync(pvId, default, copy: true));
        }

        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            AssertPdf(await svc.BuildPdfAsync(pvId, default, copy: false)); // Approved
            await svc.PostAsync(pvId, default);
            AssertPdf(await svc.BuildPdfAsync(pvId, default, copy: false)); // Posted
        }
    }
}
