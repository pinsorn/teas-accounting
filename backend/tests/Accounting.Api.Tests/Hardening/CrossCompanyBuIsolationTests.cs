using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
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
/// M13 — cross-company Business Unit isolation (§4.7). The EF global tenant
/// filter contains a super-admin bypass (<c>IsSuperAdmin || CompanyId == …</c>),
/// so every BU validation that relied on the filter alone accepted a BU
/// belonging to ANOTHER company when the actor was a super admin. That let a
/// company-1 admin mint an API key whose DefaultBusinessUnitId pointed at a
/// company-2 BU — the key's own (non-super) principal then correctly failed
/// <c>bu.invalid</c> on every request. BU references must be validated against
/// the tenant's company EXPLICITLY, independent of the super-admin bypass.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CrossCompanyBuIsolationTests
{
    private readonly PostgresFixture _fx;
    public CrossCompanyBuIsolationTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, bool isSuperAdmin = false)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        return services
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = companyId,
                BranchId = companyId,   // seeded: branch 1 → company 1, branch 2 → company 2
                UserId = 1,
                IsSuperAdmin = isSuperAdmin,
            })
            .BuildServiceProvider();
    }

    private static string NewCode(string prefix) =>
        (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    /// <summary>Creates an ACTIVE BU under company 2 (normal, non-super tenant).</summary>
    private async Task<int> ForeignBu(string code)
    {
        await using var sp = Provider(companyId: 2);
        await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .CreateAsync(new CreateBusinessUnitRequest(code, "หน่วย " + code, code, null), default);
    }

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static CreateTaxInvoiceRequest TiReq(long customerId, int? buId) =>
        new(DocDate: new DateOnly(2026, 5, 16), CustomerId: customerId,
            IsTaxInclusive: false, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null, PaymentTerms: null, DueDate: null,
            Lines: [new TaxInvoiceLineInput(null, null, "สินค้า", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            BusinessUnitId: buId);

    [SkippableFact]
    public async Task Super_admin_cannot_attach_foreign_company_bu_to_tax_invoice()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var foreignBu = await ForeignBu(NewCode("XCO"));

        await using var sp = Provider(companyId: 1, isSuperAdmin: true);
        var cust = await CustomerId(sp);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();

        var act = () => svc.CreateDraftAsync(TiReq(cust, foreignBu), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("bu.invalid");
    }

    [SkippableFact]
    public async Task Api_key_mint_rejects_foreign_company_default_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var foreignBu = await ForeignBu(NewCode("XKEY"));

        await using var sp = Provider(companyId: 1, isSuperAdmin: true);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();

        var act = () => svc.CreateAsync(new CreateApiKeyRequest(
            "cross-bu", ["sales.tax_invoice.create"], DefaultBusinessUnitId: foreignBu), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("api_key.invalid_business_unit");
    }

    [SkippableFact]
    public async Task Bu_list_is_scoped_to_tenant_company_even_for_super_admin()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var foreignBu = await ForeignBu(NewCode("XLST"));

        await using var sp = Provider(companyId: 1, isSuperAdmin: true);
        await using var s = sp.CreateAsyncScope();
        var list = await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .ListAsync(includeInactive: true, default);

        list.Should().NotContain(b => b.BusinessUnitId == foreignBu,
            "the BU dropdown (key minting, doc forms) must never offer another company's BU");
    }

    [SkippableFact]
    public async Task Bu_get_and_update_do_not_reach_foreign_company_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var foreignBu = await ForeignBu(NewCode("XGET"));

        await using var sp = Provider(companyId: 1, isSuperAdmin: true);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBusinessUnitService>();

        (await svc.GetAsync(foreignBu, default)).Should().BeNull();

        var act = () => svc.UpdateAsync(foreignBu,
            new UpdateBusinessUnitRequest("ชื่อใหม่", null, null, true), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("bu.not_found");
    }

    [SkippableFact]
    public async Task Bu_code_duplicate_check_is_per_company_for_super_admin()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var code = NewCode("XDUP");
        await ForeignBu(code);   // company 2 owns the code

        // Same code under company 1 must still be creatable (unique per company,
        // ix on (company_id, code)) — even when the actor is a super admin whose
        // filter-bypass would otherwise see the company-2 row.
        await using var sp = Provider(companyId: 1, isSuperAdmin: true);
        await using var s = sp.CreateAsyncScope();
        var id = await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .CreateAsync(new CreateBusinessUnitRequest(code, "หน่วย " + code, code, null), default);
        id.Should().BeGreaterThan(0);
    }
}
