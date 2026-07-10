using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Application.Expense;
using Accounting.Application.FixedAsset;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// mcp-expansion-v2 — bank reconciliation (read-only) + expense claims (read+draft) +
/// fixed assets (read+draft) + the employee master lookup expense-claim drafting needs.
/// Reuses the <see cref="McpApiFactory"/> + mcp-kind key minting pattern from
/// McpServerSmokeTests.cs / McpWriteExpansionTests.cs. Every test uses a fresh
/// <see cref="TestCompanyFactory"/> company for isolation (company 1 accumulates state
/// across the whole suite).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpBankExpenseFixedAssetTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpBankExpenseFixedAssetTests(PostgresFixture fx) => _fx = fx;

    private static readonly string[] FullScopes =
    [
        "bank.account.read", "bank.report.read",
        "expense.claim.read", "expense.claim.create",
        "master.employee.manage",
        "fixedasset.read", "fixedasset.manage",
    ];

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

    private static bool IsNullResult(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault() is not { } block
        || block.Text == "null";

    private async Task<string> MintKeyAsync(int companyId, int branchId, IReadOnlyList<string>? scopes = null)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-bx"), scopes ?? FullScopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    // ── seed helpers (all scoped to the caller-supplied company/branch) ───────

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

    private async Task<int> SeedExpenseCategoryAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new Accounting.Domain.Entities.Sys.ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวด MCP", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return cat.CategoryId;
    }

    private async Task<int> SeedBankAccountAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBankAccountService>();
        return await svc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-" + TestIds.Suffix(), null, null, null, "THB"), default);
    }

    private static object FixedAssetRequest(DateOnly acquireDate, decimal cost = 5000m, decimal salvage = 0m) => new
    {
        name = "เครื่องพิมพ์ MCP", category = "EQUIPMENT", acquireDate,
        vendorInvoiceId = (long?)null, cost, salvageValue = salvage, usefulLifeMonths = 12,
        depreciationStartDate = (DateOnly?)null, assetCostAccountId = (long?)null,
        accumDepAccountId = (long?)null, depExpenseAccountId = (long?)null,
        notes = (string?)null, businessUnitId = (int?)null,
    };

    // ══════════════════════ tool inventory ══════════════════════

    [SkippableFact]
    public async Task Mcp_lists_bank_expense_fixed_asset_tools_with_valid_key()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintKeyAsync(companyId: 1, branchId: 1);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        names.Should().Contain(new[]
        {
            "list_bank_accounts", "get_bank_reconciliation_report",
            "list_employees",
            "create_expense_claim_draft", "update_expense_claim_draft",
            "list_expense_claims", "get_expense_claim",
            "create_fixed_asset_draft", "update_fixed_asset_draft",
            "list_fixed_assets", "get_fixed_asset",
            "get_fixed_asset_register", "get_accumulated_depreciation_report",
            "list_depreciation_runs",
        });

        // HARD INVARIANT — no state-changing verb tool is exposed for these three domains.
        var actionVerbs = new[] { "activate", "dispose", "writeoff", "generate", "approve", "submit", "reject", "cancel" };
        names.Where(n => n.Contains("bank") || n.Contains("expense") || n.Contains("fixed_asset") || n.Contains("employee") || n.Contains("depreciation"))
            .Should().NotContain(n => n.Split('_').Any(tok => actionVerbs.Contains(tok)),
                "no state-changing tool may be exposed for bank/expense/fixed-asset — agents only draft/read");
        names.Should().NotContain("pay_expense_claim");
    }

    // ══════════════════════ bank reconciliation (read-only) ══════════════════════

    [SkippableFact]
    public async Task Bank_list_bank_accounts_returns_seeded_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var bankAccountId = await SeedBankAccountAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["bank.account.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_bank_accounts", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        root.EnumerateArray().Should().Contain(e =>
            e.GetProperty("bankAccountId").GetInt32() == bankAccountId
            && e.GetProperty("bankCode").GetString() == "KBANK"
            && e.GetProperty("isActive").GetBoolean());
    }

    [SkippableFact]
    public async Task Bank_get_reconciliation_report_returns_zero_diff_for_empty_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var bankAccountId = await SeedBankAccountAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["bank.report.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await client.CallToolAsync("get_bank_reconciliation_report", new Dictionary<string, object?>
        {
            ["bankAccountId"] = bankAccountId,
            ["fromDate"] = new DateOnly(today.Year, today.Month, 1),
            ["toDate"] = today,
        });

        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        root.GetProperty("statementClosingBalance").GetDecimal().Should().Be(0m);
        root.GetProperty("glBalance").GetDecimal().Should().Be(0m);
        root.GetProperty("difference").GetDecimal().Should().Be(0m);
    }

    [SkippableFact]
    public async Task Bank_get_reconciliation_report_unknown_account_is_rejected()
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
            ["bankAccountId"] = 999_999_999, ["fromDate"] = today, ["toDate"] = today,
        });

        result.IsError.Should().BeTrue("an unknown bankAccountId must be rejected (bank_account.not_found)");
    }

    // ══════════════════════ employees (master-data lookup) ══════════════════════

    [SkippableFact]
    public async Task Employee_list_employees_returns_active_only_by_default()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var activeId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        var inactiveId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
            await svc.DeactivateAsync(inactiveId, default);
        }
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["master.employee.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var defaultResult = await client.CallToolAsync("list_employees", new Dictionary<string, object?>());
        defaultResult.IsError.Should().NotBe(true);
        var defaultIds = ResultRoot(defaultResult).EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetInt64()).ToHashSet();
        defaultIds.Should().Contain(activeId);
        defaultIds.Should().NotContain(inactiveId, "list_employees defaults to active-only (spec: id/code/Thai name/active only)");

        var allResult = await client.CallToolAsync("list_employees",
            new Dictionary<string, object?> { ["includeInactive"] = true });
        var allIds = ResultRoot(allResult).EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetInt64()).ToHashSet();
        allIds.Should().Contain(inactiveId);
        // Payroll PII must not leak through this projection.
        ResultRoot(allResult).EnumerateArray().First().TryGetProperty("nationalId", out _).Should().BeFalse();
        ResultRoot(allResult).EnumerateArray().First().TryGetProperty("baseSalary", out _).Should().BeFalse();
    }

    // ══════════════════════ expense claims (read + draft) ══════════════════════

    [SkippableFact]
    public async Task ExpenseClaim_create_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var employeeId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["expense.claim.create", "master.employee.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new
        {
            employeeId, claimDate = today, title = "ค่าเดินทาง MCP", notes = (string?)null,
            businessUnitId = (int?)null,
            lines = new[]
            {
                new { expenseCategoryId = catId, expenseAccountId = (long?)null, description = "แท็กซี่",
                      expenseDate = (DateOnly?)null, amount = 1000m, taxCodeId = (int?)null,
                      vatRate = 0m, isRecoverableVat = false },
            },
        };

        var result = await client.CallToolAsync(
            "create_expense_claim_draft", new Dictionary<string, object?> { ["request"] = request });

        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        var id = root.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);
        root.GetProperty("approvalUrl").GetString().Should().Be($"http://localhost:3000/expense-claims/{id}?action=approve");
    }

    [SkippableFact]
    public async Task ExpenseClaim_create_draft_unknown_employee_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId);
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

        result.IsError.Should().BeTrue("unknown employeeId must be rejected by the GuardEmployeeAsync require-list guard");
    }

    [SkippableFact]
    public async Task ExpenseClaim_update_draft_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var employeeId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId);

        long claimId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            claimId = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
                employeeId, DateOnly.FromDateTime(DateTime.UtcNow), "เดิม", null,
                [new ExpenseClaimLineInput(catId, null, "เดิม", null, 500m, null, 0m, false)]), default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["expense.claim.create", "master.employee.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await client.CallToolAsync("update_expense_claim_draft", new Dictionary<string, object?>
        {
            ["expenseClaimId"] = claimId,
            ["request"] = new
            {
                employeeId, claimDate = today, title = "แก้ไขแล้ว", notes = (string?)null,
                businessUnitId = (int?)null,
                lines = new[]
                {
                    new { expenseCategoryId = catId, expenseAccountId = (long?)null, description = "แก้ไขแล้ว",
                          expenseDate = (DateOnly?)null, amount = 750m, taxCodeId = (int?)null,
                          vatRate = 0m, isRecoverableVat = false },
                },
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var verifySvc = verifyScope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var detail = await verifySvc.GetDetailAsync(claimId, default);
        detail!.Title.Should().Be("แก้ไขแล้ว");
        detail.Lines.Should().ContainSingle(l => l.Amount == 750m);
    }

    [SkippableFact]
    public async Task ExpenseClaim_update_draft_after_submit_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var employeeId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId);

        long claimId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            claimId = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
                employeeId, DateOnly.FromDateTime(DateTime.UtcNow), "เดิม", null,
                [new ExpenseClaimLineInput(catId, null, "เดิม", null, 500m, null, 0m, false)]), default);
            await svc.SubmitAsync(claimId, default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["expense.claim.create", "master.employee.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("update_expense_claim_draft", new Dictionary<string, object?>
        {
            ["expenseClaimId"] = claimId,
            ["request"] = new
            {
                employeeId, claimDate = DateOnly.FromDateTime(DateTime.UtcNow), title = "ไม่ควรสำเร็จ",
                notes = (string?)null, businessUnitId = (int?)null,
                lines = new[]
                {
                    new { expenseCategoryId = catId, expenseAccountId = (long?)null, description = "X",
                          expenseDate = (DateOnly?)null, amount = 500m, taxCodeId = (int?)null,
                          vatRate = 0m, isRecoverableVat = false },
                },
            },
        });

        result.IsError.Should().BeTrue("editing a Submitted (non-Draft/Rejected) claim must throw expense_claim.not_editable");
    }

    [SkippableFact]
    public async Task ExpenseClaim_list_and_get_round_trip()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var employeeId = await SeedEmployeeAsync(co.CompanyId, co.BranchId);
        var catId = await SeedExpenseCategoryAsync(co.CompanyId, co.BranchId);

        long claimId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            claimId = await svc.CreateDraftAsync(new CreateExpenseClaimRequest(
                employeeId, DateOnly.FromDateTime(DateTime.UtcNow), "รอบทดสอบ", null,
                [new ExpenseClaimLineInput(catId, null, "X", null, 500m, null, 0m, false)]), default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["expense.claim.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var listResult = await client.CallToolAsync("list_expense_claims",
            new Dictionary<string, object?> { ["employeeId"] = employeeId });
        listResult.IsError.Should().NotBe(true);
        ResultRoot(listResult).EnumerateArray()
            .Should().Contain(e => e.GetProperty("expenseClaimId").GetInt64() == claimId);

        var getResult = await client.CallToolAsync("get_expense_claim",
            new Dictionary<string, object?> { ["id"] = claimId });
        getResult.IsError.Should().NotBe(true);
        ResultRoot(getResult).GetProperty("title").GetString().Should().Be("รอบทดสอบ");

        var missing = await client.CallToolAsync("get_expense_claim",
            new Dictionary<string, object?> { ["id"] = 999_999_999L });
        IsNullResult(missing).Should().BeTrue("an unknown expense claim id must return null, not throw");
    }

    // ══════════════════════ fixed assets (read + draft) ══════════════════════

    [SkippableFact]
    public async Task FixedAsset_create_draft_returns_id_and_approval_url()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await client.CallToolAsync("create_fixed_asset_draft",
            new Dictionary<string, object?> { ["request"] = FixedAssetRequest(today) });

        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        var id = root.GetProperty("id").GetInt64();
        id.Should().BeGreaterThan(0);
        root.GetProperty("approvalUrl").GetString().Should().Be($"http://localhost:3000/fixed-assets/{id}?action=approve");
    }

    [SkippableFact]
    public async Task FixedAsset_create_draft_invalid_salvage_exceeds_cost_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // SalvageValue (6000) > Cost (5000) — CreateFixedAssetValidator rejects this.
        var result = await client.CallToolAsync("create_fixed_asset_draft",
            new Dictionary<string, object?> { ["request"] = FixedAssetRequest(today, cost: 5000m, salvage: 6000m) });

        result.IsError.Should().BeTrue("SalvageValue > Cost must fail CreateFixedAssetValidator");
    }

    [SkippableFact]
    public async Task FixedAsset_update_draft_persists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        long assetId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            assetId = await svc.CreateDraftAsync(new CreateFixedAssetRequest(
                "เดิม", "EQUIPMENT", today, null, 5000m, 0m, 12, today, null, null, null, null), default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("update_fixed_asset_draft", new Dictionary<string, object?>
        {
            ["fixedAssetId"] = assetId,
            ["request"] = new
            {
                name = "แก้ไขแล้ว", category = "EQUIPMENT", acquireDate = today,
                vendorInvoiceId = (long?)null, cost = 8000m, salvageValue = 0m, usefulLifeMonths = 24,
                depreciationStartDate = (DateOnly?)null, assetCostAccountId = (long?)null,
                accumDepAccountId = (long?)null, depExpenseAccountId = (long?)null,
                notes = (string?)null, businessUnitId = (int?)null,
            },
        });
        result.IsError.Should().NotBe(true);

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var verifySvc = verifyScope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var detail = await verifySvc.GetDetailAsync(assetId, default);
        detail!.Name.Should().Be("แก้ไขแล้ว");
        detail.Cost.Should().Be(8000m);
    }

    [SkippableFact]
    public async Task FixedAsset_update_draft_after_activate_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        long assetId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            assetId = await svc.CreateDraftAsync(new CreateFixedAssetRequest(
                "เดิม", "EQUIPMENT", today, null, 5000m, 0m, 12, today, null, null, null, null), default);
            await svc.ActivateAsync(assetId, default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("update_fixed_asset_draft", new Dictionary<string, object?>
        {
            ["fixedAssetId"] = assetId,
            ["request"] = new
            {
                name = "ไม่ควรสำเร็จ", category = "EQUIPMENT", acquireDate = today,
                vendorInvoiceId = (long?)null, cost = 5000m, salvageValue = 0m, usefulLifeMonths = 12,
                depreciationStartDate = (DateOnly?)null, assetCostAccountId = (long?)null,
                accumDepAccountId = (long?)null, depExpenseAccountId = (long?)null,
                notes = (string?)null, businessUnitId = (int?)null,
            },
        });

        result.IsError.Should().BeTrue("editing an Active (non-Draft) fixed asset must throw fixed_asset.not_editable");
    }

    [SkippableFact]
    public async Task FixedAsset_list_and_get_round_trip()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        long assetId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            assetId = await svc.CreateDraftAsync(new CreateFixedAssetRequest(
                "รอบทดสอบ", "EQUIPMENT", today, null, 5000m, 0m, 12, today, null, null, null, null), default);
        }

        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var listResult = await client.CallToolAsync("list_fixed_assets", new Dictionary<string, object?>());
        listResult.IsError.Should().NotBe(true);
        ResultRoot(listResult).EnumerateArray()
            .Should().Contain(e => e.GetProperty("fixedAssetId").GetInt64() == assetId);

        var getResult = await client.CallToolAsync("get_fixed_asset",
            new Dictionary<string, object?> { ["id"] = assetId });
        getResult.IsError.Should().NotBe(true);
        ResultRoot(getResult).GetProperty("name").GetString().Should().Be("รอบทดสอบ");

        var missing = await client.CallToolAsync("get_fixed_asset",
            new Dictionary<string, object?> { ["id"] = 999_999_999L });
        IsNullResult(missing).Should().BeTrue("an unknown fixed asset id must return null, not throw");
    }

    [SkippableFact]
    public async Task FixedAsset_register_and_accumulated_depreciation_reports_round_trip()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var registerResult = await client.CallToolAsync("get_fixed_asset_register", new Dictionary<string, object?>());
        registerResult.IsError.Should().NotBe(true);
        ResultRoot(registerResult).ValueKind.Should().Be(JsonValueKind.Array);

        var depResult = await client.CallToolAsync(
            "get_accumulated_depreciation_report", new Dictionary<string, object?>());
        depResult.IsError.Should().NotBe(true);
        ResultRoot(depResult).ValueKind.Should().Be(JsonValueKind.Array);
    }

    [SkippableFact]
    public async Task FixedAsset_list_depreciation_runs_returns_empty_when_none_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["fixedasset.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("list_depreciation_runs", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true);
        ResultRoot(result).EnumerateArray().Should().BeEmpty();
    }
}
