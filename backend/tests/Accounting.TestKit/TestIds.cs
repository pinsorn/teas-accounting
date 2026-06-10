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

    // cont.75 — these used a truncated 3–4 char suffix (≈4K–65K space). On the long-lived
    // shared teas_test DB that saturates after enough historical rows → 23505 unique-violation
    // flakes (e.g. ix_expense_categories_company_id_category_code). Columns are all ≤20, so use
    // the FULL 8-char suffix (16^8 ≈ 4.3B); "EXP-A1B2C3D4" (12) / "BUA1B2C3D4" (10) still fit.
    /// <summary>BU code: uppercase, 20-char column.</summary>
    public static string BusinessUnitCode(string prefix = "BU") =>
        $"{prefix}{Suffix().ToUpperInvariant()}";

    /// <summary>Expense category code: uppercase, 20-char column.</summary>
    public static string ExpenseCategoryCode(string prefix = "EXP") =>
        $"{prefix}-{Suffix().ToUpperInvariant()}";

    /// <summary>WHT type code: uppercase, 20-char column.</summary>
    public static string WhtTypeCode(string prefix = "WHT") =>
        $"{prefix}-{Suffix().ToUpperInvariant()}";

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

    /// <summary>A fiscal-year label far in the future with a wide random spread —
    /// tax.cit_year_summaries is unique per (company, fiscal_year) on the shared
    /// teas_test DB. Callers that upsert-then-assert should still tolerate (or
    /// delete) a pre-existing row for the chosen year.</summary>
    public static int FutureFiscalYear() => 2200 + Random.Shared.Next(0, 5000);
}
