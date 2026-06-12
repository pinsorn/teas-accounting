using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Read + 50 ทวิ PDF render/serve. Certificates are issued in
/// <see cref="PaymentVoucherService.PostAsync"/>; the only write here is the lazily
/// materialized <see cref="WhtCertificate.PdfStoragePath"/> — on first render the PDF is
/// persisted via <see cref="IFileStorageService"/> and frozen (the cert's source data is
/// immutable, so the stored copy is the canonical issued document on every later download).
/// </summary>
public sealed class WhtCertificateService(
    AccountingDbContext db, ITenantContext tenant, IFileStorageService storage)
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
            w.Status.ToString(), w.IssuedAt, w.WhtCondition);
    }

    /// <summary>
    /// หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ — layout per RD form
    /// (docs/accounting-system-plan.md §15.10): payer/payee blocks, ภ.ง.ด. type,
    /// income-type row (ม.40), tax withheld, total.
    /// </summary>
    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        // Tracked load — BuildPdfAsync persists PdfStoragePath on first materialization.
        var w = await db.WhtCertificates.FirstOrDefaultAsync(x => x.WhtCertificateId == id, ct)
            ?? throw new DomainException("wht.not_found", $"WHT certificate {id} not found.");

        // Serve the frozen issued copy if it has already been materialized.
        if (!string.IsNullOrEmpty(w.PdfStoragePath) && await storage.ExistsAsync(w.PdfStoragePath, ct))
        {
            await using var s = await storage.OpenReadAsync(w.PdfStoragePath, ct);
            using var buf = new MemoryStream();
            await s.CopyToAsync(buf, ct);
            return buf.ToArray();
        }

        var bytes = RenderPdf(w);

        // Persist + freeze: store via IFileStorageService and pin the path. The cert's source
        // data is immutable (snapshotted at PV-post), so this becomes the canonical copy.
        using (var src = new MemoryStream(bytes, writable: false))
            w.PdfStoragePath = await storage.SaveAsync(
                w.CompanyId, "WHT_CERTIFICATE", w.WhtCertificateId, src, $"{w.DocNo}.pdf", ct);
        await db.SaveChangesAsync(ct);
        return bytes;
    }

    private static byte[] RenderPdf(WhtCertificate w)
    {
        var formType = w.FormType.ToString();

        // Domestic certs → fill the official RD 50ทวิ AcroForm (Ham's requirement:
        // "fill ใส่ไฟล์นี้"). Foreign ภ.ง.ด.54 has no checkbox on this form, so it
        // keeps the QuestPDF layout below as a fallback.
        // RD 50ทวิ must be issued in 2 copies (ฉบับ1 แนบแบบ / ฉบับ2 เก็บหลักฐาน) →
        // FillCopies emits a 2-page PDF (the form pre-prints both ฉบับ labels).
        if (formType is "Pnd1" or "Pnd2" or "Pnd3" or "Pnd53")
            return Wht50TawiFormFiller.FillCopies(new Wht50TawiData(
                DocNo: w.DocNo, FormType: formType,
                PayerName: w.PayerName, PayerTaxId: w.PayerTaxId, PayerAddress: w.PayerAddress,
                PayeeName: w.PayeeName, PayeeTaxId: w.PayeeTaxId, PayeeAddress: w.PayeeAddress,
                IncomeTypeMa40: w.IncomeTypeCode, IncomeDescription: w.IncomeDescription,
                PayDate: w.CertDate, IncomeAmount: w.IncomeAmount, WhtAmount: w.WhtAmount,
                Condition: w.WhtCondition));

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
                h.Item().AlignRight().Text($"เล่มที่/เลขที่ No.: {w.DocNo}").Bold();
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(6);

                col.Item().Border(1).Padding(6).Column(payer =>
                {
                    payer.Item().Text("ผู้มีหน้าที่หักภาษี ณ ที่จ่าย (Payer)").Bold();
                    payer.Item().Text($"ชื่อ / Name: {w.PayerName}");
                    payer.Item().Text($"เลขประจำตัวผู้เสียภาษี / Tax ID: {w.PayerTaxId}"
                        + $"   สาขา / Branch: {w.PayerBranchCode}");
                    payer.Item().Text($"ที่อยู่ / Address: {w.PayerAddress}");
                });

                col.Item().Border(1).Padding(6).Column(payee =>
                {
                    payee.Item().Text("ผู้ถูกหักภาษี ณ ที่จ่าย (Payee)").Bold();
                    payee.Item().Text($"ชื่อ / Name: {w.PayeeName}");
                    payee.Item().Text($"เลขประจำตัวผู้เสียภาษี / Tax ID: {w.PayeeTaxId ?? "-"}");
                    payee.Item().Text($"ที่อยู่ / Address: {w.PayeeAddress}");
                    payee.Item().Text($"ประเภทผู้รับเงิน / Type: {w.PayeeType}");
                });

                col.Item().Text($"แบบยื่นรายการ / Return type: {formType}"
                    + $"     ลงวันที่ / Date: {w.CertDate:dd/MM/yyyy}").Bold();

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
                        // income_type_code IS the ม.40 sub-section (per the official
                        // ภ.ง.ด.3/53 income box). Render it as the legal section ref, not a
                        // bare number. A non-numeric/blank code degrades to just the desc.
                        (w.IncomeTypeCode is { Length: > 0 } code && char.IsDigit(code[0])
                            ? $"ตามมาตรา 40({code})" : w.IncomeTypeCode)
                        + (string.IsNullOrEmpty(w.IncomeDescription) ? "" : $" — {w.IncomeDescription}")
                        + $"  (อัตรา {w.WhtRate:P2})");
                    t.Cell().Element(C).AlignRight().Text($"{w.IncomeAmount:N2}");
                    t.Cell().Element(C).AlignRight().Text($"{w.WhtAmount:N2}");

                    t.Cell().Element(C).AlignRight().Text("รวม / Total").Bold();
                    t.Cell().Element(C).AlignRight().Text($"{w.IncomeAmount:N2}").Bold();
                    t.Cell().Element(C).AlignRight().Text($"{w.WhtAmount:N2}").Bold();
                });

                col.Item().PaddingTop(8).Text(
                    "ผู้จ่ายเงินออกหนังสือรับรองฉบับนี้เพื่อเป็นหลักฐานการหักภาษี ณ ที่จ่าย")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(20).AlignRight().Text("ลงชื่อ ......................................... ผู้จ่ายเงิน");
            });

            page.Footer().AlignCenter()
                .Text($"ออกโดยระบบ TEAS — อ้างอิงใบสำคัญจ่าย PV#{w.PaymentVoucherId}")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }
}
