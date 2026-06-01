using System.Text;
using Accounting.Application.Payroll;
using Accounting.Infrastructure.Payroll;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Payroll;

/// <summary>
/// สปส.1-10 e-payment file — pure golden tests (no DB). Lock the field byte-positions (135-char
/// records, type chars, header count/totals from the detail rows), the actual-wage vs capped-contribution
/// split (the key spec point), the numeric prefix codes, and the single-byte TIS-620 encoding. Cross-checked
/// against the raw sample in `docs/SSO-Forms/spec/sps110-spec.md` §5.
/// </summary>
public sealed class SpsBatchFormatTests
{
    private static SsoMonthlyModel Model() => new(
        EmployerTaxId: "0105511000001", BranchCode: "00000", EmployerName: "บริษัท ทดสอบ จำกัด",
        Building: null, RoomNo: null, Floor: null, Village: null, HouseNo: null, Moo: null,
        Soi: null, Street: null, SubDistrict: "แขวงทดสอบ", District: "เขตทดสอบ",
        Province: "กรุงเทพมหานคร", PostalCode: "10110",
        PeriodMonth: 5, PeriodYearBE: 2568, PeriodYearCE: 2025, PayDate: new DateOnly(2025, 6, 15),
        EmployerAccountNo: "1001849434",
        Lines: new List<SsoLine>
        {
            // wage 50,000 (actual, over ceiling) but contribution capped at 750; wage 10,000 under ceiling.
            new("1100000000001", "1100000000001", "นาย", "สมชาย", "ใจดี", 50_000m, 750m, 750m),
            new("1100000000002", "1100000000002", "นางสาว", "สมหญิง", "รักดี", 10_000m, 500m, 500m),
        });

    private static string[] Lines(string file) =>
        file.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Every_record_is_exactly_135_chars_with_a_header_then_one_detail_per_insured()
    {
        var file = SpsBatchFormat.Build(Model());
        var lines = Lines(file);

        lines.Should().HaveCount(3);                       // 1 header + 2 insured
        lines.Should().OnlyContain(l => l.Length == SpsBatchFormat.RecordLength);
        lines[0][0].Should().Be('1');                       // header record type
        lines[1][0].Should().Be('2');                       // detail record type
        lines[2][0].Should().Be('2');
        file.Should().EndWith("\r\n");                      // every record terminated
    }

    [Fact]
    public void Header_fields_sit_at_their_fixed_positions_and_count_matches_the_detail_rows()
    {
        var h = Lines(SpsBatchFormat.Build(Model()))[0];

        h.Substring(1, 10).Should().Be("1001849434");                      // เลขที่บัญชีนายจ้าง (SSO reg no)
        h.Substring(17, 6).Should().Be("150668");                          // วันที่ชำระเงิน 15/06/2025 → ddMMyy BE 68
        h.Substring(23, 4).Should().Be("0568");                            // งวด 05/2025 → MMyy BE 68
        h.Substring(27, 45).TrimEnd().Should().Be("บริษัท ทดสอบ จำกัด");   // ชื่อสถานประกอบการ
        h.Substring(72, 4).Should().Be("0500");                            // อัตรา 5.00%
        h.Substring(76, 6).Should().Be("000002");                          // จำนวนผู้ประกันตน = 2 detail rows
        h.Substring(82, 15).Should().Be(SpsBatchFormat.Amt(60_000m, 15));  // ค่าจ้างรวม = Σ actual (50k+10k)
        h.Substring(97, 14).Should().Be(SpsBatchFormat.Amt(2_500m, 14));   // เงินสมทบรวม (1,250+1,250)
        h.Substring(111, 12).Should().Be(SpsBatchFormat.Amt(1_250m, 12));  // ส่วนผู้ประกันตน
        h.Substring(123, 12).Should().Be(SpsBatchFormat.Amt(1_250m, 12));  // ส่วนนายจ้าง == ผู้ประกันตน
    }

    [Fact]
    public void Detail_carries_the_actual_uncapped_wage_with_the_capped_contribution()
    {
        var d = Lines(SpsBatchFormat.Build(Model()))[1];

        d.Substring(1, 13).Should().Be("1100000000001");      // เลขประจำตัวประชาชน
        d.Substring(14, 3).Should().Be("001");                // นาย → numeric code 001
        d.Substring(17, 30).TrimEnd().Should().Be("สมชาย");   // ชื่อ
        d.Substring(47, 35).TrimEnd().Should().Be("ใจดี");    // ชื่อสกุล
        d.Substring(82, 14).Should().Be(SpsBatchFormat.Amt(50_000m, 14));  // ค่าจ้างที่จ่ายจริง = actual 50,000 (NOT 15,000)
        d.Substring(96, 12).Should().Be(SpsBatchFormat.Amt(750m, 12));     // เงินสมทบ = capped 750
        d.Substring(108, 27).Should().Be(new string(' ', 27));             // filler
    }

    [Fact]
    public void Amount_and_prefix_use_the_verified_conventions()
    {
        SpsBatchFormat.Amt(750m, 12).Should().Be("000000075000");           // ×100 สตางค์, zero-filled
        SpsBatchFormat.Amt(44_505m, 14).Should().Be("00000004450500");      // matches spec §5 sample row
        SpsBatchFormat.Amt(617.25m, 12).Should().Be("000000061725");        // satang precision survives
        SpsBatchFormat.IntField(2, 6).Should().Be("000002");
        SpsBatchFormat.Digits("1-1009-00409-98-1", 13).Should().Be("1100900409981");
        SpsBatchFormat.PrefixCode("นางสาว").Should().Be("003");
        SpsBatchFormat.PrefixCode("").Should().Be("099");                   // unknown → อื่น ๆ
        SpsBatchFormat.DateDdMmYy(new DateOnly(2019, 5, 15)).Should().Be("150562");   // spec-verified example
    }

    [Fact]
    public void File_encodes_as_single_byte_tis620_no_bom()
    {
        var text = SpsBatchFormat.Build(Model());
        var bytes = SpsBatchFormat.BuildBytes(Model());

        // No UTF-8 BOM.
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        // Single-byte: every char (Thai in TIS-620 + ASCII) is exactly 1 byte, so byte len == char len.
        bytes.Length.Should().Be(text.Length);
        // Round-trips through Windows-874.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding.GetEncoding(SpsBatchFormat.CodePage).GetString(bytes).Should().Be(text);
    }
}
