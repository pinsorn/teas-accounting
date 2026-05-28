using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// F (Question-Backend36) — read-only Purchase chain resolver. Strategy: anchor → walk UP
/// to the upstream Purchase Order (the unique upstream root, if any) → walk DOWN from
/// the resolved PO + the discovered VIs to collect every related PV and 50ทวิ. Every
/// query is tenant-scoped on top of the global EF filter (gotcha §26 belt-and-braces),
/// matching the Sales-side <c>DocumentCrossRefService.GetChainAsync</c> hygiene.
/// </summary>
public sealed class PurchaseChainService(
    AccountingDbContext db, ITenantContext tenant) : IPurchaseChainService
{
    public async Task<PurchaseChainDto?> GetAsync(string anchorType, long id, CancellationToken ct)
    {
        var cid = tenant.CompanyId;

        long? poId = null;
        var viIds = new HashSet<long>();
        var pvIds = new HashSet<long>();
        var whtIds = new HashSet<long>();

        // ── 1. Seed from the anchor + tenant existence check. ──────────────────
        switch (anchorType)
        {
            case "purchase-order":
                if (!await db.PurchaseOrders.AnyAsync(x => x.CompanyId == cid && x.PurchaseOrderId == id, ct)) return null;
                poId = id;
                break;
            case "vendor-invoice":
                if (!await db.VendorInvoices.AnyAsync(x => x.CompanyId == cid && x.VendorInvoiceId == id, ct)) return null;
                viIds.Add(id);
                break;
            case "payment-voucher":
                if (!await db.PaymentVouchers.AnyAsync(x => x.CompanyId == cid && x.PaymentVoucherId == id, ct)) return null;
                pvIds.Add(id);
                break;
            case "wht-certificate":
                if (!await db.WhtCertificates.AnyAsync(x => x.CompanyId == cid && x.WhtCertificateId == id, ct)) return null;
                whtIds.Add(id);
                break;
            default:
                return null;
        }

        // ── 2. Walk UP: WHT → PV → VI → PO. ────────────────────────────────────
        if (whtIds.Count > 0)
        {
            var pvFromWht = await db.WhtCertificates.AsNoTracking()
                .Where(w => w.CompanyId == cid && whtIds.Contains(w.WhtCertificateId) && w.PaymentVoucherId != null)
                .Select(w => w.PaymentVoucherId!.Value).ToListAsync(ct);
            foreach (var p in pvFromWht) pvIds.Add(p);
        }
        if (pvIds.Count > 0)
        {
            var viFromPv = await db.PaymentVouchers.AsNoTracking()
                .Where(p => p.CompanyId == cid && pvIds.Contains(p.PaymentVoucherId) && p.VendorInvoiceId != null)
                .Select(p => p.VendorInvoiceId!.Value).ToListAsync(ct);
            foreach (var v in viFromPv) viIds.Add(v);
            // PaymentVoucherApplication is the multi-VI settlement join; pick up extras.
            var viFromApps = await db.PaymentVoucherApplications.AsNoTracking()
                .Where(a => pvIds.Contains(a.PaymentVoucherId)
                         && db.PaymentVouchers.Any(p => p.PaymentVoucherId == a.PaymentVoucherId && p.CompanyId == cid))
                .Select(a => a.VendorInvoiceId).ToListAsync(ct);
            foreach (var v in viFromApps) viIds.Add(v);
        }
        if (viIds.Count > 0)
        {
            var poFromVi = await db.VendorInvoices.AsNoTracking()
                .Where(v => v.CompanyId == cid && viIds.Contains(v.VendorInvoiceId) && v.PurchaseOrderId != null)
                .Select(v => v.PurchaseOrderId!.Value).FirstOrDefaultAsync(ct);
            if (poFromVi != 0) poId ??= poFromVi;
        }

        // ── 3. Walk DOWN from the PO root, then from each discovered VI. ───────
        if (poId is long fpo)
        {
            var visDown = await db.VendorInvoices.AsNoTracking()
                .Where(v => v.CompanyId == cid && v.PurchaseOrderId == fpo)
                .Select(v => v.VendorInvoiceId).ToListAsync(ct);
            foreach (var v in visDown) viIds.Add(v);
        }
        if (viIds.Count > 0)
        {
            var pvsDown = await db.PaymentVouchers.AsNoTracking()
                .Where(p => p.CompanyId == cid && p.VendorInvoiceId != null && viIds.Contains(p.VendorInvoiceId!.Value))
                .Select(p => p.PaymentVoucherId).ToListAsync(ct);
            foreach (var p in pvsDown) pvIds.Add(p);
            var pvFromApps = await db.PaymentVoucherApplications.AsNoTracking()
                .Where(a => viIds.Contains(a.VendorInvoiceId)
                         && db.PaymentVouchers.Any(p => p.PaymentVoucherId == a.PaymentVoucherId && p.CompanyId == cid))
                .Select(a => a.PaymentVoucherId).Distinct().ToListAsync(ct);
            foreach (var p in pvFromApps) pvIds.Add(p);
        }
        if (pvIds.Count > 0)
        {
            var whtDown = await db.WhtCertificates.AsNoTracking()
                .Where(w => w.CompanyId == cid && w.PaymentVoucherId != null && pvIds.Contains(w.PaymentVoucherId!.Value))
                .Select(w => w.WhtCertificateId).ToListAsync(ct);
            foreach (var w in whtDown) whtIds.Add(w);
        }

        // ── 4. Materialize nodes (tenant-scoped). ──────────────────────────────
        PurchaseChainNode? po = poId is long fq
            ? await db.PurchaseOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.PurchaseOrderId == fq)
                .Select(x => new PurchaseChainNode(x.PurchaseOrderId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
                .FirstOrDefaultAsync(ct)
            : null;

        var vis = viIds.Count == 0 ? new List<PurchaseChainNode>() : await db.VendorInvoices.AsNoTracking()
            .Where(x => x.CompanyId == cid && viIds.Contains(x.VendorInvoiceId))
            .OrderBy(x => x.VendorInvoiceId)
            .Select(x => new PurchaseChainNode(x.VendorInvoiceId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
            .ToListAsync(ct);

        var pvs = pvIds.Count == 0 ? new List<PurchaseChainNode>() : await db.PaymentVouchers.AsNoTracking()
            .Where(x => x.CompanyId == cid && pvIds.Contains(x.PaymentVoucherId))
            .OrderBy(x => x.PaymentVoucherId)
            .Select(x => new PurchaseChainNode(x.PaymentVoucherId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalPaid))
            .ToListAsync(ct);

        var whts = whtIds.Count == 0 ? new List<PurchaseChainNode>() : await db.WhtCertificates.AsNoTracking()
            .Where(x => x.CompanyId == cid && whtIds.Contains(x.WhtCertificateId))
            .OrderBy(x => x.WhtCertificateId)
            .Select(x => new PurchaseChainNode(x.WhtCertificateId, x.DocNo, x.CertDate, x.Status.ToString(), x.WhtAmount))
            .ToListAsync(ct);

        return new PurchaseChainDto(po, vis, pvs, whts);
    }
}
