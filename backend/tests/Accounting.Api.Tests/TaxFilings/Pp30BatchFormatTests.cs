using System.Text;
using Accounting.Infrastructure.TaxFilings;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Golden-string tests for the ภ.พ.30 RD-Prep "Format กลาง" batch file. Pure (no DB).
/// The 16-field DETAIL layout + the empty-vs-zero / identity rules are derived FROM RD's shipped
/// importer: SQLite <c>MASTER_PP30_TRN_CONFIG</c> (START_POINT 0..15) + the <c>pp30-trn-validator</c>
/// branches (ข้อ4 = ข้อ1−ข้อ2−ข้อ3 ; ข้อ8/9 = ข้อ5−ข้อ7 ; ข้อ2/ข้อ3 empty-when-0 ;
/// amended boxes ข้อ1.1/1.2/6.1/6.2 always empty on a normal filing). Not yet cross-checked against
/// a live RD Prep upload (mirrors the WhtBatchFormatTests caveat).
/// </summary>
public class Pp30BatchFormatTests
{
    private static Pp30BatchFormat.Header Header(int period = 202605) =>
        new(TaxId: "0105500001234", Period: period);

    private static Pp30BatchFormat.Branch Branch(
        decimal sales, decimal zero, decimal exempt, decimal outVat,
        decimal purchase, decimal inVat,
        string branchNo = "00000", string num = "123/45", string post = "10310") =>
        new(branchNo, num, post, sales, zero, exempt, outVat, purchase, inVat);

