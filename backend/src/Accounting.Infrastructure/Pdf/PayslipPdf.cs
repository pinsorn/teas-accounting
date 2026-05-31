using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Pdf;

/// <summary>Payroll P-D — per-employee monthly payment-evidence / payslip (Ham hard requirement):
/// the salary-transfer proof showing gross → PIT (ภ.ง.ด.1) / SSO (ม.33) → net, bank destination +
/// pay date. QuestPDF + the bundled Sarabun font (Program.cs), same ink/peach palette as
/// <see cref="PaperDocumentPdf"/>. Buddhist-era dates only at this print boundary (CE internally).</summary>
public sealed record PayslipPdfModel(
    string EmployerName, string EmployerTaxId, string? EmployerAddress,
    string PeriodThai, string PayDateThai, string? DocNo,
    string EmployeeCode, string EmployeeName, string NationalId, string? EmployeeAddress,
    decimal GrossTaxable, decimal GrossNonTaxable, decimal Pit, decimal SsoEmployee,
    decimal SsoEmployer, decimal OtherDeductions, decimal NetPay,
    decimal YtdIncome, decimal YtdPit,
    string? BankName, string? BankAccountNo, string? BankAccountName, string NetInWords);

public static class PayslipPdf
{
    private const string Font = "Sarabun";
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static string Num(decimal v) => v.ToString("N2", Th);

    private static readonly object FontLock = new();
    private static bool _fontReady;

    // Register the embedded Sarabun weights + the Community license exactly once, so the payslip
    // renders Thai regardless of whether Program.cs ran (tests, workers, any host) — mirrors RdAcroFormFiller.
    private static void EnsureFont()
    {
        if (_fontReady) return;
        lock (FontLock)
        {
            if (_fontReady) return;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var asm = typeof(PayslipPdf).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
                using (var s = asm.GetManifestResourceStream(name)!)
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
            _fontReady = true;
        }
    }

    public static byte[] Render(PayslipPdfModel m)
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

                // Title + employer.
                root.Item().PaddingTop(12).Text("สลิปเงินเดือน / หนังสือรับรองการจ่ายเงินได้")
                    .FontSize(17).Bold();
                root.Item().PaddingTop(2).Text(m.EmployerName).FontSize(13).Bold();
                root.Item().Text($"เลขประจำตัวผู้เสียภาษี {m.EmployerTaxId}").FontColor(PaperColors.Ink600);
                if (!string.IsNullOrWhiteSpace(m.EmployerAddress))
                    root.Item().Text(m.EmployerAddress).FontColor(PaperColors.Ink600);

                // Meta strip — period / pay date / doc no.
                root.Item().PaddingTop(10).BorderTop(1).BorderBottom(1).BorderColor(PaperColors.Ink200)
                    .PaddingVertical(6).Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span("งวดเดือน  ").FontColor(PaperColors.Ink500); t.Span(m.PeriodThai).Bold(); });
                        r.RelativeItem().Text(t => { t.Span("วันที่จ่าย  ").FontColor(PaperColors.Ink500); t.Span(m.PayDateThai).Bold(); });
                        r.RelativeItem().AlignRight().Text(t => { t.Span("เลขที่  ").FontColor(PaperColors.Ink500); t.Span(m.DocNo ?? "—").Bold(); });
                    });

                // Employee block.
                root.Item().PaddingTop(10).Text("พนักงาน").FontColor(PaperColors.Ink500);
                root.Item().Text($"{m.EmployeeName}  ({m.EmployeeCode})").Bold();
                root.Item().Text($"เลขประจำตัวประชาชน {m.NationalId}").FontColor(PaperColors.Ink600);
                if (!string.IsNullOrWhiteSpace(m.EmployeeAddress))
                    root.Item().Text(m.EmployeeAddress!).FontColor(PaperColors.Ink600);

                // Earnings / deductions table.
                root.Item().PaddingTop(14).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });
                    Row(t, "เงินเดือน / ค่าจ้าง (ม.40(1))", m.GrossTaxable);
                    if (m.GrossNonTaxable != 0m) Row(t, "เงินได้ที่ได้รับยกเว้น", m.GrossNonTaxable);
                    Total(t, "รวมเงินได้", m.GrossTaxable + m.GrossNonTaxable);
                    Row(t, "หัก  ภาษีเงินได้ หัก ณ ที่จ่าย (ภ.ง.ด.1)", -m.Pit);
                    Row(t, "หัก  เงินสมทบประกันสังคม (ลูกจ้าง)", -m.SsoEmployee);
                    if (m.OtherDeductions != 0m) Row(t, "หัก  รายการหักอื่น ๆ", -m.OtherDeductions);
                    Total(t, "เงินได้สุทธิ (รับจริง)", m.NetPay);
                });

                root.Item().PaddingTop(4).AlignRight().Text($"({m.NetInWords})").Italic().FontColor(PaperColors.Ink600);

                // Bank + employer SSO note + YTD.
                root.Item().PaddingTop(12).Background(PaperColors.Ink50).Padding(8).Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(m.BankAccountNo))
                        c.Item().Text($"โอนเข้าบัญชี  {m.BankName} เลขที่ {m.BankAccountNo} ({m.BankAccountName})");
                    c.Item().Text($"เงินสมทบประกันสังคมส่วนนายจ้าง  {Num(m.SsoEmployer)} บาท (บริษัทออกให้ ไม่หักจากพนักงาน)")
                        .FontColor(PaperColors.Ink500);
                    c.Item().Text($"สะสมตั้งแต่ต้นปี  เงินได้ {Num(m.YtdIncome)} · ภาษีหัก ณ ที่จ่าย {Num(m.YtdPit)} บาท")
                        .FontColor(PaperColors.Ink500);
                });

                // Signatures.
                root.Item().PaddingTop(40).Row(r =>
                {
                    r.RelativeItem().AlignCenter().Column(c =>
                    {
                        c.Item().Text("...............................................");
                        c.Item().AlignCenter().Text("ผู้จ่ายเงิน").FontColor(PaperColors.Ink600);
                    });
                    r.RelativeItem().AlignCenter().Column(c =>
                    {
                        c.Item().Text("...............................................");
                        c.Item().AlignCenter().Text("ผู้รับเงิน").FontColor(PaperColors.Ink600);
                    });
                });
            });
        })).GeneratePdf();
    }

    private static void Row(TableDescriptor t, string label, decimal amount)
    {
        t.Cell().PaddingVertical(3).Text(label);
        t.Cell().PaddingVertical(3).AlignRight().Text(Num(amount));
    }

    private static void Total(TableDescriptor t, string label, decimal amount)
    {
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).Text(label).Bold();
        t.Cell().BorderTop(1).BorderColor(PaperColors.Ink900).PaddingVertical(4).AlignRight().Text(Num(amount)).Bold();
    }
}
