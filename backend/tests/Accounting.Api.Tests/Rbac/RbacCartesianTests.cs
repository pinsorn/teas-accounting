using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Phase C — the Cartesian RBAC enforcement test. For every (role × permission-gated endpoint)
/// pair it mints a real JWT for the role (permissions = the role's DB grants), fires the endpoint
/// over the full HTTP pipeline, and asserts: a role WITH a required permission is NOT 403; a role
/// WITHOUT it IS 403. Plus super-admin bypass, cross-company isolation, and the API-key scope path.
///
/// Side-effect discipline (allow-cases execute the handler): deny-cases run zero handler code
/// (UseAuthorization precedes model binding); allow-cases use a non-existent id (404) and an empty
/// body (FluentValidation → 400/422); the handful of parameterless committing mutations are not
/// fired in their allow-case (their deny-case still runs). Runs against the shared, disposable
/// teas_test.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RbacCartesianTests
{
    private readonly PostgresFixture _fx;
    public RbacCartesianTests(PostgresFixture fx) => _fx = fx;

    /// <summary>
    /// Allow-cases NOT fired: these mutations target the caller's own company / global state with no
    /// {id} (fake-id 404 doesn't protect) and no body validator that rejects an empty body, so the
    /// handler would COMMIT and pollute the shared teas_test for sibling tests in the collection
    /// (e.g. wiping company 1's profile, or finalising CIT year 2099 a Pnd50 test relies on).
    /// Their DENY-cases still run — denial is decided before the handler, so security coverage is
    /// intact; super-admin bypass is proven by the 200+ other endpoints (uniform in PermissionHandler).
    /// Endpoints with an {id} (fake id → 404) or a FluentValidation gate (empty body → 400) are NOT
    /// listed — they're safe to fire.
    /// </summary>
    private static readonly HashSet<string> SkipAllowMutation =
    [
        "POST /periods/{year:int}/{month:int}/close",
        "PUT /company-profile/soft",
        "PUT /company-profile/hard",
        "PUT /company-profile/registered-address",
        "POST /company-profile/logo",
        "PUT /business-units/company-setting",
        "POST /tax-filings/cit/adjustments",
        "PUT /tax-filings/cit/years/{year:int}",
        "POST /tax-filings/cit/years/{year:int}/compute",
        "POST /tax-filings/pnd3",
        "POST /tax-filings/pnd30",
        "POST /tax-filings/pnd36",
        "POST /tax-filings/pnd53",
        "POST /tax-filings/pnd54",
        "POST /tax-filings/pnd51/estimate",
        "POST /api-keys/",
    ];

    /// <summary>
    /// First-run setup endpoints that are AuthnOnly at the POLICY (RequireAuthorization / AllowAnonymous)
    /// but enforce super-admin INSIDE the handler — by design, because at first-run the super-admin may
    /// carry no permission claims, so a permission policy would wrongly 403 it. The Cartesian matrix
    /// classifies them as AuthnOnly and would therefore expect ALLOW for every authenticated role, but the
    /// handler correctly 403s a non-super role. That 403 is the intended behaviour, not a finding, so the
    /// ALLOW-case is skipped for these (the DENY side is meaningless here — there is no permission to lack).
    /// See InstanceSetupEndpoints (instance-keys) and BootstrapAdminEndpoints (bootstrap-admin is Anonymous
    /// + zero-users-gated, so it never reaches this Perm/Assertion/AuthnOnly target set at all).
    /// </summary>
    private static readonly HashSet<string> HandlerGatedAuthnOnly =
    [
        "POST /system/setup/instance-keys",
    ];

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static string Token(JwtTokenIssuer issuer, string role, bool isSuper,
        IEnumerable<string> perms, int companyId = RbacMatrixData.ReferenceCompanyId) =>
        issuer.Issue(new TokenClaims(
            UserId: 990_000 + Math.Abs(role.GetHashCode()) % 1000,
            Username: $"rbac-cartesian-{role}",
            CompanyId: companyId,
            BranchId: 1,
            IsSuperAdmin: isSuper,
            Roles: [role],
            Permissions: perms.ToList())).Token;

    private static string FillRoute(string route) =>
        Regex.Replace(route, @"\{(\w+)[^}]*\}", m =>
        {
            var n = m.Groups[1].Value.ToLowerInvariant();
            return n.Contains("year") ? "2099" : n.Contains("month") ? "1" : "999999999";
        });

    private static async Task<int> FireAsync(HttpClient client, string token, EndpointAuth ep)
    {
        var method = ep.Method == "ANY" ? "GET" : ep.Method;
        using var req = new HttpRequestMessage(new HttpMethod(method), FillRoute(ep.Route));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method is "POST" or "PUT" or "PATCH")
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(req);
        return (int)resp.StatusCode;
    }

    [SkippableFact]
    public async Task Every_role_x_endpoint_pair_enforces_the_seeded_grants()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var fx = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(fx);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var issuer = Issuer();

        var targets = RbacEndpointInventory.Build(factory.Services)
            .Where(e => e.Kind is AuthKind.Perm or AuthKind.Assertion or AuthKind.AuthnOnly)
            .ToList();
        targets.Should().NotBeEmpty();

        // Sanity: a valid token must authenticate (else everything is 401 and the deny-logic inverts).
        var superToken = Token(issuer, "SUPER_ADMIN", true, []);
        var probe = await FireAsync(client, superToken,
            targets.First(t => t.Key == "GET /me/permissions"));
        probe.Should().NotBe(401, "the minted JWT must validate against the factory's Jwt config");

        var failures = new List<string>();

        foreach (var role in roles)
        {
            var token = Token(issuer, role.RoleCode, role.IsSuperAdmin, role.Permissions);

            foreach (var ep in targets)
            {
                var expectAllow = role.IsSuperAdmin
                    || ep.Kind == AuthKind.AuthnOnly
                    || ep.Permissions.Any(p => role.Permissions.Contains(p));

                if (expectAllow && SkipAllowMutation.Contains(ep.Key))
                    continue;   // committing mutation — don't execute the handler in allow-mode

                // AuthnOnly-at-policy but super-admin-gated in the handler: a non-super role's 403 is
                // correct, so don't assert ALLOW for it. Super-admin still asserts ALLOW normally.
                if (expectAllow && !role.IsSuperAdmin && HandlerGatedAuthnOnly.Contains(ep.Key))
                    continue;

                var status = await FireAsync(client, token, ep);

                if (expectAllow && status is 401 or 403)
                    failures.Add($"ALLOW expected, got {status}: [{role.RoleCode}] {ep.Key} "
                        + $"(needs {string.Join("|", ep.Permissions)})");
                else if (!expectAllow && status != 403)
                    failures.Add($"DENY(403) expected, got {status}: [{role.RoleCode}] {ep.Key} "
                        + $"(needs {string.Join("|", ep.Permissions)})");
            }
        }

        failures.Should().BeEmpty("RBAC enforcement must match the seeded grants for every "
            + $"role×endpoint pair. {failures.Count} mismatch(es):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public async Task Company_admin_cannot_manage_another_companys_rbac()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var fx = _fx.BuildServiceProvider();
        var other = (await fx.GetRequiredService<Accounting.Infrastructure.Persistence.AccountingDbContext>()
            .Database.SqlQueryRaw<int>(
                "SELECT company_id AS \"Value\" FROM master.companies WHERE company_id <> 1 ORDER BY company_id LIMIT 1")
            .ToListAsync()).FirstOrDefault();
        Skip.If(other == 0, "teas_test has no second company to test cross-company isolation");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Synthetic token: a NON-super company-1 admin holding the RBAC-admin permissions. (We mint
        // it directly rather than depend on a seeded role having sys.role.manage — the point is to
        // exercise RbacAdminService.ResolveTargetCompany's company-scope guard, §4.7.)
        var token = Token(Issuer(), "COMPANY_ADMIN", isSuper: false,
            perms: ["sys.role.manage", "sys.user.manage"], companyId: 1);

        // Own company → passes the scope guard (not 403).
        (await Get(client, token, "/admin/rbac/roles?companyId=1"))
            .Should().NotBe(403, "a company admin may manage their OWN company's roles");

        // Another company → 403 (rbac.cross_company.scope_required → DomainExceptionMiddleware).
        (await Get(client, token, $"/admin/rbac/roles?companyId={other}"))
            .Should().Be(403, "a company admin must not read another company's roles");

        using var put = new HttpRequestMessage(HttpMethod.Put, "/admin/rbac/users/999999999/roles");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content = new StringContent($"{{\"companyId\":{other},\"roleIds\":[]}}",
            Encoding.UTF8, "application/json");
        using var putResp = await client.SendAsync(put);
        ((int)putResp.StatusCode).Should().Be(403,
            "a company admin must not assign roles in another company");
    }

    [SkippableFact]
    public async Task Api_key_is_authorized_by_scope_with_no_super_bypass()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Mint a company-1 key scoped to customer.read ONLY (a key never gets super bypass).
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var infra = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1 })
            .BuildServiceProvider();
        string plaintext;
        await using (var s = infra.CreateAsyncScope())
            plaintext = (await s.ServiceProvider.GetRequiredService<IApiKeyService>()
                .CreateAsync(new CreateApiKeyRequest("rbac-cartesian-key", ["master.customer.read"]), default))
                .Plaintext;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // In scope → not 403.
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers"))
        {
            req.Headers.Add("X-Api-Key", plaintext);
            using var resp = await client.SendAsync(req);
            ((int)resp.StatusCode).Should().NotBe(403, "the key holds master.customer.read");
            ((int)resp.StatusCode).Should().NotBe(401, "the key must authenticate on the v1 scheme");
        }

        // Out of scope → 403 (no super bypass for API keys).
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products"))
        {
            req.Headers.Add("X-Api-Key", plaintext);
            using var resp = await client.SendAsync(req);
            ((int)resp.StatusCode).Should().Be(403,
                "the key lacks master.product.read and keys never get super-admin bypass");
        }
    }

    private static async Task<int> Get(HttpClient client, string token, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);
        return (int)resp.StatusCode;
    }
}
