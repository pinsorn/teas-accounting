using System.Security.Claims;
using Accounting.Api.OAuth;
using Accounting.Application.Abstractions;
using FluentAssertions;
using OpenIddict.Validation.AspNetCore;
using Xunit;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// The validation-time defense-in-depth gate (spec §6b): the /mcp Bearer principal is rejected if it
/// lacks a positive company/branch or is_api_key, or if it carries is_super_admin — and is a no-op
/// for non-OAuth (JWT / X-Api-Key) principals.
/// </summary>
public class McpBearerClaimsTransformTests
{
    private static ClaimsPrincipal Bearer(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)),
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme));

    [Fact]
    public async Task Valid_bearer_principal_passes_through()
    {
        var p = await new McpBearerClaimsTransform().TransformAsync(Bearer(
            (TenantClaims.CompanyId, "7"), (TenantClaims.BranchId, "1"), (TenantClaims.IsApiKey, "true")));
        p.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Theory]
    [InlineData("0", "1", "true")]    // company_id = 0 (silent-zero → tenant break)
    [InlineData("7", "0", "true")]    // branch_id  = 0
    [InlineData("7", "1", "false")]   // is_api_key not "true" → PermissionHandler would ignore scopes
    public async Task Bad_bearer_principal_is_rejected(string company, string branch, string apiKey)
    {
        var p = await new McpBearerClaimsTransform().TransformAsync(Bearer(
            (TenantClaims.CompanyId, company), (TenantClaims.BranchId, branch), (TenantClaims.IsApiKey, apiKey)));
        p.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Super_admin_bearer_is_rejected()
    {
        var p = await new McpBearerClaimsTransform().TransformAsync(Bearer(
            (TenantClaims.CompanyId, "7"), (TenantClaims.BranchId, "1"),
            (TenantClaims.IsApiKey, "true"), (TenantClaims.IsSuperAdmin, "true")));
        p.Identity!.IsAuthenticated.Should().BeFalse("an OAuth token must never bypass RLS");
    }

    [Fact]
    public async Task Non_bearer_principal_is_untouched()
    {
        var input = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TenantClaims.CompanyId, "0")], "ApiKey"));
        var p = await new McpBearerClaimsTransform().TransformAsync(input);
        p.Should().BeSameAs(input);   // no-op for non-OAuth schemes (no regression on ApiKey/JWT)
    }
}
