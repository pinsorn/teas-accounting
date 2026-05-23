namespace Accounting.Application.Reports;

/// <summary>Sprint 8.6 — WHT withheld by customers (AR-side). Feeds the
/// year-end ภ.ง.ด.50 corporate-tax credit + chasing un-received 50ทวิ.</summary>
public sealed record WhtReceivableRegisterRow(
    string DocNo, DateOnly DocDate, string CustomerName, string? CustomerTaxId,
    decimal WhtAmount, string? CustomerWhtCertNo);

public sealed record WhtReceivableRegister(
    DateOnly FromDate, DateOnly ToDate,
    IReadOnlyList<WhtReceivableRegisterRow> Rows, decimal TotalWht);

public sealed record WhtReceivableAgingRow(
    string CustomerName, string? CustomerTaxId, string DocNo,
    DateOnly DocDate, decimal WhtAmount, int AgeDays,
    bool CertReceived, bool Reconciled);   // Sprint 9

/// <summary>Sprint 9 — aging buckets by days since receipt post.</summary>
public sealed record WhtReceivableAgingBuckets(
    decimal Current, decimal Days30, decimal Days60, decimal Days90Plus);

public sealed record WhtReceivableAging(
    IReadOnlyList<WhtReceivableAgingRow> Rows, decimal TotalOutstanding,
    WhtReceivableAgingBuckets Buckets);

// Sprint 13j-tail — posted receipts that withheld WHT but have NOT yet recorded
// the customer's 50ทวิ certificate number ("ใบเสร็จที่ขาดใบทวิ 50"). The cert can
// be entered later from the receipt detail; this report drives the chase per
// filing period so the ภ.ง.ด./ภ.ง.ด.50 credit is not lost.
public sealed record WhtMissingCertRow(
    long ReceiptId, string DocNo, DateOnly DocDate,
    string CustomerName, string? CustomerTaxId, decimal WhtAmount);

public sealed record WhtMissingCertReport(
    int Period, IReadOnlyList<WhtMissingCertRow> Rows, decimal TotalWht);

public interface IWhtReceivableReportService
{
    Task<WhtReceivableRegister> GetRegisterAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<WhtReceivableAging> GetAgingAsync(CancellationToken ct);
    Task<WhtMissingCertReport> GetMissingCertAsync(int period, CancellationToken ct);
}
