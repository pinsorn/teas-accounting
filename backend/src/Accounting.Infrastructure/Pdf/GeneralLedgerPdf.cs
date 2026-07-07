using System.Globalization;
using Accounting.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// General Ledger (บัญชีแยกประเภท) export PDF — per-account ledger: opening row,
/// movement rows with running balance, closing row, page numbers. QuestPDF + the bundled
/// Sarabun font (same EnsureFont idiom as <see cref="FinancialStatementPdf"/>/PayslipPdf).
/// Pure: report DTO + company name → byte[]. No DB, no I/O.
/// </summary>
public static class GeneralLedgerPdf
{
    private const string Font = "Sarabun";
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static string Num(decimal v) => v.ToString("N2", Th);
    private static string DateTh(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static readonly object FontLock = new();
    private static bool _fontReady;

    private static void EnsureFont()
    {
        if (_fontReady) return;
        lock (FontLock)
        {
            if (_fontReady) return;
            QuestPDF.Settings.License = LicenseType.Community;
            var asm = typeof(GeneralLedgerPdf).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
                using (var s = asm.GetManifestResourceStream(name)!)
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
            _fontReady = true;
        }
    }

    public static byte[] Render(string companyName, GeneralLedgerReport report)
    {
        EnsureFont();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(10).FontColor(PaperColors.Ink900).LineHeight(1.3f));

            page.Content().Column(root =>
            {
                root.Item().Height(5).Row(r =>
                {
                    r.RelativeItem(35).Background(PaperColors.Ink900);
                    r.RelativeItem(65).Background(PaperColors.Peach400);
                });

                root.Item().PaddingTop(12).Text(companyName).FontSize(13).Bold();
                root.Item().Text("บัญชีแยกประเภท (General Ledger)").FontSize(16).Bold();
                root.Item().Text($"{report.AccountCode}  {report.AccountNameTh}").FontSize(12).Bold();
                root.Item().Text($"ตั้งแต่วันที่ {DateTh(report.FromDate)} ถึงวันที่ {DateTh(report.ToDate)}")
                    .FontColor(PaperColors.Ink600);

                root.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);   // วันที่
                        c.RelativeColumn(2);   // เลขที่เอกสาร
                        c.RelativeColumn(4);   // คำอธิบาย
                        c.RelativeColumn(2);   // อ้างอิง
                        c.RelativeColumn(2);   // เดบิต
                        c.RelativeColumn(2);   // เครดิต
                        c.RelativeColumn(2);   // คงเหลือ
                    });

                    t.Header(h =>
                    {
                        HCell(h.Cell(), "วันที่");
                        HCell(h.Cell(), "เลขที่เอกสาร");
                        HCell(h.Cell(), "คำอธิบาย");
                        HCell(h.Cell(), "อ้างอิง");
                        HCell(h.Cell(), "เดบิต", right: true);
                        HCell(h.Cell(), "เครดิต", right: true);
                        HCell(h.Cell(), "คงเหลือ", right: true);
                    });

                    // Opening row.
                    t.Cell().ColumnSpan(4).PaddingVertical(3).Text("ยอดยกมา").Italic();
                    t.Cell().PaddingVertical(3).Text("");
                    t.Cell().PaddingVertical(3).Text("");
                    t.Cell().PaddingVertical(3).AlignRight().Text(Num(report.OpeningBalance)).Italic();

                    foreach (var r in report.Rows)
                    {
                        t.Cell().PaddingVertical(2).Text(DateTh(r.DocDate));
                        t.Cell().PaddingVertical(2).Text(r.DocNo);
                        t.Cell().PaddingVertical(2).Text(r.Description ?? "");
                        t.Cell().PaddingVertical(2).Text(r.Reference ?? "");
                        t.Cell().PaddingVertical(2).AlignRight().Text(r.Debit == 0m ? "" : Num(r.Debit));
                        t.Cell().PaddingVertical(2).AlignRight().Text(r.Credit == 0m ? "" : Num(r.Credit));
                        t.Cell().PaddingVertical(2).AlignRight().Text(Num(r.RunningBalance));
                    }

                    // Totals row.
                    t.Cell().ColumnSpan(4).BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).Text("รวม").Bold();
                    t.Cell().BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).AlignRight().Text(Num(report.TotalDebit)).Bold();
                    t.Cell().BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).AlignRight().Text(Num(report.TotalCredit)).Bold();
                    t.Cell().BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).Text("");

                    // Closing row.
                    t.Cell().ColumnSpan(6).BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).Text("ยอดยกไป").Bold();
                    t.Cell().BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).AlignRight().Text(Num(report.ClosingBalance)).Bold();
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("หน้า ").FontSize(8).FontColor(PaperColors.Ink400);
                t.CurrentPageNumber().FontSize(8).FontColor(PaperColors.Ink400);
                t.Span(" / ").FontSize(8).FontColor(PaperColors.Ink400);
                t.TotalPages().FontSize(8).FontColor(PaperColors.Ink400);
            });
        })).GeneratePdf();
    }

    private static void HCell(IContainer cell, string text, bool right = false)
    {
        var c = cell.BorderBottom(1).BorderColor(PaperColors.Ink900).PaddingBottom(3);
        (right ? c.AlignRight() : c).Text(text).Bold().FontSize(9);
    }
}
