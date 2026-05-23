namespace Accounting.Domain.Enums;

/// <summary>Sprint 10 — Quotation lifecycle (non-fiscal).</summary>
public enum QuotationStatus
{
    Draft,
    Sent,
    Accepted,
    Rejected,
    Expired,
    Cancelled,
}

/// <summary>Sprint 10 — Sales Order (internal commitment).</summary>
public enum SalesOrderStatus
{
    Draft,
    Posted,
    Closed,     // all lines delivered
    Cancelled,
}

/// <summary>Sprint 10 — Delivery Order (ใบส่งของ). Sprint 13h P9: 4-state machine.
/// Draft → Issued (doc_no allocated, no TI yet) → Delivered (TI auto-fires). Cancelled is terminal from Draft|Issued.</summary>
public enum DeliveryOrderStatus
{
    Draft,
    Issued,
    Delivered,
    Cancelled,
}

/// <summary>Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล).
/// Draft → Issued (doc_no allocated) → Settled (full receipt against linked TIs).
/// Cancelled is terminal from Draft|Issued. Non-fiscal — does NOT trigger e-Tax.</summary>
public enum BillingNoteStatus
{
    Draft,
    Issued,
    Settled,
    Cancelled,
}
