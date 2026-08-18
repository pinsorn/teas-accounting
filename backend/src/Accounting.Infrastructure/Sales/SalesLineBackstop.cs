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
///  • An <b>EXEMPT_GOOD / EXEMPT_SERVICE product</b> (ม.81) NEVER carries VAT &gt; 0, regardless
///    of what tax code the caller sends — ม.81 exemption is a property of the product MASTER,
///    not of the request (N1, specs/fix-review-n-findings-2026-08-17.md §N1.1).
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

    /// <summary>Product-master tax defaults for the ม.81 exempt clamp (N1) — ProductType plus
    /// the tenant-curated DefaultOutputTaxCodeId (ladder step 3).</summary>
    public readonly record struct ProductTaxDefaults(string ProductType, int? DefaultOutputTaxCodeId);

    /// <summary>Every tax code of the CALLER'S OWN company (EF tenant filter,
    /// AccountingDbContext.cs:174 — no super-admin arm since 2026-07-08; RLS company_isolation
    /// is the second belt). Loaded ONCE per request. tax.tax_codes is 12 rows on a seeded
    /// tenant, so loading the whole master is cheaper than the round-trip that filtered it
    /// (N3), and it precomputes every lookup <see cref="Resolve"/> needs so it can stay
    /// synchronous (Unit A trap §9.4).</summary>
    public sealed class TaxCodeMaster
    {
        /// keyed case-INSENSITIVELY on the master row's Code (this is the N3 fix).
        public required IReadOnlyDictionary<string, TaxCodeFlags> ByCode { get; init; }
        /// keyed on tax_code_id — resolves Product.DefaultOutputTaxCodeId (ladder step 3).
        /// Only rows with Direction == Output && IsActive are present.
        public required IReadOnlyDictionary<int, TaxCodeFlags> ActiveOutputById { get; init; }
        /// ม.81 fallback (ladder step 4): lowest TaxCodeId among Direction==Output && IsActive && IsExempt.
        /// null when the tenant has no exempt output code at all → ladder step 5.
        public required TaxCodeFlags? ExemptOutputFallback { get; init; }
    }

    /// <summary>The seeded standard output VAT code (ม.80) — used as the code for a VAT line
    /// whose request carried no tax code, so the label matches the charged rate.</summary>
    private const string StandardOutputVatCode = "VAT7";

    /// <summary>N1 ladder step 5 — a THIRD documented synthetic pair, joining ("VAT0", 0) and
    /// ("VAT7", 0): an exempt product on a tenant whose master holds NO exempt output code at
    /// all (raw-SQL-seeded tenants — memory seed-cos-bypass-createasync-taxcodes). Rate 0 is
    /// the invariant that must hold even here.</summary>
    private const string ExemptOutputVatCode = "EXEMPT";

    /// <summary>Product-master ProductType (RD screaming-snake form) + tax defaults, keyed by
    /// ProductId (N1 — was ProductType only).</summary>
    public static async Task<Dictionary<long, ProductTaxDefaults>> LoadProductDefaultsAsync(
        AccountingDbContext db, IEnumerable<long?> productIds, CancellationToken ct)
    {
        var ids = productIds.Where(p => p.HasValue).Select(p => p!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, ProductTaxDefaults>();
        var rows = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.ProductType, p.DefaultOutputTaxCodeId })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ProductId,
            r => new ProductTaxDefaults(ToScreamingSnake(r.ProductType), r.DefaultOutputTaxCodeId));
    }

    /// <summary>
    /// N3 — loads every tax code of the caller's own company (drops the request-code filter
    /// entirely: tax.tax_codes is company-scoped by the EF tenant filter, so "all" already
    /// means "all of this company"). Serves three lookups from one rowset — this is also the
    /// whole of N1's exempt-fallback resolution.
    /// </summary>
    public static async Task<TaxCodeMaster> LoadTaxCodeMasterAsync(AccountingDbContext db, CancellationToken ct)
    {
        var rows = await db.TaxCodes.AsNoTracking()
            .Select(c => new { c.TaxCodeId, c.Code, c.IsExempt, c.IsZeroRated, c.Direction, c.IsActive })
            .ToListAsync(ct);

        // OrderBy(TaxCodeId) BEFORE GroupBy is new and load-bearing: the unique index is
        // (company_id, code) case-SENSITIVE (TaxCodeConfiguration.cs:29), so "VAT7" and "vat7"
        // can coexist in one company; without the ordering g.First() is non-deterministic
        // across calls.
        var byCode = rows
            .OrderBy(r => r.TaxCodeId)
            .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                // g.Key IS the master row's own casing (grouped from db.TaxCodes.Code) —
                // storing it (not the caller's casing) is what trap §9.2 requires.
                g => new TaxCodeFlags(g.First().TaxCodeId, g.Key, g.First().IsExempt, g.First().IsZeroRated),
                StringComparer.OrdinalIgnoreCase);

        var activeOutput = rows
            .Where(r => r.IsActive && r.Direction == TaxDirection.Output)
            .ToDictionary(r => r.TaxCodeId, r => new TaxCodeFlags(r.TaxCodeId, r.Code, r.IsExempt, r.IsZeroRated));

        var exemptFallback = activeOutput.Values
            .Where(f => f.IsExempt)
            .OrderBy(f => f.TaxCodeId)
            .Select(f => (TaxCodeFlags?)f)
            .FirstOrDefault();

        return new TaxCodeMaster
        {
            ByCode = byCode,
            ActiveOutputById = activeOutput,
            ExemptOutputFallback = exemptFallback,
        };
    }

    /// <summary>fix-chain-conversion-integrity (§3.1) — the tenant's own standard output VAT
    /// code (ม.80), used as the ladder's fallback (step 6) when a request either supplies no
    /// code or supplies one absent from this company's master (F13 — the "V7"/"VAT0" orphans).
    /// NEVER throws: returns null when the tenant has no tax-code master at all (raw-SQL-seeded
    /// companies — memory seed-cos-bypass-createasync-taxcodes), so ladder step 7 can fall back
    /// to today's hardcoded "VAT7" byte-for-byte. Deterministic pick: "VAT7" first if present,
    /// else the lowest id — so two calls in the same request never disagree.</summary>
    public static async Task<(int TaxCodeId, string Code)?> LoadStandardOutputTaxCodeAsync(
        AccountingDbContext db, CancellationToken ct)
    {
        var row = await db.TaxCodes.AsNoTracking()
            .Where(c => c.IsActive && c.Direction == TaxDirection.Output
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
    /// N1 (specs/fix-review-n-findings-2026-08-17.md §N1.1) — the resolution ladder, exact,
    /// in order. `exemptProduct` = productId resolved in <paramref name="productDefaults"/> AND
    /// its master ProductType is EXEMPT_GOOD or EXEMPT_SERVICE:
    ///
    ///   1. non-VAT company                        → (type, 0, "VAT0", SYNTHETIC_TAX_CODE_ID).
    ///   2. code supplied AND found in this company's master as `flags`:
    ///      2a. !exemptProduct
    ///            → (type, flags.IsExempt||IsZeroRated ? 0 : companyVatRate, flags.Code,
    ///               flags.TaxCodeId) — today's step 2, byte-identical (M2).
    ///      2b. exemptProduct AND flags.IsExempt
    ///            → (type, 0, flags.Code, flags.TaxCodeId) — the caller named a real ม.81
    ///               category; honour it.
    ///      2c. exemptProduct AND !flags.IsExempt (a taxable OR a zero-rated code)
    ///            → FALL THROUGH to step 3. ม.81 exemption is a property of the goods/service
    ///               (master data), so a non-exempt code on an exempt product is discarded.
    ///   3. exemptProduct AND the product's DefaultOutputTaxCodeId resolves, in THIS company's
    ///      master, to a row `d` with d.Direction==Output && d.IsActive && d.IsExempt
    ///            → (type, 0, d.Code, d.TaxCodeId) — the tenant curated the right category.
    ///   4. exemptProduct AND the company master holds at least one Output+IsActive+IsExempt code
    ///            → (type, 0, e.Code, e.TaxCodeId); e = ExemptOutputFallback — the LOWEST
    ///               TaxCodeId among that set, a REAL master row of this company (Unit A I4
    ///               case (a)). A real row (not a sentinel) so SalesCategorizer buckets EXEMPT,
    ///               not ZERO_RATED (which would also skew the ม.82/6 proportional-input ratio).
    ///   5. exemptProduct AND the company has NO exempt output code at all
    ///            → (type, 0, "EXEMPT", SYNTHETIC_TAX_CODE_ID) — a THIRD documented synthetic
    ///               pair (joins ("VAT0",0) and ("VAT7",0)). Rate 0 is the invariant that holds.
    ///   6. standardOutput present                 → (type, companyVatRate, so.Code, so.TaxCodeId).
    ///   7. otherwise (no tax-code master at all)   → (type, companyVatRate, "VAT7",
    ///      SYNTHETIC_TAX_CODE_ID) — byte-for-byte today's code, sentinel id (trap §9.3).
    ///
    /// Steps 3/4/5 are reachable ONLY when exemptProduct is true, and one of them always
    /// returns — so there is no path from exemptProduct==true to a rate greater than 0. A
    /// non-exempt product-linked line and every free-text line take exactly today's path
    /// (2a → 6 → 7); a free-text line claiming productType "EXEMPT_GOOD" does NOT get the
    /// clamp (no productId ⇒ productDefaults never resolves ⇒ exemptProduct is false).
    ///
    /// // ponytail: Rule D (deferred — specs/fix-review-n-findings-2026-08-17.md §N1.2) — a
    /// // GOOD/SERVICE product's DefaultOutputTaxCodeId is NOT consulted for a taxable line in
    /// // this unit; only the exempt clamp (steps 3–5) reads productDefaults for its rate.
    ///
    /// Money invariant: TaxRate is identical to today's for every non-exempt-product input —
    /// only TaxCode/TaxCodeId change (M2). <paramref name="standardOutput"/> must be loaded
    /// ONCE per request, never inside the per-line loop (trap §9.4).
    /// </summary>
    public static (string ProductType, decimal TaxRate, string TaxCode, int TaxCodeId) Resolve(
        bool vatMode, decimal companyVatRate, long? productId, string? requestedType,
        decimal requestedRate, string? taxCode,
        IReadOnlyDictionary<long, ProductTaxDefaults> productDefaults,
        TaxCodeMaster taxCodes,
        (int TaxCodeId, string Code)? standardOutput)
    {
        var productDefault = productId is { } id && productDefaults.TryGetValue(id, out var pd)
            ? pd
            : (ProductTaxDefaults?)null;
        var type = productDefault?.ProductType ?? (requestedType ?? "GOOD");

        // ม.86 / §4.6 — a non-VAT company never carries VAT on any line. [step 1]
        if (!vatMode)
            return (type, 0m, "VAT0", SYNTHETIC_TAX_CODE_ID);

        var exemptProduct = productDefault is { ProductType: "EXEMPT_GOOD" or "EXEMPT_SERVICE" };

        // Step 2 — code supplied and found in THIS company's master (case-insensitive lookup).
        if (!string.IsNullOrWhiteSpace(taxCode) && taxCodes.ByCode.TryGetValue(taxCode, out var flags))
        {
            if (!exemptProduct)   // 2a — unchanged, today's step 2.
            {
                var rate = (flags.IsExempt || flags.IsZeroRated) ? 0m : companyVatRate;
                return (type, rate, flags.Code, flags.TaxCodeId);
            }
            if (flags.IsExempt)   // 2b — an exempt product with a real exempt code: honour it.
                return (type, 0m, flags.Code, flags.TaxCodeId);
            // 2c — a taxable or zero-rated code on an exempt product: discarded, fall through.
        }

        if (exemptProduct)
        {
            // Step 3 — the product's own curated exempt output code.
            if (productDefault!.Value.DefaultOutputTaxCodeId is { } defId
                && taxCodes.ActiveOutputById.TryGetValue(defId, out var d) && d.IsExempt)
                return (type, 0m, d.Code, d.TaxCodeId);

            // Step 4 — the company's own exempt-output fallback (a real master row).
            if (taxCodes.ExemptOutputFallback is { } e)
                return (type, 0m, e.Code, e.TaxCodeId);

            // Step 5 — tenant has no exempt output code at all: synthetic pair, rate still 0.
            return (type, 0m, ExemptOutputVatCode, SYNTHETIC_TAX_CODE_ID);
        }

        // Steps 6–7 — no code, or an orphan code: the company's own standard output code, or
        // (tenant has no tax-code master) the byte-for-byte legacy fallback with the sentinel id.
        return standardOutput is { } so
            ? (type, companyVatRate, so.Code, so.TaxCodeId)
            : (type, companyVatRate, StandardOutputVatCode, SYNTHETIC_TAX_CODE_ID);
    }

    // Mirror of the Product entity's value-converter string form (ProductConfiguration).
    private static string ToScreamingSnake(ProductType t) => t switch
    {
        ProductType.Service        => "SERVICE",
        ProductType.ExemptGood     => "EXEMPT_GOOD",
        ProductType.ExemptService  => "EXEMPT_SERVICE",
        _ => "GOOD",
    };
}
