using Accounting.Domain.Common;

namespace Accounting.Application.Sales;

/// <summary>cancel-reissue spec 3.3.4 - allowed reason codes per document and action. BE is authoritative
/// (the FE list in lib/cancel-reasons.ts mirrors it). Codes are data: cheap to change after CPA review.</summary>
public static class CancelReasonCodes
{
    public static readonly string[] TaxInvoiceReissue =
        ["BUYER_DETAILS_ERROR", "ITEM_DESCRIPTION_ERROR", "OTHER_PARTICULARS_ERROR"];
    public static readonly string[] TaxInvoiceCancel =
        ["ISSUED_IN_ERROR", "DUPLICATE", "SALE_CANCELLED_BEFORE_DELIVERY"];
    public static readonly string[] ReceiptReissue =
        ["PAYER_DETAILS_ERROR", "PAYMENT_DETAILS_ERROR", "OTHER_PARTICULARS_ERROR"];
    public static readonly string[] ReceiptCancel =
        ["ISSUED_IN_ERROR", "DUPLICATE", "PAYMENT_NOT_RECEIVED"];
    public static readonly string[] BillingNoteCancel =
        ["ISSUED_IN_ERROR", "DUPLICATE", "SALE_CANCELLED", "DETAILS_ERROR"];

    /// <summary>Throws <c>cancel.reason_code_invalid</c> unless <paramref name="code"/> is in one of the sets.</summary>
    public static void Require(string? code, params string[][] allowed)
    {
        if (code is null || !allowed.Any(set => set.Contains(code)))
            throw new DomainException("cancel.reason_code_invalid",
                $"Reason code '{code}' is not valid for this document / action.");
    }
}
