using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// Server-side compliance guards applied when a sales-chain document line is built
/// from a client request. These do NOT trust the caller:
///  • <b>ProductType</b> is snapshotted from the product master whenever a ProductId is
///    supplied — the WHT classification (SERVICE / EXEMPT_SERVICE → withholdable, ม.50 ทวิ)
///    must come from master data, not the request body.
///  • A <b>non-VAT</b> company (companies.vat_registered=false — ม.86 / CLAUDE.md §4.6) never carries VAT
///    on any line: the tax rate is forced to 0 and the code to VAT0 regardless of input.
///  • For a <b>VAT</b> company the per-line VAT RATE is DERIVED from company master data, never
///    the caller's <c>taxRate</c> (ม.80 / §4.6): a STANDARD output VAT code uses the company's
///    configured VatRate; an EXEMPT (ม.81) or ZERO-RATED (ม.80/1) code is forced to 0. This
///    closes the "VAT7 + taxRate:0 → 0-VAT tax invoice" hole — the caller's taxRate is ignored.
/// Chain-copy paths (DO→Invoice, Q→SO, …) inherit from the already-normalized source line,
/// so the guards only need to run at the request-fed origin builders.
/// </summary>
internal static class SalesLineBackstop
{
    /// <summary>fix-chain-conversion-integrity (F13/§3.1) — sentinel <c>tax_code_id</c> meaning
    /// "no master row backs this line's code" (non-VAT synthetic pair, or a tenant with no
    /// tax-code master at all). The column is NOT NULL with no FK (F1.16), so 0 — never a real
    /// identity value — is the honest, always-valid placeholder.</summary>
    public const int SYNTHETIC_TAX_CODE_ID = 0;

    /// <summary>Classification + identity of a per-company VAT tax code (tax.tax_codes).
    /// <c>Code</c> carries the MASTER ROW's casing (the lookup is case-insensitive; a matched
    /// line must store the master's casing, not whatever casing the caller sent — trap §9.2).</summary>
    public readonly record struct TaxCodeFlags(int TaxCodeId, string Code, bool IsExempt, bool IsZeroRated);

    /// <summary>The seeded standard output VAT code (ม.80) — used as the code for a VAT line
    /// whose request carried no tax code, so the label matches the charged rate.</summary>
    private const string StandardOutputVatCode = "VAT7";

