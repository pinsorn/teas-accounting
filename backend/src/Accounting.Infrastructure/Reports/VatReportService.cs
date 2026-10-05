using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.TaxFilings;
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

        // Cancel+reissue (spec 3.5.1): Voided TIs stay listed at 0.00 (never omitted) with a remark,
        // a replacement carries "ออกแทนเลขที่ ..." and a TI voided THIS month but dated earlier is a
        // zero-amount memo row after the in-month rows. Pnd30 math is unchanged (voided rows sum 0).
        var tiRaw = await _db.TaxInvoices
            .Where(t => (t.Status == DocumentStatus.Posted || t.Status == DocumentStatus.Voided)
                     && t.DocDate >= from && t.DocDate <= to)
            .Where(t => businessUnitId == null || t.BusinessUnitId == businessUnitId)
            .OrderBy(t => t.DocDate).ThenBy(t => t.DocNo)
            .Select(t => new RegisterTi(t.TaxInvoiceId, t.DocDate, t.DocNo, t.CustomerName, t.CustomerTaxId,
                t.SubtotalAmount, t.TaxAmount, t.TotalAmount, t.Status))
            .ToListAsync(ct);
        var memoRaw = await MemoTisAsync(_db, from, to, businessUnitId, ct);
        var remarks = await RemarksAsync(_db, tiRaw, ct);
        var ti = tiRaw.Select(t => t.Status == DocumentStatus.Voided
                ? new SalesVatRegisterRow(t.DocDate, t.DocNo!, "TI", t.CustomerName, t.CustomerTaxId, 0m, 0m, 0m,
                    "Voided", remarks.GetValueOrDefault(t.Id))
                : new SalesVatRegisterRow(t.DocDate, t.DocNo!, "TI", t.CustomerName, t.CustomerTaxId,
                    t.Subtotal, t.Tax, t.Total, "Posted", remarks.GetValueOrDefault(t.Id)))
            .ToList();
        var memo = memoRaw.Select(t => new SalesVatRegisterRow(t.DocDate, t.DocNo!, "TI", t.CustomerName,
                t.CustomerTaxId, 0m, 0m, 0m, "Voided", MemoRemark(t.DocDate))).ToList();

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

        var sales = ti.Concat(notes).OrderBy(x => x.DocDate).ThenBy(x => x.DocNo).Concat(memo).ToList();

        // ภาษีซื้อ source = Vendor Invoices by ม.82/4 vat_claim_period (NOT doc_date,
        // NOT Payment Voucher). One row per VI; legal refs = the vendor's tax invoice
        // no/date snapshot. Non-recoverable-only VIs (VatAmount == 0) carry no
        // claimable input VAT → excluded from the input register.
        var period = year * 100 + month;
        var purchaseRows = await _db.VendorInvoices
            .Where(v => v.Status == DocumentStatus.Posted
                     && v.VatClaimPeriod == period
                     && v.VatAmount > 0m
                     && v.HasInputVat)
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

    /// <summary>TI projection shared by the two sales registers (this service + TaxFilingService).</summary>
    internal sealed record RegisterTi(long Id, DateOnly DocDate, string? DocNo, string CustomerName,
        string? CustomerTaxId, decimal Subtotal, decimal Tax, decimal Total, DocumentStatus Status);

    private static string BuddhistDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";

    internal static string MemoRemark(DateOnly docDate) =>
        $"หมายเหตุ: ยกเลิกใบลงวันที่ {BuddhistDate(docDate)} — ไม่นำมารวมยอดเดือนนี้";

    /// <summary>TIs voided in this Bangkok month but dated before it (cross-month memo rows).</summary>
    internal static async Task<List<RegisterTi>> MemoTisAsync(
        AccountingDbContext db, DateOnly from, DateOnly to, int? businessUnitId, CancellationToken ct)
    {
        var bkk = TimeSpan.FromHours(7);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), bkk).ToUniversalTime();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), bkk).ToUniversalTime();
        return await db.TaxInvoices
            .Where(t => t.Status == DocumentStatus.Voided && t.DocDate < from
                     && t.CancelledAt >= start && t.CancelledAt < end)
            .Where(t => businessUnitId == null || t.BusinessUnitId == businessUnitId)
            .OrderBy(t => t.DocDate).ThenBy(t => t.DocNo)
            .Select(t => new RegisterTi(t.TaxInvoiceId, t.DocDate, t.DocNo, t.CustomerName, t.CustomerTaxId,
                t.SubtotalAmount, t.TaxAmount, t.TotalAmount, t.Status))
            .ToListAsync(ct);
    }

    /// <summary>Remark per TI id: replacement -> "ออกแทนเลขที่ X ลงวันที่ d"; voided -> "ยกเลิก [— ออกแทนโดย Y]";
    ///</summary>
    internal static async Task<Dictionary<long, string>> RemarksAsync(
        AccountingDbContext db, IEnumerable<RegisterTi> tis, CancellationToken ct)
    {
        var list = tis.DistinctBy(t => t.Id).ToList();
        var ids = list.Select(t => t.Id).ToList();
        var replaces = await db.TaxInvoices
            .Where(t => t.ReplacesTaxInvoiceId != null && ids.Contains(t.ReplacesTaxInvoiceId.Value))
            .Select(t => new { Orig = t.ReplacesTaxInvoiceId!.Value, t.DocNo, t.Status }).ToListAsync(ct);
        var replDoc = replaces.Where(r => r.Status == DocumentStatus.Posted && r.DocNo != null)
            .ToDictionary(r => r.Orig, r => r.DocNo!);
        var mine = await db.TaxInvoices
            .Where(t => ids.Contains(t.TaxInvoiceId) && t.ReplacesTaxInvoiceId != null)
            .Join(db.TaxInvoices, t => t.ReplacesTaxInvoiceId, o => o.TaxInvoiceId,
                (t, o) => new { t.TaxInvoiceId, o.DocNo, o.DocDate }).ToListAsync(ct);
        var origOf = mine.ToDictionary(m => m.TaxInvoiceId, m => $"ออกแทนเลขที่ {m.DocNo} ลงวันที่ {BuddhistDate(m.DocDate)}");

        var res = new Dictionary<long, string>();
        foreach (var t in list)
        {
            if (t.Status == DocumentStatus.Voided)
                res[t.Id] = replDoc.TryGetValue(t.Id, out var rd) ? $"ยกเลิก — ออกแทนโดย {rd}" : "ยกเลิก";
            else if (origOf.TryGetValue(t.Id, out var o))
                res[t.Id] = o;
        }
        return res;
    }

    // WP-2 (2026-08-16) — bad year/month used to construct DateOnly directly and leak an
    // unmapped ArgumentOutOfRangeException as a raw 500. Reuse TaxFilingPeriod's guard (same
    // error code, no new validation helper) instead of re-deriving the range literals here.
    // Round-trip the decomposed (y,m) against the input: year*100+month is only a faithful
    // yyyymm encoding for month in [1,12] — an out-of-band month (e.g. -88 or 112) can alias
    // to a DIFFERENT, in-range period and silently return the wrong month's data with a 200.
    private static (DateOnly from, DateOnly to) MonthRange(int year, int month)
    {
        var range = TaxFilingPeriod.MonthRange(year * 100 + month);
        if (range.from.Year != year || range.from.Month != month)
            throw new DomainException("tax_filing.bad_period",
                $"Year '{year}' Month '{month}' must describe a valid calendar month.");
        return range;
    }
}
