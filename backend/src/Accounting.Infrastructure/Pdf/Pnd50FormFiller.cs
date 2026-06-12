namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// p3 รายการที่ 2 ladder — SIGNED values internally; the filler prints absolute values and sets the
/// three sign radios (Group100 row 3, Group101 row 9, Group9 row 21). Rows are the form's printed
/// lines (margin numbers in comments); arithmetic invariants and the sign-flip renderability rule
/// are enforced by <c>Pnd50FilingService.BuildLadder</c> (boxes have no sign of their own).
/// </summary>
public sealed record Pnd50Ladder(
    decimal DirectRevenue,         // 1  (63)
    decimal CostOfSales,           // 2  (64) = รายการที่ 3 row 9 (0 — TEAS keeps no COGS/inventory)
    decimal GrossProfit,           // 3  (65-66) signed → Group100
    decimal OtherIncome,           // 4  (67) 0 in v2
    decimal Total5,                // 5  = 3.+4. (signed)
    decimal OtherExpenses,         // 6  (68) 0 in v2
    decimal Total7,                // 7  = 5.−6. (signed)
    decimal SellingAdminExpenses,  // 8  (69) = FY total expense
    decimal AccountingNetProfit,   // 9  (70-71) signed → Group101
    decimal IncomeAdditions,       // 10 (72) 0 in v2
    decimal DisallowedExpenses,    // 11 (73) = Σ positive ม.65ทวิ/ตรี adjustments
    decimal Total12,               // 12 signed = 9.+10.+11.
    decimal ExemptDeductions,      // 13 (74) = |Σ negative adjustments|
    decimal Total14,               // 14 signed = 12.−13. == CitComputation.TaxableBeforeLoss
    decimal LossCarryForward,      // 15 (75) = CitComputation.LossApplied (ม.65ตรี(12))
    decimal Total16,               // 16 signed = 14.−15.
    decimal Excess10Pct,           // 17 (75.1) 0 in v2
    decimal CharityExcess,         // 18 (76) 0 in v2
    decimal EducationExcess,       // 19 (77) 0 in v2
    decimal Total20,               // 20 (78) signed = 16.+17.+18.+19.
    decimal TaxableNetProfit);     // 21 signed → Group9 (TaxableProfit, or TaxableBeforeLoss when loss)

/// <summary>
/// p5 รายการที่ 7 รายจ่ายในการขายและบริหาร (boxes 110-129.1, column ③ only) — a PARTITION of the
/// FY per-account expense rows by the TEAS 4-digit account-code convention: 5400-5499→1(110)
/// พนักงาน · 5100-5199→6(115) ค่าเช่า · 5300-5349→9(118) โฆษณา/ส่งเสริมการขาย ·
/// 5350-5399→11(120) ค่าภาษีอากรอื่นๆ · 5200-5299→19(126) ค่าธรรมเนียมอื่นๆ · everything else
/// (incl. unparseable) →22(129) อื่นๆ. Lines with no mapped range print explicit 0. Built by
/// <c>Pnd50FilingService.BuildExpenseSchedule</c>, whose Total MUST equal the p3 ladder row 8
/// (SellingAdminExpenses) — รายการที่ 7 is the detail of that single ladder row.
/// </summary>
public sealed record Pnd50ExpenseSchedule(
    decimal Employee,            // 1  (110) 5400-5499 รายจ่ายเกี่ยวกับพนักงาน
    decimal DirectorComp,        // 2  (111) 0 — not tracked
    decimal Utilities,           // 3  (112) 0 — no mapped range
    decimal Travel,              // 4  (113) 0
    decimal Freight,             // 5  (114) 0
    decimal Rent,                // 6  (115) 5100-5199
    decimal Repairs,             // 7  (116) 0
    decimal Entertainment,       // 8  (117) 0 (add-back side lives in รายการที่ 8 ข้อ 2)
    decimal Marketing,           // 9  (118) 5300-5349
    decimal SbtTax,              // 10 (119) 0 — no SBT in TEAS
    decimal OtherTaxes,          // 11 (120) 5350-5399 (e.g. irrecoverable VAT)
    decimal FinanceCost,         // 12 (121) 0 — no mapped range (RD instructions ambiguity vs p4 ร.6 ข้อ 3)
    decimal Bookkeeping,         // 13 (121.1) 0
    decimal AuditFee,            // 14 (122) 0
    decimal PoliticalDonation,   // 15 (122.1) 0
    decimal CharityDonation,     // 16 (123) 0 — donations booked in GL land in 22; excess lives on ladder 18
    decimal EducationSport,      // 17 (124) 0 — same; ladder 19
    decimal Consulting,          // 18 (125) 0
    decimal OtherFees,           // 19 (126) 5200-5299
    decimal BadDebt,             // 20 (127) 0
    decimal Depreciation,        // 21 (128) 0
    decimal Other,               // 22 (129) catch-all for every unmapped account
    decimal DoubleDeduct,        // 23 (129.1) 0 — TEAS records no double-deduction expenses
    decimal Total);              // 24 = Σ 1-23 == ladder row 8

