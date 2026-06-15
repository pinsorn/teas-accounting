namespace Accounting.Infrastructure.Pdf;

/// <summary>Payer header + foreign-payee identity for ภ.ง.ด.54 (ม.70 remittance).</summary>
public sealed record Pnd54Model(
    string TaxId, string BranchCode, string PayerName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Yaek, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? PayeeName);

/// <summary>
/// Fills the official RD ภ.ง.ด.54 AcroForm (single page; ม.70 foreign-payment remittance) and flattens
/// it, via the generic <see cref="RdAcroFormFiller"/>. ภ.ง.ด.54 is a single-payment form (one foreign
/// payee per sheet), structurally unlike the ภ.ง.ด.3/53 payee-list forms — hence its own filler.
///
/// v1 = a header + radio PREFILL (like ภ.พ.01/09): payer identity, the (1) ม.70 / ยื่นปกติ marks, and the
/// foreign payee's name. The payment-amount / income-type / WHT boxes are left for completion — they
/// require ม.70 seed data + an amount-field render-verify pass (no co2 data exists yet). Field map decoded
/// from the template (Num1=payer taxId comb18 · Text1.0=branch · Text1.1=payer name · Text1.2..Text1.13=
/// address · Text1.15=ผู้รับเงินได้ · Radio Button1#0=ม.70 · Radio Button2#0=ยื่นปกติ).
/// </summary>
public static class Pnd54FormFiller
{
    public static byte[] Fill(Pnd54Model m)
    {
        var fields = new List<RdField>
        {
            new("Num1", Digits(m.TaxId)),                 // payer taxId (comb 18 = 13 digits + separators)
            new("Text1.0", Digits(m.BranchCode ?? "00000")),
            new("Text1.1", m.PayerName ?? ""),
            new("Text1.2",  m.Building    ?? ""),
            new("Text1.3",  m.RoomNo      ?? ""),
            new("Text1.4",  m.Floor       ?? ""),
            new("Text1.5",  m.Village     ?? ""),
            new("Text1.6",  m.HouseNo     ?? ""),
            new("Text1.7",  m.Moo         ?? ""),
            new("Text1.8",  m.Soi         ?? ""),
            new("Text1.9",  m.Yaek        ?? ""),
            new("Text1.09", m.Road        ?? ""),
            new("Text1.10", m.SubDistrict ?? ""),
            new("Text1.11", m.District    ?? ""),
            new("Text1.12", m.Province    ?? ""),
            new("Text1.13", m.PostalCode  ?? ""),
            new("Text1.15", m.PayeeName   ?? ""),         // ชื่อผู้รับเงินได้ (foreign payee)
        };
        // Select by on-state (export value), not positional index — same-row radio pairs (ยื่นปกติ/
        // เพิ่มเติม) tie-break unreliably on the geometric sort.
        var radios = new List<RdRadio>
        {
            new("Radio Button1", "0"),   // (1) ภาษีหักจากเงินได้ที่จ่าย ตาม ม.70
            new("Radio Button2", "0"),   // (1) ยื่นปกติ
        };
        return RdAcroFormFiller.Render(Template("pnd54_main.pdf"), fields, radios, Cells.Value);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<double>>> Cells =
        new(() => RdCells.Load("Accounting.Infrastructure.Pdf.Templates.pnd54_cells.json"));

    private static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd54FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
