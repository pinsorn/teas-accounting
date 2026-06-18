using System.Net;
using System.Text;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// M2 (MCP) — smoke tests for the in-process MCP server mounted at <c>/mcp</c>.
/// Boots the real API against the shared teas_test DB (full middleware pipeline,
/// real PermissionPolicyProvider + ApiKey scheme) so the tests exercise genuine
/// HTTP auth, tool registration and a real protocol round-trip.
///
/// The mcp-kind key is minted via a SEPARATE ServiceProvider with a StubTenant
/// (company 1): in a manually-created scope the factory's HttpTenantContext has no
/// HttpContext, so IApiKeyService.CreateAsync couldn't resolve the company there.
/// Both that SP and the factory point at the same teas_test connection string.
/// The MCP client transport is driven through the factory's in-memory HttpClient
/// (TestServer is not a real socket) — the HttpClientTransport ctor accepts a
/// pre-built HttpClient, so no port is opened.
/// </summary>
public sealed class McpApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (host config), NOT ConfigureAppConfiguration — Program reads
        // ConnectionStrings:Postgres eagerly at top level (see RbacApiFactory note).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Database:RunInitializerOnStartup", "false");
        builder.UseSetting("App:BaseUrl", "http://localhost:3000");
    }
}

[Collection(nameof(PostgresCollection))]
public sealed class McpServerSmokeTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpServerSmokeTests(PostgresFixture fx) => _fx = fx;

    // The full read + create scope set an mcp key may hold (M1 guard forbids *.post).
    private static readonly string[] FullMcpScopes =
    [
        "sales.tax_invoice.read", "sales.tax_invoice.create",
        "sales.receipt.read", "sales.receipt.create",
        "sales.quotation.read", "sales.quotation.create",
        "master.customer.read", "master.product.read",
    ];

    // Mint an mcp-kind key with the given scopes on company 1 via a stub-tenant SP.
    private async Task<string> MintMcpKeyAsync(IReadOnlyList<string>? scopes = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-smoke"),
            scopes ?? FullMcpScopes,
            Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    // Seed a customer on company 1 (for the create-draft round-trip) via stub-tenant SP.
    private async Task<long> SeedCustomerAsync()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        return await svc.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า MCP", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);
    }

    private static async Task<McpClient> ConnectAsync(HttpClient http) =>
        await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            loggerFactory: null,
            ownsHttpClient: false));

    // (a) No API key → /mcp must reject (401). Raw POST, no protocol needed.
    [SkippableFact]
    public async Task Mcp_without_api_key_is_unauthorized()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var body = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            Encoding.UTF8, "application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        var resp = await http.PostAsync("/mcp", body);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // (b) Valid mcp-kind key → tools/list succeeds and exposes the read + create tools.
    [SkippableFact]
    public async Task Mcp_lists_read_and_create_tools_with_valid_key()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);
        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();

        // Read tools
        names.Should().Contain(new[]
        {
            "list_tax_invoices", "get_tax_invoice", "list_receipts", "get_receipt",
            "list_quotations", "get_quotation", "list_customers", "get_customer",
            "list_products", "get_product",
        });
        // Create-draft tools
        names.Should().Contain(new[]
        {
            "create_tax_invoice_draft", "create_quotation_draft", "create_receipt_draft",
        });
        // No post/issue/send tool is exposed.
        names.Should().NotContain(n =>
            n.Contains("post") || n.Contains("issue") || n.Contains("send"));
    }

    // (c) Calling a create-draft tool returns a draft id + the ?action=approve URL.
    [SkippableFact]
    public async Task Mcp_create_quotation_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // The tool parameter is the CreateQuotationRequest DTO ("request"); the line
        // is a non-VAT line (TaxRate 0) so no tax-code lookup is needed for a draft.
        var request = new
        {
            docDate = today,
            validUntilDate = today.AddDays(30),
            customerId,
            businessUnitId = (int?)null,
            currencyCode = "THB",
            exchangeRate = 1m,
            notes = (string?)null,
            internalNotes = (string?)null,
            lines = new[]
            {
                new
                {
                    productId = (long?)null, descriptionTh = "บริการ MCP", quantity = 1m,
                    uomText = "ครั้ง", unitPrice = 1000m, discountPercent = 0m,
                    taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null,
                },
            },
        };

        var result = await client.CallToolAsync(
            "create_quotation_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        // UseStructuredContent defaults to false, so the DraftCreated { Id, ApprovalUrl }
        // record comes back JSON-serialized inside a TextContentBlock (camelCase props).
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt64();
        var url = root.GetProperty("approvalUrl").GetString();

        id.Should().BeGreaterThan(0);
        url.Should().Be($"http://localhost:3000/quotations/{id}?action=approve");
    }

    // (d) COMPLIANCE — an under-scoped key (read-only, no quotation.create) must be
    // DENIED the create-draft tool. This proves AddAuthorizationFilters() + the per-tool
    // [Authorize(Policy="apiperm:sales.quotation.create")] actually resolve and enforce
    // against the ApiKey principal — the core M2 write-safety control. The SDK filters an
    // unauthorized tool out of tools/list AND blocks the call; assert both.
    [SkippableFact]
    public async Task Mcp_read_only_key_is_denied_the_create_draft_tool()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Read scopes only — NO *.create.
        var key = await MintMcpKeyAsync(
        [
            "sales.tax_invoice.read", "sales.receipt.read",
            "sales.quotation.read", "master.customer.read", "master.product.read",
        ]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        // The create-draft tools are filtered out of this key's tools/list.
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().Contain("list_quotations");            // read tool still visible
        names.Should().NotContain("create_quotation_draft");  // create tool hidden (no scope)
        names.Should().NotContain("create_tax_invoice_draft");
        names.Should().NotContain("create_receipt_draft");

        // And an explicit call is rejected (not silently executed).
        var act = async () => await client.CallToolAsync(
            "create_quotation_draft",
            new Dictionary<string, object?> { ["request"] = new { } });
        await act.Should().ThrowAsync<Exception>();
    }
}
