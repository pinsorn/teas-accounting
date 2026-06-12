using Accounting.Application.Tax;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Phase C-D — pure p5 schedule builders (no DB). รายการที่ 7 = partition of FY expense rows by
/// the account-code convention; รายการที่ 8 = positive adjustments by LegalRefCode/Label.
/// Both must foot to their ladder rows (8 / 11) or throw — caller bug, never silent.
/// </summary>
public sealed class Pnd50ScheduleTests
{
    private static ExpenseAccountRow Row(string code, decimal amount, string name = "x") =>
        new(code, name, amount);

    [Fact]
    public void ExpenseSchedule_partitions_by_account_code_convention()
    {
        var rows = new[]
        {
            Row("5400", 1_200_000m), Row("5410", 36_000m),   // → 1 พนักงาน
            Row("5100", 240_000m),                           // → 6 ค่าเช่า
            Row("5300", 60_000m), Row("5349", 1_000m),       // → 9 โฆษณา
            Row("5350", 7_000m),                             // → 11 ภาษีอากรอื่น
            Row("5200", 30_000m),                            // → 19 ค่าธรรมเนียมอื่น
            Row("5510", 4_500m),                             // → 12 ต้นทุนทางการเงิน (ร.7 side, not p4 ร.6)
            Row("5990", 12_345.67m),                         // → 22 อื่นๆ (unmapped range)
            Row("ABC", 0.33m),                               // → 22 (unparseable code)
        };
        var total = rows.Sum(r => r.Amount);

        var s = Pnd50FilingService.BuildExpenseSchedule(rows, total);

        s.Employee.Should().Be(1_236_000m);
        s.Rent.Should().Be(240_000m);
        s.Marketing.Should().Be(61_000m);
        s.OtherTaxes.Should().Be(7_000m);
        s.OtherFees.Should().Be(30_000m);
        s.FinanceCost.Should().Be(4_500m);
        s.Other.Should().Be(12_346m);
        s.Total.Should().Be(total);
        // Unmapped lines print explicit zeros.
        (s.DirectorComp + s.Utilities + s.Travel + s.Freight + s.Repairs + s.Entertainment
         + s.SbtTax + s.Bookkeeping + s.AuditFee + s.PoliticalDonation
         + s.CharityDonation + s.EducationSport + s.Consulting + s.BadDebt + s.Depreciation
         + s.DoubleDeduct).Should().Be(0m);
        // Partition identity: the 23 data lines sum to Total.
        (s.Employee + s.Rent + s.Marketing + s.OtherTaxes + s.OtherFees + s.FinanceCost + s.Other)
            .Should().Be(s.Total);
    }

    [Fact]
    public void ExpenseSchedule_foot_mismatch_throws_caller_bug()
    {
        var act = () => Pnd50FilingService.BuildExpenseSchedule(
            new[] { Row("5400", 100m) }, sellingAdminExpenses: 99m);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SellingAdminExpenses*");
    }

    [Fact]
    public void ExpenseSchedule_empty_year_is_all_zero_and_foots_to_zero()
    {
        var s = Pnd50FilingService.BuildExpenseSchedule(Array.Empty<ExpenseAccountRow>(), 0m);
        s.Total.Should().Be(0m);
        s.Employee.Should().Be(0m);
        s.Other.Should().Be(0m);
    }

    private static CitAdjustmentDto Adj(string code, string label, decimal amount, long id = 1) =>
        new(id, 2599, code, label, amount);

    [Fact]
    public void DisallowedSchedule_classifies_by_exact_legal_ref_then_label()
    {
        var adj = new[]
        {
            Adj("ม.65ตรี(6)", "ภาษีเงินได้นิติบุคคล", 10_000m),
            Adj("ม.65ตรี(4)", "ค่ารับรองส่วนเกิน", 15_000m),
            Adj("ม.65ทวิ(9)", "หนี้สูญตัดบัญชีไม่เข้าเกณฑ์", 2_000m),
            Adj("ม.65ตรี(1)", "เงินสำรองทั่วไป", 3_000m),
            // ม.65ตรี(13) must NOT land in the (1) เงินสำรอง bucket — exact-code matching.
            Adj("ม.65ตรี(13)", "รายจ่ายมิใช่เพื่อกิจการ", 5_000m),
            Adj("อื่นๆ", "ค่าปรับจราจร", 500m),
            // Negative adjustments (รายได้ยกเว้น) are ladder row 13 — ignored here.
            Adj("ม.65ทวิ", "เงินปันผลยกเว้น", -7_000m),
        };
        var positives = 10_000m + 15_000m + 2_000m + 3_000m + 5_000m + 500m;

        var s = Pnd50FilingService.BuildDisallowedSchedule(adj, positives);

        s.IncomeTax.Should().Be(10_000m);
        s.Entertainment.Should().Be(15_000m);
        s.BadDebt.Should().Be(2_000m);
        s.Provisions.Should().Be(3_000m);
        s.FromItem7Line23.Should().Be(0m);
        s.Other.Should().Be(5_500m);
        s.Total.Should().Be(positives);
    }

    [Fact]
    public void DisallowedSchedule_foot_mismatch_throws_caller_bug()
    {
        var act = () => Pnd50FilingService.BuildDisallowedSchedule(
            new[] { Adj("ม.65ตรี(4)", "ค่ารับรอง", 100m) }, disallowedExpenses: 99m);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DisallowedExpenses*");
    }

    [Fact]
    public void DisallowedSchedule_empty_is_all_zero()
    {
        var s = Pnd50FilingService.BuildDisallowedSchedule(
            Array.Empty<CitAdjustmentDto>(), 0m);
        s.Total.Should().Be(0m);
    }
}
