using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// 2026-07-04 review, finding H3 — 4 `PUT` (update) endpoints reached the service layer
/// with zero FluentValidation (one validator, <c>UpdateVendorValidator</c>, existed but was
/// dead code — never wired). Fixed by wiring/creating <c>IValidator&lt;T&gt;</c> on each and
/// calling <c>ValidateAsync</c> before the service, mirroring the sibling <c>MapPost</c>.
///
/// Each case fires the real HTTP pipeline (<see cref="RbacApiFactory"/>) with a SUPER_ADMIN
/// token (bypasses permission checks) against a NON-existent id + an intentionally-invalid
/// body. Validation runs BEFORE the id lookup in every wired handler, so this never touches
/// real rows — no <c>Accounting.TestKit.TestIds</c> unique values are needed.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class H3PutValidationTests
{
    private readonly PostgresFixture _fx;
    public H3PutValidationTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static async Task<(int Status, string Body)> PutAsync(
        HttpClient client, string token, string route, string json)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [SkippableTheory]
    [InlineData("/customers/999999999", "{}")]                 // UpdateCustomerRequest: NameTh required
    [InlineData("/branches/999999999", "{}")]                  // UpdateBranchRequest: NameTh required
    [InlineData("/vendors/999999999", "{}")]                   // UpdateVendorRequest: NameTh required (dead validator, now wired)
    [InlineData("/accounts/999999999", "{}")]                  // UpdateAccountRequest: AccountNameTh required
    public async Task Invalid_body_on_newly_guarded_put_returns_400_validation_problem(
        string route, string invalidJson)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Issuer().Issue(new TokenClaims(
            UserId: 990_999, Username: "h3-super", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: true, Roles: ["SUPER_ADMIN"], Permissions: [])).Token;

        var (status, body) = await PutAsync(client, token, route, invalidJson);

        status.Should().Be(400, $"{route} must reject an invalid body via FluentValidation " +
            $"before reaching the service — got {status}: {body}");
        // ErrorEnvelope middleware reshapes Results.ValidationProblem's ProblemDetails into this
        // codebase's field-error envelope (see DomainExceptionMiddleware / ErrorEnvelope.cs).
        body.Should().Contain("fieldErrors", "the ErrorEnvelope-wrapped ValidationProblem carries " +
            "a fieldErrors array");
    }
}
