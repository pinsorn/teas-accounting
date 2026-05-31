using System.Text;
using Accounting.Infrastructure.TaxFilings;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// cont.82.1 P2 — golden-string tests for the RD WHT batch file (FORMAT กลาง V2.0). Pure (no DB).
/// NOTE: golden values are CONSTRUCTED FROM the official RD V2.0 spec PDFs (FormatPND53V2_0 /
/// FormatPND3V2_0), not yet cross-checked against a real portal upload — see spec §validation.
/// </summary>
public class WhtBatchFormatTests
{
    private static WhtBatchFormat.Header Header(int period = 202605, string tax = "PND53") =>
        new(TaxType: tax, PayerTaxId: "0105500001234", PayerBranch: "000000",
            DeptName: "สำนักงานใหญ่", Period: period,
            SectionA: true, SectionB: false, SectionC: false,
            BranchType: "V", UserId: null);

    private static WhtBatchFormat.Income Inc(
        decimal paid, decimal tax, decimal ratePct = 3.00m, string type = "ค่าบริการ") =>
        new(new DateOnly(2026, 5, 15), ratePct, paid, tax, type, "1");

    private static WhtBatchFormat.Payee Payee(
        string taxId, params WhtBatchFormat.Income[] incomes) =>
        new(taxId, TitleName: "", FirstName: "บริษัท ทดสอบ จำกัด", LastName: "",
            BranchNo: "000000", Incomes: incomes);

    [Fact]
    public void Header_and_one_corporate_payee_match_the_spec_layout()
    {
        var s = WhtBatchFormat.Build(
            Header(), new[] { Payee("0105512345678", Inc(1000.00m, 30.00m)) });

        var lines = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        // HEADER: 25 pipe-separated fields, starts H.
        var h = lines[0].Split('|');
        h.Should().HaveCount(25);
        h[0].Should().Be("H");
        h[1].Should().Be("0000");            // SENDER_ID
        h[2].Should().Be("0105500001234");   // SENDER_NID
        h[4].Should().Be("1");               // SENDER_ROLE
        h[5].Should().Be("PND53");           // TAX_TYPE
        h[9].Should().Be("1");               // SECTION3 (ม.3 เตรส) on
        h[10].Should().Be("0");
        h[13].Should().Be("05");             // TAX_MONTH
        h[14].Should().Be("2569");           // TAX_YEAR — 2026 + 543 (พ.ศ.)
        h[17].Should().Be("1");              // TOT_NUM (one DETAIL row)
        h[18].Should().Be("1000.00");        // TOT_AMT
        h[19].Should().Be("30.00");          // TOT_TAX
        h[20].Should().Be("0.00");           // SUR_AMT
        h[21].Should().Be("30.00");          // GTOT_TAX
        h[24].Should().Be("2");              // FORM_FLAG (internet)

        // DETAIL: 38 fields, starts D.
        var d = lines[1].Split('|');
        d.Should().HaveCount(38);
        d[0].Should().Be("D");
        d[1].Should().Be("1");               // SEQ_NO
        d[3].Should().Be("0105512345678");   // NID
        d[4].Should().Be("0000000000");      // TIN
        d[6].Should().Be("บริษัท ทดสอบ จำกัด");// FNAME
        d[8].Should().Be("15052569");        // PAID_DATE1 (BE)
        d[9].Should().Be("3.00");            // TAX_RATE1
        d[10].Should().Be("1000.00");        // PAID_AMT1
        d[11].Should().Be("30.00");          // TAX_AMT1
        d[12].Should().Be("ค่าบริการ");        // INC_TYPE_PND1
        d[13].Should().Be("1");              // PAY_CON1
        d[14].Should().Be("00000000");       // PAID_DATE2 empty
        d[15].Should().Be("0.00");           // TAX_RATE2 empty
        d[^1].Should().Be("");               // POSTAL_CODE (address block blank)
    }

    [Fact]
    public void Fourth_income_for_a_payee_starts_a_new_seq_no()
    {
        var s = WhtBatchFormat.Build(Header(), new[]
        {
            Payee("0105512345678",
                Inc(100m, 3m), Inc(200m, 6m), Inc(300m, 9m), Inc(400m, 12m)),
        });
        var lines = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // 1 header + 2 detail rows (3 incomes + 1 income).
        lines.Should().HaveCount(3);
        lines[0].Split('|')[17].Should().Be("2");   // TOT_NUM = 2 SEQ_NO rows
        lines[1].Split('|')[1].Should().Be("1");    // SEQ_NO 1
        lines[2].Split('|')[1].Should().Be("2");    // SEQ_NO 2
        // Row 1 carries 3 income triples (3rd triple amount populated).
        lines[1].Split('|')[10].Should().Be("100.00");
        lines[1].Split('|')[22].Should().Be("300.00");  // PAID_AMT3 (triple-3 amount)
        // Row 2 carries the 4th income as its first triple.
        lines[2].Split('|')[10].Should().Be("400.00");
        // Totals across both rows.
        lines[0].Split('|')[18].Should().Be("1000.00");  // TOT_AMT
        lines[0].Split('|')[19].Should().Be("30.00");    // TOT_TAX
    }

    [Fact]
    public void Forbidden_characters_are_stripped_from_text_fields()
    {
        // Commas, &, /, quotes etc. are illegal (V2.0 note #10) and would break the file.
        var payee = new WhtBatchFormat.Payee(
            "0105512345678", "", "A&B Co., Ltd. \"Tech\"/Group", "", "000000",
            new[] { Inc(100m, 3m, type: "ค่าบริการ, IT*support") });
        var s = WhtBatchFormat.Build(Header(), new[] { payee });
        var d = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1].Split('|');

        d[6].Should().Be("AB Co. Ltd. TechGroup");          // & , " / stripped
        d[12].Should().Be("ค่าบริการ ITsupport");            // , * stripped (no space at '*')
        // No stray pipe could have leaked into a value (field count stays 38).
        s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1].Split('|').Should().HaveCount(38);
    }

    [Fact]
    public void Negative_amounts_keep_their_sign()
    {
        WhtBatchFormat.N(-500m).Should().Be("-500.00");
        WhtBatchFormat.N(0m).Should().Be("0.00");
        WhtBatchFormat.N(1234.5m).Should().Be("1234.50");   // no thousands comma
    }

    [Fact]
    public void Date_uses_buddhist_year_ddmmyyyy()
    {
        WhtBatchFormat.Date(new DateOnly(2026, 1, 1)).Should().Be("01012569");
        WhtBatchFormat.Date(new DateOnly(2025, 12, 31)).Should().Be("31122568");
    }

    [Fact]
    public void Bytes_are_utf8_without_a_bom()
    {
        var bytes = WhtBatchFormat.BuildBytes(
            Header(), new[] { Payee("0105512345678", Inc(100m, 3m)) });
        // No UTF-8 BOM (EF BB BF) — RD Prep treats a BOM as part of the first field.
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        // Thai round-trips intact.
        Encoding.UTF8.GetString(bytes).Should().Contain("บริษัท ทดสอบ จำกัด");
    }

    [Fact]
    public void Filename_follows_the_rd_convention()
    {
        WhtBatchFormat.FileName(Header(202605))
            .Should().Be("PND53_0105500001234_000000_2569_05_00_00.txt");
    }
}
