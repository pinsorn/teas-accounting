using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Identity;

/// <summary>System user account. Cross-tenant; tenant scoping comes from UserRole rows.</summary>
public class User : IAuditable, IConcurrencyVersioned
{
    public long UserId { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }

    /// <summary>AES-encrypted TOTP secret. NULL until user enrols in MFA.</summary>
    public byte[]? MfaSecretEnc { get; set; }

    public required string FullName { get; set; }
    public string? EmployeeCode { get; set; }

    /// <summary>เลขผู้ทำบัญชี (Certified Public Accountant / CPD number).</summary>
    public string? CpdNumber { get; set; }

    public bool IsSuperAdmin { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;
    public bool HasMfa => MfaSecretEnc is { Length: > 0 };
}
