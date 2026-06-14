using System.Collections.Generic;
using System.Linq;
using Accounting.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Api.Tests.Rbac;

/// <summary>How an endpoint gates access — the canonical "expected" for the Cartesian matrix.</summary>
public enum AuthKind
{
    /// <summary>[AllowAnonymous] — login, health, refresh. Out of the role matrix.</summary>
    Anonymous,
    /// <summary>RequireAuthorization() with no permission — any signed-in user.</summary>
    AuthnOnly,
    /// <summary>RequireAuthorization("perm:X") — needs claim perm=X (or super-admin).</summary>
    Perm,
    /// <summary>RequireAssertion OR-set (CN/DN) — needs ANY of <see cref="Permissions"/> (or super-admin).</summary>
    Assertion,
    /// <summary>/api/v1/* ApiKey-scheme-only — JWT yields 401, not a role decision. Excluded from the JWT loop.</summary>
    ApiKeyOnly,
    /// <summary>No auth metadata and not anonymous — a FINDING (forgot RequireAuthorization).</summary>
    Unprotected,
}

/// <param name="Permissions">For Perm: the single required perm. For Assertion: the OR-set. Else empty.</param>
public sealed record EndpointAuth(
    string Method,
    string Route,
    AuthKind Kind,
    IReadOnlyList<string> Permissions,
    string RawDetail)
{
    public string Key => $"{Method} {Route}";
}

/// <summary>
/// Phase A — reflects <see cref="EndpointDataSource"/> into an endpoint→permission map.
/// Assertion-based policies (compiled lambdas) can't be reverse-engineered reliably, so the
/// OR-set for those routes is curated in <see cref="AssertionOverrides"/> and asserted against
/// reality (kind detection still happens by reflection; only the perm names are curated).
/// </summary>
public static class RbacEndpointInventory
{
    /// <summary>
    /// Curated OR-sets for the assertion-gated routes. Keyed by "METHOD /route".
    /// Mirrors <see cref="Accounting.Api.Endpoints.TaxAdjustmentNoteEndpoints"/>.
    /// </summary>
    // Keys must match EXACTLY the inventory key ("METHOD /<RawText>"), including the trailing
    // slash on the group root and the :long route constraints.
    private static readonly string[] CnDnRead =
    [
        "sales.credit_note.read", "sales.debit_note.read",
        "sales.credit_note.create", "sales.debit_note.create",
        "sales.credit_note.post", "sales.debit_note.post",
    ];

    public static readonly IReadOnlyDictionary<string, string[]> AssertionOverrides =
        new Dictionary<string, string[]>
        {
            ["POST /tax-adjustment-notes/"] =
                ["sales.credit_note.create", "sales.debit_note.create"],
            ["POST /tax-adjustment-notes/{id:long}/post"] =
                ["sales.credit_note.post", "sales.debit_note.post"],
            ["GET /tax-adjustment-notes/"] = CnDnRead,
            ["GET /tax-adjustment-notes/{id:long}"] = CnDnRead,
            ["GET /tax-adjustment-notes/{id:long}/pdf"] = CnDnRead,
        };

    public static IReadOnlyList<EndpointAuth> Build(IServiceProvider services)
    {
        var source = services.GetRequiredService<EndpointDataSource>();
        var result = new List<EndpointAuth>();

        foreach (var ep in source.Endpoints.OfType<RouteEndpoint>())
        {
            var route = "/" + (ep.RoutePattern.RawText ?? "").TrimStart('/');
            var methods = ep.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? new[] { "ANY" };

            foreach (var method in methods)
            {
                var (kind, perms, detail) = Classify(ep, method, route);
                result.Add(new EndpointAuth(method, route, kind, perms, detail));
            }
        }

        return result
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)
            .ToList();
    }

    private static (AuthKind, IReadOnlyList<string>, string) Classify(
        RouteEndpoint ep, string method, string route)
    {
        if (ep.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return (AuthKind.Anonymous, [], "AllowAnonymous");

        var authData = ep.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var policies = authData.Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();

        // perm: / apiperm: named policies — the suffix IS the permission.
        var permPolicies = policies
            .Where(p => p!.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
            .Select(p => p![PermissionPolicyProvider.PolicyPrefix.Length..])
            .ToList();
        var apiPermPolicies = policies
            .Where(p => p!.StartsWith(PermissionPolicyProvider.ApiKeyPolicyPrefix, StringComparison.Ordinal))
            .Select(p => p![PermissionPolicyProvider.ApiKeyPolicyPrefix.Length..])
            .ToList();

        // /api/v1/* is ApiKey-scheme-only (group policy or apiperm:). A JWT → 401 there.
        if (route.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase) || apiPermPolicies.Count > 0)
            return (AuthKind.ApiKeyOnly, apiPermPolicies, "ApiKey scheme; policies=" + string.Join("|", policies));

        if (permPolicies.Count > 0)
            return (AuthKind.Perm, permPolicies, string.Join("|", permPolicies));

        // Inline RequireAssertion → the built AuthorizationPolicy carries an AssertionRequirement.
        var policyMeta = ep.Metadata.GetMetadata<AuthorizationPolicy>();
        var hasAssertion = policyMeta?.Requirements.OfType<AssertionRequirement>().Any() ?? false;
        if (hasAssertion)
        {
            var key = $"{method} {route}";
            var or = AssertionOverrides.TryGetValue(key, out var set) ? set : [];
            return (AuthKind.Assertion, or, "AssertionRequirement; curated=" + string.Join("|", or));
        }

        // Any auth metadata at all (AuthorizeAttribute with null policy, or a RequireAuthenticatedUser
        // policy) but no permission → authenticated-only.
        var requiresAuth = authData.Count > 0
            || (policyMeta?.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Any() ?? false);
        if (requiresAuth)
            return (AuthKind.AuthnOnly, [], "Authenticated only");

        return (AuthKind.Unprotected, [], "NO auth metadata");
    }
}
