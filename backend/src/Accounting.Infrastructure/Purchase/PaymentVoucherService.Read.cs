using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

public sealed partial class PaymentVoucherService
{
    public async Task<CursorPage<PaymentVoucherListItem>> ListAsync(long? cursor, int limit, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.PaymentVouchers.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(p => p.PaymentVoucherId < c);
        var rows = await q.OrderByDescending(p => p.PaymentVoucherId).Take(lim + 1)
            .Select(p => new PaymentVoucherListItem(
                p.PaymentVoucherId, p.DocNo, p.DocDate, p.VendorName, p.VendorTaxId,
                p.SubPrefix, p.TotalPaid, p.WhtAmount, p.Status.ToString(), p.CurrencyCode))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<PaymentVoucherListItem>(
            rows, more ? rows[^1].PaymentVoucherId : null, more);
    }

    public async Task<PaymentVoucherDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var p = await _db.PaymentVouchers.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PaymentVoucherId == id, ct);
        if (p is null) return null;

        // Sprint 13j-PURCH Flag-2 — downward ref: WHT certificate(s) (50ทวิ) issued
        // from this PV. WhtCertificate is ITenantOwned so the global filter scopes by
        // company. Direction='P' (payable) carries our PaymentVoucherId.
        var whtCerts = await _db.WhtCertificates.AsNoTracking()
            .Where(w => w.PaymentVoucherId == id)
            .OrderBy(w => w.WhtCertificateId)
            .Select(w => new PaymentVoucherWhtCertificate(
                w.WhtCertificateId, w.DocNo, w.Status.ToString()))
            .ToListAsync(ct);

        return new PaymentVoucherDetail(
            p.PaymentVoucherId, p.DocNo, p.Status.ToString(), p.DocDate,
            p.VendorId, p.VendorName, p.VendorTaxId, p.VendorBranchCode, p.VendorAddress,
            p.ExpenseCategoryId, p.SubPrefix, p.PaymentMethod.ToString(), p.ChequeNo,
            p.ChequeDate, p.BankAccountId, p.CurrencyCode, p.ExchangeRate,
            p.SubtotalAmount, p.VatAmount, p.WhtAmount, p.TotalPaid, p.TotalAmountThb,
            p.Description, p.Notes, p.VendorInvoiceId,
            p.ApprovedBy, p.ApprovedAt, p.PostedAt,
            p.SelfWithholdMode, p.RequiresPnd36ReverseCharge,
            p.Lines.OrderBy(l => l.LineNo).Select(l => new PaymentVoucherLineView(
                l.LineNo, l.ExpenseAccountId, l.Description, l.Amount, l.VatRate,
                l.VatAmount, l.IsRecoverableVat, l.WhtTypeId, l.WhtRate, l.WhtAmount)).ToList(),
            whtCerts);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("pv.not_found", $"Payment Voucher {id} not found.");

        // Sprint 13j-PURCH Phase C — render via the shared PaperDocument mirror.
        // Seller = the issuing company (the payer); Customer = the payee (vendor).
        // Three-box sign (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน). The foot carries the
        // WHT-deducted net via PaperSummary.Wht → grand total reads "จ่ายสุทธิ".
        // Payment method / cheque detail goes into Notes (no §86/4 schema applies).
        var seller = await Pdf.PaperSellerSource.FromCompanyProfileAsync(_db, _tenant.CompanyId, ct);

        var notes = $"วิธีชำระ / Method: {d.PaymentMethod}"
            + (string.IsNullOrEmpty(d.ChequeNo) ? "" : $"  เช็คเลขที่ {d.ChequeNo}");
        if (!string.IsNullOrWhiteSpace(d.Description)) notes = $"{d.Description}\n{notes}";
        if (!string.IsNullOrWhiteSpace(d.Notes)) notes = $"{notes}\n{d.Notes}";

        var model = new Pdf.PaperDocModel(
            DocType: "ใบสำคัญจ่าย",
            DocTypeEn: "Payment Voucher",
            DocNo: d.DocNo ?? "(ร่าง)",
            IssueDate: d.DocDate,
            Seller: seller,
            Customer: new Pdf.PaperCustomer(
                d.VendorName, Pdf.PaperFormat.TaxId(d.VendorTaxId), d.VendorBranchCode, d.VendorAddress),
            Items: d.Lines.Select(l => new Pdf.PaperLine(
                l.Description, null, null, null, null, null, l.Amount)).ToList(),
            Summary: new Pdf.PaperSummary(
                Subtotal: d.SubtotalAmount, Discount: null, BeforeVat: d.SubtotalAmount,
                Vat: d.VatAmount, Total: d.TotalPaid, VatRate: null, ShowVat: true,
                Wht: d.WhtAmount > 0m ? d.WhtAmount : null),
            SignRoles: new Pdf.PaperSignRoles("ผู้จัดทำ", "ผู้รับเงิน", Middle: "ผู้อนุมัติ"),
            Notes: notes,
            Watermark: new Pdf.PaperWatermark(
                copy ? "สำเนา" : "ต้นฉบับ",
                copy ? Pdf.PaperWatermarkVariant.Warning : Pdf.PaperWatermarkVariant.Success));
        return Pdf.PaperDocumentPdf.Render(model);
    }
}
