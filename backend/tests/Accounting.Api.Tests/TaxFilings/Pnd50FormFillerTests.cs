using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// ภ.ง.ด.50 v1 filler — structural gates (no DB). Visual correctness (cell alignment, the right
/// circle ticked) is covered by the Task-5 raster gate; these tests pin the mechanical contract:
/// renders, embeds, geometry present, on-state radio resolution refuses unknown states.
/// </summary>
public sealed class Pnd50FormFillerTests
{
    private static Pnd50Sheet ProfitSheet(bool sme = false) => new(
        BaseAmount: 1_234_567.89m, IsLoss: false,
        TaxComputed: 246_913.58m, WhtCredit: 5_000.25m, Pnd51Prepaid: 10_000m,
        CreditsTotal: 15_000.25m, NetAmount: 231_913.33m, PayMore: true,
        Surcharge: 1_234.56m, TotalAmount: 233_147.89m, IsSme: sme);

    private static Pnd50Sheet LossSheet() => new(
        BaseAmount: 200_000m, IsLoss: true,
        TaxComputed: 0m, WhtCredit: 3_000m, Pnd51Prepaid: 0m,
        CreditsTotal: 3_000m, NetAmount: 3_000m, PayMore: false,
        Surcharge: 0m, TotalAmount: 3_000m, IsSme: false);

    internal static Pnd50Model Model(Pnd50Sheet sheet) => new(
        TaxId: "0105561234567", CompanyName: "บริษัท ทดสอบภาษี จำกัด",
        PeriodStart: new DateOnly(2026, 1, 1), PeriodEnd: new DateOnly(2026, 12, 31),
        Building: "อาคารทดสอบ", RoomNo: "12B", Floor: "3", Village: "หมู่บ้านตัวอย่าง",
        HouseNo: "99/1", Moo: "4", Soi: "สุขใจ 5", Road: "พหลโยธิน",
        SubDistrict: "จตุจักร", District: "จตุจักร", Province: "กรุงเทพมหานคร", PostalCode: "10900",
        Website: "https://example.co.th", Email: "acc@example.co.th",
        HasRelatedPartyOver200M: false,
        Sheet: sheet);

    [Fact]
    public void Fill_renders_nonempty_pdf()
    {
        var pdf = Pnd50FormFiller.Fill(Model(ProfitSheet()));
        pdf.Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
        pdf.Length.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Cells_geometry_loads_and_has_the_v1_combs()
    {
        var cells = Pnd50FormFiller.Cells;
        cells.Should().ContainKey("1").WhoseValue.Should().HaveCount(13);     // taxid grid
        foreach (var box in new[] { "Text661", "662", "665", "666", "669", "670", "671", "672" })
            cells.Should().ContainKey(box).WhoseValue.Should().HaveCount(13,  // 11 baht + 2 satang
                $"box {box} is an 11+2 comb (the 3pt dash gap is not a cell)");
        cells.Should().ContainKey("Text2000-1").WhoseValue.Should().HaveCount(11);
        cells.Should().ContainKey("Text3").WhoseValue.Should().HaveCount(2);
    }

    [Fact]
    public void Loss_overpaid_variant_renders()
    {
        Pnd50FormFiller.Fill(Model(LossSheet())).Length.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Sme_variant_renders()
    {
        Pnd50FormFiller.Fill(Model(ProfitSheet(sme: true))).Length.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Unknown_radio_onstate_throws_instead_of_misticking()
    {
        // Direct render with a wrong on-state — proves the refuse path on the real template.
        var template = typeof(Pnd50FormFiller)
            .GetMethod("Template", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, ["pnd50_main.pdf"]) as byte[];
        var act = () => RdAcroFormFiller.Render(
            template!, [], [new RdRadio("Group5", "ChoiceX")], null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*ChoiceX*");
    }

    [Fact]
    public void Filled_pdf_draws_more_than_a_blank_render()
    {
        var template = typeof(Pnd50FormFiller)
            .GetMethod("Template", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, ["pnd50_main.pdf"]) as byte[];
        var blank = RdAcroFormFiller.Render(template!, [], [], null);
        Pnd50FormFiller.Fill(Model(ProfitSheet())).Length.Should().BeGreaterThan(blank.Length);
    }
}
