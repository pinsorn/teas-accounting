using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.FixedAsset;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md §10.10, §8.3 role split) — HTTP-level (real pipeline,
/// RbacApiFactory, mirrors ExpenseClaimPermissionTests) RBAC gating: an ACCOUNTANT-shaped token
/// (read+manage+depreciation.run, per 620_seed_fixed_asset_perms.sql) is 403 on dispose; a
/// CHIEF_ACCOUNTANT-shaped token (also has dispose) succeeds.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FixedAssetPermissionTests
{
    private readonly PostgresFixture _fx;
    public FixedAssetPermissionTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private static string Token(int companyId, int branchId, string[] permissions) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "fixed-asset-perm-tests", CompanyId: companyId, BranchId: branchId,
            IsSuperAdmin: false, Roles: ["test"], Permissions: permissions)).Token;

    private static readonly string[] AccountantPerms =
        [Permissions.FixedAsset.Read, Permissions.FixedAsset.Manage, Permissions.FixedAsset.DepreciationRun];
    private static readonly string[] ChiefAccountantPerms =
        [Permissions.FixedAsset.Read, Permissions.FixedAsset.Manage,
         Permissions.FixedAsset.Dispose, Permissions.FixedAsset.DepreciationRun];

    private static HttpRequestMessage Post(string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Seeds an Active asset directly via the service (StubTenant), bypassing HTTP —
    /// only the dispose CALL itself goes through the real HTTP pipeline.</summary>
    private async Task<(int CompanyId, int BranchId, long AssetId)> SeedAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var id = await svc.CreateDraftAsync(new CreateFixedAssetRequest(
            "สินทรัพย์ทดสอบสิทธิ์", "EQUIPMENT", Today, null, 5000m, 0m, 12, Today, null, null, null, null), default);
        await svc.ActivateAsync(id, default);

        return (co.CompanyId, co.BranchId, id);
    }

    [SkippableFact]
    public async Task Accountant_shaped_token_is_403_on_dispose()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, assetId) = await SeedAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(companyId, branchId, AccountantPerms);

        using var disposeResp = await client.SendAsync(Post($"/fixed-assets/{assetId}/dispose", token,
            new { disposalDate = Today.ToString("yyyy-MM-dd"), proceeds = 1000m, vatAmount = (decimal?)null, buyerName = (string?)null }));
        disposeResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ACCOUNTANT has no fixedasset.dispose (per 620's role split)");
    }

    [SkippableFact]
    public async Task ChiefAccountant_shaped_token_succeeds_on_dispose()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, assetId) = await SeedAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(companyId, branchId, ChiefAccountantPerms);

        using var disposeResp = await client.SendAsync(Post($"/fixed-assets/{assetId}/dispose", token,
            new { disposalDate = Today.ToString("yyyy-MM-dd"), proceeds = 1000m, vatAmount = (decimal?)null, buyerName = (string?)null }));
        disposeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
