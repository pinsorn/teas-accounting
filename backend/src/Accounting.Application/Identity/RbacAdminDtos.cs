namespace Accounting.Application.Identity;

// Sprint 13k — per-company RBAC admin API (Phases A–C). All operations are scoped
// to a single company via ITenantContext + ResolveTargetCompany; the system-global
// SUPER_ADMIN role (company_id NULL) is never returned, edited or assigned here.

// ---- Phase A: read ---------------------------------------------------------

/// <summary>One catalog permission, rendered by the FE role editor.</summary>
public sealed record PermissionCatalogItem(string Code, string Module, string LabelTh, string LabelEn);

/// <summary>Role row for the per-company roles list. Excludes SUPER_ADMIN.</summary>
public sealed record RoleListItem(
    int RoleId, string RoleCode, string NameTh, string? Description,
    bool IsSystem, int UserCount, int PermissionCount);

/// <summary>Full role detail incl. its granted permission codes.</summary>
public sealed record RoleDetail(
    int RoleId, int? CompanyId, string RoleCode, string NameTh, string? Description,
    bool IsSystem, string[] PermissionCodes);

// ---- Phase B: write --------------------------------------------------------

public sealed record SetRolePermissionsRequest(string[] PermissionCodes);

public sealed record CreateRoleRequest(string RoleCode, string NameTh, string? Description, int? CompanyId);

public sealed record UpdateRoleRequest(string NameTh, string? Description);

// ---- Phase C: user-role assignment ----------------------------------------

public sealed record RoleRef(int RoleId, string RoleCode, string NameTh);

public sealed record UserListItem(
    long UserId, string Username, string FullName, bool IsActive, bool IsSuperAdmin, RoleRef[] Roles);

/// <summary>Whole-set role assignment for a user. <c>CompanyId</c> selects the company
/// whose role-set is being edited: super-admins may target any company; company-admins
/// are pinned to their own (a foreign id → <c>rbac.cross_company.scope_required</c>).</summary>
public sealed record SetUserRolesRequest(int[] RoleIds, int? CompanyId = null);

/// <summary>
/// Per-company RBAC admin service. Every method resolves a target company through
/// <c>ResolveTargetCompany</c>: non-super-admins are pinned to their own company and
/// a cross-company request throws <c>rbac.cross_company.scope_required</c>; super-admins
/// may target any company (acting AS that company). See §4.7 multi-tenant isolation.
/// </summary>
public interface IRbacAdminService
{
    // Phase A — read (gate sys.role.manage)
    Task<IReadOnlyList<RoleListItem>> ListRolesAsync(int? companyId, CancellationToken ct);
    Task<RoleDetail> GetRoleAsync(int roleId, CancellationToken ct);

    // Phase B — write (gate sys.role.manage)
    Task SetRolePermissionsAsync(int roleId, SetRolePermissionsRequest req, CancellationToken ct);
    Task<int> CreateRoleAsync(CreateRoleRequest req, CancellationToken ct);
    Task UpdateRoleAsync(int roleId, UpdateRoleRequest req, CancellationToken ct);
    Task DeleteRoleAsync(int roleId, CancellationToken ct);

    // Phase C — user-role assignment (gate sys.user.manage)
    Task<IReadOnlyList<UserListItem>> ListUsersAsync(int? companyId, CancellationToken ct);
    Task SetUserRolesAsync(long userId, SetUserRolesRequest req, CancellationToken ct);
}