    private static string[] Row(string s) =>
        s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0].Split('|');

    [Fact]
    public void Single_branch_normal_filing_matches_the_16_field_layout()
    {
        // Taxable-only company: ยอดขาย 100,000 (all taxable) · ภาษีขาย 7,000 · ยอดซื้อ 40,000 · ภาษีซื้อ 2,800.
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(100000m, 0m, 0m, 7000m, 40000m, 2800m) });

        var lines = s.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);                  // DETAIL only — ภ.พ.30 Format กลาง has NO header record
        var d = Row(s);
        d.Should().HaveCount(16);

        d[0].Should().Be("1");                        // SEQ
        d[1].Should().Be("0");                        // BRANCH_NO — 00000 normalises to 0 (HQ)
        d[2].Should().Be("123/45");                   // NUMBER — '/' KEPT (no San on the address)
        d[3].Should().Be("10310");                    // POSTAL_CODE — 5 digits
        d[4].Should().Be("100000.00");                // ข้อ1 SALE_AMT
        d[5].Should().Be("");                         // ข้อ1.1 SALE_OUT_AMT — amended only → empty
        d[6].Should().Be("");                         // ข้อ1.2 SALE_OVER_AMT — amended only → empty
        d[7].Should().Be("");                         // ข้อ2 (0% sales) — empty when 0
        d[8].Should().Be("");                         // ข้อ3 (exempt) — empty when 0
        d[9].Should().Be("100000.00");                // ข้อ4 = ข้อ1 − ข้อ2 − ข้อ3
        d[10].Should().Be("7000.00");                 // ข้อ5 SALE_VAT (output)
        d[11].Should().Be("40000.00");                // ข้อ6 PURCHASE_AMT
        d[12].Should().Be("");                        // ข้อ6.1 — amended only → empty
        d[13].Should().Be("");                        // ข้อ6.2 — amended only → empty
        d[14].Should().Be("2800.00");                 // ข้อ7 PURCHASE_VAT (input)
        d[15].Should().Be("4200.00");                 // ข้อ8/9 = ข้อ5 − ข้อ7 = 7000 − 2800 (payable, positive)
    }

    [Fact]
    public void Zero_purchase_month_emits_present_zeros_for_box6_and_box7_not_blanks()
    {
        // Validator (buyamt/vatbuytsm normal-filing branch): ข้อ6/ข้อ7 are required-present + numeric,
        // with NO ">0" rule — so a no-purchase month emits "0.00" (NOT empty, unlike ข้อ2/ข้อ3).
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(100000m, 0m, 0m, 7000m, 0m, 0m) });
        var d = Row(s);
        d[11].Should().Be("0.00");                    // ข้อ6 PURCHASE_AMT — present zero, required
        d[14].Should().Be("0.00");                    // ข้อ7 PURCHASE_VAT — present zero, required
        d[15].Should().Be("7000.00");                 // ข้อ8/9 = 7000 − 0 (fully payable)
    }

    [Fact]
    public void Mixed_sales_emit_zero_rated_and_exempt_and_foot_the_box4_identity()
    {
        // ข้อ1 200,000 = taxable 150,000 + 0% 30,000 + exempt 20,000.
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(200000m, 30000m, 20000m, 10500m, 0m, 0m) });
        var d = Row(s);

        d[4].Should().Be("200000.00");                // ข้อ1
        d[7].Should().Be("30000.00");                 // ข้อ2 — present because > 0
        d[8].Should().Be("20000.00");                 // ข้อ3 — present because > 0
        d[9].Should().Be("150000.00");                // ข้อ4 = 200000 − 30000 − 20000 (identity holds)
    }

    [Fact]
    public void Overpaid_period_emits_a_negative_net_in_box_8_or_9()
    {
        // ภาษีซื้อ > ภาษีขาย → ชำระเกิน → ข้อ8/9 must be NEGATIVE in the file (importer flips to flag "O").
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(50000m, 0m, 0m, 3500m, 90000m, 6300m) });
        var d = Row(s);

        d[10].Should().Be("3500.00");                 // ข้อ5 output
        d[14].Should().Be("6300.00");                 // ข้อ7 input
        d[15].Should().Be("-2800.00");               // ข้อ8/9 = 3500 − 6300 = −2800 (overpaid)
    }

    [Fact]
    public void Break_even_period_emits_zero_net()
    {
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(50000m, 0m, 0m, 3500m, 50000m, 3500m) });
        Row(s)[15].Should().Be("0.00");               // ข้อ5 == ข้อ7 → net 0.00 (importer sets flag "U", amt 0)
    }

    [Fact]
    public void Boxes_4_and_8or9_use_consistent_rounding_of_the_emitted_components()
    {
        // Sub-cent inputs (money is decimal(19,4)). The identity must foot on the ROUNDED boxes:
        // round(100.005)=100.01 ; round(0.004)=0.00 ; ข้อ4 = 100.01 − 0.00 − 0.00 = 100.01.
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(100.005m, 0.004m, 0m, 7.005m, 0m, 2.004m) });
        var d = Row(s);

        d[4].Should().Be("100.01");                   // ข้อ1
        d[7].Should().Be("");                         // ข้อ2 — 0.004 rounds to 0.00 → empty (would else be rejected)
        d[9].Should().Be("100.01");                   // ข้อ4 = round(ข้อ1) − round(ข้อ2) − round(ข้อ3)
        d[10].Should().Be("7.01");                    // ข้อ5 = round(7.005)
        d[14].Should().Be("2.00");                    // ข้อ7 = round(2.004)
        d[15].Should().Be("5.01");                    // ข้อ8/9 = round(ข้อ5) − round(ข้อ7) = 7.01 − 2.00
    }

    [Fact]
    public void Address_number_keeps_slash_and_dash_but_is_capped_at_20()
    {
        var s = Pp30BatchFormat.Build(Header(),
            new[] { Branch(100m, 0m, 0m, 7m, 0m, 0m, num: "199/123-456 หมู่บ้านทดสอบยาวมากเกินไป") });
        var num = Row(s)[2];
        num.Should().NotContain("|");                 // delimiter never leaks
        num.Length.Should().Be(20);                   // ≤20 (validator: "ไม่เกิน 20 ตัวอักษร")
        num.Should().StartWith("199/123-456");        // '/' and '-' preserved (no forbidden-char strip)
    }

    [Fact]
    public void Bytes_are_utf8_without_a_bom()
    {
        var bytes = Pp30BatchFormat.BuildBytes(Header(),
            new[] { Branch(100m, 0m, 0m, 7m, 0m, 0m, num: "บ้านเลขที่ 1") });
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().Contain("บ้านเลขที่ 1");
    }

    [Fact]
    public void Filename_carries_nid_and_buddhist_period()
    {
        Pp30BatchFormat.FileName(Header(202605))
            .Should().Be("PP30_0105500001234_2569_05.txt");   // 2026 + 543 = 2569 (พ.ศ.)
    }

    [Fact]
    public void N_is_sign_safe_and_comma_free_and_nonzero_blanks_only_exact_zero()
    {
        Pp30BatchFormat.N(-2800m).Should().Be("-2800.00");
        Pp30BatchFormat.N(1234.5m).Should().Be("1234.50");
        Pp30BatchFormat.NonZero(0m).Should().Be("");
        Pp30BatchFormat.NonZero(0.001m).Should().Be("");      // rounds to 0.00 → blank
        Pp30BatchFormat.NonZero(30000m).Should().Be("30000.00");
    }
}
