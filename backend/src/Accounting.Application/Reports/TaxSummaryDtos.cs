namespace Accounting.Application.Reports;

/// <summary>
/// One month (or the year-total row, Month=0) of the tax summary dashboard.
/// All figures are derived from Posted documents only.
/// </summary>
public sealed record TaxSummaryMonth(
    int     Month,            // 1..12; 0 = year total
    decimal Revenue,          // GL Revenue (Cr−Dr) by DocDate month
    decimal Expense,          // GL Expense (Dr−Cr)
    decimal NetProfit,        // Revenue − Expense
    decimal OutputVat,        // ภาษีขาย (ภ.พ.30)
    decimal InputVat,         // ภาษีซื้อ
    decimal VatPayable,       // > 0 = ชำระเพิ่ม
    decimal VatRefundable,    // > 0 = ขอคืน / ยกไป
    decimal WhtPaidPnd3,      // ภ.ง.ด.3 (บุคคลธรรมดา)
    decimal WhtPaidPnd53,     // ภ.ง.ด.53 (นิติบุคคล)
    decimal WhtPaidPnd54,     // ภ.ง.ด.54 (ต่างประเทศ)
    decimal WhtPaidPnd1,      // ภ.ง.ด.1 (เงินเดือน)
    decimal WhtPaidTotal,     // นำส่งรวม
    decimal WhtReceived);     // ถูกลูกค้าหัก = เครดิต ภ.ง.ด.50

/// <summary>Per-month tax overview for a calendar year + a year-total row.</summary>
public sealed record TaxSummaryReport(
    int Year,
    IReadOnlyList<TaxSummaryMonth> Months,   // exactly 12, Jan..Dec
    TaxSummaryMonth Totals);                  // Month = 0

public interface ITaxSummaryService
{
    // businessUnitId (2026-06-13): optional analytical BU lens. null = company-wide.
    // WHT is BU-resolved via the source PV (Direction='P') / Receipt (Direction='R');
    // cross-BU receipts (header BU null) fall outside any single-BU filter.
    Task<TaxSummaryReport> GetAsync(int year, CancellationToken ct, int? businessUnitId = null);
}
