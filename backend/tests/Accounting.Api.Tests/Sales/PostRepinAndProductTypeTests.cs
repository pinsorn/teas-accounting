using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// Code-review 2026-06-19 backend fixes:
///  • B6 (BE-1a) — at POST, DocDate/TaxPointDate (and PV DocDate/PostingDate + 50ทวิ CertDate)
///    are RE-PINNED to server today in Asia/Bangkok, and the sequential number is bucketed on the
///    post-date period — so a draft created in a past month and posted today gets THIS month's
///    number + tax point (ม.78 / ม.86/4(7) / §4.3).
///  • B2 (BE-1b) — for a product-linked line the stored ProductType is ALWAYS the Product master's,
///    overriding caller input (goods/service-split integrity for WHT base + ภ.พ.30).
///
/// Uses isolated companies via TestCompanyFactory so it never touches shared company-1 data.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostRepinAndProductTypeTests
{
    private readonly PostgresFixture _fx;
    public PostRepinAndProductTypeTests(PostgresFixture fx) => _fx = fx;

    private static int ThisMonth => new SystemClock().TodayInBangkok().Month;
    private static int ThisYear  => new SystemClock().TodayInBangkok().Year;

    // ── BE-1a: Tax Invoice re-pins DocDate + buckets the number on the post-date period ──
    // ม.78 / ม.86/4(7) / §4.3
    [SkippableFact]
    public async Task TaxInvoice_post_repins_docdate_and_number_to_today()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var db  = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Draft (CreateDraft pins DocDate to today; ID=1 TaxCode VAT7 seeded for the new company).
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new SystemClock().TodayInBangkok(), c.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ B6", 1m, 1, "ครั้ง", 1000m, 0m, 1, "VAT7", 0.07m)]),
            default);

        // Simulate a STALE draft: backdate DocDate/TaxPointDate to last month (still Draft = mutable).
        var stale = new SystemClock().TodayInBangkok().AddMonths(-1);
        var draft = await db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == id);
        draft.DocDate = stale; draft.TaxPointDate = stale;
        await db.SaveChangesAsync();

        await svc.PostAsync(id, default);

        var posted = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == id);
        var today  = new SystemClock().TodayInBangkok();
        posted.DocDate.Should().Be(today, "POST re-pins DocDate to today (ม.78)");
        posted.TaxPointDate.Should().Be(today, "tax point = issue date = today (ม.86/4(7))");
        posted.DocNo.Should().NotBeNullOrEmpty();
        // DocNo format MM-YYYY-... — the month/year segment must be THIS month, not the stale one.
        posted.DocNo!.Should().StartWith($"{today.Month:D2}-{today.Year:D4}-",
            "the sequential number must be bucketed on the post-date period (§4.3)");
    }

    // ── BE-1a: Payment Voucher re-pins DocDate/PostingDate + 50ทวิ CertDate to today ──
    [SkippableFact]
    public async Task PaymentVoucher_post_repins_docdate_number_and_cert_date_to_today()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        // Seed vendor + expense category (+default expense account) + a WHT type so a 50ทวิ is issued.
        long vendorId; int catId; long expAcct; int whtTypeId;
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            expAcct = await db.ChartOfAccounts
                .Where(a => a.CompanyId == c.CompanyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var v = new Vendor
            {
                CompanyId = c.CompanyId, VendorCode = TestIds.VendorCode(),
                VendorType = CustomerType.Corporate, NameTh = "ผู้ขาย B6",
                TaxId = TestIds.TaxId(), BranchCode = "00000", VatRegistered = true,
            };
            var cat = new ExpenseCategory
            {
                CompanyId = c.CompanyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "หมวด B6", DefaultExpenseAccountId = expAcct, DefaultIsRecoverableVat = true,
            };
            var wht = new Accounting.Domain.Entities.Tax.WhtType
            {
                CompanyId = c.CompanyId, Code = TestIds.WhtTypeCode(), NameTh = "ค่าบริการ B6",
                IncomeTypeCode = "2", FormType = WhtFormType.Pnd53, Rate = 0.03m,
            };
            db.Vendors.Add(v); db.ExpenseCategories.Add(cat); db.WhtTypes.Add(wht);
            await db.SaveChangesAsync();
            vendorId = v.VendorId; catId = cat.CategoryId; whtTypeId = wht.WhtTypeId;
        }

        long pvId;
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                new SystemClock().TodayInBangkok(), vendorId, catId,
                PaymentMethod.Transfer, null, null, null, "THB", 1m, "B6", null,
                [new PaymentVoucherLineInput(expAcct, "pv line", 1000m, null, 0m, true, whtTypeId, 0.03m)]),
                default);

            // Backdate the draft to last month (Draft → mutable) to simulate draft-now/post-later.
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var stale = new SystemClock().TodayInBangkok().AddMonths(-1);
            var pvDraft = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
            pvDraft.DocDate = stale; pvDraft.PostingDate = stale;
            await db.SaveChangesAsync();
        }

        // Approve + Post (cont.77 — creator may approve; no SoD CHECK).
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            await svc.PostAsync(pvId, default);
        }

        await using var sc = sp.CreateAsyncScope();
        var rdb  = sc.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var today = new SystemClock().TodayInBangkok();
        var pv = await rdb.PaymentVouchers.AsNoTracking().FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.DocDate.Should().Be(today, "POST re-pins PV DocDate to today (§4.3)");
        pv.PostingDate.Should().Be(today);
        pv.DocNo!.Should().StartWith($"{today.Month:D2}-{today.Year:D4}-",
            "the PV number must be bucketed on the post-date period");

        var cert = await rdb.WhtCertificates.AsNoTracking().FirstAsync(w => w.PaymentVoucherId == pvId);
        cert.CertDate.Should().Be(today, "the 50ทวิ certificate date follows the post date");
        cert.DocNo.Should().StartWith($"{today.Month:D2}-{today.Year:D4}-",
            "the WT number must be bucketed on the post-date period");
    }

    // ── BE-1b: caller cannot override the master ProductType on a product-linked line ──
    // WHT/goods-service split — caller cannot override master ProductType.
    [SkippableFact]
    public async Task TaxInvoice_product_line_type_comes_from_master_not_caller()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        // Seed a SERVICE product on the new company.
        var prodSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
        var productId = await prodSvc.CreateAsync(new CreateProductRequest(
            TestIds.ProductCode(), "บริการ B2", null, "SERVICE", "ครั้ง",
            DefaultUnitPrice: 1000m, null, null, null, null, null, IsSaleable: true), default);

        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        // Caller lies: passes ProductType="GOOD" on a line that references a SERVICE product.
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new SystemClock().TodayInBangkok(), c.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(productId, null, "บริการ B2", 1m, 1, "ครั้ง", 1000m, 0m, 1, "VAT7", 0.07m, "GOOD")]),
            default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking().FirstAsync(l => l.TaxInvoiceId == id);
        line.ProductType.Should().Be("SERVICE",
            "the master ProductType (SERVICE) is authoritative; caller-supplied 'GOOD' must NOT win");
    }
}
