using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Read + 50 ทวิ PDF render. Certificates are issued in
/// <see cref="PaymentVoucherService.PostAsync"/> — this service never writes.
/// </summary>
public sealed class WhtCertificateService(AccountingDbContext db, ITenantContext tenant)
    : IWhtCertificateService
{
    public async Task<CursorPage<WhtCertificateListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = db.WhtCertificates.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(w => w.WhtCertificateId < c);
        var rows = await q.OrderByDescending(w => w.WhtCertificateId).Take(lim + 1)
            .Select(w => new WhtCertificateListItem(
                w.WhtCertificateId, w.DocNo, w.CertDate, w.PaymentVoucherId,
                w.PayeeName, w.PayeeTaxId, w.IncomeTypeCode, w.IncomeAmount,
                w.WhtAmount, w.FormType.ToString(), w.Status.ToString()))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<WhtCertificateListItem>(
            rows, more ? rows[^1].WhtCertificateId : null, more);
    }

    public async Task<WhtCertificateDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var w = await db.WhtCertificates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WhtCertificateId == id, ct);
        if (w is null) return null;
        return new WhtCertificateDetail(
            w.WhtCertificateId, w.DocNo, w.CertDate, w.PaymentVoucherId, w.FormType.ToString(),
            w.PayerName, w.PayerTaxId, w.PayerBranchCode, w.PayerAddress,
            w.PayeeName, w.PayeeTaxId, w.PayeeAddress, w.PayeeType.ToString(),
            w.IncomeTypeCode, w.IncomeDescription, w.IncomeAmount, w.WhtRate, w.WhtAmount,
            w.Status.ToString(), w.IssuedAt);
    }

    /// <summary>
    /// หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ — layout per RD form
    /// (docs/accounting-system-plan.md §15.10): payer/payee blocks, ภ.ง.ด. type,
    /// income-type row (ม.40), tax withheld, total.
    /// </summary>
    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("wht.not_found", $"WHT certificate {id} not found.");

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(s => s.FontSize(10));

            page.Header().Column(h =>
            {
                h.Item().AlignCenter().Text("หนังสือรับรองการหักภาษี ณ ที่จ่าย").Bold().FontSize(14);
                h.Item().AlignCenter().Text("ตามมาตรา 50 ทวิ แห่งประมวลรัษฎากร / Withholding Tax Certificate")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                h.Item().AlignRight().Text($"เล่มที่/เลขที่ No.: {d.DocNo}").Bold();
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(6);

                col.Item().Border(1).Padding(6).Column(payer =>
                {
                    payer.Item().Text("ผู้มีหน้าที่หักภาษี ณ ที่จ่าย (Payer)").Bold();
                    payer.Item().Text($"ชื่อ / Name: {d.PayerName}");
                    payer.Item().Text($"เลขประจำตัวผู้เสียภาษี / Tax ID: {d.PayerTaxId}"
                        + $"   สาขา / Branch: {d.PayerBranchCode}");
                    payer.Item().Text($"ที่อยู่ / Address: {d.PayerAddress}");
                });

                col.Item().Border(1).Padding(6).Column(payee =>
                {
                    payee.Item().Text("ผู้ถูกหักภาษี ณ ที่จ่าย (Payee)").Bold();
                    payee.Item().Text($"ชื่อ / Name: {d.PayeeName}");
                    payee.Item().Text($"เลขประจำตัวผู้เสียภาษี / Tax ID: {d.PayeeTaxId ?? "-"}");
                    payee.Item().Text($"ที่อยู่ / Address: {d.PayeeAddress}");
                    payee.Item().Text($"ประเภทผู้รับเงิน / Type: {d.PayeeType}");
                });

                col.Item().Text($"แบบยื่นรายการ / Return type: {d.FormType}"
                    + $"     ลงวันที่ / Date: {d.CertDate:dd/MM/yyyy}").Bold();

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    static IContainer H(IContainer x) =>
                        x.Background(Colors.Grey.Lighten3).Border(1).Padding(4);
                    static IContainer C(IContainer x) => x.Border(1).Padding(4);

                    t.Header(hr =>
                    {
                        hr.Cell().Element(H).Text("ประเภทเงินได้ / Income type (ม.40)").Bold();
                        hr.Cell().Element(H).AlignRight().Text("จำนวนเงิน / Amount").Bold();
                        hr.Cell().Element(H).AlignRight().Text("ภาษีที่หัก / Tax").Bold();
                    });
                    t.Cell().Element(C).Text(
                        $"{d.IncomeTypeCode}"
                        + (string.IsNullOrEmpty(d.IncomeDescription) ? "" : $" — {d.IncomeDescription}")
                        + $"  (อัตรา {d.WhtRate:P2})");
                    t.Cell().Element(C).AlignRight().Text($"{d.IncomeAmount:N2}");
                    t.Cell().Element(C).AlignRight().Text($"{d.WhtAmount:N2}");

                    t.Cell().Element(C).AlignRight().Text("รวม / Total").Bold();
                    t.Cell().Element(C).AlignRight().Text($"{d.IncomeAmount:N2}").Bold();
                    t.Cell().Element(C).AlignRight().Text($"{d.WhtAmount:N2}").Bold();
                });

                col.Item().PaddingTop(8).Text(
                    "ผู้จ่ายเงินออกหนังสือรับรองฉบับนี้เพื่อเป็นหลักฐานการหักภาษี ณ ที่จ่าย")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(20).AlignRight().Text("ลงชื่อ ......................................... ผู้จ่ายเงิน");
            });

            page.Footer().AlignCenter()
                .Text($"ออกโดยระบบ TEAS — อ้างอิงใบสำคัญจ่าย PV#{d.PaymentVoucherId}")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }
}
