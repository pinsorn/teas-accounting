namespace Accounting.Domain.Entities.Purchase;

/// <summary>
/// Sprint 12 — pure PO ↔ VI loose-matching decision. ≥95% of PO total →
/// auto-close; &gt;105% → over-receipt warning (HTTP 200 chip, not an error).
/// Kept pure + in Domain so the thresholds are unit-tested independently of
/// the VendorInvoice posting pipeline.
/// </summary>
public static class PoSettlement
{
    public const decimal CloseThreshold = 0.95m;
    public const decimal OverReceiptTolerance = 1.05m;

    public static (bool ShouldClose, bool OverReceipt) Evaluate(
        decimal linkedViTotal, decimal poTotal)
    {
        if (poTotal <= 0m) return (false, false);
        var close = linkedViTotal >= poTotal * CloseThreshold;
        var over = linkedViTotal > poTotal * OverReceiptTolerance;
        return (close, over);
    }
}
