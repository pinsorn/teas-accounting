namespace Accounting.Infrastructure.Pdf;

/// <summary>Payer header + foreign-vendor payee identity + VAT self-assessment calc for ภ.พ.36.</summary>
public sealed record Pp36Model(
    string TaxId, string BranchCode, string PayerName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? PayeeName = null, string? Country = null, DateOnly? PayDate = null,
    // 2. การคำนวณภาษี — null ⇒ leave the amount boxes blank (header-only prefill, same fallback
    // Pnd54FormFiller uses when a sheet has no ม.70/ม.83·6 payment attached).
    decimal? ServiceAmount = null, decimal? VatAmount = null);

/// <summary>
/// Fills the official RD ภ.พ.36 AcroForm (self-assess VAT remittance for import of service /
/// foreign-vendor reverse charge, ม.83/6) and flattens it, via the generic <see cref="RdAcroFormFiller"/>.
/// One sheet per foreign-vendor payment — ภ.พ.36's own page-2 instructions say so verbatim
/// ("แยกเปนแต่ละรายผู้รับ และหรือแยกเปนแต่ละรายประเภทการจ่ายเงิน") — structurally the same
/// one-sheet-per-payment approach as <see cref="Pnd54FormFiller"/>, merged via
/// <see cref="WhtFormFiller.Merge"/>.
///
/// Field map decoded by MEASUREMENT, not guessed: an AcroForm roster dump for names/rects, THEN
/// a rendered-and-VIEWED fill (production-path sample data, screenshotted via Playwright/Edge —
/// no poppler on this box — and visually read) to catch exactly the kind of off-by-one the RD's
/// own field numbering invites. It caught one: Text1.10/1.11/1.111/1.12 are each one slot ahead
/// of what their numeric name suggests (Text1.10 prints under "แยก", not "ถนน"; Text1.111 prints
/// under "ตำบล/แขวง", not the field FOLLOWING it as the ".111" suffix implies). Full recon in
/// specs/fix-o5-pp36-pdf.md. Summary: Text1.0=payer taxId (comb17) · Text1.1=branch (comb5) ·
/// Text1.2=payer name · Text1.3..1.9=address · Text1.10=แยก (unused, no data source, like
/// pnd54's Yaek=null) · Text1.11=ถนน(Road) · Text1.111=ตำบล/แขวง(SubDistrict) ·
/// Text1.12=อำเภอ/เขต(District) · Text1.13=จังหวัด(Province) · Text1.14=รหัสไปรษณีย์(PostalCode,
/// comb5) · Text1.15=โทรศัพท์, unused (no phone in our data model, matching every other WHT
/// filler) · Text1.18=payee(vendor) name · Text1.23=payee country (printed label is "รัฐ" — this
/// form has no separate ประเทศ box) · Text1.26/27/28=payment day/Thai-month/BE-year ·
/// Radio Button1 on-state "0"=ยื่นปกติ · Radio Button2 on-state "1"=top-right case (1)
/// "จ่ายเงิน...ให้บริการในต่างประเทศ" · Radio Button3 on-state "0"=payee-status case (2)
/// "เป็นผู้ประกอบการที่ได้ให้บริการในต่างประเทศ...ใช้บริการนั้นในราชอาณาจักร" — every
/// <c>Pnd36Row</c> is exactly this case by construction (RequiresPnd36ReverseCharge foreign
/// vendors only), so both ticks are unconditional. Radios selected by ON-STATE, never
/// WidgetIndex: Button1's two widgets differ by &lt;1pt in Y (same tie-break hazard
/// Pnd54FormFiller's own radio comment warns about), and Button2/Button3 widget ORDER doesn't
/// match visual reading order either. การคำนวณภาษี: Text2.1=(1)จำนวนเงินที่จ่าย ·
/// Text2.2=(2)ภาษีมูลค่าเพิ่มที่ต้องนำส่ง · Text2.6=(5)รวม(2.+3.+4.) — rows 3/4 (เงินเพิ่ม/
/// เบี้ยปรับ, late-filing only) stay blank: a blank box asserts zero, and we only ever emit an
/// on-time normal filing. The "(ตัวอักษร)" spelled-out-amount fields (Text2.3, Text2.7) and the
/// signature block (Text2.8, Text2.9) are never filled — no Thai number-to-words anywhere in
/// this repo, and no filler in this family ever auto-signs.
/// </summary>
public static class Pp36FormFiller
{
    public static byte[] Fill(Pp36Model m)
    {
        var fields = new List<RdField>
        {
            new("Text1.0", Digits(m.TaxId)),
            new("Text1.1", Digits(m.BranchCode ?? "00000")),
            new("Text1.2",  m.PayerName   ?? ""),
            new("Text1.3",  m.Building    ?? ""),
            new("Text1.4",  m.RoomNo      ?? ""),
            new("Text1.5",  m.Floor       ?? ""),
            new("Text1.6",  m.Village     ?? ""),
            new("Text1.7",  m.HouseNo     ?? ""),
            new("Text1.8",  m.Moo         ?? ""),
            new("Text1.9",  m.Soi         ?? ""),
            // Text1.10 = แยก (junction) — no data source, left blank (like pnd54's Yaek=null).
            new("Text1.11", m.Road        ?? ""),
            new("Text1.111", m.SubDistrict ?? ""),
            new("Text1.12", m.District    ?? ""),
            new("Text1.13", m.Province    ?? ""),
            new("Text1.14", Digits(m.PostalCode ?? "")),
            new("Text1.18", m.PayeeName   ?? ""),
            new("Text1.23", m.Country     ?? ""),   // printed label is "รัฐ" — no separate ประเทศ box on this form
        };
        if (m.PayDate is { } d)
        {
            fields.Add(new("Text1.26", $"{d.Day:00}"));
            fields.Add(new("Text1.27", ThaiMonth(d.Month)));
            fields.Add(new("Text1.28", $"{d.Year + 543}"));
        }
        if (m.ServiceAmount is { } svc)
            fields.Add(new("Text2.1", Comb(svc), Right: true));
        if (m.VatAmount is { } vat)
        {
            fields.Add(new("Text2.2", Comb(vat), Right: true));
            fields.Add(new("Text2.6", Comb(vat), Right: true));   // 5. รวม = 2. + 3.(blank=0) + 4.(blank=0)
        }
        var radios = new List<RdRadio>
        {
            new("Radio Button1", "0"),   // ยื่นปกติ
            new("Radio Button2", "1"),   // (1) จ่ายเงินค่าซื้อ...ให้บริการในต่างประเทศ
            new("Radio Button3", "0"),   // (2) เป็นผู้ประกอบการที่ได้ให้บริการในต่างประเทศ...
        };
        return RdAcroFormFiller.Render(Template("pp36_main.pdf"), fields, radios, Cells.Value);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<double>>> Cells =
        new(() => RdCells.Load("Accounting.Infrastructure.Pdf.Templates.pp36_cells.json"));

    private static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    // Amount as comb digits (no comma; baht then 2 satang) for the per-digit comb amount boxes.
    private static string Comb(decimal v)
    {
        var baht = Math.Truncate(v);
        return $"{baht:0}{Math.Round((v - baht) * 100m):00}";
    }

    private static string ThaiMonth(int m) => m is >= 1 and <= 12
        ? new[] { "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
                  "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม" }[m - 1]
        : m.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Template(string file)
    {
        var asm = typeof(Pp36FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
