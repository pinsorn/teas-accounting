using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Accounting.Infrastructure.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace Accounting.Api.Tests.Hardening;

// Tests the production 50ทวิ AcroForm filler end-to-end: fill a sample cert via
// Wht50TawiFormFiller, then reopen and read back the raw /V values to prove the
// fields are populated (Thai round-trips). The filler uses the raw /Fields-dict
// path because PdfSharp 6.2's typed PdfTextField ctor throws on this RD form.
// Field map + verification method: Pdf/Templates/wht_50tawi_fieldmap.md.
public sealed class _PdfSharpProbe
{
    private readonly ITestOutputHelper _o;
    public _PdfSharpProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Filler_populates_50tawi_fields_with_thai()
    {
        var data = new Wht50TawiData(
            DocNo: "WT-2569-000123",
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
            WhtAmount: 1500m,
            CopyLabel: "ฉบับที่ 1 (สำหรับผู้ถูกหักภาษี ณ ที่จ่าย ใช้แนบพร้อมกับแบบแสดงรายการ)");

        var bytes = Wht50TawiFormFiller.Fill(data);
        Assert.True(bytes.Length > 5000, "expected a real PDF");

        // Dump for visual render self-check (pypdfium2) outside the test.
        var outPath = Path.Combine(Path.GetTempPath(), "50tawi-filled.pdf");
        File.WriteAllBytes(outPath, bytes);
        _o.WriteLine("OUT " + outPath + " size=" + bytes.Length);

        // Reopen + read raw /V by full name.
        using var ms = new MemoryStream(bytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var fields = doc.AcroForm!.Elements.GetArray("/Fields")!;
        var map = new System.Collections.Generic.Dictionary<string, string>();
        void Walk(PdfItem? it, string prefix)
        {
            var dct = (it as PdfReference)?.Value as PdfDictionary ?? it as PdfDictionary;
            if (dct is null) return;
            var t = dct.Elements.GetString("/T");
            var name = string.IsNullOrEmpty(t) ? prefix : prefix.Length == 0 ? t : $"{prefix}.{t}";
            var kids = dct.Elements.GetArray("/Kids");
            if (kids is { Elements.Count: > 0 }) { foreach (var k in kids) Walk(k, name); }
            else if (!string.IsNullOrEmpty(name)) map[name] = dct.Elements.GetString("/V");
        }
        foreach (var f in fields) Walk(f, "");

        Assert.Equal("บริษัท ทดสอบหักภาษี จำกัด", map.GetValueOrDefault("name1"));
        Assert.Equal("นายผู้รับเงิน ทดสอบ", map.GetValueOrDefault("name2"));
        Assert.Equal("ค่าจ้างแรงงาน", map.GetValueOrDefault("spec3"));
        Assert.Equal("50,000.00", map.GetValueOrDefault("pay1.13.0"));
        Assert.Equal("1,500.00", map.GetValueOrDefault("tax1.13.0"));
        Assert.Equal("1,500.00", map.GetValueOrDefault("total"));
        Assert.Equal("/Yes", map.GetValueOrDefault("chk4"));   // ภ.ง.ด.3
        Assert.Equal("/Yes", map.GetValueOrDefault("chk8"));   // หัก ณ ที่จ่าย
        doc.Close();
    }
}
