using Accounting.Application.Abstractions;

namespace Accounting.Application.Identity;

/// <summary>
/// Onboarding-switcher spec (2026-06-16) — super-admin re-scopes their session to a
/// different company. RLS is enforced at the DB session (<c>app.company_id</c> set per
/// request from the JWT), so switching company REQUIRES a freshly-issued JWT — a header
/// swap cannot move the tenant. This service validates the target and mints that token.
/// </summary>
public interface ICompanySwitchService
{
    /// <summary>
    /// Re-issues a JWT scoped to <paramref name="targetCompanyId"/> for the CURRENT
    /// (super-admin) caller. Throws:
    /// <list type="bullet">
    /// <item><c>auth.forbidden</c> — caller is not a super-admin (the endpoint maps this to 403);</item>
    /// <item><c>company.not_found</c> — target company does not exist or is inactive (→404).</item>
    /// </list>
    /// On success writes an <c>audit.activity_log</c> row (action <c>company_switch</c>) in the
    /// same transaction and returns the new access token.
    /// </summary>
    Task<AccessToken> SwitchAsync(int targetCompanyId, CancellationToken ct);
}
