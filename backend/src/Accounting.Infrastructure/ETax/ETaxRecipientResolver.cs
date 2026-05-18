using Accounting.Domain.Common;

namespace Accounting.Infrastructure.ETax;

/// <summary>Resolved e-Tax email recipients after the Tier-2 safety guard.</summary>
public readonly record struct ETaxResolvedRecipient(string To, string Cc, bool Redirected);

/// <summary>
/// Sprint 13c — pure recipient-safety logic (no IO/config object construction),
/// so the Tier-1/2/3 redirect + whitelist behaviour is unit-testable without
/// SMTP. Used by <see cref="ETaxEmailSender"/>.
/// </summary>
public static class ETaxRecipientResolver
{
    /// <summary>
    /// If <paramref name="redirectAllTo"/> is set → divert both To and Cc there
    /// (Tier 1 dev / Tier 2 UAT). Otherwise the real customer + RD cc (Tier 3).
    /// </summary>
    public static ETaxResolvedRecipient Resolve(string intendedTo, string defaultCc, string? redirectAllTo)
    {
        if (!string.IsNullOrWhiteSpace(redirectAllTo))
            return new ETaxResolvedRecipient(redirectAllTo!, redirectAllTo!, Redirected: true);
        return new ETaxResolvedRecipient(intendedTo, defaultCc, Redirected: false);
    }

    /// <summary>
    /// True when no whitelist is configured, or <paramref name="email"/>'s domain
    /// is one of <paramref name="whitelistDomains"/> (case-insensitive).
    /// </summary>
    public static bool IsWhitelisted(string email, string[]? whitelistDomains)
    {
        if (whitelistDomains is not { Length: > 0 })
            return true;
        return whitelistDomains.Any(d =>
            email.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Throws <c>etax.email.whitelist_violation</c> when a whitelist is configured
    /// and <paramref name="actualTo"/> is outside it. No-op otherwise.
    /// </summary>
    public static void EnsureWhitelisted(string actualTo, string[]? whitelistDomains)
    {
        if (!IsWhitelisted(actualTo, whitelistDomains))
            throw new DomainException("etax.email.whitelist_violation",
                $"Recipient {actualTo} not in whitelist. Configured for " +
                $"{string.Join(",", whitelistDomains!)}");
    }
}
