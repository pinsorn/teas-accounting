namespace Accounting.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record TokenClaims(
    long UserId,
    string Username,
    int CompanyId,
    int BranchId,
    bool IsSuperAdmin,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public interface IJwtTokenIssuer
{
    AccessToken Issue(TokenClaims claims);
}
