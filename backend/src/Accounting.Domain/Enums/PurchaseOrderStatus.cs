namespace Accounting.Domain.Enums;

/// <summary>Sprint 12 — internal PO lifecycle (no external Sent/Confirmed).</summary>
public enum PurchaseOrderStatus
{
    Draft,
    Approved,
    Closed,
    Cancelled,
}
