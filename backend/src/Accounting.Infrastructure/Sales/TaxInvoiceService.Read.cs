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

        // Desc paging by id; cursor = last id from the previous page.
        if (q.Cursor is { } cur) query = query.Where(t => t.TaxInvoiceId < cur);

        var rows = await query
            .OrderByDescending(t => t.TaxInvoiceId)
            .Take(limit + 1)
            .Select(t => new TaxInvoiceListItem(
                t.TaxInvoiceId, t.DocNo, t.DocDate, t.CustomerName, t.CustomerTaxId,
                t.TotalAmount, t.TaxAmount, t.Status.ToString(), t.PaymentStatus, t.CurrencyCode))
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
                l.TaxAmount, l.TotalAmount)).ToList());
    }

    public Task<string> BuildXmlAsync(long id, CancellationToken ct) =>
        Task.FromResult(_etaxXml.BuildTaxInvoiceXml(id, ct));

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");

        // Sprint 8.5 — non-VAT companies must NOT head the doc "ใบกำกับภาษี" (ม.86).
        var (hdrTh, hdrEn) = DocumentLabels.TaxInvoiceHeader(
            _vat.VatMode, _vat.NonVatDocLabelTh, _vat.NonVatDocLabelEn);
        var showVat = DocumentLabels.ShowVatBreakdown(_vat.VatMode);

        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(28);
                p.DefaultTextStyle(s => s.FontSize(9));

                p.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text($"{hdrTh} / {hdrEn}").Bold().FontSize(15);
                    col.Item().AlignCenter().Text(d.Status == "Posted" ? "(ต้นฉบับ / ORIGINAL)" : "(ร่าง / DRAFT)")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                p.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(s =>
                        {
                            s.Item().Text("ผู้ขาย / Seller").Bold();
                            s.Item().Text(d.SupplierName);
                            s.Item().Text(d.SupplierAddress).FontSize(8);
                            s.Item().Text($"เลขประจำตัวผู้เสียภาษี: {d.SupplierTaxId}  สาขา: {d.SupplierBranchCode}");
                        });
                        r.ConstantItem(12);
                        r.RelativeItem().Column(s =>
                        {
                            s.Item().Text("ผู้ซื้อ / Buyer").Bold();
                            s.Item().Text(d.CustomerName);
                            s.Item().Text(d.CustomerAddress).FontSize(8);
                            if (!string.IsNullOrEmpty(d.CustomerTaxId))
                                s.Item().Text($"เลขประจำตัวผู้เสียภาษี: {d.CustomerTaxId}  สาขา: {d.CustomerBranchCode}");
                        });
                    });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"เลขที่ / No.: {d.DocNo ?? "(ร่าง)"}");
                        r.RelativeItem().AlignRight().Text($"วันที่ / Date: {d.DocDate:dd/MM/yyyy}");
                    });

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(24);   // #
                            c.RelativeColumn(5);     // desc
                            c.RelativeColumn(1.4f);  // qty
                            c.RelativeColumn(1.8f);  // unit price
                            c.RelativeColumn(2);     // amount
                        });
                        t.Header(h =>
                        {
                            h.Cell().Element(Th).Text("#");
                            h.Cell().Element(Th).Text("รายการ / Description");
                            h.Cell().Element(Th).AlignRight().Text("จำนวน");
                            h.Cell().Element(Th).AlignRight().Text("ราคา/หน่วย");
                            h.Cell().Element(Th).AlignRight().Text("จำนวนเงิน");
                        });
                        foreach (var l in d.Lines)
                        {
                            t.Cell().Element(Td).Text(l.LineNo.ToString());
                            t.Cell().Element(Td).Text(l.DescriptionTh);
                            t.Cell().Element(Td).AlignRight().Text(Num(l.Quantity));
                            t.Cell().Element(Td).AlignRight().Text(Num(l.UnitPrice));
                            t.Cell().Element(Td).AlignRight().Text(Num(l.LineAmount));
                        }
                    });

                    col.Item().AlignRight().Column(s =>
                    {
                        // Non-VAT: no output VAT to report — single total only (§2.1).
                        if (showVat)
                        {
                            s.Item().Text($"มูลค่าสินค้า / Subtotal: {Num(d.SubtotalAmount)} {d.CurrencyCode}");
                            s.Item().Text($"ภาษีมูลค่าเพิ่ม / VAT: {Num(d.TaxAmount)} {d.CurrencyCode}").Bold();
                        }
                        s.Item().Text($"ยอดรวม / Total: {Num(d.TotalAmount)} {d.CurrencyCode}").Bold().FontSize(11);
                    });
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("ออกโดยระบบ TEAS — ");
                    t.Span(DateTimeOffset.UtcNow.ToString("u")).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        static IContainer Th(IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Padding(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);
        static IContainer Td(IContainer c) => c.Padding(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
        static string Num(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
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
        return new TaxInvoiceResendResult(id, Sent: true, "e-Tax email re-sent.");
    }
}
