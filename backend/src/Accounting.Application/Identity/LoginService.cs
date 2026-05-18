using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;

namespace Accounting.Application.Identity;

public sealed record LoginRequest(string Username, string Password, string? MfaCode);
public sealed record LoginResult(AccessToken Token, bool MfaRequired);

public interface ILoginService
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct);
}

/// <summary>
/// Username + password + optional TOTP. Lockout: 5 failed attempts → locked 15 minutes.
/// </summary>
public sealed class LoginService : ILoginService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService    _totp;
    private readonly IJwtTokenIssuer _tokens;
    private readonly IClock          _clock;
    private readonly IPermissionLookup _permissions;

    public LoginService(
        IUserRepository users,
        IPasswordHasher hasher,
        ITotpService totp,
        IJwtTokenIssuer tokens,
        IClock clock,
        IPermissionLookup permissions)
    {
        _users      = users;
        _hasher     = hasher;
        _totp       = totp;
        _tokens     = tokens;
        _clock      = clock;
        _permissions = permissions;
    }

    public async Task<LoginResult> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await _users.FindByUsernameAsync(req.Username, ct)
            ?? throw new DomainException("auth.invalid_credentials", "Invalid username or password.");

        var now = _clock.UtcNow;
        if (user.IsLocked(now))
            throw new DomainException("auth.account_locked", "Account temporarily locked. Try again later.");
        if (!user.IsActive)
            throw new DomainException("auth.account_disabled", "Account is disabled.");

        if (!_hasher.Verify(req.Password, user.PasswordHash))
        {
            await _users.RegisterFailedLoginAsync(user, now, ct);
            throw new DomainException("auth.invalid_credentials", "Invalid username or password.");
        }

        if (user.HasMfa)
        {
            if (string.IsNullOrWhiteSpace(req.MfaCode))
                return new LoginResult(new AccessToken("", DateTimeOffset.MinValue), MfaRequired: true);

            if (!_totp.Verify(user.MfaSecretEnc!, req.MfaCode))
            {
                await _users.RegisterFailedLoginAsync(user, now, ct);
                throw new DomainException("auth.invalid_mfa", "Invalid MFA code.");
            }
        }

        await _users.RegisterSuccessfulLoginAsync(user, now, ct);

        // Pick the primary (company, branch) — for Phase 1 just take the first active assignment.
        var assignment = user.Roles
            .Where(r => r.IsActiveOn(_clock.TodayInBangkok()))
            .OrderBy(r => r.CompanyId).ThenBy(r => r.BranchId)
            .FirstOrDefault();

        if (assignment is null && !user.IsSuperAdmin)
            throw new DomainException("auth.no_company_assignment", "User has no active company assignment.");

        var companyId = assignment?.CompanyId ?? 0;
        var branchId  = assignment?.BranchId  ?? 0;
        var (roles, perms) = await _permissions.LoadAsync(user.UserId, companyId, ct);

        var token = _tokens.Issue(new TokenClaims(
            UserId: user.UserId,
            Username: user.Username,
            CompanyId: companyId,
            BranchId: branchId,
            IsSuperAdmin: user.IsSuperAdmin,
            Roles: roles,
            Permissions: perms));

        return new LoginResult(token, MfaRequired: false);
    }
}

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct);
    Task RegisterFailedLoginAsync(User user, DateTimeOffset now, CancellationToken ct);
    Task RegisterSuccessfulLoginAsync(User user, DateTimeOffset now, CancellationToken ct);
}

public interface IPermissionLookup
{
    Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadAsync(
        long userId, int companyId, CancellationToken ct);
}
