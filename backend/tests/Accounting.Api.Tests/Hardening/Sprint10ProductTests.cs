using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 10 Part A — Product master CRUD + FK + ProductCode POST snapshot +
/// retroactive enables (wht-base-suggest service/goods split, sales-summary
/// group_by=product).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint10ProductTests
{
    private readonly PostgresFixture _fx;
    public Sprint10ProductTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static async Task<long> NewProduct(
        ServiceProvider sp, string type, decimal? price = null,
        bool isSaleable = true, bool isPurchasable = false, int? businessUnitId = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        return await svc.CreateAsync(new CreateProductRequest(
            "SKU-" + Sfx(), "สินค้า " + Sfx(), null, type,
            "ชิ้น", price, null, null, null, null, null,
            IsSaleable: isSaleable, IsPurchasable: isPurchasable,
            BusinessUnitId: businessUnitId), default);
    }

    private static async Task<int> AnyBusinessUnitId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.BusinessUnits.Select(b => b.BusinessUnitId).FirstAsync();
    }

    private static async Task<long> PostTiWithProduct(
        ServiceProvider sp, long cust, long productId, decimal price)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(productId, null, "line", 1m, 1, "ชิ้น", price, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
        return id;
    }

    [SkippableFact]
    public async Task Product_crud_roundtrip_and_case_insensitive_duplicate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();

        var code = "PX-" + Sfx();
        var id = await svc.CreateAsync(new CreateProductRequest(
            code, "บริการที่ปรึกษา", "Consulting", "SERVICE",
            "ชม.", 1500m, null, null, null, null, null), default);

        var got = await svc.GetAsync(id, default);
        got!.ProductType.Should().Be("SERVICE");
        got.DefaultUnitPrice.Should().Be(1500m);

        // Case-insensitive duplicate is refused.
        var dup = () => svc.CreateAsync(new CreateProductRequest(
            code.ToLowerInvariant(), "dup", null, "GOOD",
            null, null, null, null, null, null, null), default);
        (await dup.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("product.duplicate");

        await svc.UpdateAsync(id, new UpdateProductRequest(
            "บริการที่ปรึกษา (แก้)", null, "SERVICE", "ชม.", 1800m,
            null, null, null, null, null, true), default);
        (await svc.GetAsync(id, default))!.DefaultUnitPrice.Should().Be(1800m);

        await svc.DeactivateAsync(id, default);
        var list = await svc.ListAsync(
            includeInactive: false, search: null, purpose: null, businessUnitId: null,
            productType: null, isActive: null, default);
        list.Should().NotContain(p => p.ProductId == id);
    }

    // cont.81 — purchase/sale split + BU scoping + price auto-fill.

    [SkippableFact]
    public async Task Product_with_no_purpose_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        var act = () => svc.CreateAsync(new CreateProductRequest(
            "NP-" + Sfx(), "ไม่มีจุดประสงค์", null, "GOOD",
            null, null, null, null, null, null, null,
            IsSaleable: false, IsPurchasable: false), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("product.no_purpose");
    }

    [SkippableFact]
    public async Task Purpose_filter_separates_sale_and_purchase()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var saleId = await NewProduct(sp, "GOOD", 100m, isSaleable: true, isPurchasable: false);
        var buyId  = await NewProduct(sp, "GOOD", 100m, isSaleable: false, isPurchasable: true);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();

        var sale = await svc.ListAsync(false, null, "sale", null, null, null, default);
        sale.Should().Contain(p => p.ProductId == saleId);
        sale.Should().NotContain(p => p.ProductId == buyId);

        var purchase = await svc.ListAsync(false, null, "purchase", null, null, null, default);
        purchase.Should().Contain(p => p.ProductId == buyId);
        purchase.Should().NotContain(p => p.ProductId == saleId);
    }

    [SkippableFact]
    public async Task Bu_filter_shows_selected_bu_and_shared_products()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var bu = await AnyBusinessUnitId(sp);
        var sharedId = await NewProduct(sp, "GOOD", 100m, businessUnitId: null);
        var buId     = await NewProduct(sp, "GOOD", 100m, businessUnitId: bu);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        var list = await svc.ListAsync(false, null, null, bu, null, null, default);
        list.Should().Contain(p => p.ProductId == sharedId, "shared (null-BU) products show in every BU");
        list.Should().Contain(p => p.ProductId == buId);
    }

    [SkippableFact]
    public async Task Type_and_active_filters_narrow_the_list()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var goodId = await NewProduct(sp, "GOOD", 100m);
        var svcId  = await NewProduct(sp, "SERVICE", 100m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();

        // productType filter
        var services = await svc.ListAsync(true, null, null, null, "SERVICE", null, default);
        services.Should().Contain(p => p.ProductId == svcId);
        services.Should().NotContain(p => p.ProductId == goodId);

        // isActive=false → inactive only (after deactivating the goods product)
        await svc.DeactivateAsync(goodId, default);
        var inactive = await svc.ListAsync(true, null, null, null, null, false, default);
        inactive.Should().Contain(p => p.ProductId == goodId);
        inactive.Should().NotContain(p => p.ProductId == svcId);
    }

    [SkippableFact]
    public async Task Bu_of_another_company_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        var act = () => svc.CreateAsync(new CreateProductRequest(
            "BU-" + Sfx(), "สินค้า", null, "GOOD",
            null, null, null, null, null, null, null,
            BusinessUnitId: 2_000_000_000), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("product.bu_invalid");
    }

    [SkippableFact]
    public async Task Wht_on_goods_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        var act = () => svc.CreateAsync(new CreateProductRequest(
            "G-" + Sfx(), "สินค้า", null, "GOOD",
            null, null, null, null, DefaultWhtTypeId: 1, null, null), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("product.wht_on_goods");
    }

    [SkippableFact]
    public async Task Posted_ti_line_snapshots_product_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var pid = await NewProduct(sp, "GOOD", 500m);
        var tiId = await PostTiWithProduct(sp, cust, pid, 500m);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var prodCode = await db.Products.Where(p => p.ProductId == pid)
            .Select(p => p.ProductCode).FirstAsync();
        var line = await db.TaxInvoiceLines
            .FirstAsync(l => l.TaxInvoiceId == tiId && l.ProductId == pid);
        line.ProductCode.Should().Be(prodCode, "POST snapshots the product code");
    }

    [SkippableFact]
    public async Task Wht_base_suggest_splits_service_and_goods()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var svcP = await NewProduct(sp, "SERVICE", 4000m);
        var goodP = await NewProduct(sp, "GOOD", 6000m);
        var ti1 = await PostTiWithProduct(sp, cust, svcP, 4000m);
        var ti2 = await PostTiWithProduct(sp, cust, goodP, 6000m);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // Sprint (multi-category WHT) — suggest is now pro-rata aware; pass each TI's
        // full total as the applied amount → fraction 1 (full payment) so the split
        // matches the line ex-VAT amounts.
        var t1 = await db.TaxInvoices.Where(t => t.TaxInvoiceId == ti1).Select(t => t.TotalAmount).FirstAsync();
        var t2 = await db.TaxInvoices.Where(t => t.TaxInvoiceId == ti2).Select(t => t.TotalAmount).FirstAsync();
        var rc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var sug = await rc.SuggestWhtBaseAsync(
            [new ReceiptApplicationInput(ti1, t1), new ReceiptApplicationInput(ti2, t2)], cust, default);

        sug.ServiceSubtotal.Should().Be(4000m);
        sug.GoodsSubtotal.Should().Be(6000m);
        // Base now defaults to the service portion (was full ex-VAT in 8.6).
        if (sug.SuggestedWhtTypeId is not null)
            sug.SuggestedWhtBase.Should().Be(4000m);
    }

    [SkippableFact]
    public async Task Sales_summary_group_by_product_is_reenabled()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var pid = await NewProduct(sp, "GOOD", 1234m);
        await PostTiWithProduct(sp, cust, pid, 1234m);

        // TI is server-pinned to today's Bangkok date (ม.86/4(7)); query the current Bangkok month.
        var today = new SystemClock().TodayInBangkok();
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        await using var s = sp.CreateAsyncScope();
        var rep = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        // R-Q2 reversed — no longer throws report.product_unsupported.
        var ss = await rep.SalesSummaryAsync(from, to, "product", default);
        ss.GroupBy.Should().Be("product");
        ss.Rows.Should().Contain(r => r.Subtotal >= 1234m);
        ss.Totals.Total.Should().Be(ss.Rows.Sum(r => r.Total));
    }
}
