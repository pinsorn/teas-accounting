using System.Globalization;
using System.Text;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// cont.82.1 P2 — pure builder for the RD WHT batch-upload file (รูปแบบข้อมูล FORMAT กลาง V2.0,
/// ปรับปรุง 16/06/2568). No DB, no I/O — fully unit-testable against golden strings.
///
/// Format rules (identical for ภ.ง.ด.3 / 3ก / 53):
///  • UTF-8 (V2.0 note #6 — NOT TIS-620). Fields separated by Pipe '|'; no leading/trailing
///    pipe; empty field = two adjacent pipes. Record terminator = CR/LF.
///  • Section 1 = exactly one HEADER row (first field 'H', 25 fields).
///  • Section 2 = DETAIL rows (first field 'D', 38 fields); one per payee SEQ_NO carrying
///    up to 3 income triples. A 4th income for the same payee starts a new SEQ_NO.
///  • Numeric N(15,2): always 2 dp, no thousands comma, sign-safe; empty → "0.00".
///  • Dates: Buddhist year (พ.ศ.), DDMMYYYY; empty → "00000000".
///  • Forbidden chars in any field: * + / \ ! $ % # & @ , ' " and pipe / CR / LF — stripped.
/// </summary>
public static class WhtBatchFormat
{
    public const string Separator = "|";
    public const string RecordTerminator = "\r\n";
    private const int MaxIncomePerRow = 3;

    // The income triple for one payment of a withholding type.
    public sealed record Income(
        DateOnly PaidDate, decimal RatePercent, decimal PaidAmount,
        decimal TaxAmount, string IncomeType, string PayCondition);

    public sealed record Payee(
        string TaxId,          // PIN (PND3) / NID (PND53), 13 digits
        string TitleName,      // "" for corporate; "-" or prefix for individual
        string FirstName,      // full entity / person name
        string LastName,       // usually ""
        string BranchNo,       // payer branch of the income payment (000000 = HQ)
        IReadOnlyList<Income> Incomes);

    public sealed record Header(
        string TaxType,        // "PND53" | "PND3" | "PND3A"
        string PayerTaxId,     // company NID (13)
        string PayerBranch,    // company BRANCH_NO (6) — 000000 = HQ
        string DeptName,       // DEPT_NAME (e.g. สำนักงานใหญ่)
        int Period,            // yyyymm
        bool SectionA,         // PND53: ม.3 เตรส | PND3: ม.3 เตรส
        bool SectionB,         // PND53: ม.65 จัตวา | PND3: ม.48 ทวิ
        bool SectionC,         // PND53: ม.69 ทวิ | PND3: ม.50(3)(4)(5)
        string BranchType,     // "V" / "S" / ""
        string? UserId);       // รหัสลงทะเบียน e-Filing (config or blank)

    /// <summary>Build the file body (UTF-8) from a header + the period's payee groups.</summary>
    public static string Build(Header h, IReadOnlyList<Payee> payees)
    {
        // DETAIL rows first so the header totals/counts are exact.
        var detail = new StringBuilder();
        int seq = 0;
        decimal totIncome = 0m, totTax = 0m;

        foreach (var p in payees)
            foreach (var chunk in p.Incomes.Chunk(MaxIncomePerRow))
            {
                seq++;
                foreach (var inc in chunk) { totIncome += inc.PaidAmount; totTax += inc.TaxAmount; }
                detail.Append(DetailRow(p, chunk, seq)).Append(RecordTerminator);
            }

        var header = HeaderRow(h, recordCount: seq, totalIncome: totIncome, totalTax: totTax);
        return header + RecordTerminator + detail.ToString();
    }

