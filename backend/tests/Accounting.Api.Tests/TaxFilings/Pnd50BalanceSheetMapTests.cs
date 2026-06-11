using Accounting.Application.Reports;
using Accounting.Infrastructure.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// ภ.ง.ด.50 v2 — pure p6 งบแสดงฐานะการเงิน classifier (spec pnd50-v2-dashboard.md §4).
/// GL accounts route to the form's named lines by the TEAS 4-digit account-code convention;
/// anything unmatched lands in the section's honest "อื่น (นอกจากที่ระบุ)" line. The mapped
/// totals must reproduce the BalanceSheetReport totals exactly (asserted in the mapper).
/// </summary>
public sealed class Pnd50BalanceSheetMapTests
{
    private static BalanceSheetReport Bs(
        (string Code, decimal Bal)[] assets, (string Code, decimal Bal)[] liabs,
        (string Code, decimal Bal)[] equity, decimal currentEarnings)
    {
        static BalanceSheetSection Sec((string Code, decimal Bal)[] rows) => new(
            rows.Select(r => new BalanceSheetRow(r.Code, "x", r.Bal)).ToList(),
            rows.Sum(r => r.Bal));
        var a = Sec(assets);
        var l = Sec(liabs);
        var e = Sec(equity);
        return new BalanceSheetReport(new DateOnly(2026, 12, 31), 1, a, l, e,
            currentEarnings, l.Total + e.Total + currentEarnings,
            a.Total == l.Total + e.Total + currentEarnings, "");
    }

    [Fact]
    public void Classifies_by_account_code_convention()
    {
        var bs = Bs(
            assets: [("1110", 10m), ("1120", 20m), ("1130", 30m), ("1145", 5m), ("1170", 7m), ("1510", 100m)],
            liabs:  [("2110", 40m), ("2151", 9m), ("2510", 50m)],
            equity: [("3100", 60m), ("3200", 11m), ("3900", 2m)],
            currentEarnings: 0m);
        var b = Pnd50FilingService.MapBalanceSheet(bs);

        b.CashAndEquivalents.Should().Be(30m);        // 140 = 1110+1120
        b.TradeReceivables.Should().Be(30m);          // 141 = 1130
        b.Inventory.Should().Be(5m);                  // 142 = 114x
        b.OtherCurrentAssets.Should().Be(7m);         // 143 = 1170
        b.OtherNonCurrentAssets.Should().Be(100m);    // 148 = 15xx
        b.TotalAssets.Should().Be(172m);
        b.TradePayables.Should().Be(40m);             // 150 = 2110
        b.OtherCurrentLiabilities.Should().Be(9m);    // 152
        b.OtherNonCurrentLiabilities.Should().Be(50m);// 154 = 25xx
        b.TotalLiabilities.Should().Be(99m);
        b.PaidUpShareCapital.Should().Be(60m);        // 156 = 31xx
        b.RetainedEarnings.Should().Be(11m);          // 158-159 = 32xx
        b.OtherEquity.Should().Be(2m);                // 157
        b.TotalEquity.Should().Be(73m);
        b.TotalLiabilitiesAndEquity.Should().Be(bs.LiabilitiesAndEquityTotal);
    }

    [Fact]
    public void Current_period_earnings_fold_into_retained()
    {
        var bs = Bs([("1110", 100m)], [], [("3200", 30m)], currentEarnings: 70m);
        var b = Pnd50FilingService.MapBalanceSheet(bs);
        b.RetainedEarnings.Should().Be(100m);
        b.TotalEquity.Should().Be(100m);
        b.TotalLiabilitiesAndEquity.Should().Be(100m);
    }

    [Fact]
    public void Negative_retained_stays_signed_for_group91()
    {
        var bs = Bs([("1110", 10m)], [], [("3200", -40m)], currentEarnings: 25m);
        Pnd50FilingService.MapBalanceSheet(bs).RetainedEarnings.Should().Be(-15m);
    }

    [Fact]
    public void Unparseable_code_lands_in_the_other_bucket()
    {
        var bs = Bs([("1110", 1m), ("ABC", 5m)], [("20", 3m)], [("EQ-X", 4m)], 0m);
        var b = Pnd50FilingService.MapBalanceSheet(bs);
        b.OtherCurrentAssets.Should().Be(5m);
        b.OtherCurrentLiabilities.Should().Be(3m);
        b.OtherEquity.Should().Be(4m);
    }

    [Fact]
    public void Seeded_demo_chart_maps_cleanly()
    {
        // The dev-seed CoA (120/230/482 scripts): every account must land on a named/other line
        // with the totals reproducing the report (the mapper throws if not).
        var bs = Bs(
            assets: [("1110", 1m), ("1120", 2m), ("1130", 3m), ("1170", 4m), ("1180", 5m)],
            liabs:  [("2110", 1m), ("2151", 2m), ("2152", 3m), ("2153", 4m), ("2160", 5m), ("2170", 6m)],
            equity: [],
            currentEarnings: -6m);
        var b = Pnd50FilingService.MapBalanceSheet(bs);
        b.TotalAssets.Should().Be(15m);
        b.OtherCurrentAssets.Should().Be(9m);          // 1170+1180
        b.TotalLiabilities.Should().Be(21m);
        b.RetainedEarnings.Should().Be(-6m);
        b.TotalLiabilitiesAndEquity.Should().Be(15m);
    }
}
