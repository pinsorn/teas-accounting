using System.Collections.Concurrent;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Tax;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// specs/mcp-error-surfacing.md — gates for (1) the central call-tool error-surfacing
/// filter (business exceptions reach the client as readable text, not the SDK's generic
/// "An error occurred invoking '...'." swallow) and (2) the 4 new read-only master-data
/// resolver tools (list_tax_codes/list_wht_types/list_expense_categories/list_business_units).
/// Reuses the <see cref="McpApiFactory"/> + mcp-kind key minting pattern from
/// McpServerSmokeTests.cs. Every test uses a FRESH <see cref="TestCompanyFactory"/> company
/// for isolation (company 1 accumulates state across the whole suite).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpErrorSurfacingTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpErrorSurfacingTests(PostgresFixture fx) => _fx = fx;

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

    private static string ErrorText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private async Task<string> MintKeyAsync(int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-errsurf"), scopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    // ── seed helpers (all scoped to the caller-supplied company/branch) ───────

    private async Task<long> SeedCustomerAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        return await svc.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า MCP errsurf", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);
    }

    private async Task<long> SeedProductAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProductService>();
        return await svc.CreateAsync(new CreateProductRequest(
            TestIds.ProductCode(), "บริการ MCP errsurf", null, "SERVICE",
            "ครั้ง", DefaultUnitPrice: 100m, null, null, null, null, null,
            IsSaleable: true), default);
    }

    private async Task<long> SeedEmployeeAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var nationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString();
        return await svc.CreateAsync(new CreateEmployeeRequest(
            $"EMP-{TestIds.Suffix()}", null, "ทดสอบ", "MCP", null, null, null,
            nationalId, null, null,
            new DateOnly(2024, 1, 1), null, 20000m,
            null, null, null, false, null,
            "SINGLE", false, 0), default);
    }

    private async Task<int> SeedExpenseCategoryAsync(int companyId, int branchId, string code)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
        return await svc.CreateAsync(new CreateExpenseCategoryRequest(
            code, "หมวด MCP errsurf", null, null,
            null, null, DefaultIsRecoverableVat: true, null, false, false, null), default);
    }

    private async Task<int> SeedBusinessUnitAsync(int companyId, int branchId, string code)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        return await svc.CreateAsync(new CreateBusinessUnitRequest(code, "BU MCP errsurf", null, null), default);
    }

    private async Task SetRequiresBusinessUnitAsync(int companyId, int branchId, bool value)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        await svc.SetCompanyRequiresBuAsync(value, default);
    }

    private static object TaxInvoiceRequest(DateOnly docDate, long customerId, long productId, int? businessUnitId = null, bool emptyLines = false) => new
    {
        docDate, customerId, isTaxInclusive = false,
        currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
        paymentTerms = (string?)null, dueDate = (DateOnly?)null, businessUnitId,
        quotationId = (long?)null,
        lines = emptyLines ? Array.Empty<object>() : new object[]
        {
            new { productId, descriptionTh = "X", quantity = 1m,
                  uomId = 1, uomText = "ครั้ง", unitPrice = 100m, discountPercent = 0m,
                  taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
        },
    };

    // ══════════════════════ (1) error-surfacing filter — gates (a)-(c) ══════════════════════

    // (a) create_tax_invoice_draft on a non-VAT-registered company → IsError=true AND the
    // content text contains the ม.86/4 VAT-not-registered domain message.
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_on_non_vat_company_surfaces_the_domain_rule_message()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = TaxInvoiceRequest(today, customerId, productId) });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.domain_rule]")
            .And.Contain("VAT-not-registered", "the filter must forward DomainException.Message verbatim, not the SDK's generic swallow");
    }

    // Tier-2 review fix (2026-07-13): a caught business exception must still leave a
    // server-side log record (spec §1 "Server-side logging must stay") even though the
    // filter swallows it into a friendly, non-throwing CallToolResult — the SDK's own
    // "unhandled exception" log line never fires for these 4 classes precisely BECAUSE the
    // filter is inside the SDK's built-in catch-all and stops the exception from propagating
    // that far. Pins the regression class the reviewer flagged, via a captured ILoggerProvider
    // layered onto the shared McpApiFactory with WithWebHostBuilder (no shared-fixture edit).
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_on_non_vat_company_still_logs_server_side()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);

        var logs = new ConcurrentQueue<CapturedLog>();
        await using var baseFactory = new McpApiFactory(_fx.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(b =>
            b.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(logs))));
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = TaxInvoiceRequest(today, customerId, productId) });
        result.IsError.Should().BeTrue();

        logs.Should().Contain(l =>
            l.Category == "Accounting.Api.Mcp.McpErrorSurfacingFilter"
            && l.Level == LogLevel.Warning
            && l.Message.Contains("create_tax_invoice_draft")
            && l.Message.Contains("VAT-not-registered"),
            "the filter must log a Warning server-side even though the exception never reaches " +
            "the SDK's own built-in catch-all/log line (it's swallowed into a friendly result first)");
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider(ConcurrentQueue<CapturedLog> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception));
    }

    // (b) create_expense_claim_draft with a bogus employeeId → content contains
    // [mcp.employee_required] (McpE2Exception forwarded verbatim).
    [SkippableFact]
    public async Task CreateExpenseClaimDraft_unknown_employee_surfaces_the_mcpe2_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId, TestIds.ExpenseCategoryCode());
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["expense.claim.create", "master.employee.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            employeeId = 999_999_999L, claimDate = today, title = "X", notes = (string?)null,
            businessUnitId = (int?)null,
            lines = new[]
            {
                new { expenseCategoryId = catId, expenseAccountId = (long?)null, description = "X",
                      expenseDate = (DateOnly?)null, amount = 100m, taxCodeId = (int?)null,
                      vatRate = 0m, isRecoverableVat = false },
            },
        };

        var result = await client.CallToolAsync(
            "create_expense_claim_draft", new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.employee_required]");
    }

    // (c) tax-invoice line with "uomId": null → JsonException from argument binding reaches
    // the filter; content text contains the JSON path.
    //
    // IMPORTANT test-harness footgun (do NOT pass a C# anonymous object with a null-valued
    // property here): ModelContextProtocol.Client's McpClient.CallToolAsync serializes each
    // dictionary argument via McpJsonUtilities.DefaultOptions, which sets
    // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull (confirmed by decompiling
    // ModelContextProtocol.Core 1.4.0). An anonymous object like `uomId = (int?)null` is
    // therefore OMITTED from the outgoing wire JSON entirely — the server sees a MISSING
    // key, and System.Text.Json's parameterless-default constructor-parameter binding
    // silently substitutes default(int) = 0 for a missing (not explicitly-null) argument. No
    // exception is thrown in that path — a first attempt at this test asserted IsError and
    // failed, because it went through that lenient "missing key" path, not the "explicit
    // null" path prod actually hits.
    // The REAL prod client (Claude's own MCP client) sends the LLM's literal JSON verbatim,
    // including an explicit `"uomId": null` token — a PRESENT key whose value genuinely is
    // JSON null. Converting JsonValueKind.Null to System.Int32 is a hard STJ conversion
    // failure (regardless of "required" tracking), which DOES throw JsonException. To
    // reproduce that exact wire shape, build the JSON by hand and pass a pre-parsed
    // JsonElement as the argument value — CallToolAsync's ToArgumentsDictionary uses a
    // JsonElement argument AS-IS (no re-serialization), bypassing the WhenWritingNull
    // omission entirely, matching what a raw client actually puts on the wire.
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_explicit_null_uomId_surfaces_the_json_path()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var requestJson = $$"""
            {
                "docDate": "{{today:yyyy-MM-dd}}", "customerId": {{customerId}}, "isTaxInclusive": false,
                "currencyCode": "THB", "exchangeRate": 1, "notes": null,
                "paymentTerms": null, "dueDate": null, "businessUnitId": null, "quotationId": null,
                "lines": [
                    { "productId": {{productId}}, "descriptionTh": "X", "quantity": 1,
                      "uomId": null, "uomText": "ครั้ง", "unitPrice": 100, "discountPercent": 0,
                      "taxCodeId": 0, "taxCode": "NONE", "taxRate": 0, "productType": null }
                ]
            }
            """;
        using var requestDoc = JsonDocument.Parse(requestJson);

        var result = await client.CallToolAsync("create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = requestDoc.RootElement.Clone() });

        result.IsError.Should().BeTrue(
            "an EXPLICIT JSON null against the non-nullable int UomId parameter must fail conversion");
        var text = ErrorText(result);
        text.Should().StartWith("[mcp.bad_input]");
        text.Should().Contain("uomId", "the JsonException.Message must embed the JSON path so the agent can self-correct");
    }

    // WP-E3 (specs/fix-army-findings-2026-07-22.md) — args not wrapped in the schema's `request`
    // object (a flat DTO instead of the nested shape every create_*/update_* tool advertises)
    // throws a plain System.ArgumentException from the MCP SDK's own argument-binding layer,
    // previously uncaught by this filter and swallowed into the SDK's generic
    // "An error occurred invoking '<tool>'." — misled a whole army test leg into a false
    // CRITICAL (root-caused via prod log 2026-07-25: the write path itself works fine, verified
    // by a live probe with the correctly-nested payload).
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_args_not_wrapped_in_request_surfaces_mcp_arguments()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        // Flat DTO fields at the TOP level (no "request" wrapper) — exactly the malformed shape
        // an agent sent in the live army-test repro; the tool's schema requires a nested `request`.
        var flatArgs = TaxInvoiceRequest(today, customerId, productId);
        var flatDict = JsonSerializer.SerializeToElement(flatArgs).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

        var result = await client.CallToolAsync("create_tax_invoice_draft", flatDict);

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().StartWith("[mcp.arguments]",
            "a malformed tools/call (missing the nested `request` object) must surface a clean, " +
            "actionable message instead of the SDK's generic \"An error occurred invoking\" swallow");
    }

    // ── extra scenarios (coordinator note 2026-07-12) — mirrors real prod exceptions ──

    // McpE2Exception [mcp.pdf_not_posted] — pdf-url tool called on a still-DRAFT document.
    [SkippableFact]
    public async Task GetTaxInvoicePdfUrl_on_a_draft_surfaces_pdf_not_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create", "sales.tax_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var createResult = await client.CallToolAsync("create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = TaxInvoiceRequest(today, customerId, productId) });
        createResult.IsError.Should().NotBe(true);
        var id = ResultRoot(createResult).GetProperty("id").GetInt64();

        var result = await client.CallToolAsync("get_tax_invoice_pdf_url",
            new Dictionary<string, object?> { ["id"] = id });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.pdf_not_posted]");
    }

    // DomainException "Business Unit is required for this company" — company opted into
    // requires_business_unit; draft omits businessUnitId.
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_missing_business_unit_surfaces_the_bu_required_message()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await SetRequiresBusinessUnitAsync(co.CompanyId, co.BranchId, true);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var productId = await SeedProductAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("create_tax_invoice_draft",
            new Dictionary<string, object?> { ["request"] = TaxInvoiceRequest(today, customerId, productId, businessUnitId: null) });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.domain_rule]")
            .And.Contain("Business Unit is required for this company");
    }

    // DomainException "Bank account N not found" — get_bank_reconciliation_report with an
    // unknown bankAccountId.
    [SkippableFact]
    public async Task GetBankReconciliationReport_unknown_bank_account_surfaces_the_not_found_message()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["bank.report.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await client.CallToolAsync("get_bank_reconciliation_report", new Dictionary<string, object?>
        {
            ["bankAccountId"] = 999_999_999, ["fromDate"] = today.AddDays(-30), ["toDate"] = today,
        });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.domain_rule]")
            .And.Contain("Bank account 999999999 not found");
    }

    // FluentValidation.ValidationException — create_tax_invoice_draft with an empty Lines
    // array (RuleFor(x => x.Lines).NotEmpty() in CreateTaxInvoiceValidator).
    [SkippableFact]
    public async Task CreateTaxInvoiceDraft_empty_lines_surfaces_the_validation_message()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var customerId = await SeedCustomerAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("create_tax_invoice_draft", new Dictionary<string, object?>
        {
            ["request"] = TaxInvoiceRequest(today, customerId, productId: 0, emptyLines: true),
        });

        result.IsError.Should().BeTrue();
        ErrorText(result).Should().Contain("[mcp.validation]")
            .And.Contain("Lines", "FluentValidation's ValidationFailure.PropertyName for RuleFor(x => x.Lines).NotEmpty()");
    }

    // ══════════════════════ (2) master-data resolver tools — gate (d), tenancy ══════════════════════

    [SkippableFact]
    public async Task ListTaxCodes_returns_seeded_rows_and_excludes_other_company_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // ICompanyService.CreateAsync auto-seeds the SAME default tax-code set for every
        // company (DefaultTaxCodes in MasterDataServices.cs) — same Code strings, distinct
        // auto-increment TaxCodeId PKs. A leak would surface as an id belonging to co2.
        var co1 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co2 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co1.CompanyId, co1.BranchId, ["purchase.vendor_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_tax_codes", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var returnedIds = ResultRoot(result).EnumerateArray()
            .Select(e => e.GetProperty("taxCodeId").GetInt32()).ToHashSet();

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co1.CompanyId, co1.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var co1Ids = await db.TaxCodes.AsNoTracking()
            .Where(c => c.CompanyId == co1.CompanyId && c.IsActive)
            .Select(c => c.TaxCodeId).ToListAsync();
        var co2Ids = await db.TaxCodes.AsNoTracking()
            .Where(c => c.CompanyId == co2.CompanyId)
            .Select(c => c.TaxCodeId).ToListAsync();

        returnedIds.Should().NotBeEmpty("company creation seeds the default tax-code set");
        returnedIds.Should().BeEquivalentTo(co1Ids, "must return exactly co1's own seeded rows");
        returnedIds.Should().NotIntersectWith(co2Ids, "must never leak another company's rows");
    }

    [SkippableFact]
    public async Task ListWhtTypes_returns_seeded_rows_and_excludes_other_company_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // ICompanyService.CreateAsync auto-seeds the same 13 default WHT types per company.
        var co1 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co2 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co1.CompanyId, co1.BranchId, ["purchase.vendor_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_wht_types", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var returnedIds = ResultRoot(result).EnumerateArray()
            .Select(e => e.GetProperty("whtTypeId").GetInt32()).ToHashSet();

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co1.CompanyId, co1.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var co1Ids = await db.WhtTypes.AsNoTracking()
            .Where(w => w.CompanyId == co1.CompanyId && w.IsActive)
            .Select(w => w.WhtTypeId).ToListAsync();
        var co2Ids = await db.WhtTypes.AsNoTracking()
            .Where(w => w.CompanyId == co2.CompanyId)
            .Select(w => w.WhtTypeId).ToListAsync();

        returnedIds.Should().NotBeEmpty("company creation seeds the default WHT-type set");
        returnedIds.Should().BeEquivalentTo(co1Ids, "must return exactly co1's own seeded rows");
        returnedIds.Should().NotIntersectWith(co2Ids, "must never leak another company's rows");
    }

    [SkippableFact]
    public async Task ListExpenseCategories_returns_seeded_rows_and_excludes_other_company_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co1 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co2 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co1CatId = await SeedExpenseCategoryAsync(co1.CompanyId, co1.BranchId, TestIds.ExpenseCategoryCode());
        var co2CatId = await SeedExpenseCategoryAsync(co2.CompanyId, co2.BranchId, TestIds.ExpenseCategoryCode());
        var key = await MintKeyAsync(co1.CompanyId, co1.BranchId, ["purchase.vendor_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_expense_categories", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var returnedIds = ResultRoot(result).EnumerateArray()
            .Select(e => e.GetProperty("categoryId").GetInt32()).ToHashSet();

        returnedIds.Should().Contain(co1CatId, "must return co1's own seeded category");
        returnedIds.Should().NotContain(co2CatId, "must never leak another company's category");
    }

    [SkippableFact]
    public async Task ListBusinessUnits_returns_seeded_rows_and_excludes_other_company_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co1 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co2 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co1BuId = await SeedBusinessUnitAsync(co1.CompanyId, co1.BranchId, TestIds.BusinessUnitCode());
        var co2BuId = await SeedBusinessUnitAsync(co2.CompanyId, co2.BranchId, TestIds.BusinessUnitCode());
        var key = await MintKeyAsync(co1.CompanyId, co1.BranchId, ["purchase.vendor_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_business_units", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var returnedIds = ResultRoot(result).EnumerateArray()
            .Select(e => e.GetProperty("businessUnitId").GetInt32()).ToHashSet();

        returnedIds.Should().Contain(co1BuId, "must return co1's own seeded business unit");
        returnedIds.Should().NotContain(co2BuId, "must never leak another company's business unit");
    }
}
