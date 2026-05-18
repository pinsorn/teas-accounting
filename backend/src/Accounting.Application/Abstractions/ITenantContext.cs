namespace Accounting.Application.Abstractions;

/// <summary>
/// Per-request tenant + user context. Implemented in the API layer from the JWT claim set;
/// injected into DbContext to drive global query filters and audit columns.
/// </summary>
public interface ITenantContext
{
    int CompanyId { get; }
    int BranchId { get; }
    long? UserId { get; }
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }

    /// <summary>Sprint 14 — set when the caller is an external API key (no human
    /// <see cref="UserId"/>); null for JWT users. Used for audit + scope checks.</summary>
    long? ApiKeyId { get; }
}
