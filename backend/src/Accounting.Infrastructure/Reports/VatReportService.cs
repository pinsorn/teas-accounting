using Accounting.Application.Reports;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Sales/Purchase VAT registers (รายงานภาษีขาย / ภาษีซื้อ) + ภ.พ.30 monthly summary.
/// Tenant-scoped via DbContext global query filter.
/// </summary>
public sealed class VatReportService : IVatReportService
{
    private readonly AccountingDbContext _db;

    public VatReportService(AccountingDbContext db) => _db = db;

    public async Task<VatRegisterPeriod> GetRegisterAsync(
        int year, int month, CancellationToken ct, int? businessUnitId = null)
    {
        var (from, to) = MonthRange(year, month);

        var ti = await _db.TaxInvoices
            .Where(t => t.Status == DocumentStatus.Posted && t.DocDate >= from && t.DocDate <= to)
            .Where(t => businessUnitId == null || t.BusinessUnitId == businessUnitId)
            .OrderBy(t => t.DocDate).ThenBy(t => t.DocNo)
            .Select(t => new SalesVatRegisterRow(t.DocDate, t.DocNo!, "TI",
                t.CustomerName, t.CustomerTaxId,
                t.SubtotalAmount, t.TaxAmount, t.TotalAmount))
            .ToListAsync(ct);

        var notes = await _db.TaxAdjustmentNotes
            .Where(n => n.Status == DocumentStatus.Posted && n.DocDate >= from && n.DocDate <= to)
            .Where(n => businessUnitId == null || n.BusinessUnitId == businessUnitId)
            .OrderBy(n => n.DocDate).ThenBy(n => n.DocNo)
            .Select(n => new SalesVatRegisterRow(
                n.DocDate, n.DocNo!,
                n.NoteType == TaxAdjustmentNoteType.Credit ? "CN" : "DN",
                n.CustomerName, n.CustomerTaxId,
                // CN reduces, DN increases — flip signs for display
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.SubtotalAmount : n.SubtotalAmount,
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.TaxAmount      : n.TaxAmount,
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.TotalAmount    : n.TotalAmount))
            .ToListAsync(ct);

        var sales = ti.Concat(notes).OrderBy(x => x.DocDate).ThenBy(x => x.DocNo).ToList();

        // ภาษีซื้อ source = Vendor Invoices by ม.82/4 vat_claim_period (NOT doc_date,
        // NOT Payment Voucher). One row per VI; legal refs = the vendor's tax invoice
        // no/date snapshot. Non-recoverable-only VIs (VatAmount == 0) carry no
        // claimable input VAT → excluded from the input register.
        var period = year * 100 + month;
        var purchaseRows = await _db.VendorInvoices
            .Where(v => v.Status == DocumentStatus.Posted
                     && v.VatClaimPeriod == period
                     && v.VatAmount > 0m)
            .Where(v => businessUnitId == null || v.BusinessUnitId == businessUnitId)
            .OrderBy(v => v.VendorTaxInvoiceDate).ThenBy(v => v.VendorTaxInvoiceNo)
            .Select(v => new PurchaseVatRegisterRow(
                v.VendorTaxInvoiceDate, v.VendorTaxInvoiceNo, v.VendorName, v.VendorTaxId,
                v.SubtotalAmount, v.VatAmount, v.NonRecoverableVatAmount, v.TotalAmount))
            .ToListAsync(ct);

        var outputVat = sales.Sum(s => s.TaxAmount);
        var inputVat  = purchaseRows.Sum(p => p.RecoverableVat);

        return new VatRegisterPeriod(
            year, month, sales, purchaseRows,
            OutputVatTotal: outputVat,
            InputVatTotal:  inputVat,
            NetVatPayable:  outputVat - inputVat);
    }

    public async Task<Pnd30Summary> GetPnd30Async(
        int year, int month, CancellationToken ct, int? businessUnitId = null)
    {
        var reg = await GetRegisterAsync(year, month, ct, businessUnitId);
        var net = reg.OutputVatTotal - reg.InputVatTotal;

        return new Pnd30Summary(
            year, month,
            Sales:     reg.Sales.Sum(s => s.SubtotalAmount),
            OutputVat: reg.OutputVatTotal,
            Purchase:  reg.Purchase.Sum(p => p.Amount),
            InputVat:  reg.InputVatTotal,
            NetVatPayable:    net > 0 ? net : 0m,
            NetVatRefundable: net < 0 ? -net : 0m);
    }

    private static (DateOnly from, DateOnly to) MonthRange(int year, int month) =>
        (new DateOnly(year, month, 1),
         new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
}