/// <summary>
/// p5 รายการที่ 8 รายจ่ายที่ไม่ให้ถือเป็นรายจ่ายตามประมวลรัษฎากร (boxes 130-134.1, column ③) —
/// the POSITIVE `tax.cit_adjustments` lines classified by LegalRefCode (exact, whitespace-removed)
/// or Label keyword; remainder →6(134.1) อื่นๆ. Built by
/// <c>Pnd50FilingService.BuildDisallowedSchedule</c>, whose Total MUST equal the p3 ladder
/// row 11 (DisallowedExpenses = Σ positive adjustments).
/// </summary>
public sealed record Pnd50DisallowedSchedule(
    decimal IncomeTax,           // 1 (130) ม.65ตรี(6) / ภาษีเงินได้
    decimal Entertainment,       // 2 (131) ม.65ตรี(4) / ค่ารับรอง
    decimal BadDebt,             // 3 (132) หนี้สูญ
    decimal Provisions,          // 4 (133) ม.65ตรี(1) / เงินสำรอง — ⚠️ box field is Text35.2011
    decimal FromItem7Line23,     // 5 (134) 0 — pairs with รายการที่ 7 ข้อ 23 (always 0 in C-D)
    decimal Other,               // 6 (134.1) remainder
    decimal Total);              // 7 = Σ 1-6 == ladder row 11

