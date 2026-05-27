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
///  • A <b>non-VAT</b> company (Tax:VatMode=false — ม.86 / CLAUDE.md §4.6) never carries VAT
///    on any line: the tax rate is forced to 0 and the code to VAT0 regardless of input.
/// Chain-copy paths (DO→Invoice, Q→SO, …) inherit from the already-normalized source line,
/// so the guards only need to run at the request-fed origin builders.
/// </summary>
internal static class SalesLineBackstop
{
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
    /// Effective (productType, taxRate, taxCode) for a line after both guards.
    /// Call before <c>ChainMath.Line</c> so the VAT amount is computed from the forced rate.
    /// </summary>
    public static (string ProductType, decimal TaxRate, string TaxCode) Resolve(
        bool vatMode, long? productId, string? requestedType, decimal taxRate, string? taxCode,
        IReadOnlyDictionary<long, string> productTypes)
    {
        var type = productId is { } id && productTypes.TryGetValue(id, out var t)
            ? t
            : (requestedType ?? "GOOD");
        return vatMode ? (type, taxRate, taxCode ?? "VAT0") : (type, 0m, "VAT0");
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
