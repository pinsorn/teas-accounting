namespace Accounting.Domain.Entities.Identity;

/// <summary>
/// Granular permission. Code convention: "<module>.<resource>.<action>" — e.g. "sales.tax_invoice.post".
/// Module, resource, action are also stored separately so we can build matrix views.
/// </summary>
public class Permission
{
    public int PermissionId { get; set; }
    public required string PermissionCode { get; set; }
    public required string Module { get; set; }
    public required string Resource { get; set; }
    public required string Action { get; set; }
    public string? Description { get; set; }
}
