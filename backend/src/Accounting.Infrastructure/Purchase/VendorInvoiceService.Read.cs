using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

public sealed partial class VendorInvoiceService
{
    public async Task<CursorPage<VendorInvoiceListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct, bool incompleteOnly = false)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.VendorInvoices.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(v => v.VendorInvoiceId < c);

        // Cursor paging on the RAW query BEFORE the in-memory completeness filter (cursor
        // stays stable). NOTE: with incompleteOnly a page may return FEWER than `limit`
        // (even 0) while HasMore=true — completeness is post-materialization (cont.76 D2).
        var raw = await q.OrderByDescending(v => v.VendorInvoiceId).Take(lim + 1)
            .Select(v => new
            {
                v.VendorInvoiceId, v.DocNo, v.DocDate, v.VendorName, v.VendorTaxId,
                v.VendorTaxInvoiceNo, v.VatClaimPeriod, v.TotalAmount, v.VatAmount,
                v.SettledAmount, v.SettlementStatus, v.Status, v.CurrencyCode,
                v.BusinessUnitId,
            })
            .ToListAsync(ct);

        var more = raw.Count > lim;
        if (more) raw.RemoveAt(raw.Count - 1);
        long? next = more ? raw[^1].VendorInvoiceId : null;

        // Batch-load tax-invoice-file presence for the POSTED rows on this page (no N+1).
        var postedIds = raw.Where(r => r.Status == DocumentStatus.Posted)
            .Select(r => r.VendorInvoiceId).ToList();
        var withTaxInvoiceFile = postedIds.Count == 0
            ? new HashSet<long>()
            : (await _db.Attachments.AsNoTracking()
                .Where(a => a.ParentType == AttachmentParentType.VendorInvoice
                         && a.Category == AttachmentCategory.TaxInvoice
                         && a.DeletedAt == null
                         && postedIds.Contains(a.ParentId))
                .Select(a => a.ParentId).ToListAsync(ct)).ToHashSet();

        var items = new List<VendorInvoiceListItem>(raw.Count);
        foreach (var r in raw)
        {
            var complete = r.Status != DocumentStatus.Posted
                || withTaxInvoiceFile.Contains(r.VendorInvoiceId);
            if (incompleteOnly && complete) continue;
            items.Add(new VendorInvoiceListItem(
                r.VendorInvoiceId, r.DocNo, r.DocDate, r.VendorName, r.VendorTaxId,
                r.VendorTaxInvoiceNo, r.VatClaimPeriod, r.TotalAmount, r.VatAmount,
                r.SettledAmount, r.SettlementStatus, r.Status.ToString(), r.CurrencyCode,
                complete, r.BusinessUnitId));
        }

        return new CursorPage<VendorInvoiceListItem>(items, next, more);
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

        // Sprint 13j-PURCH Flag-2 — downward refs: PV(s) settling this VI.
        // Source 1 = payment_vouchers.vendor_invoice_id (1:1, tenant-filtered DbSet).
        // Source 2 = payment_voucher_applications.vendor_invoice_id (N:N join table —
        // NOT ITenantOwned, so it has NO global filter; we scope by intersecting its
        // PV ids with the tenant-filtered PaymentVouchers below, never trusting the raw
        // application rows for company isolation). Deduped by PV id.
        var appliedPvIds = await _db.PaymentVoucherApplications.AsNoTracking()
            .Where(a => a.VendorInvoiceId == id)
            .Select(a => a.PaymentVoucherId)
            .ToListAsync(ct);
        var settlingPvs = await _db.PaymentVouchers.AsNoTracking()
            .Where(p => p.VendorInvoiceId == id || appliedPvIds.Contains(p.PaymentVoucherId))
            .OrderBy(p => p.PaymentVoucherId)
            .Select(p => new VendorInvoiceSettlingPv(
                p.PaymentVoucherId, p.DocNo, p.Status.ToString()))
            .ToListAsync(ct);

        var completeness = await ComputeCompletenessAsync(v, ct);

        // cont.79 — resolve BU code/name for display (null when no BU on the VI).
        var bu = v.BusinessUnitId is { } buId
            ? await _db.BusinessUnits.AsNoTracking()
                .Where(x => x.BusinessUnitId == buId)
                .Select(x => new { x.Code, x.NameTh }).FirstOrDefaultAsync(ct)
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
                l.IsRecoverableVat, l.IsCapex, l.IsCogs, l.ProductType)).ToList(),
            settlingPvs, completeness,
            v.BusinessUnitId, bu?.Code, bu?.NameTh);   // cont.79 — BU id + display
    }

    /// <summary>
    /// cont.76 — advisory completeness for a single VI. Evaluated ONLY for POSTED docs.
    /// MISSING_TAX_INVOICE_FILE (soft) = no non-deleted (VendorInvoice, TaxInvoice)
    /// attachment. NON-BLOCKING (the POSTED gate already required SOME attachment; this
    /// flag is category-specific and advisory). Tenant-scoped via Attachment's global filter.
    /// </summary>
    private async Task<CompletenessView> ComputeCompletenessAsync(
        Domain.Entities.Purchase.VendorInvoice v, CancellationToken ct)
    {
        if (v.Status != DocumentStatus.Posted) return CompletenessView.Complete;

        var missing = new List<string>();
        var hasTaxInvoiceFile = await _db.Attachments.AsNoTracking()
            .AnyAsync(a => a.ParentType == AttachmentParentType.VendorInvoice
                        && a.Category == AttachmentCategory.TaxInvoice
                        && a.ParentId == v.VendorInvoiceId
                        && a.DeletedAt == null, ct);
        if (!hasTaxInvoiceFile) missing.Add("MISSING_TAX_INVOICE_FILE");

        return CompletenessView.From(missing);
    }
}
