using System.Globalization;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Model for ภ.ง.ด.51 (ม.67ทวิ mid-year CIT prepayment). Period dates are the FULL fiscal year
/// (form instruction: "กรอกวันเริ่มต้นและวันสุดท้ายของรอบระยะเวลาบัญชี"). Tax is the half-year
/// prepayment computed by <see cref="Accounting.Domain.Tax.CitCalculator.HalfYearPrepayment"/>.
/// </summary>
public sealed record Pnd51Model(
    string EmployerTaxId,
    string EmployerName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    decimal HalfYearTax,
    DateOnly FilingDate,
    // ── page-2 การคำนวณภาษี worksheet (Method A); null ⇒ leave page 2 blank ──
    Pnd51Worksheet? Worksheet = null);

/// <summary>
/// Method-A page-2 worksheet values, all derived by <c>Pnd51FilingService</c> from the CIT engine + H1 P&amp;L.
/// <paramref name="RevenueFullYear"/>/<paramref name="ExpenseFullYear"/> are null on the caller-override
/// estimate path (only the net figure exists → boxes 51/52/53-54 stay blank, the worksheet starts at 57-58).
/// Emitted only for a footing, clean, general-rate case (see <c>Pnd51FilingService.BuildWorksheet</c>).
/// </summary>
public sealed record Pnd51Worksheet(
    decimal? RevenueFullYear, decimal? ExpenseFullYear,   // boxes 51 / 52 (default path only)
    decimal EstimatedNetProfit,                            // box 53-54 = 57-58 (no c/f, no exempt)
    decimal HalfEstimatedProfit,                           // box 59-60 / 28-29
    decimal TaxComputed,                                   // box 32
    decimal WhtH1,                                         // box 33 / รวม 35
    decimal NetPayable,                                    // box 36-37 / 39-40
    bool IsSme);                                           // selects the rate radio (Task 5)

/// <summary>
/// Fills the official RD ภ.ง.ด.51 AcroForm (single page + worksheet page 2) from CIT data
/// and flattens it, via the generic <see cref="RdAcroFormFiller"/>.
///
/// Field map decoded from /Rect dump (Z:/temp/pnd51_field_dump.txt) + label join
/// (Z:/temp/pnd51_labels.txt). Key confirmed fields:
///   TaxID=Text1.1 · CompanyName=Text1.2
///   PeriodStart D/M/Y=Text1.18/19/20 · PeriodEnd D/M/Y=Text1.21/22/23
///   อาคาร=1.6 · ห้องเลขที่=1.7 · ชั้นที่=1.8 · หมู่บ้าน=1.9 · เลขที่=1.10
///   หมู่ที่=1.11 · ตรอก/ซอย=1.12 · ถนน=1.13 · ตำบล/แขวง=1.14
///   อำเภอ/เขต=1.15 · จังหวัด=1.16 · รหัสไปรษณีย์=1.17
///   ภาษีเพิ่มเติม(บาท)=Text1.25 · (สตางค์)=Text1.26
///   วันที่ยื่น D/M/Y=Text3.5/3.6/3.7
///   Radio Button1 idx0=ยื่นปกติ · Radio Button15 idx0=กรณีที่1 · Radio Button14 idx0=THB
/// </summary>
public static class Pnd51FormFiller
{
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");

