using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Accounting.Infrastructure.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace Accounting.Api.Tests.Hardening;

// Mechanism guard for the official 50ทวิ filler. The filler does NOT fill the AcroForm
// (PdfSharp can't shape Thai — it drops tone marks); instead RdAcroFormFiller renders a
// QuestPDF/Skia overlay at each field's /Rect, composites it via XPdfForm, and FLATTENS.
// These tests assert the flattened shape; the Thai render itself is verified visually
// (pypdfium2) against Pdf/Templates/wht_50tawi_fieldmap.md — output files are dumped to TEMP.
public sealed class _PdfSharpProbe
{
    private readonly ITestOutputHelper _o;
    public _PdfSharpProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Filler_populates_50tawi_fields_with_thai()
    {
        var data = new Wht50TawiData(
            DocNo: "05-2026-WT-0012",
            FormType: "Pnd3",
            PayerName: "บริษัท ทดสอบหักภาษี จำกัด",
            PayerTaxId: "0105556123453",
            PayerAddress: "1 ถนนทดสอบ กรุงเทพฯ 10110",
            PayeeName: "นายผู้รับเงิน ทดสอบ",
            PayeeTaxId: "1100200300400",
            PayeeAddress: "99 ซอยทดสอบ นนทบุรี 11000",
            IncomeTypeMa40: "8",                       // ม.40(8) → ช่อง 5 ม.3 เตรส
            IncomeDescription: "ค่าจ้างแรงงาน",
            PayDate: new DateOnly(2026, 1, 6),
            IncomeAmount: 50000m,
            WhtAmount: 1500m);

        var bytes = Wht50TawiFormFiller.Fill(data);
        Assert.True(bytes.Length > 5000, "expected a real PDF");

        // Dump for visual render self-check (pypdfium2) outside the test.
        var outPath = Path.Combine(Path.GetTempPath(), "50tawi-filled.pdf");
        File.WriteAllBytes(outPath, bytes);
        _o.WriteLine("OUT " + outPath + " size=" + bytes.Length);

        // The data is baked into the page via the overlay then flattened — so the output
        // must have exactly one page and NO interactive AcroForm / widget annotations left.
        using var ms = new MemoryStream(bytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        Assert.Equal(1, doc.PageCount);
        Assert.False(doc.Internals.Catalog.Elements.ContainsKey("/AcroForm"), "AcroForm must be flattened away");
        Assert.False(doc.Pages[0].Elements.ContainsKey("/Annots"), "widget annotations must be flattened away");
        doc.Close();
    }

    // Calibration: fill EVERY AcroForm field with its own name (checkboxes with ✕) so the
    // field→box mapping can be verified visually. Throwaway diagnostic — dumps to TEMP.
    [Fact]
    public void Calibration_fill_all_fields()
    {
        var asm = typeof(Wht50TawiFormFiller).Assembly;
        var resName = System.Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("wht_50tawi.pdf", System.StringComparison.Ordinal))!;
        byte[] tpl;
        using (var rs = asm.GetManifestResourceStream(resName)!)
        using (var ms0 = new MemoryStream()) { rs.CopyTo(ms0); tpl = ms0.ToArray(); }

        using var probe = new MemoryStream(tpl);
        var doc = PdfReader.Open(probe, PdfDocumentOpenMode.Modify);
        var fieldsArr = doc.AcroForm!.Elements.GetArray("/Fields")!;
        var rd = new System.Collections.Generic.List<RdField>();
        void Walk(PdfItem? it, string prefix)
        {
            var dct = (it as PdfReference)?.Value as PdfDictionary ?? it as PdfDictionary;
            if (dct is null) return;
            var t = dct.Elements.GetString("/T");
            var name = string.IsNullOrEmpty(t) ? prefix : prefix.Length == 0 ? t : $"{prefix}.{t}";
            var kids = dct.Elements.GetArray("/Kids");
            if (kids is { Elements.Count: > 0 }) { foreach (var k in kids) Walk(k, name); return; }
            var ft = dct.Elements.GetName("/FT");
            if (ft == "/Btn") rd.Add(new RdField(name, "X", Check: true));
            else rd.Add(new RdField(name, name));
        }
        foreach (var f in fieldsArr) Walk(f, "");
        doc.Close();

        var bytes = RdAcroFormFiller.Render(tpl, rd, copies: 1);
        var outPath = Path.Combine(Path.GetTempPath(), "50tawi-calib.pdf");
        File.WriteAllBytes(outPath, bytes);
        _o.WriteLine($"OUT {outPath} fields={rd.Count}");
    }

    // RD requires 2 copies — FillCopies must emit a 2-page PDF, both flattened.
    [Fact]
    public void FillCopies_emits_two_pages()
    {
        var data = new Wht50TawiData(
            DocNo: "05-2026-WT-0012", FormType: "Pnd53",
            PayerName: "บริษัท ทดสอบหักภาษี จำกัด", PayerTaxId: "0105556123453",
            PayerAddress: "1 ถนนทดสอบ กรุงเทพฯ 10110",
            PayeeName: "นายผู้รับเงิน ทดสอบ", PayeeTaxId: "1100200300400",
            PayeeAddress: "99 ซอยทดสอบ นนทบุรี 11000",
            IncomeTypeMa40: "8", IncomeDescription: "ค่าจ้างแรงงาน",
            PayDate: new DateOnly(2026, 5, 29), IncomeAmount: 50000m, WhtAmount: 1500m);

        var bytes = Wht50TawiFormFiller.FillCopies(data);
        var outPath = Path.Combine(Path.GetTempPath(), "50tawi-2copies.pdf");
        File.WriteAllBytes(outPath, bytes);
        _o.WriteLine("OUT " + outPath + " size=" + bytes.Length);

        using var ms = new MemoryStream(bytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        Assert.Equal(2, doc.PageCount);                 // ฉบับ1 + ฉบับ2
        Assert.False(doc.Internals.Catalog.Elements.ContainsKey("/AcroForm"), "flattened");
        doc.Close();
    }
}
