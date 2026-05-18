namespace Accounting.Application.Abstractions;

/// <summary>Canonical JWT claim type strings used by both the token issuer and the tenant context.</summary>
public static class TenantClaims
{
    public const string CompanyId    = "company_id";
    public const string BranchId     = "branch_id";
    public const string IsSuperAdmin = "is_super_admin";
    public const string Permission   = "perm";

    // Sprint 14 — external API key principal (no human user).
    public const string ApiKeyId            = "api_key_id";
    public const string ApiKeyName          = "api_key_name";
    public const string IsApiKey            = "is_api_key";
    public const string Scopes              = "scopes";              // CSV of permission strings
    public const string DefaultBusinessUnit = "default_business_unit_id";
}