/// <summary>
/// p6 งบแสดงฐานะการเงิน boxes, classified from BalanceSheetReport rows by the TEAS account-code
/// convention (4-digit): 1110-1129→140, 1130-1139→141, 1140-1149→142, other 1000-1499→143,
/// 1500-1999→148 · 2110→150, other 2000-2499→152, 2500-2999→154 · 3100-3199→156,
/// 3200-3299→158-159 (+CurrentPeriodEarnings), other 3xxx→157. Form lines 144-147/149/151/153
/// have no classified source accounts and print 0; unparseable codes land in the section's
/// "อื่น (นอกจากที่ระบุ)" line. RetainedEarnings is SIGNED → Group91. The mapper
/// (<c>Pnd50FilingService.MapBalanceSheet</c>) asserts the totals reproduce the report's.
/// </summary>
public sealed record Pnd50BalanceSheetBoxes(
    decimal CashAndEquivalents,         // 140
    decimal TradeReceivables,           // 141
    decimal Inventory,                  // 142
    decimal OtherCurrentAssets,         // 143
    decimal OtherNonCurrentAssets,      // 148
    decimal TotalAssets,                // รวมสินทรัพย์
    decimal TradePayables,              // 150
    decimal OtherCurrentLiabilities,    // 152
    decimal OtherNonCurrentLiabilities, // 154
    decimal TotalLiabilities,           // รวมหนี้สิน
    decimal PaidUpShareCapital,         // 156
    decimal OtherEquity,                // 157
    decimal RetainedEarnings,           // 158-159 signed → Group91
    decimal TotalEquity,                // 160
    decimal TotalLiabilitiesAndEquity); // 161

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
/// Model for ภ.ง.ด.50 v2 (annual CIT return — p1 header + p2 รายการที่ 1 + p3 รายการที่ 2/3 +
/// p6 งบฐานะ). Address block = same CompanyProfile source as ภ.ง.ด.51. Company type is fixed to
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
    Pnd50Sheet Sheet,
    Pnd50Ladder Ladder,
    Pnd50BalanceSheetBoxes BalanceSheet);

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

        // p3 รายการที่ 2 — column ③ (รวม) ONLY: the form rubric (recon cont.89) says กรณีทั่วไป/
        // ลดอัตรา fill ช่อง ③ ช่องเดียว; columns ①② are for exempt-business filers. Every row is
        // printed (zero → explicit 0) — v2 never leaves a ladder box blank-as-lie. Signed rows
        // print their absolute value; the sign lives in Group100/Group101/Group9 below.
        var L = m.Ladder;
        void Lad(string name, decimal v) => Amt(name, Math.Abs(v));
        Lad("Text17.4",  L.DirectRevenue);          // 1  (63)
        Lad("Text17.7",  L.CostOfSales);            // 2  (64) = รายการที่ 3 row 9
        Lad("Text17.10", L.GrossProfit);            // 3  (65-66)
        Lad("Text17.13", L.OtherIncome);            // 4  (67)
        Lad("Text17.16", L.Total5);                 // 5
        Lad("Text17.19", L.OtherExpenses);          // 6  (68)
        Lad("Text17.22", L.Total7);                 // 7
        Lad("Text17.25", L.SellingAdminExpenses);   // 8  (69)
        Lad("Text17.28", L.AccountingNetProfit);    // 9  (70-71)
        Lad("Text17.31", L.IncomeAdditions);        // 10 (72)
        Lad("Text17.34", L.DisallowedExpenses);     // 11 (73)
        Lad("Text17.37", L.Total12);                // 12
        Lad("Text17.40", L.ExemptDeductions);       // 13 (74)
        Lad("Text17.43", L.Total14);                // 14
        Lad("Text20",    L.LossCarryForward);       // 15 (75)
        Lad("Text23",    L.Total16);                // 16
        Lad("Text26",    L.Excess10Pct);            // 17 (75.1)
        Lad("Text29",    L.CharityExcess);          // 18 (76)
        Lad("Text32",    L.EducationExcess);        // 19 (77)
        Lad("Text35.1",  L.Total20);                // 20 (78)
        Lad("Text35.2",  L.TaxableNetProfit);       // 21 → flows to p2 box 48-49

        // p3 รายการที่ 3 ต้นทุนขาย rows 1-9 (boxes 79-85) — all explicit zeros, consistent with
        // ladder row 2 = 0: TEAS books carry no inventory/COGS section at all.
        foreach (var n in new[] { "Text35.5", "Text35.8", "Text35.11", "Text35.14", "Text35.17",
                                  "Text35.20", "Text35.23", "Text35.26", "Text35.29" })
            Amt(n, 0m);

        // p6 งบแสดงฐานะการเงิน. NOT filled (system cannot know them): 155 ทุนจดทะเบียน
        // (no registered-capital field; PaidUpCapital ≠ ทุนจดทะเบียน), Group92/93 auditor
        // opinion, and the 162.x attachment-count boxes — those stay for the filer.
        var B = m.BalanceSheet;
        Amt("Text35.210", B.CashAndEquivalents);        // 140
        Amt("Text35.211", B.TradeReceivables);          // 141
        Amt("Text35.212", B.Inventory);                 // 142
        Amt("Text35.213", B.OtherCurrentAssets);        // 143
        Amt("Text35.214", 0m);                          // 144 เงินให้กู้ยืมแก่กิจการที่เกี่ยวข้อง
        Amt("Text35.215", 0m);                          // 145 ที่ดินและอาคาร-สุทธิ
        Amt("Text35.216", 0m);                          // 146 ทรัพย์สินอื่นหักค่าเสื่อมแล้ว
        Amt("Text35.217", 0m);                          // 147 สิทธิการเช่า/สิทธิการใช้
        Amt("Text35.218", B.OtherNonCurrentAssets);     // 148
        Amt("Text35.219", B.TotalAssets);               // รวมสินทรัพย์
        Amt("Text35.220", 0m);                          // 149 เงินเบิกเกินบัญชี/กู้สั้นจากสถาบันการเงิน
        Amt("Text35.221", B.TradePayables);             // 150
        Amt("Text35.222", 0m);                          // 151 เงินกู้ยืม
        Amt("Text35.223", B.OtherCurrentLiabilities);   // 152
        Amt("Text35.224", 0m);                          // 153 เงินกู้ยืมระยะยาว
        Amt("Text35.225", B.OtherNonCurrentLiabilities);// 154
        Amt("Text35.226", B.TotalLiabilities);          // รวมหนี้สิน
        Amt("Text35.2241", B.PaidUpShareCapital);       // 156
        Amt("Text35.2251", B.OtherEquity);              // 157
        Amt("Text35.2261", Math.Abs(B.RetainedEarnings)); // 158-159 (sign → Group91)
        Amt("Text35.2242", B.TotalEquity);              // 160
        Amt("Text35.2252", B.TotalLiabilitiesAndEquity);// 161

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

        // p3/p6 sign radios — render-confirmed on-states (cont.89). ⚠️ Group100/101 use raw
        // '0'/'1' on-state names, not ChoiceN.
        radios.Add(new("Group100", L.GrossProfit         < 0m ? "1" : "0"));        // 3. กำไร/ขาดทุนขั้นต้น
        radios.Add(new("Group101", L.AccountingNetProfit < 0m ? "1" : "0"));        // 9. กำไร/ขาดทุนสุทธิ (บัญชี)
        radios.Add(new("Group9",   L.TaxableNetProfit    < 0m ? "Choice2" : "Choice1")); // 21.
        radios.Add(new("Group91",  B.RetainedEarnings    < 0m ? "Choice2" : "Choice1")); // p6 (3) กำไร/ขาดทุนสะสม

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
