using System.Text.Json;
using Accounting.Application.Payroll;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Fills the official SSO สปส.1-10 ส่วนที่ 1 (employer monthly contribution summary) — a FLAT PDF
/// with zero AcroForm widgets (the SSO, unlike the RD, ships no fillable form), so every value is
/// overlaid at the coordinates measured in docs/RD-Forms/sps1-10/fieldmap/sps110_boxes.json
/// (marker-raster verified; see sps110_map.md). v1 fills: employer header + address, เลขที่บัญชี
/// 10-cell comb, ลำดับที่สาขา 6-cell comb, งวดเดือน/พ.ศ., the 4 contribution rows + employee
/// count, and the amount-in-words line. Payment-method checkboxes, signatures, dates and the
/// officer panels stay blank for the filer (print-and-sign — same posture as ภ.พ.01/09).
/// </summary>
public static class Sps110FormFiller
{
    private static readonly string[] ThaiMonths =
    {
        "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    };

    public static byte[] Fill(SsoMonthlyModel m)
    {
        var map = Boxes.Value;
        var boxes = new List<RdFlatBox>();

        void Line(string key, string? text, bool right = false)
        {
            if (string.IsNullOrWhiteSpace(text) || !map.TryGetValue(key, out var b)) return;
            // Shrink-to-fit (RenderFlat draws at the given size verbatim): ≈0.55em/char like the
            // widget path — chiefly the amount-in-words line, a no-op for short values.
            var fs = Math.Clamp(b.H - 2.0, 7.5, 11.0);
            var est = text.Length * fs * 0.55;
            if (est > b.W) fs = Math.Max(6.0, b.W / (text.Length * 0.55));
            boxes.Add(new RdFlatBox(b.X, b.YTop + (b.H - fs) * 0.40, b.W, fs, text, right));
        }

        void Comb(string key, string digits)
        {
            if (string.IsNullOrWhiteSpace(digits) || !map.TryGetValue(key, out var b) || b.Cells is null)
                return;
            boxes.AddRange(RdAcroFormFiller.FlatComb(b.Cells, b.YTop, b.H, digits));
        }

        void Amount(string bahtKey, string satangKey, decimal v)
        {
            var baht = Math.Truncate(v);
            var st = Math.Round((v - baht) * 100m);
            Line(bahtKey, baht.ToString("N0"), right: true);
            Line(satangKey, st.ToString("00"), right: true);
        }

        Line("employerName", m.EmployerName);
        // Address split across the form's two dotted lines: street-level pieces first, then
        // subdistrict/district/province (postal code has its own slot).
        Line("address", JoinParts(
            m.Building, m.RoomNo is { } rn ? $"ห้อง {rn}" : null, m.Floor is { } fl ? $"ชั้น {fl}" : null,
            m.Village, m.HouseNo is { } hn ? $"เลขที่ {hn}" : null));
        Line("address2", JoinParts(
            m.Moo is { } mo ? $"หมู่ {mo}" : null, m.Soi is { } so ? $"ซอย {so}" : null,
            m.Street is { } st ? $"ถนน {st}" : null, m.SubDistrict, m.District, m.Province));
        Line("postalCode", m.PostalCode);

        // เลขที่บัญชี (10-digit SSO employer registration) — blank stays blank (not submittable
        // until CompanyProfile carries it; never print zeros as if they were an account).
        Comb("accountNoCells", new string([.. (m.EmployerAccountNo ?? "").Where(char.IsDigit)]));
        // ลำดับที่สาขา — 6 printed cells; TEAS branch codes are 5-digit RD style (00000 = HQ),
        // left-padded to the SSO's 6 (HQ → 000000).
        var seq = new string([.. (m.BranchCode ?? "").Where(char.IsDigit)]);
        if (seq.Length > 0) Comb("branchSeqCells", seq.PadLeft(6, '0'));

        Line("wageMonth", ThaiMonths[m.PeriodMonth - 1]);
        Line("wageYear", m.PeriodYearBE.ToString());

        Amount("tblWageBaht", "tblWageSatang", m.TotalWage);
        Amount("tblEmpContribBaht", "tblEmpContribSatang", m.TotalEmployeeContribution);
        Amount("tblEmployerContribBaht", "tblEmployerContribSatang", m.TotalEmployerContribution);
        Amount("tblTotalBaht", "tblTotalSatang", m.GrandTotalContribution);
        Line("tblEmployeeCount", m.EmployeeCount.ToString("N0"), right: true);
        Line("amountWords", BahtText.Of(m.GrandTotalContribution));

        return RdAcroFormFiller.RenderFlat(Template(), boxes);
    }

    private static string? JoinParts(params string?[] parts)
    {
        var s = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return s.Length == 0 ? null : s;
    }

    private sealed record Box(double X, double YTop, double W, double H, IReadOnlyList<double>? Cells);

    private static readonly Lazy<IReadOnlyDictionary<string, Box>> Boxes = new(LoadBoxes);

    private static IReadOnlyDictionary<string, Box> LoadBoxes()
    {
        var asm = typeof(Sps110FormFiller).Assembly;
        using var s = asm.GetManifestResourceStream(
            "Accounting.Infrastructure.Pdf.Templates.sps110_boxes.json")
            ?? throw new InvalidOperationException("Embedded sps110_boxes.json not found.");
        using var doc = JsonDocument.Parse(s);
        var map = new Dictionary<string, Box>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var e = p.Value;
            var h = e.GetProperty("h").GetDouble();
            var yTop = e.GetProperty("yTop").GetDouble();
            if (e.TryGetProperty("cells", out var cells))
                map[p.Name] = new Box(0, yTop, 0, h,
                    [.. cells.EnumerateArray().Select(c => c.GetDouble())]);
            else
                map[p.Name] = new Box(
                    e.GetProperty("x").GetDouble(), yTop,
                    e.GetProperty("w").GetDouble(), h, null);
        }
        return map;
    }

    private static byte[] Template()
    {
        var asm = typeof(Sps110FormFiller).Assembly;
        using var s = asm.GetManifestResourceStream(
            "Accounting.Infrastructure.Pdf.Templates.sps110_main.pdf")
            ?? throw new InvalidOperationException("Embedded sps110_main.pdf not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
