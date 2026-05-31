using System.Globalization;
using System.IO;
using System.Reflection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Accounting.Infrastructure.Pdf;

/// <summary>One employee line on the ใบแนบ ภ.ง.ด.1.</summary>
public sealed record Pnd1Line(string TaxId, string FirstName, string LastName, string PayDate, decimal Income, decimal Tax);

/// <summary>Data for a monthly ภ.ง.ด.1 (return + ใบแนบ). Salary = ม.40(1) กรณีทั่วไป (summary row 1).</summary>
public sealed record Pnd1MonthlyModel(
    string EmployerTaxId, string BranchCode, string EmployerName, string? Address,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    int PeriodMonth, int PeriodYearBE,
    IReadOnlyList<Pnd1Line> Lines);

/// <summary>
/// Fills the official RD ภ.ง.ด.1 AcroForm (return + ใบแนบ) from payroll data and flattens it, via the
/// generic <see cref="RdAcroFormFiller"/> (overlay+flatten; handles the comb tax-id + Thai shaping).
/// Field map: <c>Pdf/Templates/pnd1_fieldmap.md</c> (self-decoded; Ham visual-validation pending).
/// ⚠️ v1 limitations (deferred): the month checkbox + ยื่นปกติ radio (same-name radio groups —
/// not addressable by `RdAcroFormFiller`) are left blank; the period year is printed instead.
/// </summary>
public static class Pnd1FormFiller
{
    private const int RowsPerSheet = 8;
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static string Money(decimal v) => v.ToString("#,##0.00", Th);

    public static byte[] FillMonthly(Pnd1MonthlyModel m)
    {
        var sheets = (m.Lines.Count + RowsPerSheet - 1) / RowsPerSheet;
        if (sheets == 0) sheets = 1;

        var count = m.Lines.Count;
        var totalIncome = m.Lines.Sum(l => l.Income);
        var totalTax = m.Lines.Sum(l => l.Tax);

        var main = RdAcroFormFiller.Render(Template("pnd1_main.pdf"), MainFields(m, sheets, count, totalIncome, totalTax));

        var pages = new List<byte[]> { main };
        for (var s = 0; s < sheets; s++)
            pages.Add(RdAcroFormFiller.Render(
                Template("pnd1_attach.pdf"),
                AttachFields(m, s, sheets, totalIncome, totalTax)));

        return Merge(pages);
    }

    // ── main return ──────────────────────────────────────────────────────────
    private static List<RdField> MainFields(Pnd1MonthlyModel m, int sheets, int count, decimal income, decimal tax)
    {
        var f = new List<RdField>
        {
            new("Text1.0", FormatTaxId(m.EmployerTaxId)),
            new("Text1.1", m.BranchCode),
            new("Text1.18", m.PeriodYearBE.ToString()),
            new("Text1.2", m.EmployerName),
            new("Text1.7", m.Address ?? ""),
            new("Text1.11", m.SubDistrict ?? ""),
            new("Text1.12", m.District ?? ""),
            new("Text1.13", m.Province ?? ""),
            new("Text1.15", m.PostalCode ?? ""),
            new("Text1.21", sheets.ToString()),
            // Summary row 1 — ม.40(1) กรณีทั่วไป (salary).
            new("Text2.1", count.ToString(), Right: true),
            new("Text2.2", Money(income), Right: true),
            new("Text2.3", Money(tax), Right: true),
            // Row 6 รวม + row 8 รวมทั้งสิ้น (single income type → same totals).
            new("Text2.18", count.ToString(), Right: true),
            new("Text2.19", Money(income), Right: true),
            new("Text2.20", Money(tax), Right: true),
            new("Text2.22", Money(tax), Right: true),
        };
        return f;
    }

    // ── ใบแนบ sheet `s` (0-based), 8 rows/sheet ─────────────────────────────────
    private static List<RdField> AttachFields(Pnd1MonthlyModel m, int s, int sheets, decimal totalIncome, decimal totalTax)
    {
        var f = new List<RdField>
        {
            new("Text1.0", FormatTaxId(m.EmployerTaxId)),
            new("Text1.1", m.BranchCode),
            new("Text1.2", (s + 1).ToString()),     // แผ่นที่
            new("Text1.3", sheets.ToString()),       // ในจำนวน
        };

        var slice = m.Lines.Skip(s * RowsPerSheet).Take(RowsPerSheet).ToList();
        for (var i = 0; i < slice.Count; i++)
        {
            var line = slice[i];
            var seq = s * RowsPerSheet + i + 1;
            // Row 1 of every sheet uses the special Text1.* block; rows 2..8 use Text{2..8}.*.
            if (i == 0)
            {
                f.Add(new("Text1.4", seq.ToString()));
                f.Add(new("Text1.5", FormatTaxId(line.TaxId)));
                f.Add(new("Text1.6", line.FirstName));
                f.Add(new("Text1.7", line.LastName));
                f.Add(new("Text1.8", line.PayDate));
                f.Add(new("Text1.9", Money(line.Income), Right: true));
                f.Add(new("Text1.10", Money(line.Tax), Right: true));
                f.Add(new("Text1.11", "1"));   // เงื่อนไข = หัก ณ ที่จ่าย
            }
            else
            {
                var r = i + 1;   // block 2..8
                f.Add(new($"Text{r}.1", seq.ToString()));
                f.Add(new($"Text{r}.2", FormatTaxId(line.TaxId)));
                f.Add(new($"Text{r}.3", line.FirstName));
                f.Add(new($"Text{r}.4", line.LastName));
                f.Add(new($"Text{r}.5", line.PayDate));
                f.Add(new($"Text{r}.6", Money(line.Income), Right: true));
                f.Add(new($"Text{r}.7", Money(line.Tax), Right: true));
                f.Add(new($"Text{r}.8", "1"));
            }
        }

        // Per-sheet subtotal in the total row (Σ of this sheet; the last sheet carries the grand total
        // visually — RD accepts the running total per ใบแนบ, see the form note).
        var sheetIncome = slice.Sum(l => l.Income);
        var sheetTax = slice.Sum(l => l.Tax);
        f.Add(new("Text8.9", Money(sheetIncome), Right: true));
        f.Add(new("Text8.10", Money(sheetTax), Right: true));
        return f;
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>13 digits → the RD comb pattern X-XXXX-XXXXX-XX-X (17 chars, one per comb cell).</summary>
    private static string FormatTaxId(string raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (d.Length != 13) return d;
        return $"{d[0]}-{d.Substring(1, 4)}-{d.Substring(5, 5)}-{d.Substring(10, 2)}-{d[12]}";
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd1FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] Merge(IReadOnlyList<byte[]> pdfs)
    {
        using var outDoc = new PdfDocument();
        foreach (var pdf in pdfs)
        {
            using var ms = new MemoryStream(pdf);
            var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            for (var i = 0; i < src.PageCount; i++) outDoc.AddPage(src.Pages[i]);
        }
        using var o = new MemoryStream();
        outDoc.Save(o);
        return o.ToArray();
    }
}
