using System.Globalization;
using System.Text;
using Accounting.Application.Payroll;

namespace Accounting.Infrastructure.Payroll;

/// <summary>
/// P-D #4 — pure builder for the SSO สปส.1-10 e-payment upload TEXT file (ระบบ e-Service
/// "ส่งข้อมูลเงินสมทบ"). No DB, no I/O — fully unit-testable. Consumes a <see cref="SsoMonthlyModel"/>.
///
/// Layout = the documented 135-char fixed-width สปส.1-10 file (HEADER record "1" + one DETAIL record
/// "2" per insured person; both records sum to exactly 135). Verified field-by-field against the
/// `docs/SSO-Forms/spec/` Q&amp;A (a filled BusinessPlus form + Nimitr blog + vendor specs):
///   • ✅ ค่าจ้าง column = ACTUAL (un-capped) wage; the clamp lives only in the contribution.
///   • ✅ dates/period use a 2-digit BE (พ.ศ.) year.
///   • ✅ employer account = the 10-digit SSO registration number (NOT the RD tax id).
/// ⚠️ Remaining items still "verify on a real upload" (kept as the single-point constants below):
///   • ENCODING TIS-620 / Windows-874 (single-byte Thai) — wired in <see cref="BuildBytes"/>;
///   • amounts as 2-implied-decimals ×100 (สตางค์), zero-filled — see <see cref="Amt"/>;
///   • คำนำหน้า numeric code 001/002/003 — see <see cref="PrefixCode"/>;
///   • อัตรา "0500" = 5.00% — see <see cref="RateField"/>.
/// (Contributions are emitted as POSTED on the payslip — kept consistent with the GL; the form's
/// ≥50-สตางค์-rounds-up rule rarely bites and would desync from the books, so it is not re-applied here.)
/// </summary>
public static class SpsBatchFormat
{
    public const string RecordTerminator = "\r\n";
    public const int RecordLength = 135;

    // TIS-620 / Windows-874 (single-byte Thai). A 30/35/45-BYTE Thai name field can't hold UTF-8 Thai
    // (3 bytes/char), so the file is single-byte by construction. Verify on a real upload.
    public const int CodePage = 874;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Build the สปส.1-10 file body (one HEADER "1" line + one DETAIL "2" line per insured
    /// person). The 10-digit SSO employer account comes from the model (blank → zeros).</summary>
    public static string Build(SsoMonthlyModel m)
    {
        var sb = new StringBuilder();
        sb.Append(HeaderRecord(m)).Append(RecordTerminator);
        foreach (var l in m.Lines)
            sb.Append(DetailRecord(l)).Append(RecordTerminator);
        return sb.ToString();
    }

    private static bool _providerReady;

    /// <summary>Encode the file as TIS-620 / Windows-874 (single-byte Thai), no BOM — the SSO e-Service
    /// convention. Registers the code-pages provider once (TIS-620 is not built into .NET Core).</summary>
    public static byte[] BuildBytes(SsoMonthlyModel m)
    {
        if (!_providerReady) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _providerReady = true; }
        return Encoding.GetEncoding(CodePage).GetBytes(Build(m));
    }

    /// <summary>Download filename (SSO mandates none): sps1-10_YYYYMM(CE).txt.</summary>
    public static string FileName(SsoMonthlyModel m) =>
        $"sps1-10_{m.PeriodYearCE:0000}{m.PeriodMonth:00}.txt";

    // ── HEADER record (type "1"), 135 chars ────────────────────────────────────────────────
    //  1:1  2:10 3:6  4:6     5:4   6:45  7:4    8:6    9:15  10:14 11:12 12:12  = 135
    private static string HeaderRecord(SsoMonthlyModel m)
    {
        var s = string.Concat(
            "1",                                                 //  1 record type
            Digits(m.EmployerAccountNo, 10),                     //  2 เลขที่บัญชีนายจ้าง (SSO reg no)
            BranchSeq(m.BranchCode),                             //  3 ลำดับที่สาขา (6)
            DateDdMmYy(m.PayDate),                               //  4 วันที่ชำระเงิน ddMMyy (BE)
            PeriodMmYy(m.PeriodMonth, m.PeriodYearCE),           //  5 งวดค่าจ้าง MMyy (BE)
            Txt(m.EmployerName, 45),                             //  6 ชื่อสถานประกอบการ
            RateField,                                           //  7 อัตราเงินสมทบ "0500"
            IntField(m.EmployeeCount, 6),                        //  8 จำนวนผู้ประกันตน
            Amt(m.TotalWage, 15),                                //  9 ค่าจ้างรวม (actual)
            Amt(m.GrandTotalContribution, 14),                   // 10 เงินสมทบรวม (=11+12)
            Amt(m.TotalEmployeeContribution, 12),                // 11 ส่วนผู้ประกันตน
            Amt(m.TotalEmployerContribution, 12));               // 12 ส่วนนายจ้าง
        return Fit(s);
    }

