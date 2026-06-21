using System.Globalization;
using Accounting.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Pdf;

/// <summary>Company-header record for the financial-statement supporting report (mirrors the
/// header source the ภ.ง.ด.50 PDF uses: legal name + tax id, served per request, tenant-scoped).</summary>
public sealed record FinancialStatementHeader(string CompanyName, string TaxId);

/// <summary>
/// งบแสดงฐานะการเงิน + งบกำไรขาดทุน as a SUPPORTING / management report for attaching to / referencing
/// when filing ภ.ง.ด.50 — derived from posted GL via <see cref="IFinancialReportService"/>, NOT the
/// audited DBD/XBRL statutory งบการเงิน. The sub-title states this explicitly so no one mistakes it for
/// the auditor-signed statement. QuestPDF + the bundled Sarabun font (same EnsureFont idiom as
/// <see cref="PayslipPdf"/>). Pure: input = two report DTOs + a header record → byte[].
/// The balance sheet renders the DTO's own LiabilitiesAndEquityTotal + Balanced (never recomputed) so
/// the printed figures foot exactly like the report (CurrentPeriodEarnings is a distinct equity line —
/// Equity.Total excludes it; see BalanceSheetReport doc).
/// </summary>
public static class FinancialStatementPdf
{
    private const string Font = "Sarabun";
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static string Num(decimal v) => v.ToString("N2", Th);
    private static string DateTh(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static readonly object FontLock = new();
    private static bool _fontReady;

    // Register the embedded Sarabun weights + the Community license exactly once (mirrors PayslipPdf),
    // so this renders Thai regardless of whether Program.cs ran (tests, workers, any host).
    private static void EnsureFont()
    {
        if (_fontReady) return;
        lock (FontLock)
        {
            if (_fontReady) return;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var asm = typeof(FinancialStatementPdf).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
                using (var s = asm.GetManifestResourceStream(name)!)
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
            _fontReady = true;
        }
    }

    public static byte[] Render(FinancialStatementHeader header, BalanceSheetReport bs, ProfitLossReport pl)
    {
        EnsureFont();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(11).FontColor(PaperColors.Ink900).LineHeight(1.3f));

            page.Content().Column(root =>
            {
                // Accent bar.
                root.Item().Height(5).Row(r =>
                {
                    r.RelativeItem(35).Background(PaperColors.Ink900);
                    r.RelativeItem(65).Background(PaperColors.Peach400);
                });

                // Title + the NOT-AUDITED disclaimer (compliance: must not be mistaken for the DBD งบการเงิน).
                root.Item().PaddingTop(12).Text("งบการเงินประกอบการยื่นแบบ (เอกสารประกอบ)").FontSize(17).Bold();
                root.Item().PaddingTop(2).Background(PaperColors.Peach50).Padding(6)
                    .Text("เอกสารประกอบ — มิใช่งบการเงินที่ผ่านการตรวจสอบโดยผู้สอบบัญชี")
                    .FontSize(11).Bold().FontColor(PaperColors.Peach700);
                root.Item().Text("จัดทำจากบัญชีแยกประเภทที่บันทึกแล้ว เพื่อใช้อ้างอิง/แนบประกอบการยื่น ภ.ง.ด.50 เท่านั้น")
                    .FontSize(10).FontColor(PaperColors.Ink500);

                // Company header.
                root.Item().PaddingTop(10).Text(header.CompanyName).FontSize(13).Bold();
                root.Item().Text($"เลขประจำตัวผู้เสียภาษี {header.TaxId}").FontColor(PaperColors.Ink600);
                root.Item().Text($"รอบระยะเวลาบัญชี {DateTh(pl.From)} ถึง {DateTh(pl.To)}").FontColor(PaperColors.Ink600);

                // ── งบแสดงฐานะการเงิน (Balance Sheet) — as-of FY end ──
                root.Item().PaddingTop(16).BorderBottom(2).BorderColor(PaperColors.Ink900).PaddingBottom(3)
                    .Text($"งบแสดงฐานะการเงิน ณ วันที่ {DateTh(bs.AsOfDate)}").FontSize(14).Bold();

                root.Item().PaddingTop(8).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });
                    Section(t, "สินทรัพย์", bs.Assets, "รวมสินทรัพย์");
                    Section(t, "หนี้สิน", bs.Liabilities, "รวมหนี้สิน");
                    // Equity.Total EXCLUDES CurrentPeriodEarnings (DTO contract) — render it as its own line.
                    Section(t, "ส่วนของผู้ถือหุ้น", bs.Equity, "รวมส่วนของผู้ถือหุ้น (ก่อนกำไรงวดปัจจุบัน)");
                    Row(t, "กำไร(ขาดทุน)สะสม/งวดปัจจุบัน", bs.CurrentPeriodEarnings);
                    GrandTotal(t, "รวมหนี้สินและส่วนของผู้ถือหุ้น", bs.LiabilitiesAndEquityTotal);
                });

                root.Item().PaddingTop(6).Text(bs.Balanced
                    ? $"งบดุล: สินทรัพย์ = หนี้สินและส่วนของผู้ถือหุ้น = {Num(bs.LiabilitiesAndEquityTotal)} บาท (สมดุล)"
                    : "งบดุลไม่สมดุล — โปรดตรวจสอบรายการบัญชี")
                    .FontSize(10).Bold().FontColor(bs.Balanced ? PaperColors.Ink600 : "#B5524A");

                // ── งบกำไรขาดทุน (Profit & Loss) — FLAT: Revenue − Expense = NetProfit ──
                root.Item().PaddingTop(18).BorderBottom(2).BorderColor(PaperColors.Ink900).PaddingBottom(3)
                    .Text($"งบกำไรขาดทุน สำหรับรอบบัญชี {DateTh(pl.From)} ถึง {DateTh(pl.To)}").FontSize(14).Bold();

                root.Item().PaddingTop(8).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });
                    Row(t, "รายได้", pl.Totals.Revenue);
                    Row(t, "หัก  ค่าใช้จ่าย", pl.Totals.Expense);
                    GrandTotal(t, "กำไร(ขาดทุน)สุทธิ", pl.Totals.NetProfit);
                });

                root.Item().PaddingTop(20).Text(
                    "หมายเหตุ: ตัวเลขข้างต้นจัดทำจากรายการบัญชีที่บันทึกในระบบ ใช้สำหรับฝ่ายบริหารและประกอบการยื่นภาษีเงินได้นิติบุคคล "
                  + "มิใช่งบการเงินตามกฎหมายที่ได้รับการรับรองโดยผู้สอบบัญชีรับอนุญาตและยื่นต่อกรมพัฒนาธุรกิจการค้า")
                    .FontSize(9).Italic().FontColor(PaperColors.Ink500);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("เอกสารประกอบ (มิใช่งบการเงินที่ตรวจสอบแล้ว) — หน้า ").FontSize(8).FontColor(PaperColors.Ink400);
                t.CurrentPageNumber().FontSize(8).FontColor(PaperColors.Ink400);
                t.Span(" / ").FontSize(8).FontColor(PaperColors.Ink400);
                t.TotalPages().FontSize(8).FontColor(PaperColors.Ink400);
            });
        })).GeneratePdf();
    }

    // Section header + its account rows + the section รวม.
    private static void Section(TableDescriptor t, string title, BalanceSheetSection section, string totalLabel)
    {
        t.Cell().ColumnSpan(2).PaddingTop(6).Text(title).Bold().FontColor(PaperColors.Ink700);
        foreach (var r in section.Rows)
            Row(t, $"    {r.AccountCode}  {r.AccountNameTh}", r.Balance);
        Subtotal(t, totalLabel, section.Total);
    }

    private static void Row(TableDescriptor t, string label, decimal amount)
    {
        t.Cell().PaddingVertical(2).Text(label);
        t.Cell().PaddingVertical(2).AlignRight().Text(Num(amount));
    }

    private static void Subtotal(TableDescriptor t, string label, decimal amount)
    {
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).Text(label).SemiBold();
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink200).PaddingVertical(3).AlignRight().Text(Num(amount)).SemiBold();
    }

    private static void GrandTotal(TableDescriptor t, string label, decimal amount)
    {
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).Text(label).Bold();
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).AlignRight().Text(Num(amount)).Bold();
    }
}
