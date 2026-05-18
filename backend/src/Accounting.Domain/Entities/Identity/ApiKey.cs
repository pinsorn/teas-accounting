using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Identity;

public class ApiKey : ITenantOwned
{
    public long ApiKeyId { get; set; }
    public int CompanyId { get; set; }
    public required string Name { get; set; }
    public required string KeyHash { get; set; }
    public required string KeyPrefix { get; set; }

    /// <summary>JSON array of scope strings — stored as JSONB.</summary>
    public required string ScopesJson { get; set; }

    public long CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public long? RevokedBy { get; set; }
    public bool IsActive { get; set; } = true;
}
