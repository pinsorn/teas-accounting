using System.Linq;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Api.Tests.Rbac;

/// <param name="Permissions">Granted permission codes for the role (template = reference company).</param>
public sealed record RoleGrants(
    int RoleId, string RoleCode, bool IsSuperAdmin, IReadOnlySet<string> Permissions);

/// <summary>
/// Phase B/C shared loader — reads the canonical role×permission grants from the DB.
/// Roles are per-company but every company is cloned from one template, so the reference
/// company (id 1) is the canonical template. SUPER_ADMIN is the system-global bypass role
/// (company_id NULL); it is represented here as holding every permission.
/// </summary>
public static class RbacMatrixData
{
    public const int ReferenceCompanyId = 1;
    public const string SuperAdmin = "SUPER_ADMIN";

    public static async Task<(IReadOnlyList<RoleGrants> Roles, IReadOnlyList<string> AllPermissions)>
        LoadAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var allPerms = await db.Permissions.AsNoTracking()
            .Select(p => p.PermissionCode).OrderBy(c => c).ToListAsync(ct);

        var roleRows = await db.Roles.AsNoTracking()
            .Where(r => r.CompanyId == ReferenceCompanyId)
            .OrderBy(r => r.RoleCode)
            .Select(r => new { r.RoleId, r.RoleCode })
            .ToListAsync(ct);

        var roles = new List<RoleGrants>();
        foreach (var r in roleRows)
        {
            var perms = (await db.RolePermissions.AsNoTracking()
                    .Where(rp => rp.RoleId == r.RoleId)
                    .Select(rp => rp.Permission!.PermissionCode)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            roles.Add(new RoleGrants(r.RoleId, r.RoleCode, false, perms));
        }

        // System-global SUPER_ADMIN: PermissionHandler bypasses per-permission checks, so for
        // the matrix it effectively holds every grantable permission.
        roles.Add(new RoleGrants(-1, SuperAdmin, true,
            allPerms.ToHashSet(StringComparer.Ordinal)));

        return (roles.OrderBy(r => r.RoleCode, StringComparer.Ordinal).ToList(), allPerms);
    }
}
