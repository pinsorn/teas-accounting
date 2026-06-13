namespace Accounting.Application.Reports;

/// <summary>One row of the sales VAT register (รายงานภาษีขาย).</summary>
public sealed record SalesVatRegisterRow(
    DateOnly DocDate,
    string   DocNo,
    string   DocType,    // "TI" | "CN" | "DN"
    string   CustomerName,
    string?  CustomerTaxId,
    decimal  SubtotalAmount,
    decimal  TaxAmount,
    decimal  TotalAmount);

/// <summary>One row of the purchase VAT register (รายงานภาษีซื้อ).</summary>
public sealed record PurchaseVatRegisterRow(
    DateOnly DocDate,
    string   DocNo,
    string   VendorName,
    string?  VendorTaxId,
    decimal  Amount,
    decimal  RecoverableVat,
    decimal  NonRecoverableVat,
    decimal  TotalPaid);

public sealed record VatRegisterPeriod(
    int Year,
    int Month,
    IReadOnlyList<SalesVatRegisterRow> Sales,
    IReadOnlyList<PurchaseVatRegisterRow> Purchase,
    decimal OutputVatTotal,
    decimal InputVatTotal,
    decimal NetVatPayable);

/// <summary>
/// ภ.พ.30 summary. Carry → next period when input &gt; output.
/// </summary>
public sealed record Pnd30Summary(
    int     Year,
    int     Month,
    decimal Sales,
    decimal OutputVat,
    decimal Purchase,
    decimal InputVat,
    decimal NetVatPayable,
    decimal NetVatRefundable);

public interface IVatReportService
{
    // businessUnitId (2026-06-13): optional analytical filter for the tax-summary dashboard
    // BU lens. null = company-wide (the ภ.พ.30 filing basis — VAT is filed per company, not BU).
    Task<VatRegisterPeriod> GetRegisterAsync(int year, int month, CancellationToken ct, int? businessUnitId = null);
    Task<Pnd30Summary> GetPnd30Async(int year, int month, CancellationToken ct, int? businessUnitId = null);
}
