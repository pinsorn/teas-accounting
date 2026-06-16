using System.IO;
using System.Linq;
using System.Text;
using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Phase A — generate the endpoint→permission map and flag endpoints that forgot
/// <c>RequireAuthorization</c> (reachable with only a login). The map is the
/// "expected" half of the Cartesian matrix; it is regenerated on every run so a
/// new endpoint with a missing/over-broad gate shows up in the diff (and the
/// Unprotected assertion fails CI).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RbacAuthMapTests
{
    private readonly PostgresFixture _fx;
    public RbacAuthMapTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Intentionally public (no auth, by design) — login + liveness probe.</summary>
    private static readonly string[] ExpectedPublic =
    [
        "POST /auth/login",
        "ANY /health",
    ];

    /// <summary>
    /// Routes that are intentionally reachable by any authenticated user with no
    /// specific permission (own-data / reference / utility reads). Anything else
    /// landing in AuthnOnly is a finding to review.
    /// </summary>
    private static readonly string[] ExpectedAuthnOnly =
    [
        "GET /business-units/company-setting",
        "GET /company-profile/",
        "GET /documents/chain",
        "GET /documents/purchase-chain",
        "GET /me",
        "GET /me/permissions",
        "GET /periods/{year:int}/{month:int}/status",
        "GET /system/info",
        "GET /system/vat-threshold-status",
        "GET /wht-types/",
        "GET /wht-types/{id:int}",
    ];

    [SkippableFact]
    public async Task Generate_endpoint_permission_map_and_flag_unprotected_endpoints()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        using var factory = new RbacApiFactory(_fx.ConnectionString);
        // Touch Services to force host build (endpoints are registered at build time).
        var inventory = RbacEndpointInventory.Build(factory.Services);
        inventory.Should().NotBeEmpty();

        WriteGeneratedMap(inventory);

        // FINDING gate 0 — every permission ENFORCED on an endpoint must EXIST in sys.permissions,
        // else it is ungrantable (no role, not even via the admin UI, can ever hold it → that
        // endpoint is super-admin-only forever). This is exactly the seed-ordering bug Plan 2 fixed
        // (320/330 granted codes that 520 inserted later); without this guard all the other gates go
        // green while the hole is live (a non-existent perm → every role 403 → looks like a clean deny).
        // Scope to the ROLE/JWT permission namespace (Perm + Assertion). ApiKeyOnly endpoints
        // (/api/v1/*) are authorised against a key's free-form ScopesJson set at key-creation time,
        // NOT against sys.permissions or role grants, so their granular scopes (e.g. v1's
        // sales.quotation.read/create/send, sys.system_info.read) live in a separate namespace and
        // are intentionally not in the role catalog.
        await using var sp = _fx.BuildServiceProvider();
        var (_, catalog) = await RbacMatrixData.LoadAsync(sp);
        var ungrantable = inventory
            .Where(e => e.Kind is AuthKind.Perm or AuthKind.Assertion)
            .SelectMany(e => e.Permissions)
            .Distinct()
            .Where(p => !catalog.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        ungrantable.Should().BeEmpty(
            "every role-enforced permission must exist in sys.permissions (else it is "
            + "enforced-but-ungrantable — the 320/330-before-520 seed-ordering bug). Missing from "
            + "catalog: " + string.Join(", ", ungrantable));

        // FINDING gate 1 — no endpoint may be reachable without authorization
        // (excluding the by-design public login + liveness probe).
        var unprotected = inventory
            .Where(e => e.Kind == AuthKind.Unprotected)
            .Select(e => e.Key)
            .Except(ExpectedPublic)
            .ToList();
        unprotected.Should().BeEmpty(
            "every business endpoint must carry RequireAuthorization (ม.86/4 audit). Found: "
            + string.Join(", ", unprotected));

        // FINDING gate 2 — authn-only endpoints must be the known utility allowlist.
        var unexpectedAuthnOnly = inventory
            .Where(e => e.Kind == AuthKind.AuthnOnly)
            .Select(e => e.Key)
            .Except(ExpectedAuthnOnly)
            .ToList();
        unexpectedAuthnOnly.Should().BeEmpty(
            "an authenticated-but-permissionless endpoint is likely a missing perm gate. Found: "
            + string.Join(", ", unexpectedAuthnOnly));
    }

    private static void WriteGeneratedMap(IReadOnlyList<EndpointAuth> inventory)
    {
        var path = Path.Combine(RbacTestPaths.RbacDocsDir(), "endpoint-permission-map.generated.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Endpoint → Permission Map (GENERATED — do not edit by hand)");
        sb.AppendLine();
        sb.AppendLine("> Regenerated by `RbacAuthMapTests`. Source = live `EndpointDataSource`.");
        sb.AppendLine("> Sprint 13k Plan 2 (RBAC Cartesian audit). Reflects the running gate on each route.");
        sb.AppendLine();

        var byKind = inventory.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Kind | Count |");
        sb.AppendLine("|---|---:|");
        foreach (var k in Enum.GetValues<AuthKind>())
            sb.AppendLine($"| {k} | {(byKind.TryGetValue(k, out var c) ? c : 0)} |");
        sb.AppendLine($"| **TOTAL** | **{inventory.Count}** |");
        sb.AppendLine();

        sb.AppendLine("## Routes");
        sb.AppendLine();
        sb.AppendLine("| Method | Route | Gate | Permission(s) |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var e in inventory)
        {
            var perms = e.Permissions.Count == 0 ? "—" : string.Join(" / ", e.Permissions);
            sb.AppendLine($"| {e.Method} | `{e.Route}` | {e.Kind} | {perms} |");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
