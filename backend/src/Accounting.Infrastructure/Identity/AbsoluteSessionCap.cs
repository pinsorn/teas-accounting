using System.Security.Claims;
using Accounting.Application.Abstractions;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// 2026-07 review fix F-A — the WP2.1 absolute-session-cap check, extracted so
/// <c>/auth/refresh</c> and <c>CompanySwitchService.SwitchAsync</c> (the two paths that
/// re-issue a JWT for an already-authenticated caller) can never drift. Reproduces exactly
/// what <c>/auth/refresh</c> enforced before this extraction: the <c>auth_time</c> claim must
/// be present/parseable AND within <paramref name="capHours"/> of <c>clock.UtcNow</c>.
///
/// Before this fix, <c>CompanySwitchService</c> re-issued a token with NO cap check at all and
/// without carrying <c>auth_time</c> forward, so JwtTokenIssuer stamped a fresh "now" — each
/// switch silently reset the absolute-cap clock, letting a super-admin session slide past
/// AbsoluteSessionCapHours forever via repeated switch-company calls.
/// </summary>
public static class AbsoluteSessionCap
{
    /// <summary>The RFC-7807 <c>title</c>/<c>DomainException.Code</c> both callers use for the
    /// 403 they build on <see cref="SessionAbsoluteCapExceededException"/>.</summary>
    public const string ProblemCode = "auth.session_absolute_cap_exceeded";

    /// <summary>Returns the ORIGINAL <c>auth_time</c> on success (for the caller to forward into
    /// the re-issued token's <see cref="TokenClaims.AuthTime"/>, so it never resets). Throws
    /// <see cref="SessionAbsoluteCapExceededException"/> — NOT a bare <c>DomainException</c> —
    /// when the claim is missing/unparseable or the cap is exceeded, because
    /// DomainExceptionMiddleware maps any <c>auth.*</c>-coded DomainException to 401; this check
    /// must always surface as 403 (a full re-login is required, but the caller IS authenticated).
    /// Callers must catch it explicitly and build the 403 problem themselves.</summary>
    public static DateTimeOffset CheckOrThrow(ClaimsPrincipal principal, IClock clock, double capHours)
    {
        var authTimeRaw = principal.FindFirst("auth_time")?.Value;
        if (!long.TryParse(authTimeRaw, out var authTimeUnix))
            throw new SessionAbsoluteCapExceededException(
                "Token predates the sliding-session feature; please log in again.");

        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        if (clock.UtcNow - authTime > TimeSpan.FromHours(capHours))
            throw new SessionAbsoluteCapExceededException(
                "Session exceeded its absolute lifetime; please log in again.");

        return authTime;
    }
}

/// <summary>Thrown by <see cref="AbsoluteSessionCap.CheckOrThrow"/>. Always maps to 403
/// (<see cref="AbsoluteSessionCap.ProblemCode"/>) at the endpoint layer — deliberately NOT a
/// <c>DomainException</c>, since the generic <c>auth.*</c> middleware convention would give 401.</summary>
public sealed class SessionAbsoluteCapExceededException(string detail) : Exception(detail);
