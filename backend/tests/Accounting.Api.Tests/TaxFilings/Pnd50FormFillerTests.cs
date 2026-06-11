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

    // Worked p3 ladder (profit books, +50k/−20k adjustments, 40k loss c/f) — foots row by row.
    internal static Pnd50Ladder Ladder(decimal sign = 1m) => new(
        DirectRevenue: 5_000_000m, CostOfSales: 0m, GrossProfit: 5_000_000m,
        OtherIncome: 0m, Total5: 5_000_000m, OtherExpenses: 0m, Total7: 5_000_000m,
        SellingAdminExpenses: 4_900_000m, AccountingNetProfit: 100_000m * sign,
        IncomeAdditions: 0m, DisallowedExpenses: sign > 0 ? 50_000m : 0m,
        Total12: sign > 0 ? 150_000m : -100_000m,
        ExemptDeductions: sign > 0 ? 20_000m : 0m,
        Total14: sign > 0 ? 130_000m : -100_000m,
        LossCarryForward: sign > 0 ? 40_000m : 0m,
        Total16: sign > 0 ? 90_000m : -100_000m,
        Excess10Pct: 0m, CharityExcess: 0m, EducationExcess: 0m,
        Total20: sign > 0 ? 90_000m : -100_000m,
        TaxableNetProfit: sign > 0 ? 90_000m : -100_000m);

    internal static Pnd50BalanceSheetBoxes Boxes(decimal retained = 321_098.76m) => new(
        CashAndEquivalents: 111_111.11m, TradeReceivables: 222_222.22m, Inventory: 0m,
        OtherCurrentAssets: 33_333.33m, OtherNonCurrentAssets: 444_444.44m,
        TotalAssets: 811_111.10m,
        TradePayables: 123_456.78m, OtherCurrentLiabilities: 56_789.01m,
        OtherNonCurrentLiabilities: 9_766.55m, TotalLiabilities: 190_012.34m,
        PaidUpShareCapital: 300_000m, OtherEquity: 0m, RetainedEarnings: retained,
        TotalEquity: 300_000m + retained, TotalLiabilitiesAndEquity: 811_111.10m);

    internal static Pnd50Model Model(Pnd50Sheet sheet) => new(
        TaxId: "0105561234567", CompanyName: "บริษัท ทดสอบภาษี จำกัด",
        PeriodStart: new DateOnly(2026, 1, 1), PeriodEnd: new DateOnly(2026, 12, 31),
        Building: "อาคารทดสอบ", RoomNo: "12B", Floor: "3", Village: "หมู่บ้านตัวอย่าง",
        HouseNo: "99/1", Moo: "4", Soi: "สุขใจ 5", Road: "พหลโยธิน",
        SubDistrict: "จตุจักร", District: "จตุจักร", Province: "กรุงเทพมหานคร", PostalCode: "10900",
        Website: "https://example.co.th", Email: "acc@example.co.th",
        HasRelatedPartyOver200M: false,
        Sheet: sheet,
        Ladder: Ladder(sheet.IsLoss ? -1m : 1m),
        BalanceSheet: Boxes(sheet.IsLoss ? -321_098.76m : 321_098.76m));

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

    [Fact]
    public void Cells_geometry_has_every_v2_p3_p6_comb()
    {
        var cells = Pnd50FormFiller.Cells;
        string[] p3 = ["Text17.4", "Text17.7", "Text17.10", "Text17.13", "Text17.16",
                       "Text17.19", "Text17.22", "Text17.25", "Text17.28", "Text17.31",
                       "Text17.34", "Text17.37", "Text17.40", "Text17.43", "Text20", "Text23",
                       "Text26", "Text29", "Text32", "Text35.1", "Text35.2",
                       "Text35.5", "Text35.8", "Text35.11", "Text35.14", "Text35.17",
                       "Text35.20", "Text35.23", "Text35.26", "Text35.29"];
        string[] p6 = ["Text35.210", "Text35.211", "Text35.212", "Text35.213", "Text35.214",
                       "Text35.215", "Text35.216", "Text35.217", "Text35.218", "Text35.219",
                       "Text35.220", "Text35.221", "Text35.222", "Text35.223", "Text35.224",
                       "Text35.225", "Text35.226", "Text35.2241", "Text35.2251", "Text35.2261",
                       "Text35.2242", "Text35.2252"];
        foreach (var box in p3.Concat(p6))
            cells.Should().ContainKey(box).WhoseValue.Should().HaveCount(13,
                $"box {box} is an 11+2 comb on p3/p6");
        // ทุนจดทะเบียน (155) is a plain box, NOT a comb — must stay OUT of the cell map
        // (a 1-cell entry would stack every character at one X).
        cells.Should().NotContainKey("Text35.227");
    }
}
