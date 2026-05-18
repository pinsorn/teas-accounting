namespace Accounting.Domain.Enums;

/// <summary>
/// Sprint 11 — string ⇄ enum maps for the RD-style screaming-snake literals
/// used in the DB / API for attachment parent_type + category. Single source
/// shared by EF config, service, validators and endpoints.
/// </summary>
public static class AttachmentCodes
{
    public static readonly IReadOnlyDictionary<AttachmentParentType, string> ParentDb =
        new Dictionary<AttachmentParentType, string>
        {
            [AttachmentParentType.VendorInvoice]     = "VENDOR_INVOICE",
            [AttachmentParentType.PaymentVoucher]    = "PAYMENT_VOUCHER",
            [AttachmentParentType.Receipt]           = "RECEIPT",
            [AttachmentParentType.TaxInvoice]        = "TAX_INVOICE",
            [AttachmentParentType.TaxAdjustmentNote] = "TAX_ADJUSTMENT_NOTE",
            [AttachmentParentType.JournalEntry]      = "JOURNAL_ENTRY",
            [AttachmentParentType.Quotation]         = "QUOTATION",
            [AttachmentParentType.SalesOrder]        = "SALES_ORDER",
            [AttachmentParentType.DeliveryOrder]     = "DELIVERY_ORDER",
            [AttachmentParentType.PurchaseOrder]     = "PURCHASE_ORDER",
        };

    public static readonly IReadOnlyDictionary<AttachmentCategory, string> CategoryDb =
        new Dictionary<AttachmentCategory, string>
        {
            [AttachmentCategory.TaxInvoice]       = "TAX_INVOICE",
            [AttachmentCategory.Receipt]          = "RECEIPT",
            [AttachmentCategory.PurchaseOrder]    = "PURCHASE_ORDER",
            [AttachmentCategory.DeliveryOrder]    = "DELIVERY_ORDER",
            [AttachmentCategory.Quotation]        = "QUOTATION",
            [AttachmentCategory.WhtCert50Tawi]    = "WHT_CERT_50TAWI",
            [AttachmentCategory.BankSlip]         = "BANK_SLIP",
            [AttachmentCategory.Contract]         = "CONTRACT",
            [AttachmentCategory.ExpenseClaimForm] = "EXPENSE_CLAIM_FORM",
            [AttachmentCategory.CustomsDecl]      = "CUSTOMS_DECL",
            [AttachmentCategory.Other]            = "OTHER",
        };

    private static readonly Dictionary<string, AttachmentParentType> ParentRev =
        ParentDb.ToDictionary(kv => kv.Value, kv => kv.Key);
    private static readonly Dictionary<string, AttachmentCategory> CategoryRev =
        CategoryDb.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static string ToDb(AttachmentParentType t) => ParentDb[t];
    public static string ToDb(AttachmentCategory c) => CategoryDb[c];
    public static bool TryParent(string s, out AttachmentParentType t) => ParentRev.TryGetValue(s, out t);
    public static bool TryCategory(string s, out AttachmentCategory c) => CategoryRev.TryGetValue(s, out c);

    // Expression-tree-safe (no out/ref/decl-patterns) — for EF HasConversion.
    public static AttachmentParentType ParentFrom(string s) =>
        ParentRev.TryGetValue(s, out var t) ? t : AttachmentParentType.VendorInvoice;
    public static AttachmentCategory CategoryFrom(string s) =>
        CategoryRev.TryGetValue(s, out var c) ? c : AttachmentCategory.Other;

    public static IReadOnlyList<string> ParentValues => ParentDb.Values.ToList();
    public static IReadOnlyList<string> CategoryValues => CategoryDb.Values.ToList();
}
