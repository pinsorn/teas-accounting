using Accounting.Api.Authorization;
using Accounting.Application.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 13k — per-company RBAC admin API (Phases A–C).
/// Roles + permissions gated by <see cref="Permissions.Sys.RoleManage"/>; user-role
/// assignment gated by <see cref="Permissions.Sys.UserManage"/>. All operations are
/// company-scoped via <c>RbacAdminService.ResolveTargetCompany</c> (§4.7).
///
/// NOTE: these are root-mounted (BFF) and surface RFC-7807 problem+json. Since Sprint 14
/// the root handler honours the same code→status map as /api/v1, so a cross-company write
/// (<c>.scope_required</c>) returns 403, <c>.not_found</c> returns 404, etc.
/// </summary>
public static class RbacAdminEndpoints
{
    public static IEndpointRouteBuilder MapRbacAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- roles + permission catalog (sys.role.manage) ------------------
        var roles = app.MapGroup("/admin/rbac").WithTags("RBAC Admin")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.RoleManage);

        // Static bilingual catalog — no DB.
        roles.MapGet("/permissions", () => Results.Ok(PermissionCatalog.Items));

        roles.MapGet("/roles", async ([FromQuery] int? companyId, IRbacAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRolesAsync(companyId, ct)));

        roles.MapGet("/roles/{id:int}", async (int id, IRbacAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRoleAsync(id, ct)));

        roles.MapPost("/roles", async ([FromBody] CreateRoleRequest req, IRbacAdminService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateRoleAsync(req, ct);
            return Results.Created($"/admin/rbac/roles/{id}", new { roleId = id });
        });

        roles.MapPut("/roles/{id:int}", async (int id, [FromBody] UpdateRoleRequest req,
            IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.UpdateRoleAsync(id, req, ct);
            return Results.NoContent();
        });

        roles.MapDelete("/roles/{id:int}", async (int id, IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.DeleteRoleAsync(id, ct);
            return Results.NoContent();
        });

        roles.MapPut("/roles/{id:int}/permissions", async (int id, [FromBody] SetRolePermissionsRequest req,
            IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.SetRolePermissionsAsync(id, req, ct);
            return Results.NoContent();
        });

        // ---- user-role assignment (sys.user.manage) -----------------------
        var users = app.MapGroup("/admin/rbac").WithTags("RBAC Admin")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.UserManage);

        users.MapGet("/users", async ([FromQuery] int? companyId, IRbacAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListUsersAsync(companyId, ct)));

        users.MapPost("/users", async ([FromBody] CreateUserRequest req, IRbacAdminService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateUserAsync(req, ct);
            return Results.Created($"/admin/rbac/users/{id}", new { userId = id });
        });

        users.MapPut("/users/{id:long}/roles", async (long id, [FromBody] SetUserRolesRequest req,
            IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.SetUserRolesAsync(id, req, ct);
            return Results.NoContent();
        });

        users.MapPut("/users/{id:long}/active", async (long id, [FromBody] SetUserActiveRequest req,
            IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.SetUserActiveAsync(id, req.IsActive, ct);
            return Results.NoContent();
        });

        users.MapPut("/users/{id:long}/password", async (long id, [FromBody] ResetUserPasswordRequest req,
            IRbacAdminService svc, CancellationToken ct) =>
        {
            await svc.ResetUserPasswordAsync(id, req.Password, ct);
            return Results.NoContent();
        });

        return app;
    }
}
