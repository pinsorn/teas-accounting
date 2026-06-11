namespace Accounting.Infrastructure.Pdf;

/// <summary>Data needed to fill one official 50ทวิ (หนังสือรับรองการหักภาษี ณ ที่จ่าย).</summary>
public sealed record Wht50TawiData(
    string DocNo, string FormType,
    string PayerName, string? PayerTaxId, string? PayerAddress,
    string PayeeName, string? PayeeTaxId, string? PayeeAddress,
    string IncomeTypeMa40, string? IncomeDescription, DateOnly PayDate,
    decimal IncomeAmount, decimal WhtAmount,
    // P-D #4 (employee annual cert) — เงินสมทบประกันสังคมทั้งปี printed in the fund box
    // (Text1.0.1); employees use it for their PIT filing. Null (vendor certs) = blank.
    decimal? SsoContribution = null);

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
        var (book, no) = SplitDocNo(d.DocNo);
        var f = new List<RdField>
        {
            // ── Header ──────────────────────────────────────────────────────────
            // เล่มที่ = month/Buddhist-year, เลขที่ = running number (split from DocNo MM-YYYY-WT-NNNN).
            new("book_no", book),
            new("run_no", no),
            new("name1", d.PayerName),
            // 13-digit tax id → the "(13 หลัก)" comb (id1/id1_2, 17 cells incl. dashes); the filler
            // auto-spaces one char per cell. tin1/tin1_2 are the legacy box, left blank.
            new("id1", FormatTaxId13(d.PayerTaxId)),
            new("add1", d.PayerAddress ?? ""),
            new("name2", d.PayeeName),
            new("id1_2", FormatTaxId13(d.PayeeTaxId)),
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

        // ── Income-table totals row: pay1.14 = Σ income, tax1.14 = Σ tax. A TEAS cert carries
        //    one aggregated income, so the totals equal that single row. ──────────────────────
        f.Add(new("pay1.14", Money(d.IncomeAmount), Right: true));
        f.Add(new("tax1.14", Money(d.WhtAmount), Right: true));

        // ── Footer ──────────────────────────────────────────────────────────────
        // `total` = รวมเงินภาษีที่หักนำส่ง **เป็นตัวอักษร** (Thai words). The Text1.* boxes are
        // fund-contribution fields — Text1.0.0 กบข./กสจ./กองทุนสงเคราะห์ครูเอกชน, Text1.0.1
        // ประกันสังคม, Text1.1.0 กองทุนสำรองเลี้ยงชีพ — N/A for a PV-sourced WHT cert → left blank.
        f.Add(new("total", BahtText.Of(d.WhtAmount)));
        // P-D #4 — the annual employee cert carries the year's SSO contributions (ประกันสังคม box).
        if (d.SsoContribution is { } sso && sso > 0)
            f.Add(new("Text1.0.1", Money(sso), Right: true));
        f.Add(new("chk8", "X", Check: true));   // (1) หักภาษี ณ ที่จ่าย — TEAS always withholds
        f.Add(new("date_pay", d.PayDate.Day.ToString()));
        f.Add(new("month_pay", ThaiMonth(d.PayDate.Month)));
        f.Add(new("year_pay", (d.PayDate.Year + 543).ToString()));
        return f;
    }

    // DocNo "MM-YYYY-WT-NNNN" → (เล่มที่ = "MM/พ.ศ.", เลขที่ = "NNNN"). Falls back to the
    // whole DocNo as เลขที่ if the shape is unexpected.
    private static (string Book, string No) SplitDocNo(string docNo)
    {
        var p = docNo.Split('-');
        if (p.Length >= 4 && int.TryParse(p[0], out var mm) && int.TryParse(p[1], out var yyyy))
            return ($"{mm:00}/{yyyy + 543}", p[^1]);
        return ("", docNo);
    }

    // 13-digit Thai tax id → grouped "X-XXXX-XXXXX-XX-X" (17 chars) to fill the id1 comb's 17
    // cells (digits in digit-cells, dashes in the printed separators). Non-13-digit → as-is.
    private static string FormatTaxId13(string? taxId)
    {
        var d = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 13 ? $"{d[0]}-{d[1..5]}-{d[5..10]}-{d[10..12]}-{d[12]}" : (taxId ?? "");
    }

    private static string Money(decimal n) => n.ToString("#,##0.00");
    private static string ThaiDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";
    private static string ThaiMonth(int m) => m is >= 1 and <= 12
        ? new[] { "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
                  "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม" }[m - 1]
        : m.ToString();
}
