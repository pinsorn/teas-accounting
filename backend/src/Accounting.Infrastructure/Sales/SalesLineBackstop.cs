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
    /// <summary>Classification flags of a per-company VAT tax code (tax.tax_codes).</summary>
    public readonly record struct TaxCodeFlags(bool IsExempt, bool IsZeroRated);

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
            .Select(c => new { c.Code, c.IsExempt, c.IsZeroRated })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new TaxCodeFlags(g.First().IsExempt, g.First().IsZeroRated),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Effective (productType, taxRate, taxCode) for a line after all guards.
    /// Call before <c>ChainMath.Line</c> so the VAT amount is computed from the DERIVED rate.
    ///
    /// ม.80 / §4.6 — for a VAT-registered company the per-line VAT RATE is company master data,
    /// NOT caller input. The caller's <paramref name="requestedRate"/> is IGNORED; the rate is
    /// derived from the line's tax-code classification:
    ///   • non-VAT company           → rate 0, code VAT0 (ม.86 — unchanged);
    ///   • exempt (ม.81) code         → rate 0, code kept;
    ///   • zero-rated (ม.80/1) code   → rate 0, code kept;
    ///   • standard output VAT code   → rate = company <paramref name="companyVatRate"/>;
    ///   • null/empty code            → rate = company VatRate with the standard output code
    ///                                   <see cref="StandardOutputVatCode"/> (ม.80). A VAT-charged
    ///                                   line must never carry the VAT0 label, so we do NOT fall
    ///                                   back to "VAT0" here (that would be a 7%-rated VAT0 line).
    /// </summary>
    public static (string ProductType, decimal TaxRate, string TaxCode) Resolve(
        bool vatMode, decimal companyVatRate, long? productId, string? requestedType,
        decimal requestedRate, string? taxCode,
        IReadOnlyDictionary<long, string> productTypes,
        IReadOnlyDictionary<string, TaxCodeFlags> taxCodeFlags)
    {
        var type = productId is { } id && productTypes.TryGetValue(id, out var t)
            ? t
            : (requestedType ?? "GOOD");

        // ม.86 / §4.6 — a non-VAT company never carries VAT on any line.
        if (!vatMode)
            return (type, 0m, "VAT0");

        // ม.80 / §4.6 — VAT company: DERIVE the rate from master data, ignore requestedRate.
        if (string.IsNullOrWhiteSpace(taxCode))
            // No code supplied — default VAT: standard output code + the company's rate, so the
            // label matches the charged rate (never a 7%-rated "VAT0" line).
            return (type, companyVatRate, StandardOutputVatCode);

        if (taxCodeFlags.TryGetValue(taxCode, out var flags) && (flags.IsExempt || flags.IsZeroRated))
            // Exempt (ม.81) or zero-rated (ม.80/1) output — no VAT, keep the code.
            return (type, 0m, taxCode);

        // Standard output VAT code (or any unclassified taxable code) — the company's configured rate.
        return (type, companyVatRate, taxCode);
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
