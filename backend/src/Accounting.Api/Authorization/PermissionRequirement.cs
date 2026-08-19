using System.Security.Claims;
using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Accounting.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement requirement)
    {
        if (HasPermission(ctx.User, requirement.Permission))
            ctx.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <summary>Sprint 14 P6 / fix-c1-backend-cleanup item 2 — the single exact-match rule
    /// (API key: ScopesJson CSV claim; JWT: role-permission claim or super-admin bypass,
    /// CLAUDE.md §4.1), extracted static so a one-off OR-of-permissions policy (e.g. the
    /// employee.lookup/employee.manage back-compat fallback below) can reuse it verbatim
    /// instead of duplicating the two auth-principal branches.</summary>
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        // Sprint 14 P6 — external API key: authorize against the key's
        // ScopesJson (a CSV claim), NOT role-permission claims. A key never
        // gets super-admin bypass.
        if (string.Equals(user.FindFirst(TenantClaims.IsApiKey)?.Value, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            var scopes = user.FindFirst(TenantClaims.Scopes)?.Value ?? "";
            return scopes.Split(',', StringSplitOptions.RemoveEmptyEntries
                                    | StringSplitOptions.TrimEntries)
                .Contains(permission, StringComparer.Ordinal);
        }

        // JWT user — super admins bypass per-permission checks (CLAUDE.md §4.1).
        var isSuperAdmin = string.Equals(
            user.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        return isSuperAdmin ||
            user.HasClaim(c => c.Type == TenantClaims.Permission && c.Value == permission);
    }
}
