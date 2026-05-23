namespace Accounting.Application.Sales;

// Sprint (receipt itemize + multi-category WHT, 2026-05-22) — the compliance core.
// A bill can mix goods (no WHT) and multiple service categories (rent 5% / service
// 3% / ads 2%); a flat bill-wide rate is wrong. This pure allocator splits the
// withholding base by income category, pro-rata to the amount actually paid toward
// each applied Tax Invoice (partial payments scale down each line's base).
//
// PURE (no DB) so it is unit-tested without Postgres. Per-line WHT-type resolution
// (product → customer fallback) happens in the service layer BEFORE this runs; here
// every line already carries its resolved WhtTypeId (or null = no WHT).

/// <summary>One applied Tax Invoice line, with its income category already resolved.</summary>
public readonly record struct WhtAllocLine(
    decimal LineAmountExVat,
    string ProductType,
    int? WhtTypeId);

/// <summary>An applied Tax Invoice: the amount paid toward it (incl VAT) vs its
/// total (incl VAT) gives the pro-rata fraction for partial payments.</summary>
public readonly record struct WhtAllocApplication(
    decimal AppliedAmount,
    decimal TiTotalAmount,
    IReadOnlyList<WhtAllocLine> Lines);

/// <summary>The withholding base accumulated for one income category (rounded 2dp).</summary>
public readonly record struct WhtAllocResult(int WhtTypeId, decimal BaseAmount);

public static class ReceiptWhtAllocator
{
    /// <summary>
    /// Sum the service-line ex-VAT amounts, scaled by each TI's paid fraction,
    /// grouped by resolved WhtTypeId. Goods / exempt-goods and lines with no
    /// resolved WHT type are excluded. Bases are rounded to 2dp (away-from-zero),
    /// matching the rest of the WHT math.
    /// </summary>
    public static IReadOnlyList<WhtAllocResult> Allocate(IEnumerable<WhtAllocApplication> applications)
    {
        var byType = new Dictionary<int, decimal>();

        foreach (var app in applications)
        {
            // Guard a zero/non-positive TI total → no allocation (avoids div-by-zero
            // and nonsensical negative fractions).
            var fraction = app.TiTotalAmount > 0m
                ? app.AppliedAmount / app.TiTotalAmount
                : 0m;
            if (fraction <= 0m) continue;

            foreach (var line in app.Lines)
            {
                if (line.WhtTypeId is not { } typeId) continue;
                if (!IsService(line.ProductType)) continue;
                byType[typeId] = byType.GetValueOrDefault(typeId) + (line.LineAmountExVat * fraction);
            }
        }

        return byType
            .Where(kv => kv.Value > 0m)
            .Select(kv => new WhtAllocResult(
                kv.Key,
                Math.Round(kv.Value, 2, MidpointRounding.AwayFromZero)))
            .OrderBy(r => r.WhtTypeId)
            .ToList();
    }

    public static bool IsService(string productType) =>
        productType is "SERVICE" or "EXEMPT_SERVICE";
}
