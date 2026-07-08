using System.Net;
using System.Text.Json;
using Accounting.Api.Middleware;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// Write-side MCP expansion (specs/mcp-expansion.md — §A public PDF links, §D edit tools).
/// Reuses the <see cref="McpApiFactory"/> + mcp-kind key minting pattern from
/// McpServerSmokeTests.cs / McpReadExpansionTests.cs. §A tests use a dedicated factory
/// subclass (<see cref="PublicPdfApiFactory"/>) so the DataProtection key path can be pinned
/// for the "survives an app-host restart" regression test.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpWriteExpansionTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpWriteExpansionTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Same as <see cref="McpApiFactory"/> but with a caller-controlled DataProtection
    /// key path, so two DIFFERENT factory instances (simulating two app-host lifetimes) can be
    /// pointed at the SAME on-disk key ring — proving A.4's persistence requirement.</summary>
    private sealed class PublicPdfApiFactory(string connectionString, string dpKeyPath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.UseSetting("Database:RunInitializerOnStartup", "false");
            builder.UseSetting("App:BaseUrl", "http://localhost:3000");
            builder.UseSetting("Quartz:Enabled", "false");
            builder.UseSetting("DataProtection:KeyPath", dpKeyPath);
        }
    }

    private static async Task<McpClient> ConnectAsync(HttpClient http) =>
        await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http, loggerFactory: null, ownsHttpClient: false));

    private static JsonElement ResultRoot(CallToolResult result) =>
        JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();

    private async Task<string> MintKeyAsync(int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-write"), scopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    // ══════════════════════ §A — public PDF links ══════════════════════

    private static string MintPdfToken(
        IDataProtectionProvider dp, string docType, long docId, int companyId, int branchId, TimeSpan ttl) =>
        dp.CreateProtector(PublicPdfTokens.Purpose).ToTimeLimitedDataProtector()
            .Protect(PublicPdfTokens.Payload(docType, docId, companyId, branchId), ttl);

    private static string MintExpiredPdfToken(
        IDataProtectionProvider dp, string docType, long docId, int companyId, int branchId) =>
        dp.CreateProtector(PublicPdfTokens.Purpose).ToTimeLimitedDataProtector()
            .Protect(PublicPdfTokens.Payload(docType, docId, companyId, branchId), DateTimeOffset.UtcNow.AddSeconds(-1));

    /// <summary>Seed a posted tax invoice for the given company and return its id.</summary>
    private async Task<long> SeedPostedTaxInvoiceAsync(int companyId, int branchId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var today = new SystemClock().TodayInBangkok();
        var draftId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            today, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "public pdf test", 1m, 1, "ครั้ง", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(draftId, default);
        return draftId;
    }

    [SkippableFact]
    public async Task A_valid_token_serves_pdf_inline()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var dp = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var token = MintPdfToken(dp, "tax_invoice", tiId, co.CompanyId, co.BranchId, TimeSpan.FromHours(24));

        var resp = await http.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline",
            "A.2: must render in the tab, not force a download");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [SkippableFact]
    public async Task A_missing_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/public/pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task A_garbage_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/public/pdf?t=not-a-real-token");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task A_tampered_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var dp = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var token = MintPdfToken(dp, "tax_invoice", tiId, co.CompanyId, co.BranchId, TimeSpan.FromHours(24));
        // Flip the last character — DataProtection's authenticated encryption must reject this.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var resp = await http.GetAsync($"/public/pdf?t={Uri.EscapeDataString(tampered)}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task A_expired_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var dp = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var token = MintExpiredPdfToken(dp, "tax_invoice", tiId, co.CompanyId, co.BranchId);

        var resp = await http.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task A_unknown_doctype_in_validly_signed_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var dp = factory.Services.GetRequiredService<IDataProtectionProvider>();
        // Token is validly signed (same protector/purpose) but carries a docType the endpoint's
        // switch doesn't recognize — must fall through to the ".not_found"-suffixed exception
        // code (public_pdf.not_found) so DomainExceptionMiddleware maps it to 404, never 422.
        // A probing attacker must see the SAME 404 as any other invalid/expired/tampered token.
        var token = MintPdfToken(dp, "bogus_doc_type", tiId, co.CompanyId, co.BranchId, TimeSpan.FromHours(24));

        var resp = await http.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown docType inside a validly-signed token must never reveal itself via a different status code (e.g. 422)");
    }

    [SkippableFact]
    public async Task A_cross_tenant_token_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiIdOfB = await SeedPostedTaxInvoiceAsync(b.CompanyId, b.BranchId, b.CustomerId);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var dp = factory.Services.GetRequiredService<IDataProtectionProvider>();
        // Token minted for company A's tenant context but pointing at company B's document id.
        var token = MintPdfToken(dp, "tax_invoice", tiIdOfB, a.CompanyId, a.BranchId, TimeSpan.FromHours(24));

        var resp = await http.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the EF tenant filter must reject a doc id that belongs to a different company than the token's");

        // RLS-layer proof (same caveat as troubles-wiki "RLS masked by superuser tests" — teas_test
        // connects as SUPERUSER, bypassing RLS, so the HTTP-level 404 above only proves the EF
        // filter). Independently prove the DB policy itself blocks the same cross-tenant read
        // under a non-bypass role, mirroring SalesChainRlsTests/AuditLogRlsTests.
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var grant = new NpgsqlCommand(
            "GRANT USAGE ON SCHEMA sales TO pg_database_owner; GRANT SELECT ON sales.tax_invoices TO pg_database_owner;", conn))
            await grant.ExecuteNonQueryAsync();
        try
        {
            await using (var setRole = new NpgsqlCommand("SET ROLE pg_database_owner", conn))
                await setRole.ExecuteNonQueryAsync();
            await using (var pin = new NpgsqlCommand(
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), set_config('app.bypass_rls', 'false', false)", conn))
                await pin.ExecuteNonQueryAsync();

            await using var cmd = new NpgsqlCommand(
                $"SELECT count(*) FROM sales.tax_invoices WHERE tax_invoice_id = {tiIdOfB}", conn);
            var visible = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            visible.Should().Be(0, "the RLS policy on sales.tax_invoices must hide company B's row while app.company_id is pinned to A");
        }
        finally
        {
            await using var reset = new NpgsqlCommand("RESET ROLE", conn);
            await reset.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task A_rate_limit_429_after_30_requests_per_minute()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        const string ip = "203.0.113.55";   // TEST-NET-3 (RFC 5737) — never a real client

        async Task<HttpStatusCode> HitAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/public/pdf?t=irrelevant");
            req.Headers.Add("X-Forwarded-For", ip);
            using var resp = await http.SendAsync(req);
            return resp.StatusCode;
        }

        for (var i = 0; i < 30; i++)
        {
            var status = await HitAsync();
            status.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"request {i + 1}/30 from the same IP is still within the 30/min window");
        }
        var st31 = await HitAsync();
        st31.Should().Be(HttpStatusCode.TooManyRequests, "the 31st request in the same window must hit the public-pdf per-IP limit (A.5)");
    }

    [SkippableFact]
    public async Task A_dataprotection_keys_persist_across_a_fresh_app_host()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);
        var keyPath = Path.Combine(Path.GetTempPath(), "teas-dp-keys-test-" + Guid.NewGuid().ToString("N")[..8]);

        string token;
        await using (var factory1 = new PublicPdfApiFactory(_fx.ConnectionString, keyPath))
        {
            var dp1 = factory1.Services.GetRequiredService<IDataProtectionProvider>();
            token = MintPdfToken(dp1, "tax_invoice", tiId, co.CompanyId, co.BranchId, TimeSpan.FromHours(24));
            using var http1 = factory1.CreateClient();
            var resp1 = await http1.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
            resp1.StatusCode.Should().Be(HttpStatusCode.OK, "the token must validate against the host that minted it");
        }

        // Fresh WebApplicationFactory instance (simulates a pm2 restart/redeploy) pointed at the
        // SAME on-disk key ring — the token minted by the FIRST host must still validate.
        await using var factory2 = new PublicPdfApiFactory(_fx.ConnectionString, keyPath);
        using var http2 = factory2.CreateClient();
        var resp2 = await http2.GetAsync($"/public/pdf?t={Uri.EscapeDataString(token)}");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK,
            "A.4 regression guard: PersistKeysToFileSystem must survive an app-host restart, not just live in-memory");
    }

    // ══════════════════════ §D1 — master-data edit tools ══════════════════════

    [SkippableFact]
    public async Task D1_update_customer_product_vendor_persist_full_replace()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId,
            ["master.customer.manage", "master.product.manage", "master.vendor.manage"]);

        long productId, vendorId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "เดิม", null, "GOOD", "ชิ้น", 10m, null, null, null, null, null,
                IsSaleable: true), default);
            var vendorSvc = scope.ServiceProvider.GetRequiredService<IVendorService>();
            vendorId = await vendorSvc.CreateAsync(new CreateVendorRequest(
                TestIds.VendorCode(), CustomerType.Corporate, "เดิม", null, null, null, null,
                false, null, null, null, null, 30, "THB", null), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var custResult = await client.CallToolAsync("update_customer", new Dictionary<string, object?>
        {
            ["customerId"] = co.CustomerId,
            ["request"] = new
            {
                nameTh = "ลูกค้าใหม่", nameEn = (string?)null, taxId = (string?)null,
                branchCode = (string?)null, branchName = (string?)null, vatRegistered = false,
                billingAddress = "ที่อยู่ใหม่", contactPerson = (string?)null, phone = (string?)null,
                email = (string?)null, creditLimit = 5000m, paymentTermDays = 45,
                defaultCurrency = "THB", isActive = true,
            },
        });
        custResult.IsError.Should().NotBe(true);

        var prodResult = await client.CallToolAsync("update_product", new Dictionary<string, object?>
        {
            ["productId"] = productId,
            ["request"] = new
            {
                nameTh = "สินค้าใหม่", nameEn = (string?)null, productType = "GOOD",
                defaultUomText = "ชิ้น", defaultUnitPrice = 20m,
                defaultOutputTaxCodeId = (int?)null, defaultInputTaxCodeId = (int?)null,
                defaultWhtTypeId = (int?)null, descriptionTh = (string?)null, notes = (string?)null,
                isActive = true, isSaleable = true, isPurchasable = false, businessUnitId = (int?)null,
            },
        });
        prodResult.IsError.Should().NotBe(true);

        var vendResult = await client.CallToolAsync("update_vendor", new Dictionary<string, object?>
        {
            ["vendorId"] = vendorId,
            ["request"] = new
            {
                nameTh = "ผู้ขายใหม่", nameEn = (string?)null, taxId = (string?)null,
                branchCode = (string?)null, branchName = (string?)null, vatRegistered = false,
                address = "ที่อยู่ผู้ขายใหม่", contactPerson = (string?)null, phone = (string?)null,
                email = (string?)null, paymentTermDays = 15, defaultCurrency = "THB",
                defaultWhtTypeCode = (string?)null, isActive = true,
                isForeign = false, hasThaiVatDReg = false, countryCode = (string?)null,
            },
        });
        vendResult.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Customers.AsNoTracking().FirstAsync(c => c.CustomerId == co.CustomerId)).NameTh.Should().Be("ลูกค้าใหม่");
        (await db.Products.AsNoTracking().FirstAsync(p => p.ProductId == productId)).NameTh.Should().Be("สินค้าใหม่");
        (await db.Vendors.AsNoTracking().FirstAsync(v => v.VendorId == vendorId)).NameTh.Should().Be("ผู้ขายใหม่");
    }

    // ══════════════════════ §D2 — draft-update tools (existing UpdateDraftAsync) ══════════════════════

    [SkippableFact]
    public async Task D2_update_quotation_draft_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.quotation.create", "master.product.manage"]);

        long productId, quotationId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า D2", null, "SERVICE", "ครั้ง", 100m,
                null, null, null, null, null, IsSaleable: true), default);
            var qSvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            var today = new SystemClock().TodayInBangkok();
            quotationId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), co.CustomerId, null, "THB", 1m, "เดิม", null,
                [new ChainLineInput(productId, "เดิม", 1m, "ครั้ง", 100m, 0m, 0, "NONE", 0m, null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("update_quotation_draft", new Dictionary<string, object?>
        {
            ["quotationId"] = quotationId,
            ["request"] = new
            {
                docDate = today2, validUntilDate = today2.AddDays(60), customerId = co.CustomerId,
                businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
                notes = "แก้ไขแล้ว", internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "แก้ไขแล้ว", quantity = 2m, uomText = "ครั้ง",
                          unitPrice = 200m, discountPercent = 0m, taxCodeId = 0, taxCode = "NONE",
                          taxRate = 0m, productType = (string?)null },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var qSvc2 = verifyScope.ServiceProvider.GetRequiredService<IQuotationService>();
        var detail = await qSvc2.GetAsync(quotationId, default);
        detail!.Notes.Should().Be("แก้ไขแล้ว");
        // Lines-based assertion (not TotalAmount): the tax-code→rate derivation on a VAT-
        // registered company is D3's concern, not D2's — D2 only proves the thin wrapper
        // persisted the delete-and-recreated line the caller sent.
        detail.Lines.Should().ContainSingle(l => l.DescriptionTh == "แก้ไขแล้ว" && l.Quantity == 2m);
    }

    [SkippableFact]
    public async Task D2_update_purchase_order_draft_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId,
            ["purchase.purchase_order.create", "master.vendor.manage", "master.product.manage"]);

        long productId, vendorId, poId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า PO", null, "GOOD", "ชิ้น", 50m,
                null, null, null, null, null, IsSaleable: false, IsPurchasable: true), default);
            var vendorSvc = scope.ServiceProvider.GetRequiredService<IVendorService>();
            vendorId = await vendorSvc.CreateAsync(new CreateVendorRequest(
                TestIds.VendorCode(), CustomerType.Corporate, "ผู้ขาย PO", null, null, null, null,
                false, null, null, null, null, 30, "THB", null), default);
            var poSvc = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            var today = new SystemClock().TodayInBangkok();
            poId = await poSvc.CreateDraftAsync(new CreatePurchaseOrderRequest(
                today, null, vendorId, null, "THB", 1m, "เดิม", null,
                [new PurchaseOrderLineInput(productId, "เดิม", 1m, "ชิ้น", 50m, 0m, null, null, 0m, null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("update_purchase_order_draft", new Dictionary<string, object?>
        {
            ["purchaseOrderId"] = poId,
            ["request"] = new
            {
                docDate = new SystemClock().TodayInBangkok(), expectedDeliveryDate = (DateOnly?)null,
                vendorId, businessUnitId = (int?)null, currencyCode = "THB", exchangeRate = 1m,
                notes = "แก้ไขแล้ว", internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "แก้ไขแล้ว", quantity = 3m, uomText = (string?)"ชิ้น",
                          unitPrice = 60m, discountPercent = 0m, taxCodeId = (int?)null, taxCode = (string?)null,
                          taxRate = 0m, notes = (string?)null },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).FirstAsync(p => p.PurchaseOrderId == poId);
        po.Notes.Should().Be("แก้ไขแล้ว");
        po.Lines.Should().ContainSingle(l => l.Quantity == 3m);
    }

    [SkippableFact]
    public async Task D2_update_vendor_invoice_draft_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId,
            ["purchase.vendor_invoice.create", "master.vendor.manage"]);

        long vendorId, viId;
        int expCatId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var expAcct = await db.ChartOfAccounts
                .Where(a => a.CompanyId == co.CompanyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var cat = new Accounting.Domain.Entities.Sys.ExpenseCategory
            {
                CompanyId = co.CompanyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "หมวด D2", DefaultExpenseAccountId = expAcct, DefaultIsRecoverableVat = true,
            };
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();
            expCatId = cat.CategoryId;

            var vendorSvc = scope.ServiceProvider.GetRequiredService<IVendorService>();
            vendorId = await vendorSvc.CreateAsync(new CreateVendorRequest(
                TestIds.VendorCode(), CustomerType.Corporate, "ผู้ขาย VI", null, null, null, null,
                false, null, null, null, null, 30, "THB", null), default);

            var viSvc = scope.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var today = new SystemClock().TodayInBangkok();
            viId = await viSvc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                today, vendorId, "INV-001", today, null, "THB", 1m, "เดิม",
                [new VendorInvoiceLineInput(expCatId, null, "เดิม", 500m, 0m, null)],
                HasInputVat: false), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("update_vendor_invoice_draft", new Dictionary<string, object?>
        {
            ["vendorInvoiceId"] = viId,
            ["request"] = new
            {
                docDate = today2, vendorId, vendorTaxInvoiceNo = "INV-002", vendorTaxInvoiceDate = today2,
                vatClaimPeriod = (int?)null, currencyCode = "THB", exchangeRate = 1m, notes = "แก้ไขแล้ว",
                lines = new[]
                {
                    new { expenseCategoryId = expCatId, expenseAccountId = (long?)null,
                          description = "แก้ไขแล้ว", amount = 800m, vatRate = 0m, productType = (string?)null },
                },
                hasInputVat = false, purchaseOrderId = (long?)null, businessUnitId = (int?)null,
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var vi = await db2.VendorInvoices.AsNoTracking().Include(v => v.Lines).FirstAsync(v => v.VendorInvoiceId == viId);
        vi.Notes.Should().Be("แก้ไขแล้ว");
        vi.VendorTaxInvoiceNo.Should().Be("INV-002");
        vi.Lines.Should().ContainSingle(l => l.Amount == 800m);
    }

    [SkippableFact]
    public async Task D2_update_invoice_draft_billing_note_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.billing_note.manage"]);

        long bnId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var bnSvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var today = new SystemClock().TodayInBangkok();
            bnId = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                today, today.AddDays(30), co.CustomerId, null, null, null, "THB", 1m, "เดิม", null,
                [new BillingLineInput(null, null, "เดิม", 1m, "ครั้ง", 100m, 0m, 0, "NONE", 0m, null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("update_invoice_draft", new Dictionary<string, object?>
        {
            ["invoiceId"] = bnId,
            ["request"] = new
            {
                docDate = today2, dueDate = today2.AddDays(45), customerId = co.CustomerId,
                businessUnitId = (int?)null, quotationId = (long?)null, taxInvoiceIds = (long[]?)null,
                currencyCode = "THB", exchangeRate = 1m, notes = "แก้ไขแล้ว", internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId = (long?)null, taxInvoiceId = (long?)null, descriptionTh = "แก้ไขแล้ว",
                          quantity = 1m, uomText = "ครั้ง", unitPrice = 150m, discountPercent = 0m,
                          taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var bnSvc2 = verifyScope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var detail = await bnSvc2.GetAsync(bnId, default);
        detail!.Notes.Should().Be("แก้ไขแล้ว");
        // Lines-based assertion (not TotalAmount) — see D2_update_quotation_draft_persists note.
        detail.Lines.Should().ContainSingle(l => l.DescriptionTh == "แก้ไขแล้ว" && l.UnitPrice == 150m);
    }

    // ══════════════════════ §D3 — tax invoice / receipt draft edit (new UpdateDraftAsync) ══════════════════════

    [SkippableFact]
    public async Task D3_ti_update_recomputes_tax_rate_from_tax_code_ignoring_wrong_client_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create", "master.product.manage"]);

        long productId, tiId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า D3", null, "GOOD", "ชิ้น", 100m,
                null, null, null, null, null, IsSaleable: true), default);
            var tiSvc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var today = new SystemClock().TodayInBangkok();
            tiId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                today, co.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(productId, null, "เดิม", 1m, 1, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m)],
                null), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        // Deliberately WRONG client-suggested taxRate=0 on a VAT7-coded line — the server must
        // derive the rate from the tax code classification, never trust the client's number.
        var result = await client.CallToolAsync("update_tax_invoice_draft", new Dictionary<string, object?>
        {
            ["taxInvoiceId"] = tiId,
            ["request"] = new
            {
                docDate = today2, customerId = co.CustomerId, isTaxInclusive = false,
                currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
                paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId = (int?)null,
                quotationId = (long?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "แก้ไขแล้ว", quantity = 2m,
                          uomId = 1, uomText = "ชิ้น", unitPrice = 100m, discountPercent = 0m,
                          taxCodeId = 1, taxCode = "VAT7", taxRate = 0m, productType = (string?)null },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == tiId);
        ti.Lines.Should().ContainSingle();
        ti.Lines.Single().TaxRate.Should().Be(0.07m,
            "the server must re-derive the VAT7 rate from the tax code, ignoring the client's taxRate=0");
        ti.TaxAmount.Should().Be(14m);      // 2 x 100 x 7%
        ti.TotalAmount.Should().Be(214m);
    }

    [SkippableFact]
    public async Task D3_ti_update_after_post_throws_cannot_edit_after_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await SeedPostedTaxInvoiceAsync(co.CompanyId, co.BranchId, co.CustomerId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var today = new SystemClock().TodayInBangkok();
        var act = () => svc.UpdateDraftAsync(tiId, new CreateTaxInvoiceRequest(
            today, co.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "x", 1m, 1, "ครั้ง", 1m, 0m, 1, "VAT7", 0.07m)], null), default);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("ti.cannot_edit_after_post");
    }

    [SkippableFact]
    public async Task D3_ti_update_cross_tenant_id_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long tiOfB;
        await using (var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, b.CompanyId, b.BranchId))
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var svcB = scopeB.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var today = new SystemClock().TodayInBangkok();
            tiOfB = await svcB.CreateDraftAsync(new CreateTaxInvoiceRequest(
                today, b.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "x", 1m, 1, "ครั้ง", 1m, 0m, 1, "VAT7", 0.07m)], null), default);
        }

        // EF-layer proof — company A's own scoped service can't see company B's draft.
        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId);
        await using var scopeA = spA.CreateAsyncScope();
        var svcA = scopeA.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var today2 = new SystemClock().TodayInBangkok();
        var act = () => svcA.UpdateDraftAsync(tiOfB, new CreateTaxInvoiceRequest(
            today2, a.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "x", 1m, 1, "ครั้ง", 1m, 0m, 1, "VAT7", 0.07m)], null), default);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("ti.not_found");

        // RLS-layer proof (same SET ROLE technique as A_cross_tenant_token_returns_404).
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var grant = new NpgsqlCommand(
            "GRANT USAGE ON SCHEMA sales TO pg_database_owner; GRANT SELECT ON sales.tax_invoices TO pg_database_owner;", conn))
            await grant.ExecuteNonQueryAsync();
        try
        {
            await using (var setRole = new NpgsqlCommand("SET ROLE pg_database_owner", conn))
                await setRole.ExecuteNonQueryAsync();
            await using (var pin = new NpgsqlCommand(
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), set_config('app.bypass_rls', 'false', false)", conn))
                await pin.ExecuteNonQueryAsync();
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM sales.tax_invoices WHERE tax_invoice_id = {tiOfB}", conn);
            (Convert.ToInt64(await cmd.ExecuteScalarAsync())).Should().Be(0);
        }
        finally
        {
            await using var reset = new NpgsqlCommand("RESET ROLE", conn);
            await reset.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task D3_ti_update_validation_failure_surfaces_field_errors()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);

        long tiId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var today = new SystemClock().TodayInBangkok();
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                today, co.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "x", 1m, 1, "ครั้ง", 1m, 0m, 1, "VAT7", 0.07m)], null), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("update_tax_invoice_draft", new Dictionary<string, object?>
        {
            ["taxInvoiceId"] = tiId,
            ["request"] = new
            {
                docDate = today2, customerId = co.CustomerId, isTaxInclusive = false,
                currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
                paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId = (int?)null,
                quotationId = (long?)null,
                lines = Array.Empty<object>(),   // CreateTaxInvoiceValidator: Lines must NotEmpty
            },
        });
        result.IsError.Should().BeTrue("an empty Lines array must fail FluentValidation");
    }

    [SkippableFact]
    public async Task D3_ti_race_backstop_trigger_582_aborts_stale_line_write_after_concurrent_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long tiId;
        await using (var seedSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var seedScope = seedSp.CreateAsyncScope())
        {
            var svc = seedScope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var today = new SystemClock().TodayInBangkok();
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                today, co.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "race", 1m, 1, "ครั้ง", 100m, 0m, 1, "VAT7", 0.07m)], null), default);
        }

        // Simulate the TOCTOU race D3.2 describes: an "in-flight edit" holds a tracked DRAFT
        // entity (loaded BEFORE a concurrent post commits), then tries to write its lines AFTER
        // the post has already flipped the row to POSTED. UpdateDraftAsync itself always
        // re-loads fresh (so its own status guard would catch a SEQUENTIAL call) — this test
        // isolates the DB-trigger backstop by racing at the EF/connection level directly.
        await using var raceSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var raceScope = raceSp.CreateAsyncScope();
        var raceDb = raceScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var staleTi = await raceDb.TaxInvoices.Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == tiId);
        staleTi.Status.Should().Be(Accounting.Domain.Enums.DocumentStatus.Draft);

        await using (var postSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var postScope = postSp.CreateAsyncScope())
        {
            var postSvc = postScope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            await postSvc.PostAsync(tiId, default);
        }

        // Now replay the "always delete-and-recreate lines" step of UpdateDraftAsync against the
        // STALE (still-Draft-in-memory) tracked entity.
        raceDb.RemoveRange(staleTi.Lines);
        staleTi.Lines.Clear();
        var act = () => raceDb.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23514",
                "trigger 582 (fn_ti_lines_immutable) must abort a line DELETE against a now-POSTED parent");

        // The posted doc itself must be unmodified (lines untouched).
        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var posted = await verifyDb.TaxInvoices.AsNoTracking().Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == tiId);
        posted.Status.Should().Be(Accounting.Domain.Enums.DocumentStatus.Posted);
        posted.Lines.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task D3_rc_update_recomputes_amount_from_lines_ignoring_wrong_line_amount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.receipt.create", "master.product.manage"]);

        long productId, rcId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
            productId = await productSvc.CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า RC D3", null, "GOOD", "ชิ้น", 50m,
                null, null, null, null, null, IsSaleable: true), default);
            var rcSvc = scope.ServiceProvider.GetRequiredService<IReceiptService>();
            var today = new SystemClock().TodayInBangkok();
            rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, "เดิม",
                Applications: [],
                Lines: [new ReceiptLineInput("เดิม", 1m, 100m, 100m, productId, null, "GOOD", "ชิ้น")]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("update_receipt_draft", new Dictionary<string, object?>
        {
            ["receiptId"] = rcId,
            ["request"] = new
            {
                docDate = today2, customerId = co.CustomerId, paymentMethod = "Cash",
                chequeNo = (string?)null, chequeDate = (DateOnly?)null, bankAccountId = (long?)null,
                currencyCode = "THB", exchangeRate = 1m, notes = "แก้ไขแล้ว", businessUnitId = (int?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "แก้ไขแล้ว", quantity = 3m, unitPrice = 60m,
                          amount = 180m, productType = "GOOD", uomText = "ชิ้น" },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rc = await db.Receipts.AsNoTracking().Include(r => r.Lines).FirstAsync(r => r.ReceiptId == rcId);
        rc.Notes.Should().Be("แก้ไขแล้ว");
        rc.Lines.Should().ContainSingle();
        rc.Amount.Should().Be(180m, "the header Amount must be the SUM of the (re-supplied) lines, never a client-trusted header total");
        rc.TotalAmount.Should().Be(180m);
    }

    [SkippableFact]
    public async Task D3_rc_update_after_post_throws_cannot_edit_after_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long rcId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var rcSvc = scope.ServiceProvider.GetRequiredService<IReceiptService>();
            var today = new SystemClock().TodayInBangkok();
            rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                Applications: [], Lines: [new ReceiptLineInput("x", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
            await rcSvc.PostAsync(rcId, default);
        }

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var svc = verifyScope.ServiceProvider.GetRequiredService<IReceiptService>();
        var today2 = new SystemClock().TodayInBangkok();
        var act = () => svc.UpdateDraftAsync(rcId, new CreateReceiptRequest(
            today2, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
            Applications: [], Lines: [new ReceiptLineInput("x", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("rc.cannot_edit_after_post");
    }

    [SkippableFact]
    public async Task D3_rc_update_cross_tenant_id_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long rcOfB;
        await using (var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, b.CompanyId, b.BranchId))
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var svcB = scopeB.ServiceProvider.GetRequiredService<IReceiptService>();
            var today = new SystemClock().TodayInBangkok();
            rcOfB = await svcB.CreateDraftAsync(new CreateReceiptRequest(
                today, b.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                Applications: [], Lines: [new ReceiptLineInput("x", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
        }

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId);
        await using var scopeA = spA.CreateAsyncScope();
        var svcA = scopeA.ServiceProvider.GetRequiredService<IReceiptService>();
        var today2 = new SystemClock().TodayInBangkok();
        var act = () => svcA.UpdateDraftAsync(rcOfB, new CreateReceiptRequest(
            today2, a.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
            Applications: [], Lines: [new ReceiptLineInput("x", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("rc.not_found");

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var grant = new NpgsqlCommand(
            "GRANT USAGE ON SCHEMA sales TO pg_database_owner; GRANT SELECT ON sales.receipts TO pg_database_owner;", conn))
            await grant.ExecuteNonQueryAsync();
        try
        {
            await using (var setRole = new NpgsqlCommand("SET ROLE pg_database_owner", conn))
                await setRole.ExecuteNonQueryAsync();
            await using (var pin = new NpgsqlCommand(
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), set_config('app.bypass_rls', 'false', false)", conn))
                await pin.ExecuteNonQueryAsync();
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM sales.receipts WHERE receipt_id = {rcOfB}", conn);
            (Convert.ToInt64(await cmd.ExecuteScalarAsync())).Should().Be(0);
        }
        finally
        {
            await using var reset = new NpgsqlCommand("RESET ROLE", conn);
            await reset.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task D3_rc_update_validation_failure_surfaces_field_errors()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.receipt.create"]);

        long rcId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var rcSvc = scope.ServiceProvider.GetRequiredService<IReceiptService>();
            var today = new SystemClock().TodayInBangkok();
            rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                Applications: [], Lines: [new ReceiptLineInput("x", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
        }

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today2 = new SystemClock().TodayInBangkok();
        // McpCreateReceiptRequest.Lines requires a resolvable productId (E2 list-only guard) —
        // an unknown productId is rejected before even reaching FluentValidation, proving the
        // same McpE2Exception-style field-error surfacing the create tool uses.
        var result = await client.CallToolAsync("update_receipt_draft", new Dictionary<string, object?>
        {
            ["receiptId"] = rcId,
            ["request"] = new
            {
                docDate = today2, customerId = co.CustomerId, paymentMethod = "Cash",
                chequeNo = (string?)null, chequeDate = (DateOnly?)null, bankAccountId = (long?)null,
                currencyCode = "THB", exchangeRate = 1m, notes = (string?)null, businessUnitId = (int?)null,
                lines = new[]
                {
                    new { productId = 999_999_999L, descriptionTh = "x", quantity = 1m,
                          unitPrice = 100m, amount = 100m, productType = "GOOD", uomText = (string?)null },
                },
            },
        });
        result.IsError.Should().BeTrue("an unknown productId must be rejected by the E2 list-only guard");
    }

    [SkippableFact]
    public async Task D3_rc_race_backstop_trigger_aborts_stale_header_write_after_concurrent_post()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long rcId;
        await using (var seedSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var seedScope = seedSp.CreateAsyncScope())
        {
            var svc = seedScope.ServiceProvider.GetRequiredService<IReceiptService>();
            var today = new SystemClock().TodayInBangkok();
            rcId = await svc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, "race",
                Applications: [], Lines: [new ReceiptLineInput("race", 1m, 100m, 100m, null, null, "GOOD", null)]), default);
        }

        await using var raceSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var raceScope = raceSp.CreateAsyncScope();
        var raceDb = raceScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var staleRc = await raceDb.Receipts.Include(r => r.Lines).FirstAsync(r => r.ReceiptId == rcId);
        staleRc.Status.Should().Be(Accounting.Domain.Enums.DocumentStatus.Draft);

        await using (var postSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var postScope = postSp.CreateAsyncScope())
        {
            var postSvc = postScope.ServiceProvider.GetRequiredService<IReceiptService>();
            await postSvc.PostAsync(rcId, default);
        }

        // Receipt header trigger 570 only freezes a NAMED critical-field allowlist (doc_no/
        // customer_id/amount/... — NOT every column, mirrors TI's 583), so a bare Notes-only
        // write would NOT trip it. The uniform backstop is the HARD REQUIREMENT itself: always
        // delete-and-recreate Lines — replay that against the STALE (still-Draft-in-memory)
        // tracked entity and trigger 582 (fn_receipt_lines_immutable) must abort it.
        raceDb.RemoveRange(staleRc.Lines);
        staleRc.Lines.Clear();
        var act = () => raceDb.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23514",
                "trigger 582 (fn_receipt_lines_immutable) must abort a line DELETE against a now-POSTED receipt");

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var posted = await verifyDb.Receipts.AsNoTracking().Include(r => r.Lines).FirstAsync(r => r.ReceiptId == rcId);
        posted.Status.Should().Be(Accounting.Domain.Enums.DocumentStatus.Posted);
        posted.Lines.Should().ContainSingle();
    }
}
