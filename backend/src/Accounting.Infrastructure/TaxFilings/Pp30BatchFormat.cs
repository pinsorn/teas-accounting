using System.Globalization;
using System.Text;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// ภ.พ.30 (VAT return) — pure builder for the RD Prep "Format กลาง" batch-upload file
/// (โปรแกรมโอนย้ายข้อมูล, plugin <c>vat/pp30-trn</c>). No DB, no I/O — fully unit-testable
/// against golden strings. Mirrors <see cref="WhtBatchFormat"/>'s file rules (pipe '|', UTF-8
/// no-BOM, CR/LF, N(15,2)), but the LAYOUT is ภ.พ.30's own — verified field-by-field from RD's
/// shipped importer (SQLite <c>MASTER_PP30_TRN_CONFIG</c> START_POINT 0..15 + the validator branches).
///
/// Key divergences from WHT (source-verified — do NOT "fix" to match WHT):
///  • ภ.พ.30 is a per-branch SUMMARY — the file is DETAIL rows ONLY (one per branch). There is
///    NO header (H) record: the importer SKIPS an optional title line ("Y"==isHeader &amp;&amp; row 0)
///    and reads tax-id / year / month / filing-type from the RD Prep GUI, not the file.
///  • 16 fields per row, in this exact order (config START_POINT):
///      0 SEQ · 1 BRANCH_NO(≤5 digits) · 2 NUMBER(addr, ≤20, '/'+'-' ALLOWED) · 3 POSTAL_CODE(5 digits)
///      4 ข้อ1 SALE_AMT(&gt;0) · 5 ข้อ1.1 SALE_OUT_AMT · 6 ข้อ1.2 SALE_OVER_AMT
///      7 ข้อ2 SALE_VAT_PERCEN(0%) · 8 ข้อ3 SALE_EXP_AMT(exempt) · 9 ข้อ4 SALE_INC_VAT(taxable)
///      10 ข้อ5 SALE_VAT(output) · 11 ข้อ6 PURCHASE_AMT · 12 ข้อ6.1 PURCHASE_OUT_AMT
///      13 ข้อ6.2 PURCHASE_OVER_AMT · 14 ข้อ7 PURCHASE_VAT(input) · 15 ข้อ8/9 VAT_AMT(net)
///  • Empty-vs-zero rules from the validator (a present 0 is REJECTED for these):
///      ข้อ1.1/1.2/6.1/6.2 (amended-filing boxes) → ALWAYS empty on a normal filing ("ต้องไม่มีข้อมูล").
///      ข้อ2/ข้อ3 → empty when 0; the value only when &gt;0 ("ต้องมีค่าไม่เท่ากับ 0" if present).
///  • Validator identities the file MUST satisfy, checked against the EMITTED rounded values:
///      ข้อ4 == ข้อ1 − ข้อ2 − ข้อ3   and   ข้อ8/9 == ข้อ5 − ข้อ7 (payable &gt;0; overpaid &lt;0; equal → 0.00).
///    Hence ข้อ4 and ข้อ8/9 are DERIVED here from the rounded components actually written — never
///    from GeneratePnd30Async's full-precision Net/CreditCarryForward (which round independently and
///    would fail the identity by ±0.01 on decimal(19,4) money).
/// </summary>
public static class Pp30BatchFormat
{
    public const string Separator = "|";
    public const string RecordTerminator = "\r\n";

    /// <summary>One ภ.พ.30 detail row = one branch's summary (the form boxes ข้อ 1–9).</summary>
    public sealed record Branch(
        string BranchNo,        // BRANCH_NO — ≤5 digits, 00000 / "0" = HQ
        string AddressNo,       // NUMBER — เลขที่ของสถานประกอบการ (house no., ≤20; '/' '-' kept)
        string PostalCode,      // POSTAL_CODE — 5 digits
        decimal SalesTotal,     // ข้อ1 ยอดขายในเดือนนี้ (>0 on a normal filing)
        decimal SalesZeroRated, // ข้อ2 ยอดขายที่เสียภาษี 0%
        decimal SalesExempt,    // ข้อ3 ยอดขายที่ได้รับยกเว้น
        decimal OutputVat,      // ข้อ5 ภาษีขายเดือนนี้
        decimal PurchaseTotal,  // ข้อ6 ยอดซื้อในเดือนนี้
        decimal InputVat);      // ข้อ7 ภาษีซื้อเดือนนี้
    // (ข้อ4 และ ข้อ8/9 ไม่รับเข้ามา — คำนวณจากค่าที่ปัดแล้วด้านล่าง เพื่อให้ผ่าน identity ของ importer)

    public sealed record Header(
        string TaxId,           // company NID(13) — for the filename only (not a file field)
        int Period);            // yyyymm — for the filename only (year/month entered in RD Prep GUI)

    /// <summary>Build the file body (DETAIL rows only — ภ.พ.30 Format กลาง has no H record).</summary>
    public static string Build(Header h, IReadOnlyList<Branch> branches)
    {
        var sb = new StringBuilder();
        int seq = 0;
        foreach (var b in branches)
            sb.Append(DetailRow(b, ++seq)).Append(RecordTerminator);
        return sb.ToString();
    }