    /// <summary>Product-master ProductType (RD screaming-snake form) keyed by ProductId.</summary>
    public static async Task<Dictionary<long, string>> LoadProductTypesAsync(
        AccountingDbContext db, IEnumerable<long?> productIds, CancellationToken ct)
    {
        var ids = productIds.Where(p => p.HasValue).Select(p => p!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, string>();
        var rows = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.ProductType })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ProductId, r => ToScreamingSnake(r.ProductType));
    }

    /// <summary>
    /// Exempt / zero-rated flags of the tax codes referenced by the request, keyed by code
    /// (case-insensitive). Mirrors <see cref="LoadProductTypesAsync"/> — only the codes the
    /// request actually uses are loaded. tax.tax_codes is company-scoped (EF tenant filter),
    /// so a code only resolves for the caller's own company. Used to DERIVE the line VAT rate
    /// (ม.80 / §4.6) instead of trusting the caller's taxRate.
    /// </summary>
    public static async Task<Dictionary<string, TaxCodeFlags>> LoadTaxCodeFlagsAsync(
        AccountingDbContext db, IEnumerable<string?> taxCodes, CancellationToken ct)
    {
        var codes = taxCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
            return new Dictionary<string, TaxCodeFlags>(StringComparer.OrdinalIgnoreCase);
        var rows = await db.TaxCodes.AsNoTracking()
            .Where(c => codes.Contains(c.Code))
            .Select(c => new { c.Code, c.TaxCodeId, c.IsExempt, c.IsZeroRated })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                // g.Key IS the master row's own casing (grouped from db.TaxCodes.Code) —
                // storing it (not the caller's casing) is what trap §9.2 requires.
                g => new TaxCodeFlags(g.First().TaxCodeId, g.Key, g.First().IsExempt, g.First().IsZeroRated),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>fix-chain-conversion-integrity (§3.1) — the tenant's own standard output VAT
    /// code (ม.80), used as the ladder's fallback (step 3) when a request either supplies no
    /// code or supplies one absent from this company's master (F13 — the "V7"/"VAT0" orphans).
    /// NEVER throws: returns null when the tenant has no tax-code master at all (raw-SQL-seeded
    /// companies — memory seed-cos-bypass-createasync-taxcodes), so ladder step 4 can fall back
    /// to today's hardcoded "VAT7" byte-for-byte. Deterministic pick: "VAT7" first if present,
    /// else the lowest id — so two calls in the same request never disagree.</summary>
    public static async Task<(int TaxCodeId, string Code)?> LoadStandardOutputTaxCodeAsync(
        AccountingDbContext db, CancellationToken ct)
    {
        var row = await db.TaxCodes.AsNoTracking()
            .Where(c => c.IsActive && c.Direction == Domain.Enums.TaxDirection.Output
                && !c.IsExempt && !c.IsZeroRated)
            .OrderBy(c => c.Code == StandardOutputVatCode ? 0 : 1)
            .ThenBy(c => c.TaxCodeId)
            .Select(c => new { c.TaxCodeId, c.Code })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.TaxCodeId, row.Code);
    }

    /// <summary>
    /// Effective (productType, taxRate, taxCode, taxCodeId) for a line after all guards.
    /// Call before <c>ChainMath.Line</c> so the VAT amount is computed from the DERIVED rate.
    ///
    /// ม.80 / §4.6 — for a VAT-registered company the per-line VAT RATE is company master data,
    /// NOT caller input. The caller's <paramref name="requestedRate"/> is IGNORED (trap §9.1).
    ///
    /// fix-chain-conversion-integrity (§3.1) — the resolution ladder, exact, in order:
    ///   1. non-VAT company                          → (type, 0, "VAT0", SYNTHETIC_TAX_CODE_ID).
    ///   2. code supplied AND found in this company's
    ///      master                                    → (type, 0 if exempt/zero-rated else
    ///      companyVatRate, matchedRow.Code, matchedRow.TaxCodeId) — the MASTER's casing, not
    ///      the caller's (trap §9.2).
    ///   3. code null/blank, OR supplied but NOT found
    ///      (F13 — "V7", a foreign tenant's code, a typo) → the company's standard output code:
    ///      (type, companyVatRate, standardOutput.Code, standardOutput.TaxCodeId).
    ///   4. standard output code not found (tenant has
    ///      no tax-code master at all)                 → (type, companyVatRate, "VAT7",
    ///      SYNTHETIC_TAX_CODE_ID) — byte-for-byte today's code, sentinel id (trap §9.3).
    ///
    /// Money invariant: TaxRate is identical to today's for every input — only TaxCode/TaxCodeId
    /// change. <paramref name="standardOutput"/> must be loaded ONCE per request, never inside
    /// the per-line loop (trap §9.4).
    /// </summary>
    public static (string ProductType, decimal TaxRate, string TaxCode, int TaxCodeId) Resolve(
        bool vatMode, decimal companyVatRate, long? productId, string? requestedType,
        decimal requestedRate, string? taxCode,
        IReadOnlyDictionary<long, string> productTypes,
        IReadOnlyDictionary<string, TaxCodeFlags> taxCodeFlags,
        (int TaxCodeId, string Code)? standardOutput)
    {
        var type = productId is { } id && productTypes.TryGetValue(id, out var t)
            ? t
            : (requestedType ?? "GOOD");

        // ม.86 / §4.6 — a non-VAT company never carries VAT on any line.
        if (!vatMode)
            return (type, 0m, "VAT0", SYNTHETIC_TAX_CODE_ID);

        // Step 2 — code supplied and found in THIS company's master (case-insensitive lookup).
        if (!string.IsNullOrWhiteSpace(taxCode) && taxCodeFlags.TryGetValue(taxCode, out var flags))
        {
            var rate = (flags.IsExempt || flags.IsZeroRated) ? 0m : companyVatRate;
            return (type, rate, flags.Code, flags.TaxCodeId);
        }

        // Steps 3–4 — no code, or an orphan code: the company's own standard output code, or
        // (tenant has no tax-code master) the byte-for-byte legacy fallback with the sentinel id.
        return standardOutput is { } so
            ? (type, companyVatRate, so.Code, so.TaxCodeId)
            : (type, companyVatRate, StandardOutputVatCode, SYNTHETIC_TAX_CODE_ID);
    }

    // Mirror of the Product entity's value-converter string form (ProductConfiguration).
    private static string ToScreamingSnake(ProductType t) => t switch
    {
        ProductType.Service => "SERVICE",
        ProductType.ExemptGood => "EXEMPT_GOOD",
        ProductType.ExemptService => "EXEMPT_SERVICE",
        _ => "GOOD",
    };
}
