namespace Accounting.Domain.Entities.Identity;

public class Role
{
    public int RoleId { get; set; }

    /// <summary>
    /// Owning company. NULL means a system-global role (only <see cref="SystemRoles.SuperAdmin"/>);
    /// every other role is per-company (Sprint 13k — per-company RBAC). Enforced by a DB CHECK
    /// (company_id NOT NULL OR role_code = 'SUPER_ADMIN') added in the reconcile SQL script.
    /// </summary>
    public int? CompanyId { get; set; }

    public required string RoleCode { get; set; }
    public required string RoleName { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();

    /// <summary>System-defined role codes — see <see cref="SystemRoles"/>.</summary>
    public static class SystemRoles
    {
        public const string SuperAdmin       = "SUPER_ADMIN";
        public const string CompanyAdmin     = "COMPANY_ADMIN";
        public const string ChiefAccountant  = "CHIEF_ACCOUNTANT";
        public const string Accountant       = "ACCOUNTANT";
        public const string ArClerk          = "AR_CLERK";
        public const string ApClerk          = "AP_CLERK";
        public const string SalesStaff       = "SALES_STAFF";
        public const string PurchasingStaff  = "PURCHASING_STAFF";
        public const string WarehouseStaff   = "WAREHOUSE_STAFF";
        public const string Approver         = "APPROVER";
        public const string Auditor          = "AUDITOR";
        public const string TaxOfficer       = "TAX_OFFICER";
    }
}
