namespace Accounting.TestKit;

/// <summary>
/// Sprint 14.5 — unique-per-test identifiers for fields with UNIQUE
/// constraints in shared/long-lived test DBs. Use these in EVERY
/// integration + e2e test to kill gotcha §14 (test fixture non-idempotent
/// DB state — re-applied 7× across Phase 1).
///
/// Pattern: prefix + short Guid suffix — human-readable yet collision-free
/// across 1000s of historical rows + concurrent runs.
///
/// EVERY field with a UNIQUE constraint MUST use one of these (or an explicit
/// Guid). Hardcoded "ACME-001" / "yyyymm=202601" is forbidden in any test
/// that writes-then-reads against a long-lived shared DB.
/// </summary>
public static class TestIds
{
    /// <summary>8-char lowercase alphanumeric (hex) suffix from a fresh Guid.</summary>
    public static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    public static string CustomerCode(string prefix = "CUST") => $"{prefix}-{Suffix()}";
    public static string VendorCode  (string prefix = "VEND") => $"{prefix}-{Suffix()}";
    public static string ProductCode (string prefix = "PROD") => $"{prefix}-{Suffix()}";
    public static string BranchCode  (string prefix = "BR")   => $"{prefix}-{Suffix()}";

    /// <summary>BU code: short uppercase (BUs are constrained ≤20, uppercase-ish).</summary>
    public static string BusinessUnitCode(string prefix = "BU") =>
        $"{prefix}{Suffix()[..3].ToUpperInvariant()}";

    /// <summary>Expense category code: short uppercase.</summary>
    public static string ExpenseCategoryCode(string prefix = "EXP") =>
        $"{prefix}-{Suffix()[..4].ToUpperInvariant()}";

    /// <summary>WHT type code: short uppercase.</summary>
    public static string WhtTypeCode(string prefix = "WHT") =>
        $"{prefix}-{Suffix()[..4].ToUpperInvariant()}";

    /// <summary>Email with a deterministic domain for assertions.</summary>
    public static string Email(string prefix = "test") => $"{prefix}+{Suffix()}@example.com";

    /// <summary>13-digit Thai tax ID, test-only prefix '0000' so it can never
    /// collide with a real registered company.</summary>
    public static string TaxId() => $"0000{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    /// <summary>A yyyymm at least 12 months out + a wide random month spread —
    /// avoids finalize/lock/period collisions with prior runs. The spread is wide
    /// (≈1000 months ≈ 80 yr) so the collision space dwarfs the count of periods a
    /// long-lived shared DB ever finalizes; callers that finalize-then-assert on a
    /// PERSISTENT DB should ALSO clear any prior row for the chosen period.</summary>
    public static int FuturePeriod()
    {
        var months = 12 + Random.Shared.Next(1, 1000);
        var d = DateTime.UtcNow.AddMonths(months);
        return d.Year * 100 + d.Month;
    }

    /// <summary>Random user / API-key style name.</summary>
    public static string Name(string prefix = "Test") => $"{prefix} {Suffix()}";
}
