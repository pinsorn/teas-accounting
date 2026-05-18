namespace Accounting.Application.Purchase;

/// <summary>
/// Read-only surface for 50 ทวิ certificates. Certificates are issued automatically by
/// <see cref="IPaymentVoucherService.PostAsync"/> — they are never created/edited here
/// (immutable once the source PV is posted, ม.50 ทวิ).
/// </summary>
public interface IWhtCertificateService
{
    Task<Sales.CursorPage<WhtCertificateListItem>> ListAsync(long? cursor, int limit, CancellationToken ct);
    Task<WhtCertificateDetail?> GetDetailAsync(long id, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long id, CancellationToken ct);
}
