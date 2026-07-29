using System.Text;
using Accounting.Infrastructure.TaxFilings;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// C1 (specs/pnd2-filing.md) — golden-string tests for the ภ.ง.ด.2 RD batch file
/// (FORMAT กลาง V2.0). Pure (no DB). Unlike ภ.ง.ด.3/53 (WhtBatchFormatTests), ภ.ง.ด.2 has NO
/// SECTION flags, a single income block per row (no 3-triple chunking), and a numeric
/// INC_TYPE_PND code instead of a free-text description.
///
/// NOTE (T13, mirrors WhtBatchFormatTests.cs's own caveat): golden values are CONSTRUCTED FROM
/// the official RD FormatPND2V2_0.pdf spec (v2.0, 16/06/2568), not yet cross-checked against a
/// real RD Prep / e-Filing portal upload. The predicted failure point is `ACC_NO` (§1.2 of the
/// spec) — emitted blank here for director-loan interest (not a bank-deposit account), which is
/// an interpretation of a conditional M/O field, not a verified fact.
/// </summary>
public class Pnd2BatchFormatTests
{
    private static Pnd2BatchFormat.Header Header(int period = 202605) =>
        new(PayerTaxId: "0105500001234", PayerBranch: "000000",
            DeptName: "สำนักงานใหญ่", Period: period, BranchType: "V", UserId: null);

    private static Pnd2BatchFormat.Income Inc(
        decimal paid, decimal tax, decimal ratePct = 15.00m, string incTypePnd = "2", string payCon = "1") =>
        new(new DateOnly(2026, 5, 15), ratePct, paid, tax, incTypePnd, payCon);

    private static Pnd2BatchFormat.Payee Payee(
        string taxId, params Pnd2BatchFormat.Income[] incomes) =>
        new(taxId, TitleName: "-", FirstName: "นายทดสอบ ดอกเบี้ย", LastName: "",
            BranchNo: "000000", Incomes: incomes);

    [Fact]
    public void Header_is_22_fields_with_no_section_flags_and_pnd2_layout()
    {
        var s = Pnd2BatchFormat.Build(
            Header(), new[] { Payee("1234567890123", Inc(1000.00m, 150.00m)) });
        var lines = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        var h = lines[0].Split('|');
        h.Should().HaveCount(22, "ภ.ง.ด.2 header has NO SECTION3/SECTION48/SECTION50 flags (25→22)");
        h[0].Should().Be("H");
        h[5].Should().Be("PND2");             // TAX_TYPE
        // Positionally proves the shifted (no-SECTION-flag) layout: index 9 is LTO, not a
        // section flag; TAX_MONTH/TAX_YEAR follow immediately (10/11), not at 13/14 as on PND3/53.
        h[9].Should().Be("0");                 // LTO
        h[10].Should().Be("05");               // TAX_MONTH
        h[11].Should().Be("2569");             // TAX_YEAR (BE) — 2026 + 543
        h[14].Should().Be("1");                // TOT_NUM
        h[15].Should().Be("1000.00");          // TOT_AMT
        h[16].Should().Be("150.00");           // TOT_TAX
        h[17].Should().Be("0.00");             // SUR_AMT
        h[18].Should().Be("150.00");           // GTOT_TAX = TOT_TAX + SUR_AMT
        h[21].Should().Be("2");                // FORM_FLAG (internet)
    }

    [Fact]
    public void Detail_is_27_fields_one_row_per_income_no_chunking()
    {
        // T2 — the ภ.ง.ด.3 "3 incomes per row, 4th starts a new SEQ_NO" rule does NOT apply here:
        // a payee with 4 payments must produce 4 DETAIL rows, not 2.
        var s = Pnd2BatchFormat.Build(Header(), new[]
        {
            Payee("1234567890123",
                Inc(100m, 15m), Inc(200m, 30m), Inc(300m, 45m), Inc(400m, 60m)),
        });
        var lines = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(5, "1 header + 4 detail rows (one per income)");
        lines[0].Split('|')[14].Should().Be("4");   // TOT_NUM = 4
        for (int i = 1; i <= 4; i++)
        {
            var d = lines[i].Split('|');
            d.Should().HaveCount(27);
            d[0].Should().Be("D");
            d[1].Should().Be(i.ToString());          // SEQ_NO increments per row
        }
    }

    [Fact]
    public void Detail_fields_match_pnd2_layout_and_conventions()
    {
        var s = Pnd2BatchFormat.Build(
            Header(), new[] { Payee("1234567890123", Inc(1000.00m, 150.00m, ratePct: 15.00m, incTypePnd: "2", payCon: "1")) });
        var d = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1].Split('|');

        d[1].Should().Be("1");                  // SEQ_NO
        d[2].Should().Be("000000");              // BRANCH_NO
        d[3].Should().Be("1234567890123");       // PIN
        d[4].Should().Be("0000000000");          // TIN — legacy id, not held
        d[5].Should().Be("", "ACC_NO is blank for director-loan interest (not a deposit account, §1.2)");
        d[6].Should().Be("-");                   // TITLE_NAME — "ให้ใส่ขีด -"
        d[7].Should().Be("นายทดสอบ ดอกเบี้ย");     // FNAME
        d[8].Should().Be("");                    // SNAME — one TEAS name field
        d[9].Should().Be("15052569");            // PAID_DATE (BE DDMMYYYY)
        d[10].Should().Be("15.00");              // TAX_RATE — 0.15 fraction rendered as 15.00
        d[11].Should().Be("1000.00");            // PAID_AMT
        d[12].Should().Be("150.00");             // TAX_AMT
        d[13].Should().Be("2", "INC_TYPE_PND is the 1-char numeric code, not a free-text description");
        d[14].Should().Be("1");                  // PAY_CON — round-trips WhtCondition
        d[^1].Should().Be("");                   // POSTAL_CODE — address block blank (O for ภ.ง.ด.2)
        d.Should().HaveCount(27);
    }

    [Fact]
    public void Bytes_are_utf8_without_bom_and_filename_follows_rd_convention()
    {
        var bytes = Pnd2BatchFormat.BuildBytes(
            Header(), new[] { Payee("1234567890123", Inc(100m, 15m)) });
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().Contain("นายทดสอบ ดอกเบี้ย");

        Pnd2BatchFormat.FileName(Header(202605))
            .Should().Be("PND2_0105500001234_000000_2569_05_00_00.txt");
    }
}
