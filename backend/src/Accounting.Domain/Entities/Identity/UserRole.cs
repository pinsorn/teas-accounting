namespace Accounting.Domain.Entities.Identity;

/// <summary>
/// Assigns a user to a role inside a specific (company, branch) scope and validity window.
/// branch_id = 0 means "all branches in this company".
/// </summary>
public class UserRole
{
    public long UserId { get; set; }
    public User? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public bool IsActiveOn(DateOnly date) =>
        date >= ValidFrom && (ValidTo is null || date <= ValidTo);
}
