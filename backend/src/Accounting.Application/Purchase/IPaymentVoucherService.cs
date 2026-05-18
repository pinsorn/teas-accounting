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

    Task<Sales.CursorPage<PaymentVoucherListItem>> ListAsync(long? cursor, int limit, CancellationToken ct);
    Task<PaymentVoucherDetail?> GetDetailAsync(long id, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long id, CancellationToken ct);
}
