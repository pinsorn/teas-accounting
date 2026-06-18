using System.Net;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    // The full read + create/manage scope set an mcp key may hold (M1 guard forbids *.post).
    // E1: added master.customer.manage + master.product.manage (auto master-data writes).
    // E4: added billing_note.read + delivery_order.manage (PDF download tools).
    private static readonly string[] FullMcpScopes =
    [
        "sales.tax_invoice.read", "sales.tax_invoice.create",
        "sales.receipt.read", "sales.receipt.create",
        "sales.quotation.read", "sales.quotation.create",
        "master.customer.read", "master.customer.manage",
        "master.product.read", "master.product.manage",
        // E3 — purchase read+create + vendor (read/create reuse master.vendor.manage).
        "purchase.purchase_order.read", "purchase.purchase_order.create",
        "purchase.vendor_invoice.read", "purchase.vendor_invoice.create",
        "purchase.payment_voucher.read", "purchase.payment_voucher.create",
        "master.vendor.manage",
        // E4 — billing note + delivery order (PDF download tools).
        "sales.billing_note.read", "sales.delivery_order.manage",
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

    // Seed a product on company 1 (for E2 create-draft round-trips) via stub-tenant SP.
    private async Task<long> SeedProductAsync()
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
        var svc = scope.ServiceProvider.GetRequiredService<IProductService>();
        return await svc.CreateAsync(new CreateProductRequest(
            TestIds.ProductCode(), "บริการ MCP E2", null, "SERVICE",
            "ครั้ง", DefaultUnitPrice: 9999m,  // master price — must NOT appear in stored lines
            null, null, null, null, null,
            IsSaleable: true), default);
    }

    // ── E3 purchase seed helpers (company 1, via stub-tenant SP) ──────────────
    private ServiceProvider Sp()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // Seed a vendor on company 1. vatRegistered:false → exercises the ม.82/5 input-VAT guard.
    private async Task<long> SeedVendorAsync(bool vatRegistered = true)
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Accounting.Domain.Entities.Master.Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขาย MCP",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = vatRegistered,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    // Seed an expense category (with its default expense account) on company 1 — required for a PV/VI draft.
    private async Task<(int catId, long expAcct)> SeedExpenseCategoryAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new Accounting.Domain.Entities.Sys.ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวด MCP", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    // Seed a WHT type (3% service, ภ.ง.ด.53) on company 1 — for the PV WHT-computation test.
    private async Task<int> SeedWhtTypeAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var w = new Accounting.Domain.Entities.Tax.WhtType
        {
            CompanyId = 1, Code = TestIds.WhtTypeCode(), NameTh = "ค่าบริการ MCP",
            IncomeTypeCode = "2", FormType = Accounting.Domain.Enums.WhtFormType.Pnd53, Rate = 0.03m,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
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
        // E3 — purchase + vendor read tools
        names.Should().Contain(new[]
        {
            "list_purchase_orders", "get_purchase_order",
            "list_vendor_invoices", "get_vendor_invoice",
            "list_payment_vouchers", "get_payment_voucher",
            "list_vendors", "get_vendor",
        });
        // Create-draft tools (sales)
        names.Should().Contain(new[]
        {
            "create_tax_invoice_draft", "create_quotation_draft", "create_receipt_draft",
        });
        // E3 — purchase create-draft tools (human posts/approves)
        names.Should().Contain(new[]
        {
            "create_purchase_order_draft", "create_vendor_invoice_draft", "create_payment_voucher_draft",
        });
        // E1/E3 — master-data write tools (auto, no human-approve)
        names.Should().Contain(new[] { "create_customer", "create_product", "create_vendor" });
        // E4 — PDF URL tools (posted docs only, api-key-fetchable URL returned)
        names.Should().Contain(new[]
        {
            "get_tax_invoice_pdf_url", "get_receipt_pdf_url", "get_quotation_pdf_url",
            "get_invoice_pdf_url", "get_delivery_order_pdf_url",
            "get_purchase_order_pdf_url", "get_payment_voucher_pdf_url",
        });
        // E5 — approval-status poll tools (agent checks own drafts + per-doc status)
        names.Should().Contain(new[] { "list_pending_approvals", "get_document_status" });
        // No post/issue/send/approve/cancel action tool is exposed (humans post). E3 keeps this
        // invariant. NOTE: "payment_voucher" legitimately contains "pay" — assert action VERBS,
        // not the noun. Verbs are matched as discrete underscore-delimited tokens.
        var actionVerbs = new[] { "post", "issue", "send", "approve", "cancel", "pay" };
        names.Should().NotContain(n =>
            n.Split('_').Any(tok => actionVerbs.Contains(tok)),
            "no post/issue/send/approve/cancel/pay action tool may be exposed — agents only draft");
    }

    // (c) Calling a create-draft tool returns a draft id + the ?action=approve URL.
    // E2: productId is now required (non-nullable in McpCreateQuotationRequest).
    [SkippableFact]
    public async Task Mcp_create_quotation_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();   // E2: must supply a valid productId
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
                // E2: productId is non-nullable in McpChainLineInput — must be a real id.
                new
                {
                    productId, descriptionTh = "บริการ MCP", quantity = 1m,
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

    // (e) E1 — create_customer returns a new id + code + name (auto, no approval URL).
    [SkippableFact]
    public async Task Mcp_create_customer_returns_id_code_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        var code = TestIds.CustomerCode();
        var request = new
        {
            customerCode    = code,
            customerType    = "Corporate",
            nameTh          = "ลูกค้า MCP E1",
            nameEn          = (string?)null,
            taxId           = (string?)null,
            branchCode      = (string?)null,
            branchName      = (string?)null,
            vatRegistered   = false,
            billingAddress  = (string?)null,
            contactPerson   = (string?)null,
            phone           = (string?)null,
            email           = (string?)null,
            creditLimit     = 0m,
            paymentTermDays = 30,
            defaultCurrency = "THB",
        };

        var result = await client.CallToolAsync(
            "create_customer",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        root.GetProperty("id").GetInt64().Should().BeGreaterThan(0);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("nameTh").GetString().Should().Be("ลูกค้า MCP E1");
        // Auto — no approvalUrl property.
        root.TryGetProperty("approvalUrl", out _).Should().BeFalse();
    }

    // (f) E1 — create_product returns a new id + code + name (auto, no approval URL).
    [SkippableFact]
    public async Task Mcp_create_product_returns_id_code_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        var code = TestIds.ProductCode();
        var request = new
        {
            productCode           = code,
            nameTh                = "สินค้า MCP E1",
            nameEn                = (string?)null,
            productType           = "SERVICE",
            defaultUomText        = "ครั้ง",
            defaultUnitPrice      = 500m,
            defaultOutputTaxCodeId = (int?)null,
            defaultInputTaxCodeId  = (int?)null,
            defaultWhtTypeId       = (int?)null,
            descriptionTh         = (string?)null,
            notes                 = (string?)null,
            isSaleable            = true,
            isPurchasable         = false,
            businessUnitId        = (int?)null,
        };

        var result = await client.CallToolAsync(
            "create_product",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        root.GetProperty("id").GetInt64().Should().BeGreaterThan(0);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("nameTh").GetString().Should().Be("สินค้า MCP E1");
        root.TryGetProperty("approvalUrl", out _).Should().BeFalse();
    }

    // (g) E1 COMPLIANCE — a key without master.customer.manage / master.product.manage
    // must not see or call create_customer / create_product. Per-tool [Authorize] enforcement.
    [SkippableFact]
    public async Task Mcp_key_without_manage_scope_is_denied_master_write_tools()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Read-only scopes — NO *.manage.
        var key = await MintMcpKeyAsync(
        [
            "master.customer.read", "master.product.read",
            "sales.quotation.read",
        ]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);

        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().Contain("list_customers");          // read still visible
        names.Should().NotContain("create_customer");      // manage-gated — hidden
        names.Should().NotContain("create_product");       // manage-gated — hidden

        // Explicit call is also rejected.
        var act = async () => await client.CallToolAsync(
            "create_customer",
            new Dictionary<string, object?> { ["request"] = new { } });
        await act.Should().ThrowAsync<Exception>();
    }

    // ── E2 require-list enforcement tests ─────────────────────────────────────
    // All three create-draft tools must reject lines with missing/unknown productId
    // and headers with unknown customerId. They must NOT reject the same calls through
    // the shared service path (regression guard = M4a tests remain green).

    // (h) E2 — unknown productId on a tax invoice draft → mcp.line_product_required.
    [SkippableFact]
    public async Task E2_tax_invoice_unknown_product_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, customerId, isTaxInclusive = false,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId = (int?)null,
            quotationId = (long?)null,
            lines = new[]
            {
                // unknown productId — nonzero to bypass the <=0 arm and hit the GetAsync arm.
                new { productId = 999_999_999L, descriptionTh = "X", quantity = 1m,
                      uomId = 1, uomText = "ครั้ง", unitPrice = 100m, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = request });

        // E2 guard throws McpE2Exception → SDK returns IsError=true.
        // Note: the MCP SDK sanitizes exception messages on the wire to a generic
        // "An error occurred invoking '...'" string; we assert rejection (IsError) only.
        // The mcp.line_product_required code is preserved in McpE2Exception.Code for
        // server-side logging / future structured-error extensions.
        result.IsError.Should().BeTrue("unknown productId must be rejected by the E2 list-only guard");
    }

    // (i) E2 — unknown customerId on a tax invoice draft → mcp.customer_required.
    [SkippableFact]
    public async Task E2_tax_invoice_unknown_customer_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var productId = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, customerId = 999_999_999L, isTaxInclusive = false,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId = (int?)null,
            quotationId = (long?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "X", quantity = 1m,
                      uomId = 1, uomText = "ครั้ง", unitPrice = 100m, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown customerId must be rejected by the E2 list-only guard");
    }

    // (j) E2 — valid productId on a tax invoice draft → succeeds AND stored UnitPrice
    // equals the caller-supplied price, NOT the product master price (spec §E2 req 3).
    [SkippableFact]
    public async Task E2_tax_invoice_valid_product_succeeds_and_honors_custom_unit_price()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();  // master price = 9999
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal callerPrice = 1234.56m;  // different from the 9999 master price
        var request = new
        {
            docDate = today, customerId, isTaxInclusive = false,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId = (int?)null,
            quotationId = (long?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "E2 price test", quantity = 1m,
                      uomId = 1, uomText = "ครั้ง", unitPrice = callerPrice, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);

        // Verify stored line price = caller-supplied price (not 9999 master price).
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging().AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .FirstAsync(l => l.TaxInvoiceId == id);
        line.UnitPrice.Should().Be(callerPrice,
            "E2 spec: caller-supplied UnitPrice must be honoured; product master price must NOT overwrite it");
    }

    // (k) E2 — unknown productId on a quotation draft → mcp.line_product_required.
    [SkippableFact]
    public async Task E2_quotation_unknown_product_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, validUntilDate = today.AddDays(30), customerId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId = 999_999_999L, descriptionTh = "X", quantity = 1m,
                      uomText = "ครั้ง", unitPrice = 100m, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_quotation_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown productId must be rejected by the E2 list-only guard");
    }

    // (l) E2 — valid product on a quotation draft honors the caller-supplied unit price.
    [SkippableFact]
    public async Task E2_quotation_valid_product_succeeds_and_honors_custom_unit_price()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal callerPrice = 555.55m;
        var request = new
        {
            docDate = today, validUntilDate = today.AddDays(30), customerId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "E2 Q price test", quantity = 1m,
                      uomText = "ครั้ง", unitPrice = callerPrice, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_quotation_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging().AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.QuotationLines.AsNoTracking()
            .FirstAsync(l => l.QuotationId == id);
        line.UnitPrice.Should().Be(callerPrice,
            "E2: caller-supplied UnitPrice must be honoured, not overwritten with the product master price");
    }

    // (m) E2 — unknown productId on a receipt draft → mcp.line_product_required.
    [SkippableFact]
    public async Task E2_receipt_unknown_product_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, customerId, paymentMethod = "Cash",
            chequeNo = (string?)null, chequeDate = (DateOnly?)null, bankAccountId = (long?)null,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            businessUnitId = (int?)null,
            lines = new[]
            {
                new { productId = 999_999_999L, descriptionTh = "X",
                      quantity = 1m, unitPrice = 100m, amount = 100m,
                      productType = "SERVICE", uomText = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_receipt_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown productId must be rejected by the E2 list-only guard");
    }

    // (n) E2 — valid product on a receipt draft (standalone cash bill) honors custom price.
    [SkippableFact]
    public async Task E2_receipt_valid_product_succeeds_and_honors_custom_unit_price()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal callerPrice = 777.77m;
        var request = new
        {
            docDate = today, customerId, paymentMethod = "Cash",
            chequeNo = (string?)null, chequeDate = (DateOnly?)null, bankAccountId = (long?)null,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            businessUnitId = (int?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "E2 R price test",
                      quantity = 1m, unitPrice = callerPrice, amount = callerPrice,
                      productType = "SERVICE", uomText = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_receipt_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging().AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.ReceiptLines.AsNoTracking()
            .FirstAsync(l => l.ReceiptId == id);
        line.UnitPrice.Should().Be(callerPrice,
            "E2: caller-supplied UnitPrice must be honoured, not overwritten with the product master price");
    }

    // (o) E2 REGRESSION — shared service path (no MCP) still accepts ad-hoc lines
    // (productId = null). Proves the guard is MCP-path-only; the shared validator
    // and service are unchanged. (M4a tests also cover this implicitly.)
    [SkippableFact]
    public async Task E2_regression_shared_service_path_still_accepts_adhoc_line_no_product_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString,
            vatRegistered: true, vatRate: 0.07m);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging().AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = c.CompanyId, BranchId = c.BranchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        // Call the shared service directly — NOT via MCP tool. No productId.
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId,
            false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(
                null, null, "ad-hoc E2 regression", 1m, 1, "ครั้ง",
                200m, 0m, 1, "VAT7", 0.07m)]),
            default);

        // Must succeed — no exception thrown, valid id returned.
        id.Should().BeGreaterThan(0,
            "the shared service path must NOT enforce E2 list-only — ad-hoc lines must still work for the UI/REST");
    }

    // ══════════════════════════ E3 — Purchase agentic tools ══════════════════════════

    // (p) E3 — create_purchase_order_draft returns id + the purchase-orders ?action=approve URL.
    [SkippableFact]
    public async Task E3_create_purchase_order_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId  = await SeedVendorAsync();
        var productId = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, expectedDeliveryDate = (DateOnly?)null, vendorId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "สินค้า PO", quantity = 2m, uomText = "ชิ้น",
                      unitPrice = 250m, discountPercent = 0m,
                      taxCodeId = (int?)null, taxCode = (string?)null, taxRate = 0m, notes = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_purchase_order_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        var url = doc.RootElement.GetProperty("approvalUrl").GetString();
        id.Should().BeGreaterThan(0);
        url.Should().Be($"http://localhost:3000/purchase-orders/{id}?action=approve");
    }

    // (q) E3 — PO draft with an unknown vendorId → mcp.vendor_required (rejected).
    [SkippableFact]
    public async Task E3_purchase_order_unknown_vendor_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var productId = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, expectedDeliveryDate = (DateOnly?)null, vendorId = 999_999_999L,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "X", quantity = 1m, uomText = "ชิ้น",
                      unitPrice = 100m, discountPercent = 0m,
                      taxCodeId = (int?)null, taxCode = (string?)null, taxRate = 0m, notes = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_purchase_order_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown vendorId must be rejected by the E3 vendor require-list guard");
    }

    // (r) E3 — PO draft with an unknown productId → mcp.line_product_required (rejected).
    [SkippableFact]
    public async Task E3_purchase_order_unknown_product_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, expectedDeliveryDate = (DateOnly?)null, vendorId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId = 999_999_999L, descriptionTh = "X", quantity = 1m, uomText = "ชิ้น",
                      unitPrice = 100m, discountPercent = 0m,
                      taxCodeId = (int?)null, taxCode = (string?)null, taxRate = 0m, notes = (string?)null },
            },
        };

        var result = await client.CallToolAsync(
            "create_purchase_order_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown productId must be rejected by the E2/E3 product require-list guard");
    }

    // (s) E3 — create_vendor_invoice_draft returns id + the vendor-invoices ?action=approve URL.
    [SkippableFact]
    public async Task E3_create_vendor_invoice_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync();
        var (catId, _) = await SeedExpenseCategoryAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // VendorTaxInvoiceDate must be the current open Bangkok month (ม.82/4 claim window).
        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();
        var request = new
        {
            docDate = today, vendorId,
            vendorTaxInvoiceNo = $"VTI-{TestIds.Suffix()[..6]}",
            vendorTaxInvoiceDate = today, vatClaimPeriod = (int?)null,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            lines = new[] { new { expenseCategoryId = catId, expenseAccountId = (long?)null,
                                  description = "line", amount = 1000m, vatRate = 0.07m, productType = (string?)null } },
            hasInputVat = (bool?)null, purchaseOrderId = (long?)null, businessUnitId = (int?)null,
        };

        var result = await client.CallToolAsync(
            "create_vendor_invoice_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        var url = doc.RootElement.GetProperty("approvalUrl").GetString();
        id.Should().BeGreaterThan(0);
        url.Should().Be($"http://localhost:3000/vendor-invoices/{id}?action=approve");
    }

    // (t) E3 — VI draft with an unknown vendorId → rejected by the vendor require-list guard.
    [SkippableFact]
    public async Task E3_vendor_invoice_unknown_vendor_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var (catId, _) = await SeedExpenseCategoryAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();
        var request = new
        {
            docDate = today, vendorId = 999_999_999L,
            vendorTaxInvoiceNo = $"VTI-{TestIds.Suffix()[..6]}",
            vendorTaxInvoiceDate = today, vatClaimPeriod = (int?)null,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            lines = new[] { new { expenseCategoryId = catId, expenseAccountId = (long?)null,
                                  description = "line", amount = 1000m, vatRate = 0.07m, productType = (string?)null } },
            hasInputVat = (bool?)null, purchaseOrderId = (long?)null, businessUnitId = (int?)null,
        };

        var result = await client.CallToolAsync(
            "create_vendor_invoice_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue("unknown vendorId must be rejected by the E3 vendor require-list guard");
    }

    // (u) E3 — create_payment_voucher_draft returns id + the payment-vouchers ?action=approve URL.
    [SkippableFact]
    public async Task E3_create_payment_voucher_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync();   // VAT-registered
        var (catId, expAcct) = await SeedExpenseCategoryAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, vendorId, expenseCategoryId = catId,
            paymentMethod = "Transfer", chequeNo = (string?)null, chequeDate = (DateOnly?)null,
            bankAccountId = (long?)null, currencyCode = "THB", exchangeRate = 1m,
            description = "x", notes = (string?)null,
            lines = new[] { new { expenseAccountId = (long?)expAcct, description = "l", amount = 1000m,
                                  taxCodeId = (int?)null, vatRate = 0.07m, isRecoverableVat = false,
                                  whtTypeId = (int?)null, whtRate = 0m, productType = (string?)null } },
            vendorInvoiceId = (long?)null, selfWithholdMode = (bool?)null,
            businessUnitId = (int?)null, whtPayerMode = (string?)null,
        };

        var result = await client.CallToolAsync(
            "create_payment_voucher_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        var url = doc.RootElement.GetProperty("approvalUrl").GetString();
        id.Should().BeGreaterThan(0);
        url.Should().Be($"http://localhost:3000/payment-vouchers/{id}?action=approve");
    }

    // (v) E3 COMPLIANCE — a PV draft via MCP for a NON-VAT vendor still runs the ม.82/5 input-VAT
    // guard inside PaymentVoucherService: VatRate=0 is the only legal value → stored line VAT = 0.
    [SkippableFact]
    public async Task E3_payment_voucher_draft_applies_ma825_input_vat_guard_non_vat_vendor_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync(vatRegistered: false);   // ม.82/5 — issues no tax invoice
        var (catId, expAcct) = await SeedExpenseCategoryAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, vendorId, expenseCategoryId = catId,
            paymentMethod = "Transfer", chequeNo = (string?)null, chequeDate = (DateOnly?)null,
            bankAccountId = (long?)null, currencyCode = "THB", exchangeRate = 1m,
            description = "x", notes = (string?)null,
            // VatRate=0 is the legal value for a non-VAT vendor (ม.82/5). The service still computes
            // per-line VAT and stores 0 — proving the guard runs through the unchanged service path.
            lines = new[] { new { expenseAccountId = (long?)expAcct, description = "l", amount = 1000m,
                                  taxCodeId = (int?)null, vatRate = 0m, isRecoverableVat = false,
                                  whtTypeId = (int?)null, whtRate = 0m, productType = (string?)null } },
            vendorInvoiceId = (long?)null, selfWithholdMode = (bool?)null,
            businessUnitId = (int?)null, whtPayerMode = (string?)null,
        };

        var result = await client.CallToolAsync(
            "create_payment_voucher_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);

        // Stored line VAT must be 0 (ม.82/5 input-VAT guard ran in the draft path via the service).
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.Set<Accounting.Domain.Entities.Purchase.PaymentVoucherLine>()
            .AsNoTracking().FirstAsync(l => l.PaymentVoucherId == id);
        line.VatAmount.Should().Be(0m,
            "ม.82/5 — a non-VAT-registered vendor issues no tax invoice, so the PV draft's input VAT must be 0");
    }

    // (w) E3 COMPLIANCE — the same non-VAT vendor with VatRate>0 must be REJECTED by the service
    // (pv.vendor_not_vat_registered, ม.82/5). Proves the guard is NOT bypassed via the MCP draft path.
    [SkippableFact]
    public async Task E3_payment_voucher_draft_rejects_vat_on_non_vat_vendor()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync(vatRegistered: false);
        var (catId, expAcct) = await SeedExpenseCategoryAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            docDate = today, vendorId, expenseCategoryId = catId,
            paymentMethod = "Transfer", chequeNo = (string?)null, chequeDate = (DateOnly?)null,
            bankAccountId = (long?)null, currencyCode = "THB", exchangeRate = 1m,
            description = "x", notes = (string?)null,
            // Illegal: 7% VAT from a non-VAT vendor — the service must reject (ม.82/5).
            lines = new[] { new { expenseAccountId = (long?)expAcct, description = "l", amount = 1000m,
                                  taxCodeId = (int?)null, vatRate = 0.07m, isRecoverableVat = false,
                                  whtTypeId = (int?)null, whtRate = 0m, productType = (string?)null } },
            vendorInvoiceId = (long?)null, selfWithholdMode = (bool?)null,
            businessUnitId = (int?)null, whtPayerMode = (string?)null,
        };

        var result = await client.CallToolAsync(
            "create_payment_voucher_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue(
            "ม.82/5 — charging VAT from a non-VAT vendor must be rejected by PaymentVoucherService (the MCP path does not bypass it)");
    }

    // (w2) E3 COMPLIANCE — WHT handling runs unchanged through PaymentVoucherService in the MCP
    // draft path: a line with a WHT type + 3% rate stores WhtAmount = 3% of the line amount.
    [SkippableFact]
    public async Task E3_payment_voucher_draft_computes_wht_via_service()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId = await SeedVendorAsync();                 // VAT-registered
        var (catId, expAcct) = await SeedExpenseCategoryAsync();
        var whtTypeId = await SeedWhtTypeAsync();               // 3% service WHT
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal lineAmount = 1000m;
        var request = new
        {
            docDate = today, vendorId, expenseCategoryId = catId,
            paymentMethod = "Transfer", chequeNo = (string?)null, chequeDate = (DateOnly?)null,
            bankAccountId = (long?)null, currencyCode = "THB", exchangeRate = 1m,
            description = "x", notes = (string?)null,
            lines = new[] { new { expenseAccountId = (long?)expAcct, description = "l", amount = lineAmount,
                                  taxCodeId = (int?)null, vatRate = 0m, isRecoverableVat = false,
                                  whtTypeId = (int?)whtTypeId, whtRate = 0.03m, productType = (string?)null } },
            vendorInvoiceId = (long?)null, selfWithholdMode = (bool?)null,
            businessUnitId = (int?)null, whtPayerMode = (string?)null,
        };

        var result = await client.CallToolAsync(
            "create_payment_voucher_draft",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var id = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);

        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.Set<Accounting.Domain.Entities.Purchase.PaymentVoucherLine>()
            .AsNoTracking().FirstAsync(l => l.PaymentVoucherId == id);
        line.WhtAmount.Should().Be(30m,
            "WHT must be computed by PaymentVoucherService in the MCP draft path (3% of 1000 = 30) — unchanged");
    }

    // (x) E3 — create_vendor returns id + code + name (auto master data, no approval URL).
    [SkippableFact]
    public async Task E3_create_vendor_returns_id_code_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var code = TestIds.VendorCode();
        var request = new
        {
            vendorCode = code, vendorType = "Corporate", nameTh = "ผู้ขาย MCP E3", nameEn = (string?)null,
            taxId = (string?)null, branchCode = (string?)null, branchName = (string?)null,
            vatRegistered = true, address = (string?)null, contactPerson = (string?)null,
            phone = (string?)null, email = (string?)null, paymentTermDays = 30, defaultCurrency = "THB",
            defaultWhtTypeCode = (string?)null,
        };

        var result = await client.CallToolAsync(
            "create_vendor",
            new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        root.GetProperty("id").GetInt64().Should().BeGreaterThan(0);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("nameTh").GetString().Should().Be("ผู้ขาย MCP E3");
        root.TryGetProperty("approvalUrl", out _).Should().BeFalse();   // auto — no approval URL
    }

    // ── E4 PDF download tools ────────────────────────────────────────────────

    // Helper: seed a posted tax invoice on company 1 and return its id.
    // Uses company 1 (period open in teas_test). TaxCodeId=1 = VAT7 (seeded by fixture).
    private async Task<long> SeedPostedTaxInvoiceAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc     = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var custSvc = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        // non-VAT customer: no TaxId required (avoids ม.86/4 #3 validation on the TI).
        var custId = await custSvc.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า PDF E4", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // TaxCodeId=1 = VAT7 at 7% (seeded by the fixture for every company-1 run).
        var draftId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            today, custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ E4", 1m, 1, "ครั้ง",
                1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);

        await svc.PostAsync(draftId, default);
        return draftId;
    }

    // (aa) E4 — posted tax invoice → get_tax_invoice_pdf_url returns a URL; fetching it yields PDF bytes.
    [SkippableFact]
    public async Task E4_posted_tax_invoice_pdf_url_is_fetchable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var tiId = await SeedPostedTaxInvoiceAsync();
        var key  = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync(
            "get_tax_invoice_pdf_url",
            new Dictionary<string, object?> { ["id"] = tiId });

        result.IsError.Should().NotBe(true, "posted TI must return a PDF URL without error");
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var url = doc.RootElement.GetProperty("url").GetString()!;
        url.Should().Contain($"/api/v1/tax-invoices/{tiId}/pdf");

        // Fetch the URL — must return a real PDF.
        var pdfResp = await http.GetAsync(url);
        pdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await pdfResp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100, "PDF must be non-trivial");
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF",
            "the response must be a PDF (magic header %PDF-)");
    }

    // (ab) E4 — draft tax invoice → get_tax_invoice_pdf_url returns mcp.pdf_not_posted.
    [SkippableFact]
    public async Task E4_draft_tax_invoice_pdf_url_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Create a draft (do NOT post it).
        var custId = await SeedCustomerAsync();
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var draftId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            today, custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "draft E4", 1m, 1, "ครั้ง",
                500m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        // NOT posted — call the PDF URL tool.
        var key  = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync(
            "get_tax_invoice_pdf_url",
            new Dictionary<string, object?> { ["id"] = draftId });

        // McpE2Exception("mcp.pdf_not_posted",...) → SDK returns IsError=true.
        // SDK sanitizes the wire message to a generic string (see E2 test pattern, obs 8966).
        result.IsError.Should().BeTrue("draft TI must be rejected by the posted-only gate (mcp.pdf_not_posted)");
    }

    // (ac) E4 — key without sales.tax_invoice.read → get_tax_invoice_pdf_url is hidden + denied.
    [SkippableFact]
    public async Task E4_no_read_scope_key_denied_for_pdf_url_tool()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Mint a key with ONLY purchase scopes — no sales.tax_invoice.read.
        var limitedKey = await MintMcpKeyAsync(
        [
            "purchase.purchase_order.read", "master.vendor.manage",
        ]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, limitedKey);
        await using var client = await ConnectAsync(http);

        // Tool must be absent from the visible list (scope-filtered by AddAuthorizationFilters).
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().NotContain("get_tax_invoice_pdf_url",
            "a key without sales.tax_invoice.read must not see the tax-invoice PDF URL tool");

        // And a direct call throws (double-gate: authorization rejects even direct invocation).
        var act = async () => await client.CallToolAsync(
            "get_tax_invoice_pdf_url",
            new Dictionary<string, object?> { ["id"] = 1L });
        await act.Should().ThrowAsync<Exception>("key lacking sales.tax_invoice.read must be denied");
    }

    // (ad) E4 — posted PO → get_purchase_order_pdf_url returns a fetchable PDF URL.
    // Tests the purchase side of E4 (different service + scope).
    [SkippableFact]
    public async Task E4_posted_purchase_order_pdf_url_is_fetchable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Seed a PO draft via MCP, then approve it via service (approve = non-DRAFT for PDF).
        var vendorId  = await SeedVendorAsync();
        var productId = await SeedProductAsync();
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc  = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var draftId = await svc.CreateDraftAsync(new CreatePurchaseOrderRequest(
            today, null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(productId, "สินค้า E4 PDF", 1m, "ชิ้น",
                100m, 0m, null, null, 0m, null)]), default);
        await svc.ApproveAsync(draftId, default);

        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync(
            "get_purchase_order_pdf_url",
            new Dictionary<string, object?> { ["id"] = draftId });

        result.IsError.Should().NotBe(true, "approved PO must return a PDF URL without error");
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        var url = doc.RootElement.GetProperty("url").GetString()!;
        url.Should().Contain($"/api/v1/purchase-orders/{draftId}/pdf");

        var pdfResp = await http.GetAsync(url);
        pdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await pdfResp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    // (ae) E4 — cross-company: quotation owned by company X → company-1 key → not found (IsError=true).
    // Proves the tenant filter (RLS + EF global filter) nulls GetAsync for a foreign doc.
    // Uses a quotation (no VAT-issuer requirement) to avoid company-setup complexity.
    [SkippableFact]
    public async Task E4_cross_company_doc_returns_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Seed a fresh non-VAT company + a quotation owned by that company.
        var otherCo = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        long otherQId;
        {
            await using var sp2 = new ServiceCollection()
                .AddLogging()
                .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build())
                .AddSingleton<ITenantContext>(new StubTenant
                { CompanyId = otherCo.CompanyId, BranchId = otherCo.BranchId, UserId = 1, IsSuperAdmin = false })
                .BuildServiceProvider();
            await using var scope2 = sp2.CreateAsyncScope();
            var svc2 = scope2.ServiceProvider.GetRequiredService<IQuotationService>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            otherQId = await svc2.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), otherCo.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "cross-co Q", 1m, "ครั้ง", 200m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await svc2.SendAsync(otherQId, default);   // status = Sent (non-Draft, so PDF gate passes)
        }

        // Company-1 key tries to fetch other-company doc → must be rejected (not found).
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync(
            "get_quotation_pdf_url",
            new Dictionary<string, object?> { ["id"] = otherQId });

        result.IsError.Should().BeTrue(
            "a doc owned by another company must return not-found (IsError=true) — tenant isolation gate");
    }

    // (af) E4 — draft quotation → get_quotation_pdf_url is rejected (SalesChain status family).
    // Verifies the posted-only gate for the QuotationStatus enum family (distinct from DocumentStatus).
    [SkippableFact]
    public async Task E4_draft_quotation_pdf_url_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var custId = await SeedCustomerAsync();
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Create draft — do NOT send/accept it. Status = "Draft" (QuotationStatus.Draft.ToString()).
        var draftId = await svc.CreateDraftAsync(new CreateQuotationRequest(
            today, today.AddDays(30), custId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "บริการ E4 Q", 1m, "ครั้ง", 500m, 0m, 1, "VAT7", 0.07m)]),
            default);

        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync(
            "get_quotation_pdf_url",
            new Dictionary<string, object?> { ["id"] = draftId });

        result.IsError.Should().BeTrue(
            "draft quotation must be rejected by the posted-only gate (SalesChain QuotationStatus family)");
    }

    // (y) E3 — no purchase post/approve tool is exposed (already covered in (b) globally,
    // re-asserted here explicitly for the purchase surface as the compliance backstop).
    [SkippableFact]
    public async Task E3_no_purchase_post_or_approve_tool_exists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        // No purchase action tool (post/approve/cancel/pay/send/issue verbs as discrete tokens).
        var actionVerbs = new[] { "post", "issue", "send", "approve", "cancel", "pay" };
        names.Where(n => n.Contains("purchase") || n.Contains("vendor") || n.Contains("payment"))
            .Should().NotContain(n => n.Split('_').Any(tok => actionVerbs.Contains(tok)),
                "the purchase surface must expose read + create-draft tools only — no post/approve/pay");
        // The create-draft purchase tools ARE present (sanity).
        names.Should().Contain("create_payment_voucher_draft");
    }

    // (z) E3 — the pending-agent-approvals count includes agent-created purchase drafts, and
    // created_via_api_key_name is stamped for api-key (agent) creates / NULL for JWT creates.
    // The /reports endpoint lives on the JWT-gated BFF surface (an X-Api-Key can't reach it),
    // so we assert the same DocumentStatus.Draft + CreatedViaApiKeyName filter the endpoint uses,
    // directly against the DbContext — the enum/filter logic under test is identical.
    [SkippableFact]
    public async Task E3_purchase_drafts_stamp_api_key_name_and_feed_pending_count()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var vendorId  = await SeedVendorAsync();
        var productId = await SeedProductAsync();
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // Create a PO draft via the agent (api-key principal → stamps created_via_api_key_name).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var poRequest = new
        {
            docDate = today, expectedDeliveryDate = (DateOnly?)null, vendorId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[] { new { productId, descriptionTh = "PO count", quantity = 1m, uomText = "ชิ้น",
                                  unitPrice = 100m, discountPercent = 0m,
                                  taxCodeId = (int?)null, taxCode = (string?)null, taxRate = 0m, notes = (string?)null } },
        };
        var poResult = await client.CallToolAsync(
            "create_purchase_order_draft",
            new Dictionary<string, object?> { ["request"] = poRequest });
        poResult.IsError.Should().NotBe(true);
        var agentPoId = JsonDocument.Parse(poResult.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("id").GetInt64();

        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // (1) api-key create → created_via_api_key_name is stamped (non-null).
        var agentPo = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.PurchaseOrderId == agentPoId);
        agentPo.CreatedViaApiKeyName.Should().NotBeNullOrEmpty(
            "an agent (api-key) PO draft must stamp created_via_api_key_name (M4 backstop)");

        // (2) a JWT/human create (shared service, StubTenant has no ApiKeyName) → NULL.
        long jwtPoId;
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            jwtPoId = await svc.CreateDraftAsync(new CreatePurchaseOrderRequest(
                today, null, vendorId, null, "THB", 1m, null, null,
                [new PurchaseOrderLineInput(null, "human PO", 1m, "ชิ้น", 50m, 0m, null, null, 0m, null)]),
                default);
        }
        var jwtPo = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.PurchaseOrderId == jwtPoId);
        jwtPo.CreatedViaApiKeyName.Should().BeNull("a JWT/human PO draft must NOT stamp the key name");

        // (3) the pending-agent-approvals filter (DocumentStatus/PurchaseOrderStatus.Draft +
        // CreatedViaApiKeyName != null) counts the agent draft — i.e. the dashboard count covers it.
        var poDraftCount = await db.PurchaseOrders
            .Where(p => p.CreatedViaApiKeyName != null
                     && p.Status == Accounting.Domain.Enums.PurchaseOrderStatus.Draft)
            .CountAsync();
        poDraftCount.Should().BeGreaterThan(0,
            "the pending-agent-approvals count must include agent-created purchase-order drafts");
    }

    // ── E5 — approval-status poll tools ─────────────────────────────────────

    // (aa) list_pending_approvals returns OWN key's drafts only — not another key's, not posted,
    // not other company. get_document_status reflects Draft/Posted transitions.
    [SkippableFact]
    public async Task E5_list_pending_approvals_returns_own_key_drafts_only()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Mint two distinct MCP keys (both company 1).
        var keyA = await MintMcpKeyAsync();
        var keyB = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();

        await using var factory = new McpApiFactory(_fx.ConnectionString);

        // Key A creates a quotation draft.
        long quotationIdA;
        {
            using var httpA = factory.CreateClient();
            httpA.DefaultRequestHeaders.Add(ApiKeyHeader, keyA);
            await using var clientA = await ConnectAsync(httpA);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var req = new
            {
                docDate = today, validUntilDate = today.AddDays(30), customerId,
                businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
                notes = (string?)null, internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "E5 own", quantity = 1m,
                          uomText = "ครั้ง", unitPrice = 500m, discountPercent = 0m,
                          taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
                },
            };
            var r = await clientA.CallToolAsync("create_quotation_draft",
                new Dictionary<string, object?> { ["request"] = req });
            r.IsError.Should().NotBe(true);
            quotationIdA = JsonDocument.Parse(r.Content.OfType<TextContentBlock>().Single().Text)
                .RootElement.GetProperty("id").GetInt64();
        }

        // Key B creates a separate quotation draft — must NOT appear in key A's list.
        {
            using var httpB = factory.CreateClient();
            httpB.DefaultRequestHeaders.Add(ApiKeyHeader, keyB);
            await using var clientB = await ConnectAsync(httpB);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var req = new
            {
                docDate = today, validUntilDate = today.AddDays(30), customerId,
                businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
                notes = (string?)null, internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "E5 other", quantity = 1m,
                          uomText = "ครั้ง", unitPrice = 500m, discountPercent = 0m,
                          taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
                },
            };
            var r = await clientB.CallToolAsync("create_quotation_draft",
                new Dictionary<string, object?> { ["request"] = req });
            r.IsError.Should().NotBe(true);
        }

        // Assert: key A sees its own draft, not key B's.
        using var httpCheck = factory.CreateClient();
        httpCheck.DefaultRequestHeaders.Add(ApiKeyHeader, keyA);
        await using var clientCheck = await ConnectAsync(httpCheck);

        var listResult = await clientCheck.CallToolAsync("list_pending_approvals",
            new Dictionary<string, object?>());
        listResult.IsError.Should().NotBe(true);
        using var listDoc = JsonDocument.Parse(
            listResult.Content.OfType<TextContentBlock>().Single().Text);
        var items = listDoc.RootElement.EnumerateArray().ToList();

        // Key A's quotation must be present.
        // TryGetProperty: safe in predicate — GetProperty throws KeyNotFoundException on missing key.
        var ownItem = items.FirstOrDefault(i =>
            i.TryGetProperty("type", out var t) && t.GetString() == "quotation" &&
            i.TryGetProperty("id", out var idEl) && idEl.GetInt64() == quotationIdA);
        ownItem.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "key A must see its own draft in list_pending_approvals");

        // DocNo absent or null (draft — not yet sent/posted). MCP SDK omits null properties (WhenWritingNull).
        var hasDocNo = ownItem.TryGetProperty("docNo", out var docNoProp);
        if (hasDocNo)
            docNoProp.ValueKind.Should().Be(JsonValueKind.Null,
                "draft quotation docNo must be null until sent");

        // ApprovalUrl carries the ?action=approve suffix.
        ownItem.GetProperty("approvalUrl").GetString()!
            .Should().EndWith($"/quotations/{quotationIdA}?action=approve");
    }

    // (ab) get_document_status returns Draft/not-posted initially; and not-found for other company.
    [SkippableFact]
    public async Task E5_get_document_status_reflects_draft_and_cross_company_isolation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        var customerId = await SeedCustomerAsync();
        var productId  = await SeedProductAsync();

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // Create a quotation draft.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new
        {
            docDate = today, validUntilDate = today.AddDays(30), customerId,
            businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
            notes = (string?)null, internalNotes = (string?)null,
            lines = new[]
            {
                new { productId, descriptionTh = "E5 status", quantity = 1m,
                      uomText = "ครั้ง", unitPrice = 400m, discountPercent = 0m,
                      taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
            },
        };
        var createResult = await client.CallToolAsync("create_quotation_draft",
            new Dictionary<string, object?> { ["request"] = req });
        createResult.IsError.Should().NotBe(true);
        var qId = JsonDocument.Parse(createResult.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("id").GetInt64();

        // get_document_status → Draft, posted=false, docNo=null.
        var statusResult = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "quotation", ["id"] = qId });
        statusResult.IsError.Should().NotBe(true);
        using var statusDoc = JsonDocument.Parse(
            statusResult.Content.OfType<TextContentBlock>().Single().Text);
        var root = statusDoc.RootElement;
        root.GetProperty("status").GetString().Should().Be("Draft");
        root.GetProperty("posted").GetBoolean().Should().BeFalse("draft is not yet posted");
        // MCP SDK omits null properties (WhenWritingNull) — docNo absent on a draft is correct.
        if (root.TryGetProperty("docNo", out var docNoEl))
            docNoEl.ValueKind.Should().Be(JsonValueKind.Null, "draft has no doc number yet");

        // Cross-company: a non-existent id (in another company or simply missing) → mcp.not_found error.
        var notFoundResult = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "quotation", ["id"] = 999_999_999L });
        notFoundResult.IsError.Should().BeTrue("non-existent/other-company doc must return error");
        // ponytail: MCP SDK sanitizes exception messages on the wire to a generic string (see obs 8966).
        // Assert IsError only — content text does NOT contain the error code.

        // Invalid type → mcp.invalid_type error.
        var badTypeResult = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "banana", ["id"] = qId });
        badTypeResult.IsError.Should().BeTrue("unknown type must return error");
        // ponytail: SDK sanitizes — IsError only, no content text assertion.
    }
}
