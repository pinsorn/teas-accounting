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

    /// <summary>Sprint 13k §4.8 — the JWT name claim, used as the audit actor.
    /// Null for API-key callers (no human user).</summary>
    string? Username { get; }

    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }

    /// <summary>Sprint 14 — set when the caller is an external API key (no human
    /// <see cref="UserId"/>); null for JWT users. Used for audit + scope checks.</summary>
    long? ApiKeyId { get; }

    /// <summary>Sprint 14 P7 — the API key's bound Business Unit, if any.
    /// Null for JWT users or keys without a default. Drives auto-fill + lock.</summary>
    int? ApiKeyDefaultBusinessUnitId { get; }
}
