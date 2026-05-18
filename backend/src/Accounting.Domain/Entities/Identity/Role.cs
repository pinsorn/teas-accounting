namespace Accounting.Domain.Entities.Identity;

public class Role
{
    public int RoleId { get; set; }
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
