namespace Accounting.Domain.Common;

/// <summary>
/// Sprint 14 P7 — pure per-key Business-Unit binding rule (no IO; unit-tested).
/// Only the KEY-LOCK layer; the company-level <c>requires_business_unit</c>
/// rule stays where it already lives (Sprint 8 service logic) and runs on the
/// resolved value.
/// </summary>
public static class ApiKeyBuBinding
{
    public const string LockMismatch = "business_unit.locked_mismatch";

    /// <summary>
    /// <paramref name="keyBu"/> null (JWT user / unbound key) → request value
    /// passes through untouched. Bound key: omitted → auto-filled; same → ok;
    /// different → <see cref="LockMismatch"/>.
    /// </summary>
    public static (int? Effective, string? ErrorCode) Resolve(int? requestBu, int? keyBu)
    {
        if (keyBu is null) return (requestBu, null);
        if (requestBu is null) return (keyBu, null);
        if (requestBu == keyBu) return (keyBu, null);
        return (null, LockMismatch);
    }
}
