using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
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
/// specs/mcp-manual-journal.md — create_manual_journal_draft. Wraps the EXISTING draft seam
/// (POST /journals/, JournalService.CreateDraftAsync, gl.journal.create) — the tool NEVER
/// posts; a human reviews and posts separately in the UI (gl.journal.post is structurally
/// excluded from every MCP credential, see McpScopes.ForbiddenSuffixes / T5 below).
/// Reuses the McpApiFactory + mcp-kind key minting pattern from McpErrorSurfacingTests.cs.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpManualJournalTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpManualJournalTests(PostgresFixture fx) => _fx = fx;

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
            TestIds.Name("mcp-jv"), scopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    /// <summary>Standard seeded COA codes every TestCompanyFactory company gets (5200 expense,
    /// 1110 cash) — same codes McpWriteExpansionTests/McpReadExpansionTests already rely on.</summary>
    private async Task<(long ExpenseAccountId, long CashAccountId)> SeedAccountsAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var exp = await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cash = await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == "1110")
            .Select(a => a.AccountId).FirstAsync();
        return (exp, cash);
    }

    private static object BalancedRequest(DateOnly docDate, long debitAccountId, long creditAccountId, decimal amount, string description) => new
    {
        docDate, description,
        lines = new[]
        {
            new { accountId = debitAccountId, debitAmount = amount, creditAmount = 0m, memo = (string?)null },
            new { accountId = creditAccountId, debitAmount = 0m, creditAmount = amount, memo = (string?)null },
        },
    };

    // T1 — happy path: balanced 2-line JV via the tool creates an UNPOSTED draft (GL unmoved);
    // a subsequent human-path post (direct service call — the agent never calls PostAsync) then
    // assigns a document number.
    [SkippableFact]
    public async Task T1_happy_creates_unposted_draft_then_human_path_post_moves_gl()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (expAcctId, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = BalancedRequest(today, expAcctId, cashAcctId, 100m, "MCP JV T1"),
        });
        result.IsError.Should().NotBe(true);
        var root = ResultRoot(result);
        root.GetProperty("status").GetString().Should().Be("Draft");
        root.GetProperty("totalDebit").GetDecimal().Should().Be(100m);
        root.GetProperty("totalCredit").GetDecimal().Should().Be(100m);
        // R4 — the response reports the EFFECTIVE (server-pinned) date, not just via a DB query.
        root.GetProperty("effectiveDocDate").GetString().Should().Be(today.ToString("yyyy-MM-dd"));
        var draftId = root.GetProperty("draftId").GetInt64();

        // Draft-create time: no document number, no PostedAt — GL balances untouched.
        await using (var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var verifyScope = verifySp.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == draftId);
            je.DocNo.Should().BeNull("a draft carries no document number until a human posts it");
            je.PostedAt.Should().BeNull("GL balances must stay unmoved at draft-create time");
        }

        // Human-path post: a direct service call, mirroring a human clicking "Post" in the UI —
        // the agent itself never calls this (no post tool is exposed).
        await using var postSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var postScope = postSp.CreateAsyncScope();
        var journalSvc = postScope.ServiceProvider.GetRequiredService<IJournalService>();
        var posted = await journalSvc.PostAsync(draftId, default);
        posted.DocNo.Should().NotBeNullOrWhiteSpace("the human-path post must now assign a document number");
    }

    // T2 — unbalanced lines → structured [mcp.validation] error (CreateJournalValidator's
    // Dr==Cr rule), never a raw 500; nothing persisted.
    [SkippableFact]
    public async Task T2_unbalanced_lines_surfaces_structured_error_and_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (expAcctId, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        const string marker = "MCP JV T2 unbalanced";
        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                docDate = today, description = marker,
                lines = new[]
                {
                    new { accountId = expAcctId, debitAmount = 100m, creditAmount = 0m, memo = (string?)null },
                    new { accountId = cashAcctId, debitAmount = 0m, creditAmount = 90m, memo = (string?)null },
                },
            },
        });

        result.IsError.Should().BeTrue("total debit (100) != total credit (90)");
        ErrorText(result).Should().Contain("[mcp.validation]");

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.AsNoTracking().AnyAsync(j => j.Description == marker))
            .Should().BeFalse("an unbalanced request must persist nothing");
    }

    // T3 — closed-period date. Read the seam first (per spec §6): JournalService.CreateDraftAsync
    // (Infrastructure/Ledger/JournalService.cs:39-79) calls NEITHER IPeriodCloseService.
    // EnsureOpenAsync NOR any account/BU gate — unlike CreateAndPostManualAsync, which the spec's
    // original §1 wrongly attributed to THIS seam (corrected; see attempt log). A closed period
    // therefore does NOT block a draft on this path — assert that honestly, and also prove the
    // docDate override (§10: CreateDraftAsync always pins DocDate to today, never the caller's
    // value) using the SAME closed date.
    [SkippableFact]
    public async Task T3_closed_period_date_the_draft_seam_has_no_period_gate_and_ignores_docdate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = new SystemClock().TodayInBangkok();

        await using (var closeSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var closeScope = closeSp.CreateAsyncScope())
            await closeScope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                .CloseAsync(today.Year, today.Month, "close for T3 test", default);

        var (expAcctId, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = BalancedRequest(today, expAcctId, cashAcctId, 50m, "MCP JV T3"),
        });
        result.IsError.Should().NotBe(true,
            "the draft seam has no period gate at all — a closed period must not block draft creation");
        var draftId = ResultRoot(result).GetProperty("draftId").GetInt64();

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == draftId);
        je.DocDate.Should().Be(today, "CreateDraftAsync always pins DocDate to today regardless of the caller's value");
    }

    // T4 — an accountId belonging to ANOTHER company → je.account_not_found ([mcp.domain_rule]),
    // nothing persisted. journal_lines has no company_id/FK to chart_of_accounts (same reasoning
    // CreateAndPostManualAsync's own account gate documents), so the tool must resolve+gate this
    // itself before calling CreateDraftAsync (which performs no such check on its own).
    [SkippableFact]
    public async Task T4_foreign_account_id_is_rejected_and_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var other = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (expAcctId, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        var (_, foreignCashAcctId) = await SeedAccountsAsync(other.CompanyId, other.BranchId);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        const string marker = "MCP JV T4 foreign account";
        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                docDate = today, description = marker,
                lines = new[]
                {
                    new { accountId = expAcctId, debitAmount = 100m, creditAmount = 0m, memo = (string?)null },
                    // company B's cash account — foreign to the calling key's company.
                    new { accountId = foreignCashAcctId, debitAmount = 0m, creditAmount = 100m, memo = (string?)null },
                },
            },
        });

        result.IsError.Should().BeTrue("the second line's accountId belongs to a different company");
        ErrorText(result).Should().Contain("[mcp.domain_rule]").And.Contain("not found");

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.AsNoTracking().AnyAsync(j => j.Description == marker))
            .Should().BeFalse("a foreign account id must persist nothing");
    }

    // T4b/T4c — Tier-2 R2a: the tool's OWN account gate must ALSO reject a header account and an
    // inactive account at DRAFT-CREATE time (not just missing/foreign, T4 above) — mirrors
    // JournalService.cs's own postable-account gate (je.account_is_header / je.account_inactive).
    [SkippableFact]
    public async Task T4b_header_account_is_rejected_at_draft_create_and_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (_, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        long headerId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            headerId = await coa.CreateAsync(new CreateAccountRequest(
                "9820", "หัวข้อทดสอบ MCP", "Test Header (MCP)", AccountType.Asset, null, true, NormalBalance.Debit), default);
        }
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        const string marker = "MCP JV T4b header account";
        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                docDate = today, description = marker,
                lines = new[]
                {
                    new { accountId = headerId, debitAmount = 100m, creditAmount = 0m, memo = (string?)null },
                    new { accountId = cashAcctId, debitAmount = 0m, creditAmount = 100m, memo = (string?)null },
                },
            },
        });

        result.IsError.Should().BeTrue("the first line's accountId is a header account — not postable");
        ErrorText(result).Should().Contain("[mcp.domain_rule]").And.Contain("header");

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.AsNoTracking().AnyAsync(j => j.Description == marker))
            .Should().BeFalse("a header account must persist nothing");
    }

    [SkippableFact]
    public async Task T4c_inactive_account_is_rejected_at_draft_create_and_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (_, cashAcctId) = await SeedAccountsAsync(co.CompanyId, co.BranchId);
        long inactiveId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            inactiveId = await coa.CreateAsync(new CreateAccountRequest(
                "9821", "บัญชีปิดใช้งาน MCP", "Inactive (MCP)", AccountType.Asset, null, false, NormalBalance.Debit), default);
            await coa.UpdateAsync(inactiveId,
                new UpdateAccountRequest("บัญชีปิดใช้งาน MCP", "Inactive (MCP)", false, false), default);
        }
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.create"]);

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var today = new SystemClock().TodayInBangkok();
        const string marker = "MCP JV T4c inactive account";
        var result = await client.CallToolAsync("create_manual_journal_draft", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                docDate = today, description = marker,
                lines = new[]
                {
                    new { accountId = inactiveId, debitAmount = 100m, creditAmount = 0m, memo = (string?)null },
                    new { accountId = cashAcctId, debitAmount = 0m, creditAmount = 100m, memo = (string?)null },
                },
            },
        });

        result.IsError.Should().BeTrue("the first line's accountId is inactive — not postable");
        ErrorText(result).Should().Contain("[mcp.domain_rule]").And.Contain("active");

        await using var verifySp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var verifyScope = verifySp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.JournalEntries.AsNoTracking().AnyAsync(j => j.Description == marker))
            .Should().BeFalse("an inactive account must persist nothing");
    }

    // T5 — a caller without gl.journal.create cannot see or call the tool. ALSO pins the core
    // safety invariant this whole feature depends on: gl.journal.post must never become a
    // grantable MCP scope (that line is what keeps this tool draft-only — §0/§8).
    [SkippableFact]
    public async Task T5_caller_without_permission_is_denied_and_journal_post_is_never_an_mcp_scope()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var key = await MintKeyAsync(co.CompanyId, co.BranchId, ["gl.journal.read"]);   // NOT .create

        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().NotContain("create_manual_journal_draft", "gl.journal.create-gated — hidden without the scope");

        var act = async () => await client.CallToolAsync("create_manual_journal_draft",
            new Dictionary<string, object?> { ["request"] = new { } });
        await act.Should().ThrowAsync<Exception>();

        McpScopes.All.Should().NotContain("gl.journal.post",
            "the product's core AI-safety invariant — an MCP credential can never hold a .post scope");
    }
}
