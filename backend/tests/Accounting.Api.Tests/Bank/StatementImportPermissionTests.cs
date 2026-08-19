using System.Net;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// L2-3 (PLAN-fix-findings-r2.md §U3.2) — HTTP-level (real pipeline, RbacApiFactory, mirrors
/// FixedAssetPermissionTests) RBAC gating on the new
/// <c>DELETE /bank-accounts/{bankAccountId}/imports/{importId}</c> endpoint: same permission as
/// import creation (<see cref="Permissions.Bank.StatementImport"/>) — a token holding only
/// <see cref="Permissions.Bank.AccountRead"/> must be 403.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class StatementImportPermissionTests : IDisposable
{
    private readonly PostgresFixture _fx;
    public StatementImportPermissionTests(PostgresFixture fx) => _fx = fx;

    // Same per-test temp storage root pattern as StatementImportServiceTests — ImportAsync
    // writes real bytes through the Attachment infra even for a DENY-only probe's seed step.
    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private static string Token(int companyId, int branchId, string[] permissions) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "statement-import-perm-tests", CompanyId: companyId, BranchId: branchId,
            IsSuperAdmin: false, Roles: ["test"], Permissions: permissions)).Token;

    /// <summary>Seeds a bank account + a real statement import directly via the services
    /// (StubTenant), bypassing HTTP — only the DELETE call itself goes through the real HTTP
    /// pipeline (mirrors FixedAssetPermissionTests.SeedAsync).</summary>
    private async Task<(int CompanyId, int BranchId, long ImportId)> SeedAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["FileStorage:StorageRoot"] = _storageRoot,
            ["FileStorage:MaxFileSizeMb"] = "25",
        }).Build();
        var sp = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = co.CompanyId, BranchId = co.BranchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();

        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var bankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);
        var importSvc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var result = await importSvc.ImportAsync(
            bankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        return (co.CompanyId, co.BranchId, result.StatementImportId);
    }

    [SkippableFact]
    public async Task AccountReadOnly_token_is_403_on_delete_import()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, importId) = await SeedAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        // Deliberately lacks Permissions.Bank.StatementImport — has read only.
        var token = Token(companyId, branchId, [Permissions.Bank.AccountRead]);

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/bank-accounts/1/imports/{importId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "bank.account.read alone does not grant bank.statement.import, the permission gating this route");
    }
}
