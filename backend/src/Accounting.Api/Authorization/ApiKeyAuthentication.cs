using System.Security.Claims;
using System.Text.Encodings.Web;
using Accounting.Api.ApiError;
using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Accounting.Api.Authorization;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Sprint 14 — <c>X-Api-Key</c> authentication for the external <c>/api/v1/*</c>
/// surface (no human JWT). On success builds a principal whose claims drive the
/// existing tenant context, RLS, scope checks and per-key BU binding. Failures
/// emit the stable error envelope (401) with a precise code.
/// </summary>
public sealed class ApiKeyAuthenticationHandler
    : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    private const string FailCodeItem = "__apikey_fail_code";

    private readonly IApiKeyResolver _resolver;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IApiKeyResolver resolver)
        : base(options, logger, encoder) => _resolver = resolver;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            Context.Items[FailCodeItem] = "auth.missing_api_key";
            return AuthenticateResult.NoResult();
        }

        var r = await _resolver.AuthenticateAsync(raw.ToString(), Context.RequestAborted);
        if (r.Key is null)
        {
            Context.Items[FailCodeItem] = r.FailCode ?? "auth.invalid_api_key";
            return AuthenticateResult.Fail(r.FailCode ?? "auth.invalid_api_key");
        }

        var k = r.Key;
        var claims = new List<Claim>
        {
            new(TenantClaims.CompanyId,    k.CompanyId.ToString()),
            new(TenantClaims.ApiKeyId,     k.ApiKeyId.ToString()),
            new(TenantClaims.ApiKeyName,   k.Name),
            new(TenantClaims.IsApiKey,     "true"),
            new(TenantClaims.Scopes,       k.ScopesCsv),
        };
        if (k.DefaultBusinessUnitId is { } bu)
            claims.Add(new Claim(TenantClaims.DefaultBusinessUnit, bu.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var code = Context.Items.TryGetValue(FailCodeItem, out var c) && c is string s
            ? s : "auth.invalid_api_key";
        var msg = code switch
        {
            "auth.missing_api_key" => "X-Api-Key header is required.",
            "auth.expired_api_key" => "API key has expired.",
            "auth.revoked_api_key" => "API key has been revoked.",
            _                      => "API key is invalid.",
        };
        return ErrorEnvelope.WriteAsync(Context, StatusCodes.Status401Unauthorized, code, msg);
    }
}
