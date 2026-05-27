namespace Accounting.Application.Reports;

/// <summary>
/// Sprint 13j-PURCH Phase B — AP Aging (อายุหนี้เจ้าหนี้) report rows.
/// One row per vendor, plus a Totals row, bucketed by days since the
/// vendor-invoice DocDate. Outstanding = TotalAmount − SettledAmount
/// (SettledAmount is updated on PV post — see PaymentVoucherService.PostAsync).
/// Read-only projection; no entity/migration change.
/// </summary>
public sealed record ApAgingRow(
    int VendorId, string VendorName, string VendorTaxId,
    decimal Current, decimal Bucket31To60, decimal Bucket61To90, decimal BucketOver90,
    decimal Total);

public sealed record ApAgingReport(
    DateOnly AsOf, IReadOnlyList<ApAgingRow> Rows, ApAgingRow Totals);
