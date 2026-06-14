using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 13k — per-company RBAC admin service. Role / RolePermission are NOT
/// ITenantOwned, so every query filters company_id EXPLICITLY (mirrors
/// BusinessUnitService). A concrete company_id filter also naturally excludes the
/// system-global SUPER_ADMIN row (company_id IS NULL). §4.7 multi-tenant isolation,
/// §4.8 audit trail.
/// </summary>
public sealed class RbacAdminService(AccountingDbContext db, ITenantContext tenant, IActivityRecorder activity)
    : IRbacAdminService
{
    private const string SuperAdmin = Role.SystemRoles.SuperAdmin;

    /// <summary>Compliance-critical (§4.7). Non-super-admins may only touch their own
    /// company; a mismatching explicit request is a scope violation (→ 403 on /api/v1).
    /// Super-admins act AS the chosen company (default: their own).</summary>
    private int ResolveTargetCompany(int? requested)
    {
        if (tenant.IsSuperAdmin) return requested ?? tenant.CompanyId;
        if (requested is not null && requested != tenant.CompanyId)
            throw new DomainException("rbac.cross_company.scope_required",
                "You may only manage your own company.");
        return tenant.CompanyId;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- Phase A: read -----------------------------------------------------

    public async Task<IReadOnlyList<RoleListItem>> ListRolesAsync(int? companyId, CancellationToken ct)
    {
        var target = ResolveTargetCompany(companyId);
        var today = Today;

        // Concrete company filter excludes SUPER_ADMIN (company_id NULL). UserCount =
        // distinct users with an ACTIVE user_role for the role in this company; the
        // active predicate is inlined (UserRole.IsActiveOn won't translate to SQL).
        return await db.Roles.AsNoTracking()
            .Where(r => r.CompanyId == target)
            .OrderBy(r => r.RoleCode)
            .Select(r => new RoleListItem(
                r.RoleId, r.RoleCode, r.RoleName, r.Description, r.IsSystem,
                db.UserRoles.Where(ur => ur.RoleId == r.RoleId
                        && ur.CompanyId == target
                        && ur.ValidFrom <= today
                        && (ur.ValidTo == null || ur.ValidTo >= today))
                    .Select(ur => ur.UserId).Distinct().Count(),
                db.RolePermissions.Count(rp => rp.RoleId == r.RoleId)))
            .ToListAsync(ct);
    }

    public async Task<RoleDetail> GetRoleAsync(int roleId, CancellationToken ct)
    {
        // Load by id, then scope-check (mirrors the write methods). A super-admin may
        // open any company's role; a regular admin is pinned to their own company and a
        // mismatch returns .not_found so a cross-company id leaks nothing. SUPER_ADMIN
        // (company_id NULL) is never surfaced through this endpoint.
        var role = await db.Roles.AsNoTracking()
            .Where(r => r.RoleId == roleId && r.CompanyId != null)
            .Select(r => new { r.RoleId, r.CompanyId, r.RoleCode, r.RoleName, r.Description, r.IsSystem })
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

        if (!tenant.IsSuperAdmin && role.CompanyId != tenant.CompanyId)
            throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

        var codes = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission!.PermissionCode)
            .OrderBy(c => c)
            .ToArrayAsync(ct);

        return new RoleDetail(role.RoleId, role.CompanyId, role.RoleCode, role.RoleName,
            role.Description, role.IsSystem, codes);
    }

    // ---- Phase B: write ----------------------------------------------------

    public async Task SetRolePermissionsAsync(int roleId, SetRolePermissionsRequest req, CancellationToken ct)
    {
        var role = await db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
            ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

        GuardEditable(role);
        ResolveTargetCompany(role.CompanyId);   // cross-company → scope_required

        var requested = (req.PermissionCodes ?? []).Distinct().ToArray();

        // Resolve codes → ids; every code MUST exist in the catalog.
        var known = await db.Permissions.AsNoTracking()
            .Where(p => requested.Contains(p.PermissionCode))
            .Select(p => new { p.PermissionId, p.PermissionCode })
            .ToListAsync(ct);
        if (known.Count != requested.Length)
        {
            var unknown = requested.Except(known.Select(k => k.PermissionCode)).ToArray();
            throw new DomainException("rbac.unknown_permission",
                $"Unknown permission code(s): {string.Join(", ", unknown)}");
        }

        var existingCodes = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission!.PermissionCode)
            .ToListAsync(ct);

        var added = requested.Except(existingCodes).OrderBy(c => c).ToArray();
        var removed = existingCodes.Except(requested).OrderBy(c => c).ToArray();
        if (added.Length == 0 && removed.Length == 0) return;   // no-op

        // Whole-set replace: drop all, re-add the requested set with the role's company.
        var current = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);
        db.RolePermissions.RemoveRange(current);
        foreach (var p in known)
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = p.PermissionId,
                CompanyId = role.CompanyId,   // denormalized owning company
            });

        activity.Record("role", roleId, null, role.CompanyId!.Value, "rbac_grant_change",
            note: DiffNote(added, removed), module: "sys");
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CreateRoleAsync(CreateRoleRequest req, CancellationToken ct)
    {
        var target = ResolveTargetCompany(req.CompanyId);
        var code = (req.RoleCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("rbac.role_code_required", "Role code is required.");
        if (string.Equals(code, SuperAdmin, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("rbac.super_admin_locked", "SUPER_ADMIN is reserved.");
        if (await db.Roles.AnyAsync(r => r.CompanyId == target && r.RoleCode == code, ct))
            throw new DomainException("rbac.role_code_duplicate",
                $"Role code '{code}' already exists in this company.");

        var role = new Role
        {
            CompanyId = target,
            RoleCode = code,
            RoleName = req.NameTh,
            Description = req.Description,
            IsSystem = false,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);   // assign RoleId before auditing

        activity.Record("role", role.RoleId, null, target, "role_created",
            note: $"code={code}", module: "sys");
        await db.SaveChangesAsync(ct);
        return role.RoleId;
    }

    public async Task UpdateRoleAsync(int roleId, UpdateRoleRequest req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
            ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

        GuardEditable(role);                    // SUPER_ADMIN / null-company refused
        ResolveTargetCompany(role.CompanyId);   // cross-company → scope_required

        role.RoleName = req.NameTh;             // rename only — never role_code / company
        role.Description = req.Description;

        activity.Record("role", roleId, null, role.CompanyId!.Value, "role_updated",
            note: $"name={req.NameTh}", module: "sys");
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRoleAsync(int roleId, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
            ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

        GuardEditable(role);
        ResolveTargetCompany(role.CompanyId);

        if (role.IsSystem)
            throw new DomainException("rbac.role_is_system", "System roles cannot be deleted.");

        var today = Today;
        var inUse = await db.UserRoles.AnyAsync(ur => ur.RoleId == roleId
            && ur.ValidFrom <= today
            && (ur.ValidTo == null || ur.ValidTo >= today), ct);
        if (inUse)
            throw new DomainException("rbac.role_in_use",
                "Role is assigned to active users and cannot be deleted.");

        // Hard delete — grants cascade. Audit BEFORE the row vanishes.
        activity.Record("role", roleId, null, role.CompanyId!.Value, "role_deleted",
            note: $"code={role.RoleCode}", module: "sys");
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }

    // ---- Phase C: user-role assignment -------------------------------------

    public async Task<IReadOnlyList<UserListItem>> ListUsersAsync(int? companyId, CancellationToken ct)
    {
        var target = ResolveTargetCompany(companyId);

        // "Users in company X" = users with ≥1 user_role in this company; list their
        // roles SCOPED to this company (excludes SUPER_ADMIN via the company filter).
        var userIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.CompanyId == target)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .OrderBy(u => u.Username)
            .Select(u => new { u.UserId, u.Username, u.FullName, u.IsActive, u.IsSuperAdmin })
            .ToListAsync(ct);

        // Roles per user, scoped to the target company. Join through user_roles so we
        // only surface the company-scoped roles (a user may have roles in other companies).
        var roleRows = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.CompanyId == target && userIds.Contains(ur.UserId))
            .Select(ur => new { ur.UserId, ur.Role!.RoleId, ur.Role.RoleCode, ur.Role.RoleName })
            .ToListAsync(ct);

        var rolesByUser = roleRows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.DistinctBy(r => r.RoleId)
                      .OrderBy(r => r.RoleCode)
                      .Select(r => new RoleRef(r.RoleId, r.RoleCode, r.RoleName))
                      .ToArray());

        return users
            .Select(u => new UserListItem(u.UserId, u.Username, u.FullName, u.IsActive, u.IsSuperAdmin,
                rolesByUser.TryGetValue(u.UserId, out var rr) ? rr : []))
            .ToList();
    }

    public async Task SetUserRolesAsync(long userId, SetUserRolesRequest req, CancellationToken ct)
    {
        // Target = the company whose role-set we're editing for this user. Super-admins may
        // pass any company (cross-company management); company-admins are pinned to their own
        // (a foreign id → rbac.cross_company.scope_required). §4.7 multi-tenant isolation.
        var target = ResolveTargetCompany(req.CompanyId);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");

        var requestedIds = (req.RoleIds ?? []).Distinct().ToArray();

        // Anti-lockout (§4.7 compliance): an admin must not strip their own last role
        // in their own company — that would lock them out of administration.
        bool isSelf = tenant.UserId == userId && tenant.CompanyId == target;
        if (isSelf && requestedIds.Length == 0)
            throw new DomainException("rbac.self_lockout",
                "You cannot remove all of your own roles.");

        // Every requested role MUST belong to the target company; SUPER_ADMIN is never assignable.
        var validRoles = await db.Roles.AsNoTracking()
            .Where(r => requestedIds.Contains(r.RoleId) && r.CompanyId == target)
            .Select(r => new { r.RoleId, r.RoleCode })
            .ToListAsync(ct);
        if (validRoles.Count != requestedIds.Length)
            throw new DomainException("rbac.role_company_mismatch",
                "One or more roles do not belong to this company.");

        // Whole-set replace of this user's PER-COMPANY role assignments for the target company.
        // CRITICAL (anti-lockout, §4.7): scope by the ROLE's company (ur.Role.CompanyId == target),
        // NOT just ur.CompanyId. A super-admin's user_role -> SUPER_ADMIN row has ur.CompanyId = the
        // company but role.company_id IS NULL; the replacement set (per-company roles only) can never
        // re-include SUPER_ADMIN, so deleting by ur.CompanyId alone would silently strip that user's
        // system-global assignment (their company context). Leave global rows untouched.
        var existing = await db.UserRoles
            .Where(ur => ur.UserId == userId && ur.CompanyId == target && ur.Role!.CompanyId == target)
            .ToListAsync(ct);
        var beforeCodes = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.CompanyId == target && ur.Role!.CompanyId == target)
            .Select(ur => ur.Role!.RoleCode)
            .OrderBy(c => c)
            .ToListAsync(ct);

        db.UserRoles.RemoveRange(existing);
        var today = Today;
        foreach (var r in validRoles)
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = r.RoleId,
                CompanyId = target,
                BranchId = 0,           // 0 = all branches in this company
                ValidFrom = today,
                ValidTo = null,
            });

        var afterCodes = validRoles.Select(r => r.RoleCode).OrderBy(c => c).ToArray();
        activity.Record("user", userId, user.Username, target, "user_role_change",
            note: $"[{string.Join(",", beforeCodes)}] -> [{string.Join(",", afterCodes)}]",
            module: "sys");
        await db.SaveChangesAsync(ct);
    }

    // ---- helpers -----------------------------------------------------------

    private static void GuardEditable(Role role)
    {
        if (role.CompanyId is null || string.Equals(role.RoleCode, SuperAdmin, StringComparison.Ordinal))
            throw new DomainException("rbac.super_admin_locked",
                "The system-global SUPER_ADMIN role cannot be modified.");
    }

    private static string DiffNote(string[] added, string[] removed)
    {
        var parts = new List<string>();
        if (added.Length > 0) parts.Add("+" + string.Join(",", added));
        if (removed.Length > 0) parts.Add("-" + string.Join(",", removed));
        return string.Join(" ", parts);
    }
}