    public static byte[] BuildBytes(Header h, IReadOnlyList<Payee> payees) =>
        // UTF-8 WITHOUT BOM — RD Prep expects raw UTF-8, a BOM corrupts the first field.
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Build(h, payees));

    /// <summary>RD filename: TAX_TYPE_NID_BRANCH_TAXYEAR(พ.ศ.)_TAXMONTH_FORMTYPE_seq.txt.</summary>
    public static string FileName(Header h, string formType = "00", string submission = "00")
    {
        var (month, beYear) = MonthBeYear(h.Period);
        return $"{h.TaxType}_{h.PayerTaxId}_{Pad6(h.PayerBranch)}_{beYear:0000}_{month:00}_{formType}_{submission}.txt";
    }

    private static string HeaderRow(Header h, int recordCount, decimal totalIncome, decimal totalTax)
    {
        var (month, beYear) = MonthBeYear(h.Period);
        var fields = new[]
        {
            "H",
            "0000",                                // SENDER_ID — self-filing
            Digits(h.PayerTaxId, 13),              // SENDER_NID
            Pad6(h.PayerBranch),                   // SENDER_BRANCH
            "1",                                   // SENDER_ROLE — ผู้หักภาษี ณ ที่จ่าย
            San(h.TaxType, 8),                     // TAX_TYPE
            Digits(h.PayerTaxId, 13),              // NID (payer = filer here)
            Pad6(h.PayerBranch),                   // BRANCH_NO
            San(h.DeptName, 80),                   // DEPT_NAME
            Flag(h.SectionA),                      // SECTION3
            Flag(h.SectionB),                      // SECTION65 / SECTION48
            Flag(h.SectionC),                      // SECTION69 / SECTION50
            "0",                                   // LTO — not a large taxpayer
            month.ToString("00", Inv),             // TAX_MONTH
            beYear.ToString("0000", Inv),          // TAX_YEAR (พ.ศ.)
            San(h.BranchType, 1),                  // BRANCH_TYPE
            "00",                                  // FORM_TYPE — ยื่นปกติ
            recordCount.ToString(Inv),             // TOT_NUM
            N(totalIncome),                        // TOT_AMT
            N(totalTax),                           // TOT_TAX
            N(0m),                                 // SUR_AMT — เงินเพิ่ม (none on normal filing)
            N(totalTax),                           // GTOT_TAX = TOT_TAX + SUR_AMT
            N(totalTax),                           // TRANS_AMT — assume full remittance
            San(h.UserId ?? "", 20),               // USER_ID
            "2",                                   // FORM_FLAG — ยื่นแบบผ่านอินเทอร์เน็ต
        };
        return string.Join(Separator, fields);
    }

    private static string DetailRow(Payee p, IReadOnlyList<Income> incomes, int seq)
    {
        var fields = new List<string>(38)
        {
            "D",
            seq.ToString(Inv),                     // SEQ_NO
            Pad6(p.BranchNo),                      // BRANCH_NO (payer branch)
            Digits(p.TaxId, 13),                   // PIN / NID
            "0000000000",                          // TIN (10) — legacy id, not held
            San(p.TitleName, 100),                 // TITLE_NAME
            San(p.FirstName, 100),                 // FNAME
            San(p.LastName, 80),                   // SNAME
        };

        // Three income triples; pad missing with blanks/zeros per the spec.
        for (int i = 0; i < MaxIncomePerRow; i++)
        {
            if (i < incomes.Count)
            {
                var inc = incomes[i];
                fields.Add(Date(inc.PaidDate));            // PAID_DATEi
                fields.Add(N(inc.RatePercent));           // TAX_RATEi
                fields.Add(N(inc.PaidAmount));            // PAID_AMTi
                fields.Add(N(inc.TaxAmount));             // TAX_AMTi
                fields.Add(San(inc.IncomeType, 100));     // INC_TYPE_PNDi
                fields.Add(San(inc.PayCondition, 1));     // PAY_CONi
            }
            else
            {
                // Empty triple. Date → 00000000, amounts → 0.00, text → blank.
                fields.Add("00000000");
                fields.Add(N(0m));
                fields.Add(N(0m));
                fields.Add(N(0m));
                fields.Add("");
                fields.Add("");
            }
        }

        // Address block (12 fields). PND53 = all Optional → emit blank; Vendor has no
        // structured address. (PND3 would require these populated — gated, see spec §4.)
        for (int i = 0; i < 12; i++) fields.Add("");

        return string.Join(Separator, fields);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static (int month, int beYear) MonthBeYear(int period)
        => (period % 100, period / 100 + 543);

    private static string Flag(bool b) => b ? "1" : "0";

    /// <summary>N(15,2): 2 dp, no comma, sign-safe; null/empty handled by caller as 0m.</summary>
    public static string N(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv);

    /// <summary>DDMMYYYY in Buddhist year. CE→BE boundary conversion (internal stays CE).</summary>
    public static string Date(DateOnly d) =>
        $"{d.Day:00}{d.Month:00}{d.Year + 543:0000}";

    private static string Pad6(string? branch)
    {
        var s = new string((branch ?? "").Where(char.IsDigit).ToArray());
        if (s.Length == 0) return "000000";
        return s.Length >= 6 ? s[^6..] : s.PadLeft(6, '0');
    }

    /// <summary>Keep only digits, then take the trailing <paramref name="len"/> (a tax id may
    /// arrive with separators); blank stays blank for the M/O guard upstream to catch.</summary>
    private static string Digits(string? raw, int len)
    {
        var s = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return s.Length > len ? s[..len] : s;
    }

    /// <summary>Strip RD-forbidden characters + pipe/CR/LF, collapse whitespace, trim to max.</summary>
    internal static string San(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (Forbidden.Contains(ch)) continue;
            if (ch == '\r' || ch == '\n' || ch == '\t') { sb.Append(' '); continue; }
            sb.Append(ch);
        }
        var s = sb.ToString().Trim();
        return s.Length > max ? s[..max] : s;
    }

    // RD V2.0 note #10 forbidden set + the structural pipe delimiter.
    private static readonly HashSet<char> Forbidden =
        new("*+/\\!$%#&@,'\"|".ToCharArray());
}