    public static byte[] BuildBytes(Header h, IReadOnlyList<Branch> branches) =>
        // UTF-8 WITHOUT BOM — a BOM corrupts the first field (SEQ) on import.
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Build(h, branches));

    /// <summary>RD-convention filename. ภ.พ.30 Format กลาง carries no in-file metadata, so this is
    /// a sane PP30_NID_TAXYEAR(พ.ศ.)_TAXMONTH.txt — chosen for the download, not a compliance field.</summary>
    public static string FileName(Header h)
    {
        var (month, beYear) = MonthBeYear(h.Period);
        return $"PP30_{Digits(h.TaxId, 13)}_{beYear:0000}_{month:00}.txt";
    }

    private static string DetailRow(Branch b, int seq)
    {
        // Round each emitted money box ONCE, then derive the dependent boxes from those exact
        // rounded values so the importer's identity checks foot to the cent.
        var sale       = decimal.Round(b.SalesTotal,     2, MidpointRounding.AwayFromZero);
        var zeroRated  = decimal.Round(b.SalesZeroRated, 2, MidpointRounding.AwayFromZero);
        var exempt     = decimal.Round(b.SalesExempt,    2, MidpointRounding.AwayFromZero);
        var taxable    = sale - zeroRated - exempt;                 // ข้อ4 = ข้อ1 − ข้อ2 − ข้อ3
        var outputVat  = decimal.Round(b.OutputVat,      2, MidpointRounding.AwayFromZero);
        var purchase   = decimal.Round(b.PurchaseTotal,  2, MidpointRounding.AwayFromZero);
        var inputVat   = decimal.Round(b.InputVat,       2, MidpointRounding.AwayFromZero);
        var net        = outputVat - inputVat;                     // ข้อ8/9 = ข้อ5 − ข้อ7

        var fields = new[]
        {
            seq.ToString(Inv),                  // 0  SEQ
            Branch5(b.BranchNo),                // 1  BRANCH_NO (≤5 digits)
            AddressNo(b.AddressNo),             // 2  NUMBER (≤20, '/' '-' kept — NOT San'd)
            Postal(b.PostalCode),               // 3  POSTAL_CODE (5 digits)
            N(sale),                            // 4  ข้อ1 SALE_AMT
            "",                                 // 5  ข้อ1.1 SALE_OUT_AMT  — amended only → empty
            "",                                 // 6  ข้อ1.2 SALE_OVER_AMT — amended only → empty
            NonZero(zeroRated),                 // 7  ข้อ2 SALE_VAT_PERCEN (0%) — empty when 0
            NonZero(exempt),                    // 8  ข้อ3 SALE_EXP_AMT (exempt) — empty when 0
            N(taxable),                         // 9  ข้อ4 SALE_INC_VAT (= ข้อ1−2−3)
            N(outputVat),                       // 10 ข้อ5 SALE_VAT (output)
            N(purchase),                        // 11 ข้อ6 PURCHASE_AMT
            "",                                 // 12 ข้อ6.1 PURCHASE_OUT_AMT  — amended only → empty
            "",                                 // 13 ข้อ6.2 PURCHASE_OVER_AMT — amended only → empty
            N(inputVat),                        // 14 ข้อ7 PURCHASE_VAT (input)
            N(net),                             // 15 ข้อ8/9 VAT_AMT (payable >0 · overpaid <0 · 0.00)
        };
        return string.Join(Separator, fields);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static (int month, int beYear) MonthBeYear(int period)
        => (period % 100, period / 100 + 543);

    /// <summary>N(15,2): 2 dp, no thousands comma, sign-safe (negative net keeps '-').</summary>
    public static string N(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv);

    /// <summary>ข้อ2/ข้อ3: the importer rejects a present 0 ("ต้องมีค่าไม่เท่ากับ 0") but accepts an
    /// empty field on a normal filing → emit "" when the box is exactly 0, the value otherwise.</summary>
    public static string NonZero(decimal v) => N(v) == "0.00" ? "" : N(v);

    /// <summary>BRANCH_NO — numeric, ≤5 digits ("ต้องเป็นตัวเลข 0-9 ไม่เกิน 5 หลัก"). HQ = 0.</summary>
    internal static string Branch5(string? branch)
    {
        var s = new string((branch ?? "").Where(char.IsDigit).ToArray());
        if (s.Length == 0) return "0";
        s = s.TrimStart('0');
        if (s.Length == 0) return "0";                 // 00000 → 0 (HQ)
        return s.Length > 5 ? s[^5..] : s;
    }

    /// <summary>NUMBER (เลขที่) — required, ≤20 chars, free text. The validator has NO forbidden-char
    /// rule here, so '/' and '-' in Thai house numbers (e.g. "123/45") are kept; only trim+truncate.</summary>
    internal static string AddressNo(string? value)
    {
        var s = (value ?? "")
            .Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
            .Replace(Separator, " ")               // never let the pipe delimiter leak into a value
            .Trim();
        return s.Length > 20 ? s[..20] : s;
    }

    /// <summary>POSTAL_CODE — exactly 5 digits ("ต้องเป็นตัวเลข 0-9 จำนวน 5 หลัก"). Blank stays blank
    /// for the upstream M/O guard to catch loudly.</summary>
    internal static string Postal(string? value)
    {
        var s = new string((value ?? "").Where(char.IsDigit).ToArray());
        if (s.Length == 0) return "";
        return s.Length >= 5 ? s[..5] : s.PadLeft(5, '0');
    }

    private static string Digits(string? raw, int len)
    {
        var s = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return s.Length > len ? s[..len] : s;
    }
}
