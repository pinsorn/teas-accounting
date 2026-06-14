namespace Accounting.Domain.Entities.Identity;

public class RolePermission
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }

    /// <summary>
    /// Denormalized owning company copied from the role (Sprint 13k — per-company RBAC), so RLS
    /// on <c>sys.role_permissions</c> can filter directly without joining <c>sys.roles</c>.
    /// NULL only for the system-global SUPER_ADMIN grants.
    /// </summary>
    public int? CompanyId { get; set; }
}
