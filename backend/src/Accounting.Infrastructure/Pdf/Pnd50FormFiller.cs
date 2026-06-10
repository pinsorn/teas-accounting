namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Page-2 รายการที่ 1 การคำนวณภาษี figures, all derived by <c>Pnd50FilingService.BuildSheet</c>
/// (which enforces the ภ.ง.ด.50 §4 refuse-on-unrenderable guard before this record can exist).
/// Box numbers refer to the form's printed margin numbering (spec: pnd50-fieldmap-recon.md).
/// </summary>
public sealed record Pnd50Sheet(
    decimal BaseAmount,      // box 48-49 (Text661): TaxableProfit, or |TaxableBeforeLoss| on the loss path
    bool    IsLoss,          // Group5: false→Choice1 กำไรสุทธิ, true→Choice2 ขาดทุนสุทธิ
    decimal TaxComputed,     // box 50-51 (662) = CitComputation.TaxBeforeCredits
    decimal WhtCredit,       // box 54 (665) ภาษีหัก ณ ที่จ่าย
    decimal Pnd51Prepaid,    // box 55 (666) ภาษีที่ชำระแล้วตาม ภ.ง.ด.51
    decimal CreditsTotal,    // รวม (669) = 665 + 666 (663/664/667/668 are 0 in the v1 scope)
    decimal NetAmount,       // box 58-59 (670) = |TaxBeforeCredits − CreditsTotal|
    bool    PayMore,         // Group7/Group8 Choice1 ชำระเพิ่มเติม vs Choice2 ชำระไว้เกิน + the p1 pair
    decimal Surcharge,       // box 60 (671) — ม.67ตรี under-estimate penalty (0 when none)
    decimal TotalAmount,     // box 61-62 (672) = NetAmount + Surcharge (PayMore) / NetAmount (overpaid)
    bool    IsSme);          // Group21: false→Choice1 ทั่วไป, true→Choice2 + Group6 Choice1 SMEs

/// <summary>
/// Model for ภ.ง.ด.50 v1 (annual CIT return — page 1 header + page 2 รายการที่ 1).
/// Address block = same CompanyProfile source as ภ.ง.ด.51. Company type is fixed to
/// (1) บริษัท/ห้างฯ ตั้งขึ้นตามกฎหมายไทย (Group00) — TEAS targets Thai juristic companies.
/// </summary>
public sealed record Pnd50Model(
    string TaxId, string CompanyName,
    DateOnly PeriodStart, DateOnly PeriodEnd,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Website, string? Email,
    bool HasRelatedPartyOver200M,     // ม.71ทวิ: true→Group06 มี, false→Group07 ไม่มี/รายได้≤200M
    Pnd50Sheet Sheet);

