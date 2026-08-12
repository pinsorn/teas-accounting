using System.Globalization;
using System.Text;
using Accounting.Infrastructure.Bank.Pdf;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.TaxFilings;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

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
    private readonly ITestOutputHelper _o;
    public TaxFormFillDiagnostic(ITestOutputHelper o) => _o = o;

    private static void Save(string name, byte[] pdf) =>
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Out, name), pdf);

    // ── R2/WP-1 (C4) — decode pnd1_main.pdf / pnd1a_main.pdf by MEASUREMENT, never by guessing. ──

    private static byte[] LoadTemplate(string file)
    {
        var asm = typeof(Pnd1FormFiller).Assembly;   // Templates/*.pdf are embedded in this assembly
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>One /Tx (text) AcroForm field, as PdfSharp's own field-name resolution sees it —
    /// mirrors <c>RdAcroFormFiller.ReadFieldRects</c>'s dotted-name concatenation and comb/MaxLen
    /// inheritance exactly, so the marker fill exercises the SAME rect RdField would resolve to.
    /// <paramref name="Occurrences"/> &gt; 1 means two+ widgets share this name (radio-group-like for
    /// a text field) — RdAcroFormFiller's "last wins" would silently pick one; worth flagging.</summary>
    private readonly record struct FieldRoster(string Name, bool Comb, int MaxLen, int Occurrences);

    private static List<FieldRoster> RosterTextFields(byte[] template)
    {
        using var probe = new MemoryStream(template);
        var doc = PdfReader.Open(probe, PdfDocumentOpenMode.Modify);
        var fieldsArr = doc.AcroForm?.Elements.GetArray("/Fields")
            ?? throw new InvalidOperationException("Template has no AcroForm /Fields.");
        const int combFlag = 1 << 24;
        var all = new List<(string Name, bool Comb, int MaxLen)>();

        void Walk(PdfItem? item, string prefix, string inhFt, int inhMaxLen, bool inhComb)
        {
            var dict = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;
            if (dict is null) return;
            var t = dict.Elements.GetString("/T");
            var name = string.IsNullOrEmpty(t) ? prefix : prefix.Length == 0 ? t : $"{prefix}.{t}";
            var ftHere = dict.Elements.GetName("/FT");
            var ft = string.IsNullOrEmpty(ftHere) ? inhFt : ftHere;   // /FT is inheritable (radio kids rarely repeat it)
            var maxLen = dict.Elements.ContainsKey("/MaxLen") ? dict.Elements.GetInteger("/MaxLen") : inhMaxLen;
            var comb = inhComb || (dict.Elements.ContainsKey("/Ff") && (dict.Elements.GetInteger("/Ff") & combFlag) != 0);
            var kids = dict.Elements.GetArray("/Kids");
            if (kids is { Elements.Count: > 0 })
            {
                foreach (var k in kids) Walk(k, name, ft, maxLen, comb);
                return;
            }
            if (ft == "/Tx" && !string.IsNullOrEmpty(name))
                all.Add((name, comb, maxLen));
        }
        foreach (var f in fieldsArr) Walk(f, "", "", 0, false);
        doc.Close();

        // last-wins per name, matching RdAcroFormFiller.ReadFieldRects's own `map[name] = ...` so the
        // marker fill lands on EXACTLY the rect production's RdField would resolve to.
        return all.GroupBy(x => x.Name)
            .Select(g => new FieldRoster(g.Key, g.Last().Comb, g.Last().MaxLen, g.Count()))
            .ToList();
    }

    private static void DumpWords(byte[] pdf, string outName)
    {
        using var ms = new MemoryStream(pdf);
        var words = KPlusPdfTextExtractor.Extract(ms, password: null);
        var lines = words
            .OrderBy(w => w.PageNo).ThenBy(w => w.Top).ThenBy(w => w.Left)
            .Select(w => string.Format(
                CultureInfo.InvariantCulture,
                "Page={0}  Top={1:F1}  Bottom={2:F1}  Left={3:F1}  Right={4:F1}  Text={5}",
                w.PageNo, w.Top, w.Bottom, w.Left, w.Right, w.Text));
        Save(outName, Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines)));
    }

    private void DumpMarkerAndTemplate(string templateFile, string markerOut, string templateOut)
    {
        var template = LoadTemplate(templateFile);
        var roster = RosterTextFields(template);
        var dups = roster.Where(r => r.Occurrences > 1).ToList();
        _o.WriteLine($"{templateFile}: {roster.Count} /Tx fields, {dups.Count} with >1 widget " +
            (dups.Count == 0 ? "" : $"({string.Join(", ", dups.Select(d => $"{d.Name}x{d.Occurrences}"))})"));

        // Marker render: every text field filled with ITS OWN FIELD ID, rendered + flattened through
        // RdAcroFormFiller exactly as production does. Fill_every_box_* (below) supplies the
        // human-readable Stage-B render; this one exists purely so the extracted text UNAMBIGUOUSLY
        // names which field printed where.
        var fields = roster.Select(r => new RdField(r.Name, r.Name)).ToList();
        var marker = RdAcroFormFiller.Render(template, fields);
        DumpWords(marker, markerOut);
        DumpWords(template, templateOut);   // the blank template's own printed labels/row numbers
    }

    [SkippableFact]
    public void Dump_pnd1_marker_positions()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        DumpMarkerAndTemplate("pnd1_main.pdf", "_pnd1_marker_words.txt", "_pnd1_template_words.txt");
    }

    [SkippableFact]
    public void Dump_pnd1a_marker_positions()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        DumpMarkerAndTemplate("pnd1a_main.pdf", "_pnd1a_marker_words.txt", "_pnd1a_template_words.txt");
    }

    // Known comb-shaped identity fields get realistic-looking synthetic values — a bare running
    // counter would truncate to indistinguishable digits inside a narrow comb (e.g. a 5-digit branch
    // box). Same field names on pnd1 and pnd1a (both decode to the same header layout).
    private static readonly Dictionary<string, string> KnownCombValues = new()
    {
        ["Text1.0"] = FormatTaxId13("0195561012349"),   // เลขประจำตัวผู้เสียภาษี ผู้หัก (comb 17)
        ["Text1.1"] = "00007",                          // สาขาที่ (comb 5)
        ["Text1.15"] = "10250",                         // รหัสไปรษณีย์ (comb 5)
    };

    private static string FormatTaxId13(string d) =>
        $"{d[0]}-{d.Substring(1, 4)}-{d.Substring(5, 5)}-{d.Substring(10, 2)}-{d[12]}";

    private static byte[] FillEveryBoxDistinct(string templateFile)
    {
        var template = LoadTemplate(templateFile);
        var roster = RosterTextFields(template);
        var fields = new List<RdField>();
        var i = 0;
        foreach (var r in roster)
        {
            i++;
            string value;
            if (r.Comb && r.MaxLen > 0)
            {
                value = KnownCombValues.TryGetValue(r.Name, out var known)
                    ? known
                    // generic comb fallback: a distinct digit repeated to fill the box exactly, so it
                    // is never confused with any other field's fill.
                    : new string((char)('0' + i % 10), r.MaxLen);
            }
            else
            {
                // distinct, strictly-increasing, money-shaped value: every field on this form gets a
                // number no other field on the form also gets, so a human can tell instantly which
                // value landed where (no reused numbers, per the Stage-B requirement).
                value = (i * 1111.11m).ToString("#,##0.00", CultureInfo.GetCultureInfo("th-TH"));
            }
            fields.Add(new RdField(r.Name, value));
        }
        return RdAcroFormFiller.Render(template, fields);
    }

    [SkippableFact]
    public void Fill_every_box_pnd1()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var bytes = FillEveryBoxDistinct("pnd1_main.pdf");
        Save("_diag_pnd1.pdf", bytes);
        _o.WriteLine($"OUT _diag_pnd1.pdf size={bytes.Length}");
    }

    [SkippableFact]
    public void Fill_every_box_pnd1a()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var bytes = FillEveryBoxDistinct("pnd1a_main.pdf");
        Save("_diag_pnd1a.pdf", bytes);
        _o.WriteLine($"OUT _diag_pnd1a.pdf size={bytes.Length}");
    }

    // ── Production-faithful re-test: does the REAL entrypoint (only rows 1+6(+8) populated, rows
    // 2-5 and Text2.21 genuinely EMPTY, exactly as `Pnd1FilingService` renders it) place the totals
    // where the marker render measured, or does emptiness elsewhere shift a text-extraction tool's
    // line-clustering the way the swarm's pdftotext -layout finding describes? The every-box-distinct
    // Stage-B render populates row 5 too, which the real filler never does — this closes that gap. ──

    [SkippableFact]
    public void Fill_pnd1_production_path()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var m = new Pnd1MonthlyModel(
            TaxId, "00000", "บริษัท ตัวอย่าง จำกัด",
            Building: null, RoomNo: null, Floor: null, Village: null,
            HouseNo: "99", Moo: null, Soi: null, Street: "ถนนทดสอบ",
            SubDistrict: "จอมพล", District: "จตุจักร", Province: "กรุงเทพมหานคร", PostalCode: "10900",
            PeriodMonth: 6, PeriodYearBE: 2569,
            Lines: new[] { new Pnd1Line(TaxId, "สมชาย", "ทดสอบ", "15/06/2569", 125000m, 1408.33m) });
        var bytes = Pnd1FormFiller.FillMonthly(m);
        Save("_diag_pnd1_prodpath.pdf", bytes);
        _o.WriteLine($"OUT _diag_pnd1_prodpath.pdf size={bytes.Length}");
    }

    [SkippableFact]
    public void Fill_pnd1a_production_path()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var m = new Pnd1aModel(
            TaxId, "00000", "บริษัท ตัวอย่าง จำกัด",
            Building: null, RoomNo: null, Floor: null, Village: null,
            HouseNo: "99", Moo: null, Soi: null, Street: "ถนนทดสอบ",
            SubDistrict: "จอมพล", District: "จตุจักร", Province: "กรุงเทพมหานคร", PostalCode: "10900",
            YearBE: 2569,
            Lines: new[] { new Pnd1aLine(TaxId, "สมชาย", "ทดสอบ", "99 ถนนทดสอบ", 965000m, 52450m) });
        var bytes = Pnd1aFormFiller.FillAnnual(m);
        Save("_diag_pnd1a_prodpath.pdf", bytes);
        _o.WriteLine($"OUT _diag_pnd1a_prodpath.pdf size={bytes.Length}");
    }

    [SkippableFact]
    public void Dump_sps110_positioned_words()
    {
        Skip.If(Environment.GetEnvironmentVariable("TEAS_DIAG") != "1", "diagnostic only");
        var repoRoot = Environment.GetEnvironmentVariable("TEAS_REPO_ROOT")
            ?? @"Y:\ClaudePlayground\TEAS-Project";
        var template = System.IO.Path.Combine(
            repoRoot, "backend", "src", "Accounting.Infrastructure", "Pdf", "Templates", "sps110_main.pdf");
        using var content = System.IO.File.OpenRead(template);
        var words = KPlusPdfTextExtractor.Extract(content, password: null);

        foreach (var pageNo in new[] { 1, 2, 3, 4 })
        {
            var lines = words
                .Where(w => w.PageNo == pageNo)
                .OrderBy(w => w.Top)
                .ThenBy(w => w.Left)
                .Select(w => string.Format(
                    CultureInfo.InvariantCulture,
                    "Top={0:F1}  Bottom={1:F1}  Left={2:F1}  Right={3:F1}  Text={4}",
                    w.Top, w.Bottom, w.Left, w.Right, w.Text));
            Save($"_sps110_p{pageNo}_words.txt", Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines)));
        }
    }

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
            PayeeName: "Acme Overseas Ltd.",
            Income: 1234567.89m, RatePct: 15m, Tax: 185185.18m);
        Save("_diag_pnd54.pdf", Pnd54FormFiller.Fill(m));
    }
}
