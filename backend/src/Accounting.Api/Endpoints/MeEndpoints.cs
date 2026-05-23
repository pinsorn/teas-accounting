using System.Security.Claims;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 13d P3 — the current user's effective scopes, for the frontend
/// PermissionGate (hide write buttons the user can't use, so they don't fill
/// a form that 403s on submit). Permissions are already on the JWT (issued at
/// login from roles → role_permissions); this just surfaces them — no extra
/// DB round-trip.
///
/// Minimal-API module (project convention; the spec said "MeController").
/// </summary>
public static class MeEndpoints
{
    public sealed record MePermissions(
        string[] Permissions, string[] Roles, bool IsSuperAdmin);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/permissions", (ClaimsPrincipal user) =>
        {
            var perms = user.FindAll(TenantClaims.Permission)
                .Select(c => c.Value).Distinct().OrderBy(s => s).ToArray();
            var roles = user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value).Distinct().OrderBy(s => s).ToArray();
            var isSuper = string.Equals(
                user.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
                StringComparison.OrdinalIgnoreCase);
            return Results.Ok(new MePermissions(perms, roles, isSuper));
        })
        .RequireAuthorization()
        .WithTags("Me");

        return app;
    }
}
