using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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
                l.VatAmount, l.IsRecoverableVat, l.WhtTypeId, l.WhtRate, l.WhtAmount)).ToList());
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("pv.not_found", $"Payment Voucher {id} not found.");
        return Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(28); p.DefaultTextStyle(s => s.FontSize(10));
            p.Header().AlignCenter().Text("ใบสำคัญจ่าย / PAYMENT VOUCHER").Bold().FontSize(15);
            p.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(5);
                col.Item().Text($"เลขที่ / No.: {d.DocNo ?? "(ร่าง)"}");
                col.Item().Text($"วันที่ / Date: {d.DocDate:dd/MM/yyyy}");
                col.Item().Text($"ผู้รับเงิน / Payee: {d.VendorName}  ({d.VendorTaxId ?? "-"})");
                col.Item().Text($"วิธีชำระ / Method: {d.PaymentMethod}"
                    + (string.IsNullOrEmpty(d.ChequeNo) ? "" : $"  เช็คเลขที่ {d.ChequeNo}"));
                col.Item().PaddingTop(6).Text("รายการ / Lines:").Bold();
                foreach (var l in d.Lines)
                    col.Item().Text(
                        $"  {l.LineNo}. {l.Description} — {l.Amount:N2}"
                        + (l.VatAmount > 0 ? $" (VAT {l.VatAmount:N2})" : "")
                        + (l.WhtAmount > 0 ? $" (WHT {l.WhtAmount:N2})" : ""));
                col.Item().PaddingTop(6).Text($"รวมก่อนภาษี / Subtotal: {d.SubtotalAmount:N2}");
                col.Item().Text($"ภาษีซื้อ / Input VAT: {d.VatAmount:N2}");
                col.Item().Text($"หัก ณ ที่จ่าย / WHT: {d.WhtAmount:N2}");
                col.Item().Text($"จ่ายสุทธิ / Net Paid: {d.TotalPaid:N2} {d.CurrencyCode}")
                    .Bold().FontSize(12);
            });
            p.Footer().AlignCenter().Text("ออกโดยระบบ TEAS").FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }
}