/// <summary>
/// Fills the official RD ภ.ง.ด.50 AcroForm (v1: page 1 header + page 2 รายการที่ 1) and flattens
/// it via <see cref="RdAcroFormFiller"/>. Field map: docs/superpowers/specs/pnd50-fieldmap-recon.md
/// + docs/RD-Forms/pnd50/fieldmap/_pnd50_fields_p1.txt (rect+label join). Radios are selected by
/// their RENDER-CONFIRMED on-state names (docs/RD-Forms/pnd50/pnd50_radiomap.md) — never by index.
/// Comb boxes (taxid grid + every amount box) place per-char at the printed cell centres from the
/// embedded pnd50_cells.json (11 baht + 2 satang cells; the dash gap is not a cell).
/// </summary>
public static class Pnd50FormFiller
{
    public static byte[] Fill(Pnd50Model m)
    {
        var s = m.Sheet;
        var fields = new List<RdField>
        {
            // เลขประจำตัวผู้เสียภาษี — 13-digit printed grid (cellCenters geometry).
            new("1", new string((m.TaxId ?? "").Where(char.IsDigit).ToArray())),
            new("2", m.CompanyName),
            // รอบระยะเวลาบัญชี ตั้งแต่ (17/18/19) – ถึง (20/21/22); พ.ศ. = CE + 543.
            new("17", m.PeriodStart.Day.ToString("00")),
            new("18", m.PeriodStart.Month.ToString("00")),
            new("19", (m.PeriodStart.Year + 543).ToString()),
            new("20", m.PeriodEnd.Day.ToString("00")),
            new("21", m.PeriodEnd.Month.ToString("00")),
            new("22", (m.PeriodEnd.Year + 543).ToString()),
            // ที่ตั้งสำนักงาน (rect+label join, render-confirmed by the Task-5 raster):
            // 3=อาคาร 4=ห้องเลขที่ 5=ชั้นที่ · 6=หมู่บ้าน 7=เลขที่ 8=หมู่ที่ 9=ตรอก/ซอย ·
            // 10=แยก (unused) Text10.1=ถนน 11=ตำบล/แขวง · 12=อำเภอ/เขต 13=จังหวัด ·
            // 14=รหัสไปรษณีย์ 15=โทรศัพท์ (unused)
            new("3",        m.Building    ?? ""),
            new("4",        m.RoomNo      ?? ""),
            new("5",        m.Floor       ?? ""),
            new("6",        m.Village     ?? ""),
            new("7",        m.HouseNo     ?? ""),
            new("8",        m.Moo         ?? ""),
            new("9",        m.Soi         ?? ""),
            new("Text10.1", m.Road        ?? ""),
            new("11",       m.SubDistrict ?? ""),
            new("12",       m.District    ?? ""),
            new("13",       m.Province    ?? ""),
            new("14",       m.PostalCode  ?? ""),
            new("166.1",    m.Website     ?? ""),
            new("166.2",    m.Email       ?? ""),
        };

        // p2 รายการที่ 1 comb amounts — "{baht}{satang:00}" right-justified onto the 11+2 cell grid.
        void Amt(string name, decimal v)
        {
            var b  = Math.Truncate(v);
            var st = Math.Round((v - b) * 100m);
            fields.Add(new(name, $"{b:0}{st:00}", Right: true));
        }
        Amt("Text661", s.BaseAmount);     // 48-49 ฐาน (กำไรสุทธิ/ขาดทุนสุทธิ ตาม Group5)
        Amt("662",     s.TaxComputed);    // 50-51 ภาษีที่คำนวณได้
        Amt("665",     s.WhtCredit);      // 54    หัก (3) ภาษีหัก ณ ที่จ่าย
        Amt("666",     s.Pnd51Prepaid);   // 55    หัก (4) ภาษีที่ชำระแล้วตาม ภ.ง.ด.51
        Amt("669",     s.CreditsTotal);   // รวมหัก (1)-(6)
        Amt("670",     s.NetAmount);      // 58-59 คงเหลือภาษีที่ ชำระเพิ่มเติม/ไว้เกิน
        if (s.Surcharge > 0m)
            Amt("671", s.Surcharge);      // 60    บวกเงินเพิ่ม (ถ้ามี)
        Amt("672",     s.TotalAmount);    // 61-62 รวมภาษีที่ ชำระเพิ่มเติม/ไว้เกิน

        // p1 จำนวนเงิน — fill exactly ONE pair per the bottom-line sign (mirrors boxes 58-59/61-62).
        var p1Baht   = Math.Truncate(s.TotalAmount);
        var p1Satang = Math.Round((s.TotalAmount - p1Baht) * 100m);
        if (s.PayMore)
        {
            fields.Add(new("Text2000-1", p1Baht.ToString("0"),   Right: true));   // 30-31 ชำระเพิ่มเติม
            fields.Add(new("Text3",      p1Satang.ToString("00"), Right: true));
        }
        else
        {
            fields.Add(new("Text2000",   p1Baht.ToString("0"),   Right: true));   // ชำระไว้เกิน
            fields.Add(new("Text3-2",    p1Satang.ToString("00"), Right: true));
        }

        // Radios — ALL by render-confirmed on-state (pnd50_radiomap.md). Never tick by index here.
        var radios = new List<RdRadio>
        {
            new("Group1",  "Choice1"),                                  // (1) ยื่นปกติ
            new("Group00", "Choice1"),                                  // สถานภาพ (1) นิติบุคคลตามกฎหมายไทย
            new(m.HasRelatedPartyOver200M ? "Group06" : "Group07", "Choice1"),   // ม.71ทวิ — tick ONE group
            new("Group4",  "Choice1"),                                  // สกุลเงิน: บาท (v1 = THB only)
            new("Group5",  s.IsLoss ? "Choice2" : "Choice1"),           // ฐาน: กำไรสุทธิ / ขาดทุนสุทธิ
            new("Group7",  s.PayMore ? "Choice1" : "Choice2"),          // 4. คงเหลือ ชำระเพิ่มเติม/ไว้เกิน
            new("Group8",  s.PayMore ? "Choice1" : "Choice2"),          // 6. รวม ชำระเพิ่มเติม/ไว้เกิน
        };
        if (s.IsSme)
        {
            radios.Add(new("Group21", "Choice2"));                      // 2.(2) กรณีลดอัตราภาษี
            radios.Add(new("Group6",  "Choice1"));                      //       → SMEs
        }
        else
        {
            radios.Add(new("Group21", "Choice1"));                      // 2.(1) กรณีทั่วไป
        }

        return RdAcroFormFiller.Render(Template("pnd50_main.pdf"), fields, radios, CellCenters.Value);
    }

    // field name → printed cell-centre X (PDF points), loaded once from the embedded geometry resource.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<double>>> CellCenters = new(LoadCellCenters);

    /// <summary>Test-visible view of the embedded cell-centre geometry.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<double>> Cells => CellCenters.Value;

    private static IReadOnlyDictionary<string, IReadOnlyList<double>> LoadCellCenters()
    {
        var asm = typeof(Pnd50FormFiller).Assembly;
        using var s = asm.GetManifestResourceStream("Accounting.Infrastructure.Pdf.Templates.pnd50_cells.json")
            ?? throw new InvalidOperationException("Embedded pnd50_cells.json not found.");
        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(s)
            ?? new Dictionary<string, double[]>();
        return raw.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value);
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd50FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
