using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.TaxFilings;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Diagnostic (NOT a gate): fills EVERY box of each tax form with synthetic, fully-populated data and
/// writes the PDFs to docs/RD-Forms/_fills/_diag_*.pdf so alignment of every field can be eyeballed.
/// Gated behind TEAS_DIAG=1 so it never runs in CI. Run:
///   $env:TEAS_DIAG='1'; dotnet test --filter FullyQualifiedName~TaxFormFillDiagnostic
/// </summary>
public sealed class TaxFormFillDiagnostic
{
    private const string Out = @"Y:\ClaudePlayground\TEAS-Project\docs\RD-Forms\_fills";
    private static readonly string TaxId = "0105561012345";   // 1-4-5-2-1

    private static void Save(string name, byte[] pdf) =>
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Out, name), pdf);

    [SkippableFact]
    public void Fill_every_box_pnd30()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var m = new Pnd30Model(
            TaxId, "00000", "บริษัท ตัวอย่าง ครบทุกช่อง จำกัด",
            "อาคารเอ", "ห้อง 12", "ชั้น 3", "หมู่บ้านสุข", "99/1", "5", "ลาดพร้าว 1", "พหลโยธิน",
            "จอมพล", "จตุจักร", "กรุงเทพมหานคร", "10900", "02-123-4567",
            PeriodMonth: 6, PeriodYearCe: 2026,
            TotalSales: 1234567.89m, SalesZeroRated: 11111.11m, SalesExempt: 22222.22m,
            SalesTaxable: 1201234.56m, OutputVat: 84086.42m, PurchaseClaimable: 500000m,
            InputVat: 35000m, CreditCarryForward: 1000m);
        Save("_diag_pnd30.pdf", Pnd30FormFiller.Fill(m));
    }

    [SkippableFact]
    public void Fill_every_box_pnd53() => FillWht("_diag_pnd53.pdf", WhtFilingService.Pnd53Layout);

    [SkippableFact]
    public void Fill_every_box_pnd3() => FillWht("_diag_pnd3.pdf", WhtFilingService.Pnd3Layout);

    private static void FillWht(string name, WhtFormLayout layout)
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var descs = new[] { "ค่าเช่าอาคาร", "ค่าบริการ", "ค่าวิชาชีพอิสระ", "ค่าโฆษณา", "ค่าขนส่ง",
                            "ค่านายหน้า", "ค่าจ้างทำของ" };
        var rates = new[] { 0.05m, 0.03m, 0.03m, 0.02m, 0.01m, 0.03m, 0.03m };
        var rows = Enumerable.Range(0, 7).Select(i =>
        {
            var income = 10000m * (i + 1) + 123.45m;
            return new WhtFormRow(
                Seq: i + 1, PayeeTaxId: TaxId, PayeeName: $"บริษัท ผู้รับเงิน รายที่ {i + 1} จำกัด",
                IncomeTypeText: descs[i], Rate: rates[i], Income: income,
                Wht: Math.Round(income * rates[i], 2),
                Condition: (i % 2 == 0) ? "1" : "2",
                PayDate: $"{(i % 28) + 1:00}/06/2569");
        }).ToList();
        var m = new WhtFormModel(
            TaxId, "00000", "บริษัท ตัวอย่าง ครบทุกช่อง จำกัด",
            "อาคารเอ", "ห้อง 12", "ชั้น 3", "หมู่บ้านสุข", "99/1", "5", "ลาดพร้าว 1", "ตรอกหนึ่ง", "พหลโยธิน",
            "จอมพล", "จตุจักร", "กรุงเทพมหานคร", "10900",
            PeriodMonth: 6, PeriodYearCe: 2026,
            TotalIncome: rows.Sum(r => r.Income), TotalWht: rows.Sum(r => r.Wht), Rows: rows);
        Save(name, WhtFormFiller.Fill(m, layout));
    }

    [SkippableFact]
    public void Fill_every_box_pnd54()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var m = new Pnd54Model(
            TaxId, "00000", "บริษัท ตัวอย่าง ครบทุกช่อง จำกัด",
            "อาคารเอ", "ห้อง 12", "ชั้น 3", "หมู่บ้านสุข", "99/1", "5", "ลาดพร้าว 1", "ตรอกหนึ่ง",
            "พหลโยธิน", "จอมพล", "จตุจักร", "กรุงเทพมหานคร", "10900",
            PayeeName: "Acme Overseas Ltd.");
        Save("_diag_pnd54.pdf", Pnd54FormFiller.Fill(m));
    }
}
