using System.Globalization;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Accounting.Infrastructure.Pdf;

/// <summary>One employee row on the annual ใบแนบ ภ.ง.ด.1ก (whole-year totals + address). Name is
/// split: <paramref name="FirstName"/> (with title) → ชื่อ box, <paramref name="LastName"/> → ชื่อสกุล box.</summary>
public sealed record Pnd1aLine(string TaxId, string FirstName, string LastName, string? Address, decimal Income, decimal Tax);

/// <summary>Data for the annual ภ.ง.ด.1ก (return + ใบแนบ). Salary = ม.40(1) กรณีทั่วไป.</summary>
public sealed record Pnd1aModel(
    string EmployerTaxId, string BranchCode, string EmployerName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Street,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    int YearBE, IReadOnlyList<Pnd1aLine> Lines);

/// <summary>
/// Fills the official RD ภ.ง.ด.1ก (annual WHT summary, ม.58(1)) + ใบแนบ via <see cref="RdAcroFormFiller"/>.
/// Field map self-decoded from /Rect (`_Pnd1aDump`): the ใบแนบ is LANDSCAPE and adds a ที่อยู่ column;
/// the main differs from ภ.ง.ด.1 (year = Text1.17, ยื่นปกติ = Radio Button0 idx 0).
/// </summary>
public static class Pnd1aFormFiller
{
    private const int RowsPerSheet = 7;
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static string Money(decimal v) => v.ToString("#,##0.00", Th);

    public static byte[] FillAnnual(Pnd1aModel m)
    {
        var sheets = Math.Max(1, (m.Lines.Count + RowsPerSheet - 1) / RowsPerSheet);
        var totalIncome = m.Lines.Sum(l => l.Income);
        var totalTax = m.Lines.Sum(l => l.Tax);

        var mainRadios = new List<RdRadio>
        {
            new("Radio Button0", 0),   // (1) ยื่นปกติ (idx0 = left box here)
            new("Radio Button2", 0),   // ☑ ใบแนบ ภ.ง.ด.1ก
        };
        var main = RdAcroFormFiller.Render(Template("pnd1a_main.pdf"),
            MainFields(m, sheets, m.Lines.Count, totalIncome, totalTax), mainRadios);

        var attachRadios = new List<RdRadio> { new("Radio Button0", 1) };   // ประเภทเงินได้ (1) กรณีทั่วไป
        var pages = new List<byte[]> { main };
        for (var s = 0; s < sheets; s++)
            pages.Add(RdAcroFormFiller.Render(Template("pnd1a_attach.pdf"), AttachFields(m, s, sheets), attachRadios));

        return Merge(pages);
    }

    private static List<RdField> MainFields(Pnd1aModel m, int sheets, int count, decimal income, decimal tax) => new()
    {
        new("Text1.0", FormatTaxId(m.EmployerTaxId)),
        new("Text1.1", m.BranchCode),
        new("Text1.17", m.YearBE.ToString()),   // ประจำปีภาษี (พ.ศ.)
        new("Text1.2", m.EmployerName),
        new("Text1.3", m.Building ?? ""), new("Text1.4", m.RoomNo ?? ""), new("Text1.5", m.Floor ?? ""),
        new("Text1.6", m.Village ?? ""), new("Text1.7", m.HouseNo ?? ""), new("Text1.8", m.Moo ?? ""),
        new("Text1.9", m.Soi ?? ""), new("Text1.11", m.Street ?? ""),
        new("Text1.12", m.SubDistrict ?? ""), new("Text1.13", m.District ?? ""),
        new("Text1.14", m.Province ?? ""), new("Text1.15", m.PostalCode ?? ""),
        new("Text1.19", sheets.ToString()),
        // Summary row 1 (ม.40(1) กรณีทั่วไป) + row 6 รวม.
        new("Text2.1", count.ToString(), Right: true), new("Text2.2", Money(income), Right: true), new("Text2.3", Money(tax), Right: true),
        new("Text2.18", count.ToString(), Right: true), new("Text2.19", Money(income), Right: true), new("Text2.20", Money(tax), Right: true),
    };

    private static List<RdField> AttachFields(Pnd1aModel m, int s, int sheets)
    {
        var f = new List<RdField>
        {
            new("Text1.0", FormatTaxId(m.EmployerTaxId)),
            new("Text1.1", m.BranchCode),
            new("Text1.2", (s + 1).ToString()),   // แผ่นที่
            new("Text1.3", sheets.ToString()),     // ในจำนวน
        };
        var slice = m.Lines.Skip(s * RowsPerSheet).Take(RowsPerSheet).ToList();
        for (var i = 0; i < slice.Count; i++)
        {
            var l = slice[i];
            var seq = (s * RowsPerSheet + i + 1).ToString();
            // Columns: ชื่อ | ชื่อสกุล | เงินได้ | ภาษี | เงื่อนไข ; ที่อยู่ on the line below (.8).
            if (i == 0)   // row 1 uses the special Text1.* block
            {
                f.Add(new("Text1.4", seq)); f.Add(new("Text1.5", FormatTaxId(l.TaxId)));
                f.Add(new("Text1.6", l.FirstName)); f.Add(new("Text1.7", l.LastName));
                f.Add(new("Text1.8", Money(l.Income), Right: true)); f.Add(new("Text1.9", Money(l.Tax), Right: true));
                f.Add(new("Text1.10", "1")); f.Add(new("Text1.11", l.Address ?? ""));
            }
            else
            {
                var r = i + 1;   // blocks 2..7
                f.Add(new($"Text{r}.1", seq)); f.Add(new($"Text{r}.2", FormatTaxId(l.TaxId)));
                f.Add(new($"Text{r}.3", l.FirstName)); f.Add(new($"Text{r}.4", l.LastName));
                f.Add(new($"Text{r}.5", Money(l.Income), Right: true)); f.Add(new($"Text{r}.6", Money(l.Tax), Right: true));
                f.Add(new($"Text{r}.7", "1")); f.Add(new($"Text{r}.8", l.Address ?? ""));
            }
        }
        f.Add(new("Text8.6", Money(slice.Sum(l => l.Income)), Right: true));
        f.Add(new("Text8.7", Money(slice.Sum(l => l.Tax)), Right: true));
        return f;
    }

    private static string FormatTaxId(string raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return d.Length != 13 ? d : $"{d[0]}-{d.Substring(1, 4)}-{d.Substring(5, 5)}-{d.Substring(10, 2)}-{d[12]}";
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd1aFormFiller).Assembly;
        using var s = asm.GetManifestResourceStream($"Accounting.Infrastructure.Pdf.Templates.{file}")
            ?? throw new InvalidOperationException($"Embedded template '{file}' not found.");
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
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
        using var o = new MemoryStream(); outDoc.Save(o); return o.ToArray();
    }
}
