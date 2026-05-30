namespace Accounting.Application.Purchase;

public interface IPaymentVoucherService
{
    Task<long> CreateDraftAsync(CreatePaymentVoucherRequest req, CancellationToken ct);

    /// <summary>
    /// Atomically allocate PV-CATEGORY-NNNN, mark posted, and — if WHT > 0 — issue WT-NNNN
    /// certificate snapshot in the same DB transaction.
    /// </summary>
    /// <summary>
    /// B2 SoD gate (Draft → Approved). Approver must differ from creator
    /// (CLAUDE.md §12.1). Approver MAY later be the poster.
    /// </summary>
    Task<PaymentVoucherApprovedResult> ApproveAsync(long paymentVoucherId, CancellationToken ct);

    /// <summary>Post an APPROVED PV. Throws if not yet approved (B2 workflow).</summary>
    Task<PaymentVoucherPostedResult> PostAsync(long paymentVoucherId, CancellationToken ct);

    /// <summary>
    /// cont.76 — create a Vendor Invoice (บันทึกใบกำกับภาษีซื้อ) pre-filled from this PV and link
    /// it back (PaymentVoucher.VendorInvoiceId). The guided "create VI off a PV" path; reuses the
    /// compliance-correct VI draft pipeline. Throws pv.vi_exists if the PV already has a linked VI.
    /// Returns the new VendorInvoiceId.
    /// </summary>
    Task<long> CreateVendorInvoiceFromPvAsync(long paymentVoucherId, CreateViFromPvRequest req, CancellationToken ct);

    // cont.76 — incompleteOnly=true returns only POSTED docs whose advisory completeness fails.
    Task<Sales.CursorPage<PaymentVoucherListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct, bool incompleteOnly = false);
    Task<PaymentVoucherDetail?> GetDetailAsync(long id, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false);
}
