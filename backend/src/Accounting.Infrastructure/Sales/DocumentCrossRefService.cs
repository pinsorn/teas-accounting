using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

// Sprint 13h P8 — cross-reference graph resolver. Tenant-scoped on top of the
// EF global query filter (gotcha §26 belt-and-braces). ReceiptApplication has
// no nav properties, so joins are explicit.

public sealed class DocumentCrossRefService(
    AccountingDbContext db, ITenantContext tenant) : IDocumentCrossRefService
{
    public async Task<DocumentCrossRefDto?> GetForTaxInvoiceAsync(long id, CancellationToken ct)
    {
        var ti = await db.TaxInvoices.AsNoTracking()
            .Where(t => t.CompanyId == tenant.CompanyId && t.TaxInvoiceId == id)
            .Select(t => new { t.TaxInvoiceId, t.DocNo, t.QuotationId, t.Status })
            .FirstOrDefaultAsync(ct);
        if (ti is null) return null;

        // Sprint 13i R5 — full upstream chain: Q ← SO ← DO ← TI.
        // TI links directly to its Quotation; the DO that generated the TI
        // carries the SalesOrderId, and the SO carries its own QuotationId.
        DocumentRef? deliveryOrder = null;
        DocumentRef? salesOrder = null;
        long? chainQuotationId = ti.QuotationId;

        var doRow = await db.DeliveryOrders.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId && x.TaxInvoiceId == id)
            .Select(x => new { x.DeliveryOrderId, x.DocNo, x.Status, x.SalesOrderId })
            .FirstOrDefaultAsync(ct);
        if (doRow is not null)
        {
            deliveryOrder = new DocumentRef(doRow.DeliveryOrderId, doRow.DocNo, doRow.Status.ToString());
            if (doRow.SalesOrderId is long soid)
            {
                var soRow = await db.SalesOrders.AsNoTracking()
                    .Where(s => s.CompanyId == tenant.CompanyId && s.SalesOrderId == soid)
                    .Select(s => new { s.SalesOrderId, s.DocNo, s.Status, s.QuotationId })
                    .FirstOrDefaultAsync(ct);
                if (soRow is not null)
                {
                    salesOrder = new DocumentRef(soRow.SalesOrderId, soRow.DocNo, soRow.Status.ToString());
                    chainQuotationId ??= soRow.QuotationId;
                }
            }
        }

        DocumentRef? quotation = null;
        if (chainQuotationId is long qid)
        {
            quotation = await db.Quotations.AsNoTracking()
                .Where(q => q.CompanyId == tenant.CompanyId && q.QuotationId == qid)
                .Select(q => new DocumentRef(q.QuotationId, q.DocNo, q.Status.ToString()))
                .FirstOrDefaultAsync(ct);
        }

        // Explicit join — ReceiptApplication has no nav properties.
        var receipts = await (
            from a in db.ReceiptApplications.AsNoTracking()
            join r in db.Receipts.AsNoTracking() on a.ReceiptId equals r.ReceiptId
            where a.TaxInvoiceId == id
                && r.CompanyId == tenant.CompanyId
                && r.Status == DocumentStatus.Posted
            orderby r.ReceiptId descending
            select new ReceiptRef(r.ReceiptId, r.DocNo, r.Status.ToString(), a.AppliedAmount)
        ).ToListAsync(ct);

        var notes = await db.TaxAdjustmentNotes.AsNoTracking()
            .Where(n => n.CompanyId == tenant.CompanyId && n.OriginalTaxInvoiceId == id)
            .Select(n => new { n.NoteId, n.DocNo, n.Status, n.NoteType })
            .ToListAsync(ct);
        var credits = notes.Where(n => n.NoteType == TaxAdjustmentNoteType.Credit)
            .Select(n => new DocumentRef(n.NoteId, n.DocNo, n.Status.ToString()))
            .ToList();
        var debits = notes.Where(n => n.NoteType == TaxAdjustmentNoteType.Debit)
            .Select(n => new DocumentRef(n.NoteId, n.DocNo, n.Status.ToString()))
            .ToList();

        // Sprint 13i C7 — BN ↔ TI grouping now via sales.billing_note_tax_invoices join table.
        var billingNotes = await db.BillingNotes.AsNoTracking()
            .Where(b => b.CompanyId == tenant.CompanyId
                && b.TaxInvoiceLinks.Any(j => j.TaxInvoiceId == id))
            .Select(b => new DocumentRef(b.BillingNoteId, b.DocNo, b.Status.ToString()))
            .ToListAsync(ct);

        return new DocumentCrossRefDto(
            Quotation: quotation,
            SalesOrder: salesOrder,
            DeliveryOrder: deliveryOrder,
            TaxInvoices: Array.Empty<DocumentRef>(),
            Receipts: receipts,
            CreditNotes: credits,
            DebitNotes: debits,
            BillingNotes: billingNotes);
    }

    public async Task<DocumentCrossRefDto?> GetForReceiptAsync(long id, CancellationToken ct)
    {
        var exists = await db.Receipts.AsNoTracking()
            .AnyAsync(r => r.CompanyId == tenant.CompanyId && r.ReceiptId == id, ct);
        if (!exists) return null;

        var apps = await (
            from a in db.ReceiptApplications.AsNoTracking()
            join t in db.TaxInvoices.AsNoTracking() on a.TaxInvoiceId equals t.TaxInvoiceId
            where a.ReceiptId == id && t.CompanyId == tenant.CompanyId
            select new DocumentRef(t.TaxInvoiceId, t.DocNo, t.Status.ToString())
        ).ToListAsync(ct);

        return new DocumentCrossRefDto(
            Quotation: null,
            SalesOrder: null,
            DeliveryOrder: null,
            TaxInvoices: apps,
            Receipts: Array.Empty<ReceiptRef>(),
            CreditNotes: Array.Empty<DocumentRef>(),
            DebitNotes: Array.Empty<DocumentRef>(),
            BillingNotes: Array.Empty<DocumentRef>());
    }

    public async Task<DocumentCrossRefDto?> GetForAdjustmentNoteAsync(long id, CancellationToken ct)
    {
        var n = await db.TaxAdjustmentNotes.AsNoTracking()
            .Where(an => an.CompanyId == tenant.CompanyId && an.NoteId == id)
            .Select(an => new { an.NoteId, an.OriginalTaxInvoiceId, an.NoteType })
            .FirstOrDefaultAsync(ct);
        if (n is null) return null;

        var ti = await db.TaxInvoices.AsNoTracking()
            .Where(t => t.CompanyId == tenant.CompanyId && t.TaxInvoiceId == n.OriginalTaxInvoiceId)
            .Select(t => new DocumentRef(t.TaxInvoiceId, t.DocNo, t.Status.ToString()))
            .FirstOrDefaultAsync(ct);

        return new DocumentCrossRefDto(
            Quotation: null,
            SalesOrder: null,
            DeliveryOrder: null,
            TaxInvoices: ti is null ? Array.Empty<DocumentRef>() : new[] { ti },
            Receipts: Array.Empty<ReceiptRef>(),
            CreditNotes: Array.Empty<DocumentRef>(),
            DebitNotes: Array.Empty<DocumentRef>(),
            BillingNotes: Array.Empty<DocumentRef>());
    }

    // cont.69 Phase 3 (D7) — unified full-chain resolver. Strategy: from any anchor,
    // walk UP the FK graph to discover the originating Quotation + Sales Order (the
    // unique upstream roots), then walk DOWN from those roots collecting EVERY
    // descendant (fan-out: a Q may spawn many SO/DO/Invoice/TI/RC — all returned).
    // Every query is tenant-scoped (gotcha §26 belt-and-braces on top of the global filter).
    public async Task<DocumentChainDto?> GetChainAsync(string anchorType, long id, CancellationToken ct)
    {
        var cid = tenant.CompanyId;

        // Anchor IDs collected as we walk. Each set is the working frontier for the
        // down-walk; we seed it from the anchor then expand both directions.
        long? quotationId = null;
        var soIds = new HashSet<long>();
        var doIds = new HashSet<long>();
        var invIds = new HashSet<long>();   // BillingNote ids
        var tiIds = new HashSet<long>();
        var rcIds = new HashSet<long>();

        // ── 1. Seed from the anchor + verify it belongs to the tenant ──────────
        switch (anchorType)
        {
            case "quotation":
                if (!await db.Quotations.AnyAsync(x => x.CompanyId == cid && x.QuotationId == id, ct)) return null;
                quotationId = id;
                break;
            case "sales-order":
                if (!await db.SalesOrders.AnyAsync(x => x.CompanyId == cid && x.SalesOrderId == id, ct)) return null;
                soIds.Add(id);
                break;
            case "delivery-order":
                if (!await db.DeliveryOrders.AnyAsync(x => x.CompanyId == cid && x.DeliveryOrderId == id, ct)) return null;
                doIds.Add(id);
                break;
            case "billing-note":
                if (!await db.BillingNotes.AnyAsync(x => x.CompanyId == cid && x.BillingNoteId == id, ct)) return null;
                invIds.Add(id);
                break;
            case "tax-invoice":
                if (!await db.TaxInvoices.AnyAsync(x => x.CompanyId == cid && x.TaxInvoiceId == id, ct)) return null;
                tiIds.Add(id);
                break;
            case "receipt":
                if (!await db.Receipts.AnyAsync(x => x.CompanyId == cid && x.ReceiptId == id, ct)) return null;
                rcIds.Add(id);
                break;
            case "adjustment-note":
                var anTi = await db.TaxAdjustmentNotes.AsNoTracking()
                    .Where(x => x.CompanyId == cid && x.NoteId == id)
                    .Select(x => (long?)x.OriginalTaxInvoiceId).FirstOrDefaultAsync(ct);
                if (anTi is null) return null;
                tiIds.Add(anTi.Value);
                break;
            default:
                return null;
        }

        // ── 2. Walk UP: receipt → (TI|DO|Invoice); TI → Invoice/Q/legacy-DO;
        //              Invoice → DO/Q; DO → SO; SO → Q. ─────────────────────────
        if (rcIds.Count > 0)
        {
            var apps = await db.ReceiptApplications.AsNoTracking()
                .Where(a => rcIds.Contains(a.ReceiptId)
                    && db.Receipts.Any(r => r.ReceiptId == a.ReceiptId && r.CompanyId == cid))
                .Select(a => new { a.TaxInvoiceId, a.DeliveryOrderId, a.BillingNoteId })
                .ToListAsync(ct);
            foreach (var a in apps)
            {
                if (a.TaxInvoiceId is long t) tiIds.Add(t);
                if (a.DeliveryOrderId is long d) doIds.Add(d);
                if (a.BillingNoteId is long b) invIds.Add(b);
            }
        }

        if (tiIds.Count > 0)
        {
            var tis = await db.TaxInvoices.AsNoTracking()
                .Where(t => t.CompanyId == cid && tiIds.Contains(t.TaxInvoiceId))
                .Select(t => new { t.BillingNoteId, t.QuotationId })
                .ToListAsync(ct);
            foreach (var t in tis)
            {
                if (t.BillingNoteId is long b) invIds.Add(b);
                quotationId ??= t.QuotationId;
            }
            // Legacy Pattern X: DO carries TaxInvoiceId.
            var legacyDos = await db.DeliveryOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.TaxInvoiceId != null && tiIds.Contains(x.TaxInvoiceId!.Value))
                .Select(x => x.DeliveryOrderId).ToListAsync(ct);
            foreach (var d in legacyDos) doIds.Add(d);
        }

        if (invIds.Count > 0)
        {
            var invs = await db.BillingNotes.AsNoTracking()
                .Where(b => b.CompanyId == cid && invIds.Contains(b.BillingNoteId))
                .Select(b => new { b.DeliveryOrderId, b.QuotationId })
                .ToListAsync(ct);
            foreach (var b in invs)
            {
                if (b.DeliveryOrderId is long d) doIds.Add(d);
                quotationId ??= b.QuotationId;
            }
        }

        if (doIds.Count > 0)
        {
            var dos = await db.DeliveryOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && doIds.Contains(x.DeliveryOrderId))
                .Select(x => x.SalesOrderId).ToListAsync(ct);
            foreach (var so in dos) if (so is long s) soIds.Add(s);
        }

        if (soIds.Count > 0)
        {
            var sos = await db.SalesOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && soIds.Contains(x.SalesOrderId))
                .Select(x => x.QuotationId).ToListAsync(ct);
            quotationId ??= sos.FirstOrDefault(q => q != null);
        }

        // ── 3. Walk DOWN from the Quotation root (when present) collecting every
        //       descendant. Fan-out fully expanded. ─────────────────────────────
        if (quotationId is long qid)
        {
            var soDown = await db.SalesOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.QuotationId == qid)
                .Select(x => x.SalesOrderId).ToListAsync(ct);
            foreach (var s in soDown) soIds.Add(s);

            // TIs / Invoices can link straight to the Quotation (Path B).
            var tiQ = await db.TaxInvoices.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.QuotationId == qid)
                .Select(x => x.TaxInvoiceId).ToListAsync(ct);
            foreach (var t in tiQ) tiIds.Add(t);
            var invQ = await db.BillingNotes.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.QuotationId == qid)
                .Select(x => x.BillingNoteId).ToListAsync(ct);
            foreach (var b in invQ) invIds.Add(b);
        }

        if (soIds.Count > 0)
        {
            var doDown = await db.DeliveryOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.SalesOrderId != null && soIds.Contains(x.SalesOrderId!.Value))
                .Select(x => x.DeliveryOrderId).ToListAsync(ct);
            foreach (var d in doDown) doIds.Add(d);
        }

        if (doIds.Count > 0)
        {
            var invDown = await db.BillingNotes.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.DeliveryOrderId != null && doIds.Contains(x.DeliveryOrderId!.Value))
                .Select(x => x.BillingNoteId).ToListAsync(ct);
            foreach (var b in invDown) invIds.Add(b);
            // Legacy Pattern X DO→TI.
            var tiFromDo = await db.DeliveryOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && doIds.Contains(x.DeliveryOrderId) && x.TaxInvoiceId != null)
                .Select(x => x.TaxInvoiceId!.Value).ToListAsync(ct);
            foreach (var t in tiFromDo) tiIds.Add(t);
        }

        if (invIds.Count > 0)
        {
            var tiDown = await db.TaxInvoices.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.BillingNoteId != null && invIds.Contains(x.BillingNoteId!.Value))
                .Select(x => x.TaxInvoiceId).ToListAsync(ct);
            foreach (var t in tiDown) tiIds.Add(t);
        }

        // Receipts: any application pointing at a collected TI / DO / Invoice.
        var rcDown = await db.ReceiptApplications.AsNoTracking()
            .Where(a => db.Receipts.Any(r => r.ReceiptId == a.ReceiptId && r.CompanyId == cid)
                && ((a.TaxInvoiceId != null && tiIds.Contains(a.TaxInvoiceId!.Value))
                    || (a.DeliveryOrderId != null && doIds.Contains(a.DeliveryOrderId!.Value))
                    || (a.BillingNoteId != null && invIds.Contains(a.BillingNoteId!.Value))))
            .Select(a => a.ReceiptId).Distinct().ToListAsync(ct);
        foreach (var r in rcDown) rcIds.Add(r);

        // ── 4. Materialize nodes (tenant-scoped). ──────────────────────────────
        ChainNode? quotation = quotationId is long fq
            ? await db.Quotations.AsNoTracking()
                .Where(x => x.CompanyId == cid && x.QuotationId == fq)
                .Select(x => new ChainNode(x.QuotationId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
                .FirstOrDefaultAsync(ct)
            : null;

        // Single SO root — pick the first (chain is normally linear; fan-out at SO is rare).
        ChainNode? salesOrder = soIds.Count > 0
            ? await db.SalesOrders.AsNoTracking()
                .Where(x => x.CompanyId == cid && soIds.Contains(x.SalesOrderId))
                .OrderBy(x => x.SalesOrderId)
                .Select(x => new ChainNode(x.SalesOrderId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
                .FirstOrDefaultAsync(ct)
            : null;

        var deliveryOrders = doIds.Count == 0 ? new List<ChainNode>() : await db.DeliveryOrders.AsNoTracking()
            .Where(x => x.CompanyId == cid && doIds.Contains(x.DeliveryOrderId))
            .OrderBy(x => x.DeliveryOrderId)
            .Select(x => new ChainNode(x.DeliveryOrderId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
            .ToListAsync(ct);

        var invoices = invIds.Count == 0 ? new List<ChainNode>() : await db.BillingNotes.AsNoTracking()
            .Where(x => x.CompanyId == cid && invIds.Contains(x.BillingNoteId))
            .OrderBy(x => x.BillingNoteId)
            .Select(x => new ChainNode(x.BillingNoteId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
            .ToListAsync(ct);

        var taxInvoices = tiIds.Count == 0 ? new List<ChainNode>() : await db.TaxInvoices.AsNoTracking()
            .Where(x => x.CompanyId == cid && tiIds.Contains(x.TaxInvoiceId))
            .OrderBy(x => x.TaxInvoiceId)
            .Select(x => new ChainNode(x.TaxInvoiceId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
            .ToListAsync(ct);

        var receipts = rcIds.Count == 0 ? new List<ChainNode>() : await db.Receipts.AsNoTracking()
            .Where(x => x.CompanyId == cid && rcIds.Contains(x.ReceiptId))
            .OrderBy(x => x.ReceiptId)
            .Select(x => new ChainNode(x.ReceiptId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount))
            .ToListAsync(ct);

        // Adjustment notes hang off any collected TI.
        var adjustmentNotes = tiIds.Count == 0 ? new List<ChainNode>() : await db.TaxAdjustmentNotes.AsNoTracking()
            .Where(x => x.CompanyId == cid && tiIds.Contains(x.OriginalTaxInvoiceId))
            .OrderBy(x => x.NoteId)
            .Select(x => new ChainNode(x.NoteId, x.DocNo, x.DocDate, x.Status.ToString(), x.TotalAmount,
                x.NoteType.ToString()))
            .ToListAsync(ct);

        return new DocumentChainDto(
            quotation, salesOrder, deliveryOrders, invoices, taxInvoices, receipts, adjustmentNotes);
    }
}
