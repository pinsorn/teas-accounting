namespace Accounting.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record TokenClaims(
    long UserId,
    string Username,
    int CompanyId,
    int BranchId,
    bool IsSuperAdmin,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    /// <summary>WP2.1 (D6 sliding re-issue) — the ORIGINAL login time, carried forward on every
    /// re-issue (switch-company, refresh) so the absolute session cap can be enforced from the
    /// true login moment, not reset on every slide. Null = fresh login (Issue stamps "now").</summary>
    DateTimeOffset? AuthTime = null);

public interface IJwtTokenIssuer
{
    AccessToken Issue(TokenClaims claims);
}
