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

public interface IWhtReceivableReportService
{
    Task<WhtReceivableRegister> GetRegisterAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<WhtReceivableAging> GetAgingAsync(CancellationToken ct);
}
