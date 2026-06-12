namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Company-identity values shared by the ภ.พ.01/ภ.พ.09 v1 prefill (same CompanyProfile source
/// as the ภ.ง.ด.50/51 headers). Everything beyond this header is blank-manual by design.
/// </summary>
public sealed record VatRegIdentity(
    string TaxId, string LegalName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Email, string? Website);

/// <summary>
/// Fills the official RD ภ.พ.01 AcroForm (pp01_main.pdf, 3pp) — v1 = page-1 identity header
/// ONLY, per docs/RD-Forms/pp01/fieldmap/pp01_map.md (every field raster-traced). No radio is
/// ever ticked (trap: `Radio Button2` is one group spanning two unrelated questions — a form
/// defect). ⚠️ Field numbering does not follow visual order; names below come from the map,
/// never intuition. Cross-form trap: Text1.18 = แยก here but E-mail on ภ.พ.09.
/// </summary>
public static class Pp01FormFiller
{
    public static byte[] Fill(VatRegIdentity m)
    {
        var fields = new List<RdField>
        {
            new("Text1.3",  m.LegalName),                       // 1. ชื่อผู้ประกอบการ
            new("Text1.4",  Digits(m.TaxId)),                   // เลขประจำตัวผู้เสียภาษี (13-comb)
            new("Text1.10", m.LegalName),                       // 2.1 ชื่อสถานประกอบการ
            new("Text1.11", m.Building    ?? ""),               // 2.2 อาคาร
            new("Text1.12", m.RoomNo      ?? ""),               // ห้องเลขที่
            new("Text1.13", m.Floor       ?? ""),               // ชั้นที่
            new("Text1.14", m.Village     ?? ""),               // หมู่บ้าน
            new("Text1.15", m.HouseNo     ?? ""),               // เลขที่
            new("Text1.16", m.Moo         ?? ""),               // หมู่ที่
            new("Text1.17", m.Soi         ?? ""),               // ตรอก/ซอย
            new("Text1.19", m.Road        ?? ""),               // ถนน
            new("Text1.20", m.SubDistrict ?? ""),               // ตำบล/แขวง
            new("Text1.21", m.District    ?? ""),               // อำเภอ/เขต
            new("Text1.22", m.Province    ?? ""),               // จังหวัด
            new("Text1.26", Digits(m.PostalCode ?? "")),        // รหัสไปรษณีย์ (5-comb)
            new("Text1.24", m.Email       ?? ""),               // E-mail
            new("Text1.25", m.Website     ?? ""),               // Website
        };
        return RdAcroFormFiller.Render(
            Template("pp01_main.pdf"), fields, [], Cells("pp01_cells.json"));
    }

    internal static string Digits(string s) => new([.. s.Where(char.IsDigit)]);

    internal static byte[] Template(string file)
    {
        var asm = typeof(Pp01FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Test-visible loader for the embedded comb geometry (pp01_cells/pp09_cells).</summary>
    public static Dictionary<string, IReadOnlyList<double>> Cells(string file)
    {
        var asm = typeof(Pp01FormFiller).Assembly;
        using var s = asm.GetManifestResourceStream($"Accounting.Infrastructure.Pdf.Templates.{file}")
            ?? throw new InvalidOperationException($"Embedded '{file}' not found.");
        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(s)
            ?? [];
        return raw.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value);
    }
}

/// <summary>
/// Fills the official RD ภ.พ.09 AcroForm (pp09_main.pdf, 4pp) — v1 = page-1 identity header
/// ONLY, per docs/RD-Forms/pp09/fieldmap/pp09_map.md. ⚠️ `Text1.x` numbering is SCRAMBLED vs
/// visual order (e.g. Text1.6 = เลขที่, Text1.13 = ห้องเลขที่) — the map is ground truth.
/// `Radio Button2` (change items) is never ticked (non-sequential on-states + one exclusive
/// group despite the paper allowing multi-tick). E-mail (Text1.18) originally carried a 12-cell
/// comb flag + MaxLen=12 that no real address fits — the EMBEDDED template copy has that comb
/// flag stripped (one-time pymupdf edit, 2026-06-12) and the filler drops its cell geometry, so
/// the address overlays as plain text (visual-gate verified: full address, no truncation).
/// </summary>
public static class Pp09FormFiller
{
    public static byte[] Fill(VatRegIdentity m)
    {
        var fields = new List<RdField>
        {
            new("Text1.3",  m.LegalName),                       // 1. ชื่อผู้ประกอบการจดทะเบียน
            new("Text1.4",  Pp01FormFiller.Digits(m.TaxId)),    // เลขประจำตัวผู้เสียภาษี (13-comb)
            new("Text1.5",  m.LegalName),                       // ชื่อสถานประกอบการ
            new("Text1.7",  m.Building    ?? ""),               // อาคาร
            new("Text1.13", m.RoomNo      ?? ""),               // ห้องเลขที่ (scrambled!)
            new("Text1.12", m.Floor       ?? ""),               // ชั้นที่
            new("Text1.9",  m.Village     ?? ""),               // หมู่บ้าน
            new("Text1.6",  m.HouseNo     ?? ""),               // เลขที่ (scrambled!)
            new("Text1.15", m.Moo         ?? ""),               // หมู่ที่
            new("Text1.11", m.Soi         ?? ""),               // ตรอก/ซอย
            new("Text1.14", m.Road        ?? ""),               // ถนน
            new("Text1.8",  m.SubDistrict ?? ""),               // ตำบล/แขวง
            new("Text1.10", m.District    ?? ""),               // อำเภอ/เขต
            new("Text1.21", m.Province    ?? ""),               // จังหวัด
            new("Text1.26", Pp01FormFiller.Digits(m.PostalCode ?? "")), // รหัสไปรษณีย์ (5-comb)
            new("Text1.18", m.Email       ?? ""),               // E-mail — PLAIN overlay (see doc)
            new("Text1.19", m.Website     ?? ""),               // Website
        };
        var cells = Pp01FormFiller.Cells("pp09_cells.json");
        cells.Remove("Text1.18");   // 12-cell comb flag would letter-space the email — draw plain.
        return RdAcroFormFiller.Render(Template(), fields, [], cells);
    }

    private static byte[] Template() => Pp01FormFiller.Template("pp09_main.pdf");
}
