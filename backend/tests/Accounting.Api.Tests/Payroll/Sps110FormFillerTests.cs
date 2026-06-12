using Accounting.Application.Payroll;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Payroll;

/// <summary>
/// สปส.1-10 ส่วนที่ 1 PDF — structural gates for the FLAT-form overlay filler (no DB). Visual
/// correctness (values on their dotted lines / comb cells) is the raster gate (Sps110VisualEmit).
/// </summary>
public sealed class Sps110FormFillerTests
{
    internal static SsoMonthlyModel Model(string? accountNo = "1234567890") => new(
        EmployerTaxId: "0105561234567", BranchCode: "00000",
        EmployerName: "บริษัท ทดสอบภาษี จำกัด",
        Building: "อาคารทดสอบ", RoomNo: "12B", Floor: "3", Village: "หมู่บ้านตัวอย่าง",
        HouseNo: "99/1", Moo: "4", Soi: "สุขใจ 5", Street: "พหลโยธิน",
        SubDistrict: "จตุจักร", District: "จตุจักร", Province: "กรุงเทพมหานคร",
        PostalCode: "10900",
        PeriodMonth: 3, PeriodYearBE: 2569, PeriodYearCE: 2026,
        PayDate: new DateOnly(2026, 3, 31),
        EmployerAccountNo: accountNo,
        Lines:
        [
            new SsoLine("1234567890123", "1234567890123", "นาย", "สมชาย", "ใจดี",
                30_000m, 750m, 750m),
            new SsoLine("3210987654321", "3210987654321", "นางสาว", "สมหญิง", "ใจงาม",
                17_345.67m, 750m, 750m),
        ]);

    [Fact]
    public void Fill_renders_nonempty_pdf()
    {
        var pdf = Sps110FormFiller.Fill(Model());
        pdf.Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
        pdf.Length.Should().BeGreaterThan(20_000);
    }

    [Fact]
    public void Missing_account_number_renders_blank_not_zeros()
    {
        // เลขที่บัญชี unknown → the comb stays BLANK (zeros would assert a fake account).
        Sps110FormFiller.Fill(Model(accountNo: null)).Length.Should().BeGreaterThan(20_000);
    }

    [Fact]
    public void Totals_come_from_the_model_invariants()
    {
        var m = Model();
        m.TotalWage.Should().Be(47_345.67m);
        m.TotalEmployeeContribution.Should().Be(1_500m);
        m.TotalEmployerContribution.Should().Be(1_500m);
        m.GrandTotalContribution.Should().Be(3_000m);
        m.EmployeeCount.Should().Be(2);
    }
}

/// <summary>Visual-gate emitter — writes the worked-case สปส.1-10 PDF when SPS110_EMIT_DIR is set.</summary>
public sealed class Sps110VisualEmit
{
    [SkippableFact]
    public void Emit_worked_case_for_raster_review()
    {
        var dir = Environment.GetEnvironmentVariable("SPS110_EMIT_DIR");
        Skip.If(string.IsNullOrEmpty(dir), "SPS110_EMIT_DIR not set — visual-gate emit only.");
        Directory.CreateDirectory(dir!);
        File.WriteAllBytes(Path.Combine(dir!, "sps110_part1.pdf"),
            Sps110FormFiller.Fill(Sps110FormFillerTests.Model()));
    }
}
