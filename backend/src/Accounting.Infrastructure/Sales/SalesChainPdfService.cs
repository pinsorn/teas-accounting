using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// Q / SO / DO PDF. Sprint 13j-PDF: renders via the shared PaperDocument mirror
/// (1:1 with the FE preview / lib/paper.css) instead of the old plain-text layout.
/// Seller = the tenant company (HQ branch); customer = the document's own snapshot.
/// Quotation keeps the §B4 WHT note (corporate + service lines) in the notes block.
/// </summary>
public sealed class SalesChainPdfService(
    AccountingDbContext db, ITenantContext tenant, ICompanyTaxConfigService taxCfg)
    : ISalesChainPdfService
{
    // Non-VAT companies (ม.86): suppress the VAT total rows on Q/SO/DO —
    // resolved per request from the company row (per-company-vat-mode spec).
    private async Task<bool> ShowVatAsync(CancellationToken ct) =>
        (await taxCfg.GetAsync(ct)).VatMode;

    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    private Task<PaperSeller> SellerAsync(CancellationToken ct) =>
        PaperSellerSource.FromCompanyProfileAsync(db, tenant.CompanyId, ct);

    // Mirror the FE detail line mapping EXACTLY (must be identical text — Ham
    // 2026-05-22): {description, quantity, unit, unitPrice, amount: lineAmount}.
    // No descriptionSub, no discountPercent column; amount = net LINE amount (not
    // the VAT-inclusive total — that bug printed 80,000 for a 10,000 line).
    private static PaperLine Line(decimal qty, string desc, string? unit, decimal unitPrice, decimal lineAmount) =>
        new(desc, null, qty, unit, unitPrice, null, lineAmount);

    // FE Q/SO/DO/BN summaries pass NO vatRate (PaperFoot defaults to 7%); mirror that.
    // ShowVat=false (non-VAT) collapses the foot to a single Total row.
    private static PaperSummary Summary(decimal subtotal, decimal vat, decimal total, bool showVat) =>
        new(subtotal, null, null, vat, total, null, showVat);

    // cont.69 Phase 4 (D8) — copy=true forces the สำเนา watermark; otherwise the
    // status-based watermark (DRAFT/CANCELLED/…) is used. Mirrors TaxInvoice.BuildPdfAsync.
    private static PaperWatermark? CopyOrStatus(bool copy, PaperWatermark? statusWm) =>
        copy ? new PaperWatermark("สำเนา", PaperWatermarkVariant.Warning) : statusWm;

    public async Task<byte[]> QuotationPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var q = await db.Quotations.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct)
            ?? throw new DomainException("quotation.not_found", $"Quotation {id} not found.");

        // Identical to FE quotations/[id]: notes ?? (showWhtNote ? whtNote-i18n : null).
        var notes = q.Notes ?? (q.ShowWhtNote
            ? "หมายเหตุ: ลูกค้านิติบุคคลหัก ณ ที่จ่าย 3% เฉพาะส่วนบริการ"
            : null);

        var cfg = PaperDoc.Config[PaperDocKind.Quotation];
        var model = new PaperDocModel(
            cfg.DocType, cfg.DocTypeEn, q.DocNo ?? string.Empty, q.DocDate,
            await SellerAsync(ct),
            new PaperCustomer(q.CustomerName, PaperFormat.TaxId(q.CustomerTaxId), null, q.CustomerAddress),
            q.Lines.OrderBy(l => l.LineNo).Select(l =>
                Line(l.Quantity, l.DescriptionTh, l.UomText, l.UnitPrice, l.LineAmount)).ToList(),
            Summary(q.SubtotalAmount, q.VatAmount, q.TotalAmount, await ShowVatAsync(ct)),
            new PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            ValidUntil: q.ValidUntilDate, ValidUntilLabel: cfg.ValidUntilLabel,
            Notes: notes,
            Watermark: CopyOrStatus(copy, PaperDoc.Watermark(PaperDocKind.Quotation, q.Status.ToString())));
        return PaperDocumentPdf.Render(model);
    }

    public async Task<byte[]> SalesOrderPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {id} not found.");
        var cfg = PaperDoc.Config[PaperDocKind.SalesOrder];
        var model = new PaperDocModel(
            cfg.DocType, cfg.DocTypeEn, so.DocNo ?? string.Empty, so.DocDate,
            await SellerAsync(ct),
            new PaperCustomer(so.CustomerName, PaperFormat.TaxId(so.CustomerTaxId), null, so.CustomerAddress),
            so.Lines.OrderBy(l => l.LineNo).Select(l =>
                Line(l.Quantity, l.DescriptionTh, l.UomText, l.UnitPrice, l.LineAmount)).ToList(),
            Summary(so.SubtotalAmount, so.VatAmount, so.TotalAmount, await ShowVatAsync(ct)),
            new PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            Notes: so.Notes,
            Watermark: CopyOrStatus(copy, PaperDoc.Watermark(PaperDocKind.SalesOrder, so.Status.ToString())));
        return PaperDocumentPdf.Render(model);
    }

    public async Task<byte[]> DeliveryOrderPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var dord = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        var cfg = PaperDoc.Config[PaperDocKind.DeliveryOrder];
        var docTypeTh = dord.IsCombinedWithTi ? "ใบส่งของ-ใบกำกับภาษี" : cfg.DocType;
        var docTypeEn = dord.IsCombinedWithTi ? "DELIVERY ORDER & TAX INVOICE" : cfg.DocTypeEn;
        var model = new PaperDocModel(
            docTypeTh, docTypeEn, dord.DocNo ?? string.Empty, dord.DocDate,
            await SellerAsync(ct),
            new PaperCustomer(dord.CustomerName, PaperFormat.TaxId(dord.CustomerTaxId), null, dord.CustomerAddress),
            dord.Lines.OrderBy(l => l.LineNo).Select(l =>
                Line(l.Quantity, l.DescriptionTh, l.UomText, l.UnitPrice, l.LineAmount)).ToList(),
            Summary(dord.SubtotalAmount, dord.VatAmount, dord.TotalAmount, await ShowVatAsync(ct)),
            new PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            Notes: dord.Notes,
            Watermark: CopyOrStatus(copy, PaperDoc.Watermark(PaperDocKind.DeliveryOrder, dord.Status.ToString())));
        return PaperDocumentPdf.Render(model);
    }
}
