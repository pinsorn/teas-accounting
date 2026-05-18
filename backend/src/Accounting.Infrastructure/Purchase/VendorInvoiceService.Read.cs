using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

public sealed partial class VendorInvoiceService
{
    public async Task<CursorPage<VendorInvoiceListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.VendorInvoices.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(v => v.VendorInvoiceId < c);
        var rows = await q.OrderByDescending(v => v.VendorInvoiceId).Take(lim + 1)
            .Select(v => new VendorInvoiceListItem(
                v.VendorInvoiceId, v.DocNo, v.DocDate, v.VendorName, v.VendorTaxId,
                v.VendorTaxInvoiceNo, v.VatClaimPeriod, v.TotalAmount, v.VatAmount,
                v.SettledAmount, v.SettlementStatus, v.Status.ToString(), v.CurrencyCode))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<VendorInvoiceListItem>(
            rows, more ? rows[^1].VendorInvoiceId : null, more);
    }

    public async Task<VendorInvoiceDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var v = await _db.VendorInvoices.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.VendorInvoiceId == id, ct);
        if (v is null) return null;
        var poDocNo = v.PurchaseOrderId is { } poId
            ? await _db.PurchaseOrders.AsNoTracking()
                .Where(p => p.PurchaseOrderId == poId)
                .Select(p => p.DocNo).FirstOrDefaultAsync(ct)
            : null;
        return new VendorInvoiceDetail(
            v.VendorInvoiceId, v.DocNo, v.Status.ToString(), v.DocDate,
            v.VendorTaxInvoiceNo, v.VendorTaxInvoiceDate, v.VatClaimPeriod,
            v.VendorId, v.VendorName, v.VendorTaxId, v.VendorBranchCode, v.VendorAddress,
            v.CurrencyCode, v.ExchangeRate, v.SubtotalAmount, v.VatAmount,
            v.NonRecoverableVatAmount, v.TotalAmount, v.SettledAmount, v.SettlementStatus,
            v.Notes, v.PostedAt,
            v.PurchaseOrderId, poDocNo,
            v.Lines.OrderBy(l => l.LineNo).Select(l => new VendorInvoiceLineView(
                l.LineNo, l.ExpenseCategoryId, l.ExpenseAccountId, l.Description,
                l.Amount, l.VatRate, l.VatAmount,
                l.IsRecoverableVat, l.IsCapex, l.IsCogs)).ToList());
    }
}
