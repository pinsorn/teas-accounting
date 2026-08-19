using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Purchase;

/// <summary>
/// specs/fix-c1-backend-cleanup.md item 1 (U9) — <c>PurchaseOrderService.Fill</c> wrote
/// <c>TaxCodeId = l.TaxCodeId</c> verbatim from the request, the last verbatim-id writer left
/// in the codebase after <c>specs/fix-r2-u2-billing-tax-integrity.md</c> §8 filed it as a
/// prevention-only finding (0 live violating rows). A PO line is always REQUEST-fed at the
/// point of origin (no immutable upstream to launder from, unlike the sales chain's
/// <c>SanitizeInheritedTaxCode</c>), so an invalid foreign id is REJECTED typed (mirrors
/// <c>bu.invalid</c>), never stored. Mid-task coordination update: C2's FE worker already ships
/// <c>taxCodeId: l.taxCodeId ?? null</c> (commit a1e9ff3), so a null pair reaching the server is
/// a real, FE-shaped input — resolved to the company's own standard input VAT code ONLY when
/// the line actually charges VAT (<c>TaxRate &gt; 0</c>, the FE's own proxy for the vendor VAT
/// status: <c>taxRate: vendorVat ? l.taxRate : 0</c>); a rate-0 null pair stays null.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseOrderTaxCodeIntegrityTests
{
    private readonly PostgresFixture _fx;
    public PurchaseOrderTaxCodeIntegrityTests(PostgresFixture fx) => _fx = fx;

    private static async Task<long> NewVendorAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = companyId,
            VendorCode = "V-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            NameTh = "ผู้ขายทดสอบ", VendorType = CustomerType.Corporate, IsForeign = false,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync(default);
        return v.VendorId;
    }

    private static async Task<int> OwnTaxCodeIdAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxCodes.Where(c => c.CompanyId == companyId && c.Code == code)
            .Select(c => c.TaxCodeId).SingleAsync();
    }

    private static CreatePurchaseOrderRequest Req(
        long vendorId, int? taxCodeId, string? taxCode, decimal taxRate) =>
        new(new DateOnly(2026, 5, 16), null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "สินค้า", 1m, "ชิ้น", 100m, 0m, taxCodeId, taxCode, taxRate, null)]);

    private static async Task<PurchaseOrderLine> LineOfAsync(ServiceProvider sp, long poId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.PurchaseOrderId == poId);
    }

    [SkippableFact]
    public async Task Foreign_tax_code_id_is_rejected_typed_not_stored()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var compA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var compB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, compB.CompanyId, compB.BranchId);
        var foreignTaxCodeId = await OwnTaxCodeIdAsync(spB, compB.CompanyId, "VAT7");

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, compA.CompanyId, compA.BranchId);
        var vendorId = await NewVendorAsync(spA, compA.CompanyId);

        await using var s = spA.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        (await ((Func<Task>)(() => svc.CreateDraftAsync(Req(vendorId, foreignTaxCodeId, "VAT7", 0.07m), default)))
            .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.tax_code_invalid");
    }

    [SkippableFact]
    public async Task Own_company_tax_code_id_is_stored_unchanged()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var comp = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, comp.CompanyId, comp.BranchId);
        var ownTaxCodeId = await OwnTaxCodeIdAsync(sp, comp.CompanyId, "VAT7");
        var vendorId = await NewVendorAsync(sp, comp.CompanyId);

        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vendorId, ownTaxCodeId, "VAT7", 0.07m), default);

        var line = await LineOfAsync(sp, poId);
        line.TaxCodeId.Should().Be(ownTaxCodeId);
        line.TaxCode.Should().Be("VAT7");
    }

    [SkippableFact]
    public async Task Null_pair_with_positive_rate_resolves_to_company_standard_input_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var comp = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, comp.CompanyId, comp.BranchId);
        var vendorId = await NewVendorAsync(sp, comp.CompanyId);
        var expectedInputId = await OwnTaxCodeIdAsync(sp, comp.CompanyId, "VAT-IN7");

        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vendorId, null, null, 0.07m), default);

        var line = await LineOfAsync(sp, poId);
        line.TaxCodeId.Should().Be(expectedInputId,
            "an FE-shaped null pair with a real VAT rate must resolve to the company own standard input code, never stay null");
        line.TaxCode.Should().Be("VAT-IN7");
    }

    [SkippableFact]
    public async Task Null_pair_with_matching_code_string_resolves_to_that_codes_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var comp = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, comp.CompanyId, comp.BranchId);
        var vendorId = await NewVendorAsync(sp, comp.CompanyId);
        var expectedId = await OwnTaxCodeIdAsync(sp, comp.CompanyId, "VAT7");

        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                // no id, but a code string the company own master actually has (case-varied
                // to also prove the case-insensitive match, mirroring SalesLineBackstop.ByCode).
                .CreateDraftAsync(Req(vendorId, null, "vat7", 0.07m), default);

        var line = await LineOfAsync(sp, poId);
        line.TaxCodeId.Should().Be(expectedId);
        line.TaxCode.Should().Be("VAT7", "the master row own casing wins, not the caller casing");
    }

    [SkippableFact]
    public async Task Null_pair_with_zero_rate_stays_null()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var comp = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, comp.CompanyId, comp.BranchId);
        var vendorId = await NewVendorAsync(sp, comp.CompanyId);

        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vendorId, null, null, 0m), default);

        var line = await LineOfAsync(sp, poId);
        line.TaxCodeId.Should().BeNull("a rate-0 line charges nothing, so a null pair is honest, not a defect");
        line.TaxCode.Should().BeNull();
    }

    [SkippableFact]
    public async Task Foreign_tax_code_id_is_rejected_on_update_too()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var compA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var compB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, compB.CompanyId, compB.BranchId);
        var foreignTaxCodeId = await OwnTaxCodeIdAsync(spB, compB.CompanyId, "VAT7");

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, compA.CompanyId, compA.BranchId);
        var vendorId = await NewVendorAsync(spA, compA.CompanyId);
        var ownTaxCodeId = await OwnTaxCodeIdAsync(spA, compA.CompanyId, "VAT7");

        long poId;
        await using (var s = spA.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vendorId, ownTaxCodeId, "VAT7", 0.07m), default);

        await using var s2 = spA.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        (await ((Func<Task>)(() => svc.UpdateDraftAsync(poId, Req(vendorId, foreignTaxCodeId, "VAT7", 0.07m), default)))
            .Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.tax_code_invalid");
    }
}
