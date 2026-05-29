namespace Accounting.Infrastructure.Pdf;

/// <summary>Data needed to fill one official 50ทวิ (หนังสือรับรองการหักภาษี ณ ที่จ่าย).</summary>
public sealed record Wht50TawiData(
    string DocNo, string FormType,
    string PayerName, string? PayerTaxId, string? PayerAddress,
    string PayeeName, string? PayeeTaxId, string? PayeeAddress,
    string IncomeTypeMa40, string? IncomeDescription, DateOnly PayDate,
    decimal IncomeAmount, decimal WhtAmount);

/// <summary>
/// Maps a WHT certificate onto the official RD 50ทวิ AcroForm and renders it via the generic
/// <see cref="RdAcroFormFiller"/> (which shapes Thai, embeds the font, and flattens — see that
/// class for the mechanism). This type only owns the 50ทวิ field map; positioning is /Rect-driven.
/// Field map + verification: Pdf/Templates/wht_50tawi_fieldmap.md.
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

    /// <summary>Single filled page (one ฉบับ). Used by tests/foreign fallbacks.</summary>
    public static byte[] Fill(Wht50TawiData d) => RdAcroFormFiller.Render(Template(), MapFields(d), copies: 1);

    /// <summary>
    /// RD requires the 50ทวิ in 2 copies — ฉบับที่ 1 (ผู้ถูกหักภาษีแนบพร้อมแบบแสดงรายการ)
    /// and ฉบับที่ 2 (ผู้ถูกหักภาษีเก็บไว้เป็นหลักฐาน). The official form pre-prints both
    /// labels, so the copies are identical: render+flatten once then duplicate the page.
    /// </summary>
    public static byte[] FillCopies(Wht50TawiData d) => RdAcroFormFiller.Render(Template(), MapFields(d), copies: 2);

    private static List<RdField> MapFields(Wht50TawiData d)
    {
        var f = new List<RdField>
        {
            // ── Header ──────────────────────────────────────────────────────────
            new("run_no", d.DocNo),
            new("name1", d.PayerName),
            new("tin1", d.PayerTaxId ?? ""),
            new("add1", d.PayerAddress ?? ""),
            new("name2", d.PayeeName),
            new("tin1_2", d.PayeeTaxId ?? ""),
            new("add2", d.PayeeAddress ?? ""),
            // ── Form-type checkbox ──────────────────────────────────────────────
            new(d.FormType switch
            {
                "Pnd1" => "chk1",   // ภ.ง.ด.1ก
                "Pnd2" => "chk3",   // ภ.ง.ด.2
                "Pnd53" => "chk7",  // ภ.ง.ด.53
                _ => "chk4",        // ภ.ง.ด.3 (default — individual payee)
            }, "X", Check: true),
        };

        // ── Income row by ม.40 sub-section (see field map) ─────────────────────────
        var (pay, tax, date) = d.IncomeTypeMa40 switch
        {
            "1" => ("pay1.0", "tax1.0", "date1"),
            "2" => ("pay1.1", "tax1.1", "date2"),
            "3" => ("pay1.2", "tax1.2", "date3"),
            "4" => ("pay1.3", "tax1.3", "date4"),
            "5" or "6" or "7" or "8" => ("pay1.13.0", "tax1.13.0", "date14.0"),
            _ => ("pay1.14", "tax1.14", "date14.0"),
        };
        f.Add(new(pay, Money(d.IncomeAmount), Right: true));
        f.Add(new(tax, Money(d.WhtAmount), Right: true));
        f.Add(new(date, ThaiDate(d.PayDate)));
        // ม.3 เตรส (ม.40(5)–(8)) row carries a free-text "(ระบุ)" → the income description.
        if (d.IncomeTypeMa40 is "5" or "6" or "7" or "8" or "0"
            && !string.IsNullOrWhiteSpace(d.IncomeDescription))
            f.Add(new("spec3", d.IncomeDescription!));

        // ── Footer ──────────────────────────────────────────────────────────────
        f.Add(new("total", Money(d.WhtAmount), Right: true));
        f.Add(new("Text1.0.0", BahtText.Of(d.WhtAmount)));
        f.Add(new("chk8", "X", Check: true));   // (1) หักภาษี ณ ที่จ่าย — TEAS always withholds
        f.Add(new("date_pay", d.PayDate.Day.ToString()));
        f.Add(new("month_pay", ThaiMonth(d.PayDate.Month)));
        f.Add(new("year_pay", (d.PayDate.Year + 543).ToString()));
        return f;
    }

    private static string Money(decimal n) => n.ToString("#,##0.00");
    private static string ThaiDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";
    private static string ThaiMonth(int m) => m is >= 1 and <= 12
        ? new[] { "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
                  "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม" }[m - 1]
        : m.ToString();
}
