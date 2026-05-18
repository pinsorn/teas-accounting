namespace Accounting.Domain.Enums;

/// <summary>Sprint 11 — polymorphic attachment parent. Stored as the literal
/// string; PURCHASE_ORDER added forward-compat for Sprint 12.</summary>
public enum AttachmentParentType
{
    VendorInvoice,
    PaymentVoucher,
    Receipt,
    TaxInvoice,
    TaxAdjustmentNote,   // CN + DN
    JournalEntry,
    Quotation,
    SalesOrder,
    DeliveryOrder,
    PurchaseOrder,       // Sprint 12 forward-compat
}

/// <summary>Sprint 11 — attachment purpose. OTHER requires a description.</summary>
public enum AttachmentCategory
{
    TaxInvoice,
    Receipt,
    PurchaseOrder,
    DeliveryOrder,
    Quotation,
    WhtCert50Tawi,
    BankSlip,
    Contract,
    ExpenseClaimForm,
    CustomsDecl,
    Other,
}
