using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// Sprint-2 read surface for Tax Invoices: list (cursor) / detail / xml / pdf / resend.
/// Tenant scoping comes from the DbContext global query filter — no manual company_id here.
/// </summary>
public sealed partial class TaxInvoiceService
{
    public async Task<CursorPage<TaxInvoiceListItem>> ListAsync(
        TaxInvoiceListQuery q, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var limit = Math.Clamp(q.Limit, 1, 100);

        var query = _db.TaxInvoices.AsNoTracking().AsQueryable();

        if (q.DateFrom is { } df) query = query.Where(t => t.DocDate >= df);
        if (q.DateTo   is { } dt) query = query.Where(t => t.DocDate <= dt);
        if (q.CustomerId is { } cid) query = query.Where(t => t.CustomerId == cid);
        if (!string.IsNullOrWhiteSpace(q.Status)
            && Enum.TryParse<DocumentStatus>(q.Status, ignoreCase: true, out var st))
            query = query.Where(t => t.Status == st);
        // Sprint 8 — BU filter. include_unspecified also surfaces NULL-BU (legacy) rows.
        if (q.BusinessUnitId is { } bu)
            query = q.IncludeUnspecified
                ? query.Where(t => t.BusinessUnitId == bu || t.BusinessUnitId == null)
                : query.Where(t => t.BusinessUnitId == bu);

        // Sprint 13e — TaxInvoicePicker support: free-text (doc_no / customer name)
        // + unpaid-only (RC reference). unpaid = still has a remaining balance.
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = $"%{q.Search.Trim()}%";
            query = query.Where(t =>
                (t.DocNo != null && EF.Functions.ILike(t.DocNo, like))
                || EF.Functions.ILike(t.CustomerName, like));
        }
        if (q.Unpaid)
            query = query.Where(t => t.AmountPaid < t.TotalAmount);

        // Desc paging by id; cursor = last id from the previous page.
        if (q.Cursor is { } cur) query = query.Where(t => t.TaxInvoiceId < cur);

        var rows = await query
            .OrderByDescending(t => t.TaxInvoiceId)
            .Take(limit + 1)
            .Select(t => new TaxInvoiceListItem(
                t.TaxInvoiceId, t.DocNo, t.DocDate, t.CustomerName, t.CustomerTaxId,
                t.TotalAmount, t.TaxAmount, t.Status.ToString(), t.PaymentStatus, t.CurrencyCode,
                t.CustomerId, t.BusinessUnitId))
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var next = hasMore && rows.Count > 0 ? rows[^1].TaxInvoiceId : (long?)null;

