using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// Read-side MCP expansion (specs/mcp-expansion.md — B1 fuzzy search, C1 report tools,
/// C2 document-gap tools + chain + company info, E1 list filters). Reuses the
/// <see cref="McpApiFactory"/> + mcp-kind key minting pattern from McpServerSmokeTests.cs.
/// Cross-tenant / "seeded company" assertions use a fresh <see cref="TestCompanyFactory"/>
/// company so results are deterministic (company 1 accumulates state across the whole suite).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpReadExpansionTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpReadExpansionTests(PostgresFixture fx) => _fx = fx;

    private static readonly string[] FullReadScopes =
    [
        "sales.tax_invoice.read", "sales.receipt.read", "sales.quotation.read",
        "master.customer.read", "master.product.read", "master.vendor.manage",
        "sales.billing_note.read", "sales.delivery_order.manage",
        "report.trial_balance.read", "report.profit_loss.read", "report.general_ledger.read",
        "gl.journal.read", "sys.system_info.read",
    ];

    private async Task<string> MintKeyAsync(int companyId, IReadOnlyList<string>? scopes = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-read"), scopes ?? FullReadScopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    private static async Task<McpClient> ConnectAsync(HttpClient http) =>
        await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http, loggerFactory: null, ownsHttpClient: false));

    // A C# null return (e.g. get_invoice for an unknown id) comes back with NO content
    // block at all (not a literal "null" TextContentBlock) — callers check .Count == 0 for that.
    private static JsonElement ResultRoot(CallToolResult result) =>
        JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();

    private static bool IsNullResult(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault() is not { } block
        || block.Text == "null";

    // ══════════════════════ B1 — fuzzy (trigram) search ══════════════════════
    // Service-level (ICustomerService.ListAsync is where the fallback logic lives; the MCP
    // tool is a thin passthrough already exercised by the tools/list smoke test below).

    [SkippableFact]
    public async Task B1_customer_search_finds_typo_via_trigram_fallback_and_isolates_tenant()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        const string exactName = "เงินสดย่อยทดสอบ";
        const string typoQuery = "เงนสดย่อยทดสอบ"; // missing ิ — not an ILIKE substring of exactName

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coA.CompanyId, coA.BranchId);
        await using var scopeA = spA.CreateAsyncScope();
        var customerSvcA = scopeA.ServiceProvider.GetRequiredService<ICustomerService>();
        await customerSvcA.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, exactName, null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);

        // Exact/substring path unchanged.
        var exact = await customerSvcA.ListAsync(exactName, 1, 50, default);
        exact.Should().ContainSingle(c => c.NameTh == exactName);

        // Typo query — ILIKE finds nothing, trigram fallback must find it.
        var typo = await customerSvcA.ListAsync(typoQuery, 1, 50, default);
        typo.Should().ContainSingle(c => c.NameTh == exactName,
            "B1: a typo'd query must still resolve via the pg_trgm similarity fallback");

        // Cross-tenant: the same typo query from company B's context must return nothing —
        // proves the fallback query still respects the EF tenant filter (RLS-superuser
        // caveat applies to teas_test; this proves the EF layer).
        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        await using var scopeB = spB.CreateAsyncScope();
        var customerSvcB = scopeB.ServiceProvider.GetRequiredService<ICustomerService>();
        var crossTenant = await customerSvcB.ListAsync(typoQuery, 1, 50, default);
        crossTenant.Should().BeEmpty("company B must never see company A's customer via the fallback path");
    }

    // ══════════════════════ C1/C2 — tool inventory + scope gating ══════════════════════

    [SkippableFact]
    public async Task Mcp_lists_read_expansion_tools_with_valid_key()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintKeyAsync(companyId: 1);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        // C1 — report tools
        names.Should().Contain(new[]
        {
            "get_trial_balance", "get_profit_loss", "get_general_ledger", "list_gl_accounts",
            "get_journal", "get_tax_summary",
        });
        // C2 — document gap tools + chain + company info
        names.Should().Contain(new[]
        {
            "list_invoices", "get_invoice", "list_delivery_orders", "get_delivery_order",
            "get_document_chain", "get_company_info",
        });
    }

    [SkippableFact]
    public async Task Mcp_report_tools_are_denied_without_the_report_scope()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // No report.* scope granted.
        var key = await MintKeyAsync(companyId: 1, ["sales.quotation.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().NotContain("get_trial_balance");
        names.Should().NotContain("get_general_ledger");
        names.Should().NotContain("get_journal");

        var act = async () => await client.CallToolAsync(
            "get_trial_balance", new Dictionary<string, object?>());
        await act.Should().ThrowAsync<Exception>();
    }

    [SkippableFact]
    public async Task Mcp_get_trial_balance_and_list_gl_accounts_return_company_scoped_data()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintKeyAsync(companyId: 1);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var tb = await client.CallToolAsync("get_trial_balance", new Dictionary<string, object?>());
        tb.IsError.Should().NotBe(true);
        var tbRoot = ResultRoot(tb);
        tbRoot.GetProperty("companyId").GetInt32().Should().Be(1);
        tbRoot.TryGetProperty("rows", out _).Should().BeTrue();

        var accounts = await client.CallToolAsync("list_gl_accounts", new Dictionary<string, object?>());
        accounts.IsError.Should().NotBe(true);
        var accountsRoot = ResultRoot(accounts);
        accountsRoot.GetArrayLength().Should().BeGreaterThan(0,
            "company 1's onboarding seed populates a full chart of accounts");
    }

    [SkippableFact]
    public async Task Mcp_get_company_info_returns_seeded_company_and_branch_fields()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var key = await MintKeyAsync(co.CompanyId);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("get_company_info", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        root.GetProperty("nameTh").GetString().Should().Be(co.NameTh);
        root.GetProperty("vatRegistered").GetBoolean().Should().BeTrue();
        root.GetProperty("branchCode").GetString().Should().Be("00000");
        root.GetProperty("branchNameTh").GetString().Should().Be("สำนักงานใหญ่");
    }

    [SkippableFact]
    public async Task Mcp_list_invoices_and_delivery_orders_scope_to_caller_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // Fresh company has no billing notes / delivery orders yet — tools must execute
        // cleanly and return an empty list (proves wiring + tenant scoping, not 500).
        var invoices = await client.CallToolAsync("list_invoices", new Dictionary<string, object?>());
        invoices.IsError.Should().NotBe(true);
        ResultRoot(invoices).GetArrayLength().Should().Be(0);

        var dos = await client.CallToolAsync("list_delivery_orders", new Dictionary<string, object?>());
        dos.IsError.Should().NotBe(true);
        ResultRoot(dos).GetArrayLength().Should().Be(0);

        // get_* on a nonexistent id returns null, not an error.
        var getInvoice = await client.CallToolAsync("get_invoice",
            new Dictionary<string, object?> { ["id"] = 999_999_999L });
        getInvoice.IsError.Should().NotBe(true);
        IsNullResult(getInvoice).Should().BeTrue("a nonexistent invoice id must resolve to null, not an error");
    }

    [SkippableFact]
    public async Task Mcp_get_document_chain_resolves_quotation_anchor_and_rejects_unknown_type()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId);

        long quotationId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            var productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้าทดสอบ Chain", null, "SERVICE", "ครั้ง", 100m,
                null, null, null, null, null, IsSaleable: true), default);
            var quotationSvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            var today = new SystemClock().TodayInBangkok();
            quotationId = await quotationSvc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(productId, "line", 1m, "ครั้ง", 100m, 0m, 0, "NONE", 0m, null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("get_document_chain",
            new Dictionary<string, object?> { ["docType"] = "quotation", ["id"] = quotationId });
        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        root.GetProperty("salesChain").GetProperty("quotation").GetProperty("id").GetInt64().Should().Be(quotationId);
        // System.Text.Json omits a null property entirely by default (no "purchaseChain":null
        // key at all) — absent or explicit null both mean "not populated".
        (!root.TryGetProperty("purchaseChain", out var purchaseChainEl)
            || purchaseChainEl.ValueKind == JsonValueKind.Null)
            .Should().BeTrue("purchaseChain must not be populated for a sales-side anchor");

        var bad = await client.CallToolAsync("get_document_chain",
            new Dictionary<string, object?> { ["docType"] = "not-a-real-type", ["id"] = quotationId });
        bad.IsError.Should().BeTrue("an unknown docType must be rejected, not silently return empty");
    }

    // ══════════════════════ E1 — date / customer / product list filters ══════════════════════

    [SkippableFact]
    public async Task E1_list_quotations_date_and_product_filters_narrow_and_combine()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId);

        long productA, productB, qaId, qbId;
        DateOnly today;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productA = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า E1-A", null, "SERVICE", "ครั้ง", 100m,
                null, null, null, null, null, IsSaleable: true), default);
            productB = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า E1-B", null, "SERVICE", "ครั้ง", 100m,
                null, null, null, null, null, IsSaleable: true), default);

            var quotationSvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            today = new SystemClock().TodayInBangkok();
            // Qa: today, product A. Qb: 10 days ago, product B.
            qaId = await quotationSvc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(productA, "line A", 1m, "ครั้ง", 100m, 0m, 0, "NONE", 0m, null)]), default);
            qbId = await quotationSvc.CreateDraftAsync(new CreateQuotationRequest(
                today.AddDays(-10), today.AddDays(20), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(productB, "line B", 1m, "ครั้ง", 100m, 0m, 0, "NONE", 0m, null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // productId filter: only Qa (contains product A).
        var byProduct = await client.CallToolAsync("list_quotations",
            new Dictionary<string, object?> { ["productId"] = productA });
        var byProductIds = ResultRoot(byProduct).EnumerateArray()
            .Select(e => e.GetProperty("quotationId").GetInt64()).ToList();
        byProductIds.Should().BeEquivalentTo([qaId]);

        // dateFrom = today excludes Qb (10 days ago).
        var byDate = await client.CallToolAsync("list_quotations",
            new Dictionary<string, object?> { ["dateFrom"] = today });
        var byDateIds = ResultRoot(byDate).EnumerateArray()
            .Select(e => e.GetProperty("quotationId").GetInt64()).ToList();
        byDateIds.Should().Contain(qaId).And.NotContain(qbId);

        // Combined AND: productId=B (only on Qb) AND dateFrom=today (excludes Qb) → empty,
        // proving the filters AND together rather than OR.
        var combined = await client.CallToolAsync("list_quotations",
            new Dictionary<string, object?> { ["productId"] = productB, ["dateFrom"] = today });
        ResultRoot(combined).GetArrayLength().Should().Be(0,
            "productB only appears on Qb, which dateFrom=today excludes — filters must AND together");

        // customerId filter — both quotations share the seeded customer, so it must include both.
        var byCustomer = await client.CallToolAsync("list_quotations",
            new Dictionary<string, object?> { ["customerId"] = co.CustomerId });
        var byCustomerIds = ResultRoot(byCustomer).EnumerateArray()
            .Select(e => e.GetProperty("quotationId").GetInt64()).ToList();
        byCustomerIds.Should().Contain([qaId, qbId]);
    }
}
