using System;
using System.IO;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

public sealed class RdAcroFormFillerMultiPageTests
{
    // ภ.ง.ด.51 is the only 2-page template. A page-2 field (Text4.3) must draw onto page index 1,
    // and a page-1 field (Text1.2) onto page 0 — proving Render is page-aware.
    [Fact]
    public void Page2_field_lands_on_page2_and_output_has_two_pages()
    {
        var tpl = Templates.Load("pnd51_main.pdf");
        var p1Only = RdAcroFormFiller.Render(tpl,
            new[] { new RdField("Text1.2", "PAGE1ONLY") }, Array.Empty<RdRadio>(), Pnd51FormFiller.Cells);
        var withP2 = RdAcroFormFiller.Render(tpl,
            new[] { new RdField("Text1.2", "PAGE1ONLY"), new RdField("Text4.3", "987654") },
            Array.Empty<RdRadio>(), Pnd51FormFiller.Cells);

        PageCount(withP2).Should().Be(2);
        var (p1a, p2a) = ContentLen(p1Only);
        var (p1b, p2b) = ContentLen(withP2);
        p2b.Should().BeGreaterThan(p2a + 30, "Text4.3 must add content to page 2");
        p1b.Should().BeGreaterThan(p1a - 30).And.BeLessThan(p1a + 200, "page-1 overlay unchanged by a page-2 field");
    }

    // Single-page form (50ทวิ): output is one page and Render still produces a real PDF (no regression in shape).
    [Fact]
    public void Single_page_template_stays_one_page()
    {
        var tpl = Templates.Load("wht_50tawi.pdf");
        var pdf = RdAcroFormFiller.Render(tpl, new[] { new RdField("name1", "TEST") }, Array.Empty<RdRadio>(), null);
        PageCount(pdf).Should().Be(1);
        pdf.Length.Should().BeGreaterThan(5000);
    }

    private static int PageCount(byte[] pdf)
    { using var ms = new MemoryStream(pdf); return PdfReader.Open(ms, PdfDocumentOpenMode.Import).PageCount; }

    private static (int p1, int p2) ContentLen(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        return (Len(doc.Pages[0]), doc.PageCount > 1 ? Len(doc.Pages[1]) : 0);
    }
    private static int Len(PdfPage pg)
    {
        var total = 0;
        var c = pg.Contents;                       // PdfContents (array of streams)
        for (var i = 0; i < c.Elements.Count; i++)
            if (c.Elements.GetObject(i) is PdfDictionary d && d.Stream != null) total += d.Stream.Value.Length;
        return total;
    }
}
