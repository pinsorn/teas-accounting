using System.Globalization;
using System.Text;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// C1 (specs/pnd2-filing.md) — pure builder for the RD ภ.ง.ด.2 batch-upload file (รูปแบบข้อมูล
/// FORMAT กลาง V2.0, ปรับปรุง 16/06/2568). No DB, no I/O — fully unit-testable against golden
/// strings (Pnd2BatchFormatTests). Reuses <see cref="WhtBatchFormat"/>'s primitives (N, Date,
/// San, Pad6, Digits, FileName) — it cannot reuse WhtBatchFormat.Build itself: ภ.ง.ด.2 has no
/// SECTION flags, a single income block per DETAIL row (no 3-triple chunking — one row per
/// certificate), an extra ACC_NO field (kept blank, see §1.2), and a coded (not free-text)
/// INC_TYPE_PND.
/// </summary>
public static class Pnd2BatchFormat
{
    private const string TaxType = "PND2";

    /// <summary>One payment (= one WhtCertificate) — always exactly one DETAIL row, never
    /// chunked or grouped with a payee's other payments (§1.1 — deliberately unlike PND3/53).</summary>
    public sealed record Income(
        DateOnly PaidDate, decimal RatePercent, decimal PaidAmount,
        decimal TaxAmount, string IncTypePnd, string PayCondition);

    public sealed record Payee(
        string TaxId,          // PIN, 13 digits
        string TitleName,      // "-" (no prefix) per the spec's own instruction
        string FirstName,
        string LastName,       // usually ""
        string BranchNo,       // payer branch of the income payment (000000 = HQ)
        IReadOnlyList<Income> Incomes);

    public sealed record Header(
        string PayerTaxId, string PayerBranch, string DeptName, int Period,
        string BranchType, string? UserId);

    /// <summary>Build the file body (UTF-8) from a header + the period's payments.</summary>
    public static string Build(Header h, IReadOnlyList<Payee> payees)
    {
        // DETAIL rows first so the header totals/counts are exact.
        var detail = new StringBuilder();
        int seq = 0;
        decimal totIncome = 0m, totTax = 0m;

        foreach (var p in payees)
            foreach (var inc in p.Incomes)
            {
                seq++;
                totIncome += inc.PaidAmount;
                totTax += inc.TaxAmount;
                detail.Append(DetailRow(p, inc, seq)).Append(WhtBatchFormat.RecordTerminator);
            }

        var header = HeaderRow(h, recordCount: seq, totalIncome: totIncome, totalTax: totTax);
        return header + WhtBatchFormat.RecordTerminator + detail.ToString();
    }

    public static byte[] BuildBytes(Header h, IReadOnlyList<Payee> payees) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Build(h, payees));

    public static string FileName(Header h, string formType = "00", string submission = "00") =>
        WhtBatchFormat.FileName(TaxType, h.PayerTaxId, h.PayerBranch, h.Period, formType, submission);

    // 22 fields — no SECTION flags (§1.1). Order: H · SENDER_ID · SENDER_NID · SENDER_BRANCH ·
    // SENDER_ROLE · TAX_TYPE · NID · BRANCH_NO · DEPT_NAME · LTO · TAX_MONTH · TAX_YEAR ·
    // BRANCH_TYPE · FORM_TYPE · TOT_NUM · TOT_AMT · TOT_TAX · SUR_AMT · GTOT_TAX · TRANS_AMT ·
    // USER_ID · FORM_FLAG.
    private static string HeaderRow(Header h, int recordCount, decimal totalIncome, decimal totalTax)
    {
        var (month, beYear) = MonthBeYear(h.Period);
        var fields = new[]
        {
            "H",
            "0000",                                        // SENDER_ID — self-filing
            WhtBatchFormat.Digits(h.PayerTaxId, 13),        // SENDER_NID
            WhtBatchFormat.Pad6(h.PayerBranch),             // SENDER_BRANCH
            "1",                                            // SENDER_ROLE — ผู้หักภาษี ณ ที่จ่าย
            TaxType,                                         // TAX_TYPE
            WhtBatchFormat.Digits(h.PayerTaxId, 13),        // NID (payer = filer here)
            WhtBatchFormat.Pad6(h.PayerBranch),             // BRANCH_NO
            WhtBatchFormat.San(h.DeptName, 80),             // DEPT_NAME
            "0",                                            // LTO — not a large taxpayer
            month.ToString("00", Inv),                      // TAX_MONTH
            beYear.ToString("0000", Inv),                    // TAX_YEAR (พ.ศ.)
            WhtBatchFormat.San(h.BranchType, 1),             // BRANCH_TYPE
            "00",                                            // FORM_TYPE — ยื่นปกติ
            recordCount.ToString(Inv),                       // TOT_NUM
            WhtBatchFormat.N(totalIncome),                   // TOT_AMT
            WhtBatchFormat.N(totalTax),                      // TOT_TAX
            WhtBatchFormat.N(0m),                            // SUR_AMT — เงินเพิ่ม (none on normal filing)
            WhtBatchFormat.N(totalTax),                      // GTOT_TAX = TOT_TAX + SUR_AMT
            WhtBatchFormat.N(totalTax),                      // TRANS_AMT — assume full remittance
            WhtBatchFormat.San(h.UserId ?? "", 20),          // USER_ID
            "2",                                             // FORM_FLAG — ยื่นแบบผ่านอินเทอร์เน็ต
        };
        return string.Join(WhtBatchFormat.Separator, fields);
    }

    // 27 fields — one row per income (§1.1). DETAIL · SEQ_NO · BRANCH_NO · PIN · TIN · ACC_NO ·
    // TITLE_NAME · FNAME · SNAME · PAID_DATE · TAX_RATE · PAID_AMT · TAX_AMT · INC_TYPE_PND ·
    // PAY_CON · 12-field address block (all O for ภ.ง.ด.2 — the export is fully producible).
    private static string DetailRow(Payee p, Income inc, int seq)
    {
        var fields = new List<string>(27)
        {
            "D",
            seq.ToString(Inv),                              // SEQ_NO
            WhtBatchFormat.Pad6(p.BranchNo),                 // BRANCH_NO
            WhtBatchFormat.Digits(p.TaxId, 13),              // PIN
            "0000000000",                                    // TIN (10) — legacy id, not held
            "",                                               // ACC_NO — blank (§1.2, not a deposit account)
            WhtBatchFormat.San(p.TitleName, 100),            // TITLE_NAME
            WhtBatchFormat.San(p.FirstName, 100),            // FNAME
            WhtBatchFormat.San(p.LastName, 80),              // SNAME
            WhtBatchFormat.Date(inc.PaidDate),               // PAID_DATE
            WhtBatchFormat.N(inc.RatePercent),                // TAX_RATE
            WhtBatchFormat.N(inc.PaidAmount),                 // PAID_AMT
            WhtBatchFormat.N(inc.TaxAmount),                  // TAX_AMT
            WhtBatchFormat.San(inc.IncTypePnd, 1),            // INC_TYPE_PND — coded, not free text
            WhtBatchFormat.San(inc.PayCondition, 1),          // PAY_CON
        };
        for (int i = 0; i < 12; i++) fields.Add("");          // address block — O for ภ.ง.ด.2
        return string.Join(WhtBatchFormat.Separator, fields);
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static (int month, int beYear) MonthBeYear(int period)
        => (period % 100, period / 100 + 543);
}
