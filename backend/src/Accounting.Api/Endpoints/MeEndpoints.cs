using System.Security.Claims;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;

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

    /// <summary>A company the caller is allowed to operate as. §10 — NO tax fields (vat rate/mode)
    /// are exposed; only the id + display names the switcher needs.</summary>
    public sealed record AllowedCompany(int Id, string NameTh, string? NameEn);

    public sealed record MeResponse(
        long? UserId, string? Username, int CompanyId, int BranchId, bool IsSuperAdmin,
        string? CompanyName, IReadOnlyList<AllowedCompany> AllowedCompanies);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // Onboarding-switcher spec (2026-06-16) — identity + allowed-company list for the FE
        // (company switcher visibility + onboarding gate). Read-only; authentication only.
        app.MapGet("/me", async (ITenantContext tenant, ICompanyService companies, CancellationToken ct) =>
        {
            string? companyName = null;
            IReadOnlyList<AllowedCompany> allowed;

            if (tenant.IsSuperAdmin)
            {
                // Super-admin may operate as any ACTIVE company (same source as GET /companies).
                allowed = (await companies.ListAsync(ct))
                    .Where(c => c.IsActive)
                    .Select(c => new AllowedCompany(c.CompanyId, c.NameTh, c.NameEn))
                    .ToList();
                if (tenant.CompanyId != 0)
                    companyName = allowed.FirstOrDefault(c => c.Id == tenant.CompanyId)?.NameTh;
            }
            else if (tenant.CompanyId != 0)
            {
                // Normal user → ONLY their own company. NEVER ListAsync (it ignores the tenant
                // filter and would leak every company). §4.7.
                var c = await companies.GetAsync(tenant.CompanyId, ct);
                companyName = c.NameTh;
                allowed = [new AllowedCompany(c.CompanyId, c.NameTh, c.NameEn)];
            }
            else
            {
                allowed = [];
            }

            return Results.Ok(new MeResponse(
                tenant.UserId, tenant.Username, tenant.CompanyId, tenant.BranchId,
                tenant.IsSuperAdmin, companyName, allowed));
        })
        .RequireAuthorization()
        .WithTags("Me");

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
