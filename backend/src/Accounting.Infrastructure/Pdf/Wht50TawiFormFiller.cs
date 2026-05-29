using System.Reflection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Accounting.Infrastructure.Pdf;

/// <summary>Data needed to fill one official 50ทวิ (หนังสือรับรองการหักภาษี ณ ที่จ่าย).</summary>
public sealed record Wht50TawiData(
    string DocNo, string FormType,
    string PayerName, string? PayerTaxId, string? PayerAddress,
    string PayeeName, string? PayeeTaxId, string? PayeeAddress,
    string IncomeTypeMa40, string? IncomeDescription, DateOnly PayDate,
    decimal IncomeAmount, decimal WhtAmount,
    string CopyLabel);   // "ฉบับที่ 1 ..." / "ฉบับที่ 2 ..." (RD requires 2 copies)

/// <summary>
/// Fills the official RD 50ทวิ AcroForm (bundled at Pdf/Templates/wht_50tawi.pdf) from a
/// WhtCertificate. Field map + verification: Pdf/Templates/wht_50tawi_fieldmap.md.
///
/// Mechanism (see _PdfSharpProbe): PdfSharp 6.2's typed PdfTextField ctor throws on this
/// form, so we walk the raw /Fields + /Kids dict, set /V (text) or /V+/AS=/Yes (checkbox),
/// drop stale /AP, and set /NeedAppearances so viewers render the Thai with their own font.
/// </summary>
public static class Wht50TawiFormFiller
{
    private static byte[]? _template;

    private static byte[] Template()
    {
        if (_template is not null) return _template;
        var asm = typeof(Wht50TawiFormFiller).Assembly;
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("wht_50tawi.pdf", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded 50ทวิ template not found.");
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return _template = ms.ToArray();
    }

    public static byte[] Fill(Wht50TawiData d)
    {
        using var input = new MemoryStream(Template());
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var form = doc.AcroForm
            ?? throw new InvalidOperationException("Template has no AcroForm.");

        if (form.Elements.ContainsKey("/NeedAppearances"))
            form.Elements["/NeedAppearances"] = new PdfBoolean(true);
        else
            form.Elements.Add("/NeedAppearances", new PdfBoolean(true));

        // Build full-name → leaf-dict map (recurse /Fields then /Kids).
        var map = new Dictionary<string, PdfDictionary>();
        void Walk(PdfItem? item, string prefix)
        {
            var dict = Resolve(item);
            if (dict is null) return;
            var t = dict.Elements.GetString("/T");
            var name = string.IsNullOrEmpty(t) ? prefix
                : prefix.Length == 0 ? t : $"{prefix}.{t}";
            var kids = dict.Elements.GetArray("/Kids");
            if (kids is { Elements.Count: > 0 })
                foreach (var k in kids) Walk(k, name);
            else if (!string.IsNullOrEmpty(name))
                map[name] = dict;
        }
        var fields = form.Elements.GetArray("/Fields")
            ?? throw new InvalidOperationException("AcroForm has no /Fields.");
        foreach (var f in fields) Walk(f, "");

        void Text(string n, string? v)
        {
            if (!map.TryGetValue(n, out var fd)) return;
            fd.Elements.SetString("/V", v ?? "");
            fd.Elements.Remove("/AP");   // force viewer to regenerate appearance
        }
        void Check(string n)
        {
            if (!map.TryGetValue(n, out var fd)) return;
            fd.Elements.SetName("/V", "/Yes");
            fd.Elements.SetName("/AS", "/Yes");
        }

        // ── Header ────────────────────────────────────────────────────────────
        Text("run_no", d.DocNo);
        Text("name1", d.PayerName);
        Text("tin1", d.PayerTaxId);
        Text("add1", d.PayerAddress);
        Text("name2", d.PayeeName);
        Text("tin1_2", d.PayeeTaxId);
        Text("add2", d.PayeeAddress);

        // ── Form-type checkbox ─────────────────────────────────────────────────
        Check(d.FormType switch
        {
            "Pnd1" => "chk1",   // ภ.ง.ด.1ก
            "Pnd2" => "chk3",   // ภ.ง.ด.2
            "Pnd53" => "chk7",  // ภ.ง.ด.53
            _ => "chk4",        // ภ.ง.ด.3 (default — individual payee)
        });

        // ── Income row by ม.40 sub-section (see field map) ─────────────────────
        var (pay, tax, date) = d.IncomeTypeMa40 switch
        {
            "1" => ("pay1.0", "tax1.0", "date1"),
            "2" => ("pay1.1", "tax1.1", "date2"),
            "3" => ("pay1.2", "tax1.2", "date3"),
            "4" => ("pay1.3", "tax1.3", "date4"),
            "5" or "6" or "7" or "8" => ("pay1.13.0", "tax1.13.0", "date14.0"),
            _ => ("pay1.14", "tax1.14", "date14.0"),
        };
        Text(pay, Money(d.IncomeAmount));
        Text(tax, Money(d.WhtAmount));
        Text(date, ThaiDate(d.PayDate));
        // ม.3 เตรส (ม.40(5)–(8)) row carries a free-text "(ระบุ)" → the income description.
        if (d.IncomeTypeMa40 is "5" or "6" or "7" or "8" or "0"
            && !string.IsNullOrWhiteSpace(d.IncomeDescription))
            Text("spec3", d.IncomeDescription);

        // ── Footer ─────────────────────────────────────────────────────────────
        Text("total", Money(d.WhtAmount));
        Text("Text1.0.0", BahtText.Of(d.WhtAmount));
        Check("chk8");   // (1) หักภาษี ณ ที่จ่าย — TEAS always withholds
        Text("date_pay", d.PayDate.Day.ToString());
        Text("month_pay", ThaiMonth(d.PayDate.Month));
        Text("year_pay", (d.PayDate.Year + 543).ToString());

        // ── Copy label (RD requires 2 copies with the header text) ─────────────
        Text("item", d.CopyLabel);

        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };

    private static string Money(decimal n) => n.ToString("#,##0.00");
    private static string ThaiDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";
    private static string ThaiMonth(int m) => m is >= 1 and <= 12
        ? new[] { "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
                  "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม" }[m - 1]
        : m.ToString();
}