    public static byte[] Fill(Pnd51Model m)
    {
        var baht   = Math.Truncate(m.HalfYearTax);
        var satang = Math.Round((m.HalfYearTax - baht) * 100);

        var fields = new List<RdField>
        {
            // เลขประจำตัวผู้เสียภาษีอากร — 13 digits; placed at the real cell-centres via cellCenters geometry.
            new("Text1.1",  new string((m.EmployerTaxId ?? "").Where(char.IsDigit).ToArray())),
            new("Text1.2",  m.EmployerName),
            // รอบระยะเวลาบัญชี — full fiscal year (not just H1)
            new("Text1.18", m.PeriodStart.Day.ToString("00")),
            new("Text1.19", m.PeriodStart.Month.ToString("00")),
            new("Text1.20", (m.PeriodStart.Year + 543).ToString()),
            new("Text1.21", m.PeriodEnd.Day.ToString("00")),
            new("Text1.22", m.PeriodEnd.Month.ToString("00")),
            new("Text1.23", (m.PeriodEnd.Year + 543).ToString()),
            // ที่ตั้งสำนักงาน
            // Address remapped after a label-join (Z:/temp/pnd51_labels.txt; topY = pageH - Y2):
            // the whole block was off by ~one field. building=1.3 room=1.4 floor=1.5 village=1.6
            // houseNo=1.7 moo=1.8 soi=1.9 (yaek=1.10 unused) road=1.11 subDistrict=1.12
            // district=1.13 province=1.14 postal=1.15 (phone=1.16 website=1.17 unused).
            new("Text1.3",  m.Building    ?? ""),
            new("Text1.4",  m.RoomNo      ?? ""),
            new("Text1.5",  m.Floor       ?? ""),
            new("Text1.6",  m.Village     ?? ""),
            new("Text1.7",  m.HouseNo     ?? ""),
            new("Text1.8",  m.Moo         ?? ""),
            new("Text1.9",  m.Soi         ?? ""),
            new("Text1.11", m.Road        ?? ""),
            new("Text1.12", m.SubDistrict ?? ""),
            new("Text1.13", m.District    ?? ""),
            new("Text1.14", m.Province    ?? ""),
            new("Text1.15", m.PostalCode  ?? ""),
            // Row 75 จำนวนเงิน — ภาษีที่ชำระเพิ่มเติม (ม.67ทวิ method A)
            new("Text1.25", baht.ToString("0", Th), Right: true),    // comb: digits only, RIGHT-justified, NO comma
            new("Text1.26", satang.ToString("00"), Right: true),     // satang aligned to its decimal cells
            // วันที่ยื่น
            new("Text3.5", m.FilingDate.Day.ToString("00")),
            new("Text3.6", m.FilingDate.Month.ToString("00")),
            new("Text3.7", (m.FilingDate.Year + 543).ToString()),
        };

        var radios = new List<RdRadio>
        {
            // Radio Button1: idx0=ยื่นปกติ (x≈372) idx1=ยื่นเพิ่มเติม (x≈437) — sort: y desc, x asc
            new("Radio Button1",  0),
            // NOTE: the old "Radio Button15" (กรณีที่1) / "Radio Button14" (THB) are NOT page-1
            // widgets — they were silent no-ops. The กรณีที่1/2 + currency selectors live on page 2;
            // map them when page 2 is wired (see _Pnd51Diag page-2 dump).
        };

        // Every box on ภ.ง.ด.51 has a NON-uniform printed grid (grouped cells + dash gaps), so the generic
        // equal-division comb drifts. The filler places each char at the real printed cell-centre, taken from
        // the embedded pnd51_cells.json (field name → cell-centre X, extracted once from the template dividers).
        return RdAcroFormFiller.Render(Template("pnd51_main.pdf"), fields, radios, CellCenters.Value);
    }

    // field name → printed cell-centre X (PDF points), loaded once from the embedded geometry resource.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<double>>> CellCenters = new(LoadCellCenters);

    /// <summary>Test-visible view of the embedded page cell-centre geometry.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<double>> Cells => CellCenters.Value;

    private static IReadOnlyDictionary<string, IReadOnlyList<double>> LoadCellCenters()
    {
        var asm = typeof(Pnd51FormFiller).Assembly;
        using var s = asm.GetManifestResourceStream("Accounting.Infrastructure.Pdf.Templates.pnd51_cells.json")
            ?? throw new InvalidOperationException("Embedded pnd51_cells.json not found.");
        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(s)
            ?? new Dictionary<string, double[]>();
        return raw.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value);
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd51FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
