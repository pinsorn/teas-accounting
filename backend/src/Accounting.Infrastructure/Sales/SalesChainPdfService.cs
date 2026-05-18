using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// Sprint 10 C3 — Q/SO/DO PDF. Quotation footer carries the optional WHT note
/// (B4): when ShowWhtNote && CORPORATE && any SERVICE-product line, show the
/// 3%-of-service withholding + net — computed on the fly, never stored.
/// </summary>
public sealed class SalesChainPdfService(AccountingDbContext db, ITenantContext tenant)
    : ISalesChainPdfService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    private static byte[] Render(string title, string? docNo, DateOnly date,
        string customer, IEnumerable<(string desc, decimal qty, decimal price, decimal total)> lines,
        decimal grand, Action<ColumnDescriptor>? footerNote)
        => Document.Create(d => d.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(28); p.DefaultTextStyle(s => s.FontSize(10));
            p.Header().AlignCenter().Text(title).Bold().FontSize(15);
            p.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(4);
                col.Item().Text($"เลขที่ / No.: {docNo ?? "(ร่าง)"}");
                col.Item().Text($"วันที่ / Date: {date:dd/MM/yyyy}");
                col.Item().Text($"ลูกค้า / Customer: {customer}");
                col.Item().PaddingTop(6).LineHorizontal(0.5f);
                foreach (var l in lines)
                    col.Item().Text($"{l.desc}  x{l.qty:N2} @ {l.price:N2} = {l.total:N2}");
                col.Item().PaddingTop(4).LineHorizontal(0.5f);
                col.Item().AlignRight().Text($"รวม / Total: {grand:N2}").Bold().FontSize(12);
                footerNote?.Invoke(col);
            });
            p.Footer().AlignCenter().Text("ออกโดยระบบ TEAS").FontColor(Colors.Grey.Medium);
        })).GeneratePdf();

    public async Task<byte[]> QuotationPdfAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await db.Quotations.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct)
            ?? throw new DomainException("quotation.not_found", $"Quotation {id} not found.");

        Action<ColumnDescriptor>? note = null;
        if (q.ShowWhtNote && q.CustomerType == CustomerType.Corporate)
        {
            var svcIds = (await db.Products.AsNoTracking()
                .Where(pr => pr.ProductType == ProductType.Service
                          || pr.ProductType == ProductType.ExemptService)
                .Select(pr => pr.ProductId).ToListAsync(ct)).ToHashSet();
            var serviceSub = q.Lines
                .Where(l => l.ProductId is { } pid && svcIds.Contains(pid))
                .Sum(l => l.LineAmount);
            if (serviceSub > 0m)
            {
                var wht = Math.Round(serviceSub * 0.03m, 2, MidpointRounding.AwayFromZero);
                note = col =>
                {
                    col.Item().PaddingTop(10).LineHorizontal(0.5f);
                    col.Item().Text("หมายเหตุ (สำหรับลูกค้านิติบุคคล):").Bold();
                    col.Item().Text($"  หัก ภาษี ณ ที่จ่าย 3% ของส่วนบริการ: {wht:N2}");
                    col.Item().Text($"  ยอดสุทธิที่ชำระ: {(q.TotalAmount - wht):N2}").Bold();
                };
            }
        }
        return Render("ใบเสนอราคา / QUOTATION", q.DocNo, q.DocDate, q.CustomerName,
            q.Lines.OrderBy(l => l.LineNo).Select(l =>
                (l.DescriptionTh, l.Quantity, l.UnitPrice, l.TotalAmount)),
            q.TotalAmount, note);
    }

    public async Task<byte[]> SalesOrderPdfAsync(long id, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {id} not found.");
        return Render("ใบสั่งขาย / SALES ORDER", so.DocNo, so.DocDate, so.CustomerName,
            so.Lines.OrderBy(l => l.LineNo).Select(l =>
                (l.DescriptionTh, l.Quantity, l.UnitPrice, l.TotalAmount)),
            so.TotalAmount, null);
    }

    public async Task<byte[]> DeliveryOrderPdfAsync(long id, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        var title = dord.IsCombinedWithTi
            ? "ใบส่งของ-ใบกำกับภาษี / DELIVERY ORDER & TAX INVOICE"
            : "ใบส่งของ / DELIVERY ORDER";
        return Render(title, dord.DocNo, dord.DocDate, dord.CustomerName,
            dord.Lines.OrderBy(l => l.LineNo).Select(l =>
                (l.DescriptionTh, l.Quantity, l.UnitPrice, l.TotalAmount)),
            dord.TotalAmount, null);
    }
}
