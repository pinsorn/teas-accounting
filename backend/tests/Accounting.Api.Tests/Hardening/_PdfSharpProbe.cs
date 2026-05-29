using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace Accounting.Api.Tests.Hardening;

// Mechanism guard for the 50ทวิ AcroForm fill approach. PdfSharp 6.2's typed
// PdfTextField ctor THROWS on this RD form, so the production filler must use the
// raw /Fields-dict path proven here (walk /Fields, match /T, set /V, drop /AP,
// set /NeedAppearances). This test pins that the template still has the fields and
// that Thai text round-trips. If PdfSharp is upgraded, run this first.
public sealed class _PdfSharpProbe
{
    private readonly ITestOutputHelper _o;
    public _PdfSharpProbe(ITestOutputHelper o) => _o = o;

    private static string Template()
    {
        // tests/Accounting.Api.Tests/bin/.../  → up to repo, into Infrastructure templates
        var dir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(dir,
            "..", "..", "..", "..", "..", "src", "Accounting.Infrastructure",
            "Pdf", "Templates", "wht_50tawi.pdf"));
    }

    [Fact]
    public void Can_fill_thai_and_roundtrip()
    {
        var tpl = Template();
        Assert.True(File.Exists(tpl), $"template missing: {tpl}");

        PdfDocument doc = PdfReader.Open(tpl, PdfDocumentOpenMode.Modify);
        var form = doc.AcroForm;
        Assert.NotNull(form);

        // List top-level field names so we know the API surface.
        var names = form!.Fields.Names.ToList();
        _o.WriteLine("FIELD COUNT (top-level): " + names.Count);
        _o.WriteLine(string.Join(", ", names));

        // Make viewers regenerate appearance (Thai shows via the viewer's font).
        if (!form.Elements.ContainsKey("/NeedAppearances"))
            form.Elements.Add("/NeedAppearances", new PdfBoolean(true));
        else
            form.Elements["/NeedAppearances"] = new PdfBoolean(true);

        // RAW approach — bypass PdfSharp's PdfTextField ctor (it throws on this RD form).
        // Walk the /Fields array directly, match /T, set /V to a UTF-16 PdfString.
        var fillTargets = new System.Collections.Generic.Dictionary<string, string>
        {
            ["book_no"] = "ก123",
            ["name1"]   = "บริษัท ผู้หักภาษี จำกัด",
            ["tin1"]    = "0105556123453",
            ["name2"]   = "นายผู้ถูกหักภาษี ทดสอบ",
            ["total"]   = "1,234.56",
        };
        var fieldsArr = form.Elements.GetArray("/Fields");
        int set = 0;
        for (int i = 0; i < fieldsArr!.Elements.Count; i++)
        {
            if (fieldsArr.Elements.GetObject(i) is not PdfDictionary fd) continue;
            var t = fd.Elements.GetString("/T");
            if (t is { Length: > 0 } && fillTargets.TryGetValue(t, out var val))
            {
                fd.Elements.SetString("/V", val);          // value (UTF-16 auto by PdfSharp)
                fd.Elements.Remove("/AP");                  // drop stale appearance → viewer regens
                set++;
            }
        }
        _o.WriteLine("RAW SET fields: " + set);

        var outPath = Path.Combine(Path.GetTempPath(), "50tawi-probe.pdf");
        doc.Save(outPath);
        doc.Close();

        // Reopen raw + read back /V.
        var re = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
        var rfa = re.AcroForm!.Elements.GetArray("/Fields")!;
        string Get(string n)
        {
            for (int i = 0; i < rfa.Elements.Count; i++)
                if (rfa.Elements.GetObject(i) is PdfDictionary fd
                    && fd.Elements.GetString("/T") == n)
                    return fd.Elements.GetString("/V");
            return "<none>";
        }
        _o.WriteLine("READBACK book_no=" + Get("book_no"));
        _o.WriteLine("READBACK name1=" + Get("name1"));
        _o.WriteLine("READBACK name2=" + Get("name2"));
        _o.WriteLine("OUT SIZE=" + new FileInfo(outPath).Length);

        Assert.Equal(5, set);
        Assert.Equal("บริษัท ผู้หักภาษี จำกัด", Get("name1"));
        Assert.Equal("นายผู้ถูกหักภาษี ทดสอบ", Get("name2"));
        re.Close();
    }
}
