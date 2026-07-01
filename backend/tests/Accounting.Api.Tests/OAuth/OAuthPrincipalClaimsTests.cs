using Accounting.Api.OAuth;
using Accounting.Application.Abstractions;
using FluentAssertions;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// The claim-equivalence GATE (spec §2 + §6b). Asserts directly on the principal
/// <see cref="McpPrincipalFactory.Build"/> issues — if this drifts, an OAuth token silently either
/// denies every MCP tool call (missing is_api_key) or escalates (is_super_admin / a *.post scope).
/// No DB / HTTP round-trip needed.
/// </summary>
public class OAuthPrincipalClaimsTests
{
    private const string Resource = "http://localhost:3000/mcp";
    private static readonly string[] Granted =
        ["sales.tax_invoice.read", "sales.tax_invoice.create", "master.customer.manage"];

    [Fact]
    public void Build_emits_the_x_api_key_equivalent_claim_set()
    {
        var p = McpPrincipalFactory.Build(
            userId: 42, actorName: "oauth:admin", companyId: 7, branchId: 1, Granted, Resource);

        p.FindFirst(TenantClaims.IsApiKey)!.Value.Should().Be("true");                 // load-bearing
        p.FindFirst(Claims.Subject)!.Value.Should().Be("42");                          // sub = user id
        p.FindFirst(System.Security.Claims.ClaimTypes.Name)!.Value.Should().Be("oauth:admin");
        int.Parse(p.FindFirst(TenantClaims.CompanyId)!.Value).Should().BeGreaterThan(0);
        int.Parse(p.FindFirst(TenantClaims.BranchId)!.Value).Should().BeGreaterThan(0);
        p.FindFirst(TenantClaims.Scopes)!.Value.Should().Be(string.Join(',', Granted));
        p.GetScopes().Should().BeEquivalentTo(Granted);
        p.GetResources().Should().Contain(Resource);                                   // RFC 8707 aud
    }

    [Fact]
    public void Build_never_emits_super_admin_or_api_key_id_or_a_post_scope()
    {
        var p = McpPrincipalFactory.Build(
            userId: 42, actorName: "oauth:admin", companyId: 7, branchId: 1, Granted, Resource);

        p.FindFirst(TenantClaims.IsSuperAdmin).Should().BeNull("an OAuth token must never bypass RLS");
        p.FindFirst(TenantClaims.ApiKeyId).Should().BeNull("OAuth ≠ api key");
        foreach (var scope in p.GetScopes())
            McpScopes.ForbiddenSuffixes.Any(s => scope.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                .Should().BeFalse($"'{scope}' must not be a post-class scope");
    }

    [Theory]
    [InlineData(0, 1)]   // company_id = 0 → silent-zero tenant break
    [InlineData(7, 0)]   // branch_id  = 0
    public void Build_hard_rejects_zero_company_or_branch(int companyId, int branchId)
    {
        var act = () => McpPrincipalFactory.Build(42, "oauth:admin", companyId, branchId, Granted, Resource);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
