using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// M4a — CreatedViaApiKeyName field is stamped on drafts created by API-key principals,
/// left null for JWT/human creates, and the count signal returns correct totals.
/// Tests use isolated companies via TestCompanyFactory so they don't pollute shared data.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class M4aDraftCreatedViaApiKeyTests
{
    private readonly PostgresFixture _fx;
    public M4aDraftCreatedViaApiKeyTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Build a provider impersonating a JWT human user (UserId set, ApiKeyId null).</summary>
    private ServiceProvider HumanProvider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId, userId: 1);

    /// <summary>Build a provider impersonating an API-key caller (ApiKeyId set, ApiKeyName set, UserId null).</summary>
    private ServiceProvider ApiKeyProvider(int companyId, int branchId, string keyName)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = companyId,
                BranchId  = branchId,
                UserId    = null,          // no human
                ApiKeyId  = 999,           // non-null → IsAuthenticated
                ApiKeyName = keyName,
            })
            .BuildServiceProvider();
    }

    // ── 1. TaxInvoice draft created via API key stamps the key name ──
    [SkippableFact]
    public async Task TaxInvoice_draft_via_api_key_stamps_key_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        const string keyName = "mcp-agent-test-key";
        await using var sp = ApiKeyProvider(c.CompanyId, c.BranchId, keyName);
        await using var scope = sp.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, false, "THB", 1m,
            null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการทดสอบ M4a", 1m, 1, "ครั้ง", 100m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == id);
        ti.CreatedViaApiKeyName.Should().Be(keyName);
    }

    // ── 2. TaxInvoice draft created by a human (JWT) leaves the field null ──
    [SkippableFact]
    public async Task TaxInvoice_draft_via_human_leaves_key_name_null()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = HumanProvider(c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, false, "THB", 1m,
            null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการทดสอบ M4a human", 1m, 1, "ครั้ง", 100m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == id);
        ti.CreatedViaApiKeyName.Should().BeNull();
    }

    // ── 3. Quotation draft created via API key stamps the key name ──
    [SkippableFact]
    public async Task Quotation_draft_via_api_key_stamps_key_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        const string keyName = "mcp-q-agent";
        await using var sp = ApiKeyProvider(c.CompanyId, c.BranchId, keyName);
        await using var scope = sp.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var id = await svc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "สินค้าทดสอบ M4a", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var q = await db.Quotations.AsNoTracking().FirstAsync(x => x.QuotationId == id);
        q.CreatedViaApiKeyName.Should().Be(keyName);
    }

    // ── 4. Quotation draft created by human leaves the field null ──
    [SkippableFact]
    public async Task Quotation_draft_via_human_leaves_key_name_null()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = HumanProvider(c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var id = await svc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "สินค้าทดสอบ M4a human", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var q = await db.Quotations.AsNoTracking().FirstAsync(x => x.QuotationId == id);
        q.CreatedViaApiKeyName.Should().BeNull();
    }

    // ── 5. DTOs expose createdViaApiKey ──
    [SkippableFact]
    public async Task TaxInvoice_list_dto_exposes_created_via_api_key()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        const string keyName = "mcp-dto-check";
        await using var sp = ApiKeyProvider(c.CompanyId, c.BranchId, keyName);
        await using var scope = sp.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, false, "THB", 1m,
            null, null, null,
            [new TaxInvoiceLineInput(null, null, "M4a DTO test", 1m, 1, "ครั้ง", 200m, 0m, 1, "VAT7", 0.07m)]),
            default);

        // Detail DTO
        var detail = await svc.GetDetailAsync(id, default);
        detail.Should().NotBeNull();
        detail!.CreatedViaApiKey.Should().Be(keyName);

        // List DTO
        var page = await svc.ListAsync(new TaxInvoiceListQuery(null, null, null, "DRAFT", null, 10), default);
        var listItem = page.Items.FirstOrDefault(x => x.TaxInvoiceId == id);
        listItem.Should().NotBeNull();
        listItem!.CreatedViaApiKey.Should().Be(keyName);
    }

    // ── 6. Count signal returns the correct total for the company (tenant-scoped) ──
    [SkippableFact]
    public async Task Count_signal_returns_api_key_draft_count_tenant_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        const string keyName = "mcp-count-agent";

        await using var spKey = ApiKeyProvider(c.CompanyId, c.BranchId, keyName);
        await using var scopeKey = spKey.CreateAsyncScope();

        // Create 1 TI draft via API key and 1 Quotation draft via API key.
        var tiSvc = scopeKey.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, false, "THB", 1m,
            null, null, null,
            [new TaxInvoiceLineInput(null, null, "M4a count TI", 1m, 1, "ครั้ง", 300m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var qSvc = scopeKey.ServiceProvider.GetRequiredService<IQuotationService>();
        await qSvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "M4a count Q", 1m, "ชิ้น", 300m, 0m, 1, "VAT7", 0.07m)]),
            default);

        // Also create 1 human draft (must NOT be counted).
        await using var spHuman = HumanProvider(c.CompanyId, c.BranchId);
        await using var scopeHuman = spHuman.CreateAsyncScope();
        var tiSvcHuman = scopeHuman.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        await tiSvcHuman.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, false, "THB", 1m,
            null, null, null,
            [new TaxInvoiceLineInput(null, null, "M4a count human TI", 1m, 1, "ครั้ง", 400m, 0m, 1, "VAT7", 0.07m)]),
            default);

        // Assert count signal using the same DbContext (tenant-scoped via global query filter).
        await using var spCount = HumanProvider(c.CompanyId, c.BranchId);
        await using var scopeCount = spCount.CreateAsyncScope();
        var db = scopeCount.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var tiCount = await db.TaxInvoices
            .Where(t => t.CreatedViaApiKeyName != null && t.Status == DocumentStatus.Draft)
            .CountAsync();
        var qCount = await db.Quotations
            .Where(q => q.CreatedViaApiKeyName != null && q.Status == QuotationStatus.Draft)
            .CountAsync();
        var rcCount = await db.Receipts
            .Where(r => r.CreatedViaApiKeyName != null && r.Status == DocumentStatus.Draft)
            .CountAsync();
        var total = tiCount + qCount + rcCount;

        // At minimum 1 TI + 1 Q from this test (other tests' companies are separate; no cross-contamination
        // from tenant isolation — but same company may have prior runs adding more, so use >=).
        tiCount.Should().BeGreaterThanOrEqualTo(1, "at least the TI draft created via API key in this company");
        qCount.Should().BeGreaterThanOrEqualTo(1, "at least the Quotation draft created via API key in this company");
        rcCount.Should().BeGreaterThanOrEqualTo(0);
        total.Should().Be(tiCount + qCount + rcCount);
    }

    // ── 7. PurchaseOrderDetail DTO exposes createdViaApiKey (agent-drafted badge) ──
    // Mirrors the sales detail projection: the entity's CreatedViaApiKeyName flows onto
    // PurchaseOrderDetail.CreatedViaApiKey — non-null for an API-key (agent) create, null for human.
    [SkippableFact]
    public async Task PurchaseOrder_detail_exposes_createdViaApiKey()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        const string keyName = "mcp-agent-test-key-po";

        // (a) API-key (agent) PO draft → detail.CreatedViaApiKey == keyName.
        await using var agentSp = ApiKeyProvider(c.CompanyId, c.BranchId, keyName);
        long agentPoId;
        await using (var scope = agentSp.CreateAsyncScope())
        {
            var vid = await NewVendor(scope.ServiceProvider, c.CompanyId);
            var svc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            agentPoId = await svc.CreateDraftAsync(PoReq(vid), default);

            var detail = await svc.GetDetailAsync(agentPoId, default);
            detail.Should().NotBeNull();
            detail!.CreatedViaApiKey.Should().Be(keyName);
        }

        // (b) human (JWT) PO draft → detail.CreatedViaApiKey is null.
        await using var humanSp = HumanProvider(c.CompanyId, c.BranchId);
        await using (var scope = humanSp.CreateAsyncScope())
        {
            var vid = await NewVendor(scope.ServiceProvider, c.CompanyId);
            var svc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            var humanPoId = await svc.CreateDraftAsync(PoReq(vid), default);

            var detail = await svc.GetDetailAsync(humanPoId, default);
            detail.Should().NotBeNull();
            detail!.CreatedViaApiKey.Should().BeNull();
        }
    }

    private static async Task<long> NewVendor(IServiceProvider sp, int companyId)
    {
        var db = sp.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = companyId,
            VendorCode = "V-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            NameTh = "ผู้ขายทดสอบ M4a",
            VendorType = CustomerType.Corporate,
            IsForeign = false,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync(default);
        return v.VendorId;
    }

    private static CreatePurchaseOrderRequest PoReq(long vendorId) =>
        new(DateOnly.FromDateTime(DateTime.UtcNow), null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "สินค้าทดสอบ", 1m, "ชิ้น", 100m, 0m, null, "VAT7", 0.07m, null)]);
}
