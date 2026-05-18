namespace Accounting.Application.Abstractions;

/// <summary>Canonical JWT claim type strings used by both the token issuer and the tenant context.</summary>
public static class TenantClaims
{
    public const string CompanyId    = "company_id";
    public const string BranchId     = "branch_id";
    public const string IsSuperAdmin = "is_super_admin";
    public const string Permission   = "perm";
}
