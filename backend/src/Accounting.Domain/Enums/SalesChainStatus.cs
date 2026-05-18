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

/// <summary>Sprint 10 — Delivery Order (ใบส่งของ).</summary>
public enum DeliveryOrderStatus
{
    Draft,
    Posted,
    Cancelled,
}
