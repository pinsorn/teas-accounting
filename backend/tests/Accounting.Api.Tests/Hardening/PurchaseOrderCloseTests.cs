using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
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
/// WP3.4 (F29, D3 — specs/fix-purchase-ux-findings-2026-07-14.md) — PO "ปิด" (close) wiring:
/// Approved→Closed via the endpoint's service (PurchaseOrderService.CloseAsync mirrors
/// approve/cancel), a Closed PO rejects a NEW Vendor Invoice link at CREATE time
/// (VendorInvoiceService.CreateDraftAsync), and Closed→Approved reopen is blocked once a
/// Posted VI is linked. Each test seeds its own fresh company (TestCompanyFactory) so PO
/// totals/CoA codes are self-contained and don't depend on the shared company-1 fixture data.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseOrderCloseTests
{
    private readonly PostgresFixture _fx;
    public PurchaseOrderCloseTests(PostgresFixture fx) => _fx = fx;

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
            CompanyId = companyId, VendorCode = TestIds.VendorCode("PC"),
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายทดสอบปิด PO",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode("PC"),
            NameTh = "หมวดทดสอบปิด PO", DefaultExpenseAccountId = expenseId,
            DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(vendor);
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (vendor.VendorId, cat.CategoryId);
    }

    // Line: qty 10 @ 100, VAT 7% -> net 1000, vat 70, total 1070. Reused so a VI with
    // Amount=1000/VatRate=0.07 (total 1070) matches the PO total exactly for the
    // >=95% auto-close scenario.
    private static CreatePurchaseOrderRequest PoReq(long vendorId) =>
        new(TodayBkk, null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "สินค้า", 10m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m, null)]);

    private static CreateVendorInvoiceRequest ViReq(long vendorId, int categoryId, long? purchaseOrderId) =>
        new(DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(categoryId, null, "line", 1000m, 0.07m)],
            PurchaseOrderId: purchaseOrderId);

    [SkippableFact]
    public async Task Close_ApprovedPo_TransitionsToClosed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, _) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();

        var poId = await svc.CreateDraftAsync(PoReq(vendorId), default);
        await svc.ApproveAsync(poId, default);

        await svc.CloseAsync(poId, default);

        var d = await svc.GetDetailAsync(poId, default);
        d!.Status.Should().Be("Closed");
        d.ClosedAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Close_DraftPo_Rejected_NotApproved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, _) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();

        var poId = await svc.CreateDraftAsync(PoReq(vendorId), default);   // still Draft

        var act = async () => await svc.CloseAsync(poId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.not_approved");
    }

    [SkippableFact]
    public async Task CreateVendorInvoice_LinkedToClosedPo_Rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();

        var poId = await poSvc.CreateDraftAsync(PoReq(vendorId), default);
        await poSvc.ApproveAsync(poId, default);
        await poSvc.CloseAsync(poId, default);   // now Closed — no further linking

        var act = async () => await viSvc.CreateDraftAsync(ViReq(vendorId, catId, poId), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.not_approved");
    }

    [SkippableFact]
    public async Task Reopen_ClosedPoWithNoPostedVi_ReturnsToApproved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, _) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();

        var poId = await svc.CreateDraftAsync(PoReq(vendorId), default);
        await svc.ApproveAsync(poId, default);
        await svc.CloseAsync(poId, default);

        await svc.ReopenAsync(poId, default);

        var d = await svc.GetDetailAsync(poId, default);
        d!.Status.Should().Be("Approved");
        d.ClosedAt.Should().BeNull();
    }

    [SkippableFact]
    public async Task Reopen_ClosedPoWithPostedVi_Blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId) = await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();

        var poId = await poSvc.CreateDraftAsync(PoReq(vendorId), default);
        await poSvc.ApproveAsync(poId, default);   // PO total 1070 (10 @ 100, 7% VAT)

        var viId = await viSvc.CreateDraftAsync(ViReq(vendorId, catId, poId), default);
        await viSvc.PostAsync(viId, default);      // VI total 1070 == PO total -> auto-closes (>=95%)

        var afterPost = await poSvc.GetDetailAsync(poId, default);
        afterPost!.Status.Should().Be("Closed", "posting a VI covering >=95% of the PO auto-closes it");

        var act = async () => await poSvc.ReopenAsync(poId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.reopen_blocked");
    }
}