        return new CursorPage<TaxInvoiceListItem>(rows, next, hasMore);
    }

    public async Task<TaxInvoiceDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var t = await _db.TaxInvoices.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.TaxInvoiceId == id, ct);
        if (t is null) return null;

        var buCode = t.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(b => b.BusinessUnitId == bid)
                .Select(b => b.Code).FirstOrDefaultAsync(ct)
            : null;

        return new TaxInvoiceDetail(
            t.TaxInvoiceId, t.DocNo, t.Status.ToString(), t.DocDate, t.TaxPointDate,
            t.SupplierName, t.SupplierTaxId, t.SupplierBranchCode, t.SupplierAddress,
            t.CustomerId, t.CustomerName, t.CustomerTaxId, t.CustomerBranchCode,
            t.CustomerAddress, t.CustomerVatRegistered,
            t.CurrencyCode, t.IsTaxInclusive,
            t.SubtotalAmount, t.DiscountAmount, t.TaxableAmount, t.NonTaxableAmount,
            t.TaxAmount, t.TotalAmount, t.PaymentStatus, t.DueDate, t.Notes, t.PostedAt,
            t.BusinessUnitId, buCode,
            t.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceDetailLine(
                l.LineNo, l.ProductCode, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountAmount, l.LineAmount, l.TaxCode, l.TaxRate,
                l.TaxAmount, l.TotalAmount)).ToList(),
            t.QuotationId);   // Sprint 13h P6.1 — cross-ref
    }

    public Task<string> BuildXmlAsync(long id, CancellationToken ct) =>
        Task.FromResult(_etaxXml.BuildTaxInvoiceXml(id, ct));

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");

        // Sprint 8.5 — non-VAT companies must NOT head the doc "ใบกำกับภาษี" (ม.86).
        var tax = await _taxCfg.GetAsync(ct);
        var (hdrTh, hdrEn) = DocumentLabels.TaxInvoiceHeader(
            tax.VatMode, tax.NonVatDocLabelTh, tax.NonVatDocLabelEn);

        // Sprint 13j-PDF — render via the shared PaperDocument mirror, IDENTICAL to
        // the FE TI detail mapping (tax-invoices/[id]/page.tsx): posted snapshot for
        // seller+buyer (immutable §4.2), taxId formatted, line amount = net lineAmount,
        // summary = subtotal/discount/beforeVat(taxable)/vat/total with no vatRate
        // (PaperFoot defaults 7%). docType keeps the §8.5 non-VAT label.
        var cfg = Pdf.PaperDoc.Config[Pdf.PaperDocKind.TaxInvoice];
        var model = new Pdf.PaperDocModel(
            DocType: hdrTh,
            DocTypeEn: hdrEn,
            DocNo: d.DocNo ?? string.Empty,
            IssueDate: d.DocDate,
            Seller: new Pdf.PaperSeller(d.SupplierName, Pdf.PaperFormat.TaxId(d.SupplierTaxId) ?? d.SupplierTaxId, d.SupplierBranchCode, d.SupplierAddress),
            Customer: new Pdf.PaperCustomer(d.CustomerName, Pdf.PaperFormat.TaxId(d.CustomerTaxId), d.CustomerBranchCode, d.CustomerAddress),
            Items: d.Lines.OrderBy(l => l.LineNo).Select(l => new Pdf.PaperLine(
                l.DescriptionTh, null, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList(),
            Summary: new Pdf.PaperSummary(
                d.SubtotalAmount, d.DiscountAmount > 0m ? d.DiscountAmount : null,
                d.TaxableAmount, d.TaxAmount, d.TotalAmount, null, ShowVat: tax.VatMode,
                // ponytail (01-L3): pass non-taxable amount so the exempt row renders when > 0
                NonTaxable: d.NonTaxableAmount > 0m ? d.NonTaxableAmount : null),
            SignRoles: new Pdf.PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            Notes: d.Notes,
            Watermark: copy
                ? new Pdf.PaperWatermark("สำเนา", Pdf.PaperWatermarkVariant.Warning)
                : Pdf.PaperDoc.Watermark(Pdf.PaperDocKind.TaxInvoice, d.Status));
        return Pdf.PaperDocumentPdf.Render(model);
    }

    public async Task<TaxInvoiceResendResult> ResendAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var ti = await _db.TaxInvoices.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TaxInvoiceId == id, ct)
            ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");

        if (ti.Status != DocumentStatus.Posted)
            throw new DomainException("ti.not_posted", "Only a POSTED Tax Invoice can be resent.");

        // e-Tax pipeline is inert (ETaxBehaviorOptions.Enabled=false). Wire-through only.
        if (!_etaxOpts.Enabled || !_etaxOpts.AutoSendOnTaxInvoicePost)
            return new TaxInvoiceResendResult(id, Sent: false,
                "e-Tax delivery is disabled (inert). No email sent.");

        await TryAutoSendETaxAsync(ti, ct);
        _activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "Resent",
            note: "ส่ง e-Tax อีกครั้ง");
        await _db.SaveChangesAsync(ct);
        return new TaxInvoiceResendResult(id, Sent: true, "e-Tax email re-sent.");
    }
}