    // ── DETAIL record (type "2"), 135 chars ────────────────────────────────────────────────
    //  1:1  2:13 3:3  4:30 5:35 6:14 7:12 8:27(filler) = 135
    private static string DetailRecord(SsoLine l)
    {
        var s = string.Concat(
            "2",                                                 //  1 record type
            Digits(l.NationalId, 13),                            //  2 เลขประจำตัวประชาชน
            PrefixCode(l.Title),                                 //  3 คำนำหน้า (numeric code)
            Txt(l.FirstName, 30),                                //  4 ชื่อ
            Txt(l.LastName, 35),                                 //  5 ชื่อสกุล
            Amt(l.Wage, 14),                                     //  6 ค่าจ้างที่จ่ายจริง (actual, un-capped)
            Amt(l.EmployeeContribution, 12),                     //  7 เงินสมทบผู้ประกันตน
            new string(' ', 27));                                //  8 filler
        return Fit(s);
    }

    // ── field formatters (every "verify on upload" choice lives here, so a confirmed sample = a local edit) ──

    // Amount: 2 implied decimals (×100 สตางค์, no decimal point), right-justified, zero-filled.
    public static string Amt(decimal value, int width)
    {
        var satang = (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
        var s = Math.Abs(satang).ToString(Inv);
        return s.Length >= width ? s[^width..] : s.PadLeft(width, '0');
    }

    // Right-justified, zero-filled integer (counts).
    public static string IntField(int value, int width)
    {
        var s = Math.Abs(value).ToString(Inv);
        return s.Length >= width ? s[^width..] : s.PadLeft(width, '0');
    }

    // Left-justified text, space-padded right, truncated to width (single-byte char count == TIS-620 byte).
    public static string Txt(string? value, int width)
    {
        var s = (value ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        return s.Length >= width ? s[..width] : s.PadRight(width, ' ');
    }

    // Keep only digits, take the LAST <len>, zero-pad on the left (ids/accounts).
    public static string Digits(string? raw, int len)
    {
        var s = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (s.Length > len) s = s[^len..];
        return s.PadLeft(len, '0');
    }

    // ลำดับที่สาขา: BranchCode is the 5-digit RD branch (00000 = HQ); pad to the 6-wide SSO field.
    private static string BranchSeq(string? branchCode)
    {
        var s = new string((branchCode ?? "").Where(char.IsDigit).ToArray());
        if (s.Length > 6) s = s[^6..];
        return s.PadLeft(6, '0');
    }

    // ddMMyy with a 2-digit BE year (พ.ศ.) — verified vs the Nimitr blog (150562 = 15 พ.ค. 2562).
    public static string DateDdMmYy(DateOnly d) =>
        $"{d.Day:00}{d.Month:00}{(d.Year + 543) % 100:00}";

    // งวดค่าจ้าง MMyy with a 2-digit BE year.
    private static string PeriodMmYy(int month, int yearCe) =>
        $"{month:00}{(yearCe + 543) % 100:00}";

    // อัตราเงินสมทบ: "0500" = 5.00% (rate ×100, 4 wide). Verify on upload.
    private const string RateField = "0500";

    // คำนำหน้า → SSO numeric prefix code (3-digit, zero-padded): 001=นาย 002=นาง 003=นางสาว
    // 004=เด็กชาย 005=เด็กหญิง 006=ว่าที่ ร.ต. · others/unknown → 099. Verify on upload.
    public static string PrefixCode(string? title)
    {
        var t = (title ?? "").Replace(" ", "").Trim();
        return t switch
        {
            "นาย" => "001",
            "นาง" => "002",
            "นางสาว" => "003",
            "เด็กชาย" => "004",
            "เด็กหญิง" => "005",
            "ว่าที่ร.ต." or "ว่าที่ร.ต.หญิง" => "006",
            "" => "099",
            _ => "099",
        };
    }

    // Guard the fixed-width invariant: every record is exactly RecordLength chars.
    private static string Fit(string s) =>
        s.Length == RecordLength ? s
        : s.Length > RecordLength ? s[..RecordLength]
        : s.PadRight(RecordLength, ' ');
}
