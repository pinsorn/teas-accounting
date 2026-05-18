using Accounting.Domain.Entities.Tax;
using FluentAssertions;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 8.6 — WhtType is an anemic effective-dated rate row (behaviour lives in
/// WhtTypeService + the GL/receipt path, integration-tested in
/// Sprint86ArWhtTests). The domain-level invariants worth pinning: a fresh row
/// is in force (EffectiveTo null) from the safe backfill date, and the WHT
/// amount convention is base × rate rounded half-up to 2dp.
/// </summary>
public class WhtTypeTests
{
    [Fact]
    public void New_wht_type_is_in_force_from_the_safe_backfill_date()
    {
        var w = new WhtType
        {
            CompanyId = 1, Code = "SVC", NameTh = "ค่าบริการ",
            IncomeTypeCode = "3", Rate = 0.03m,
        };
        w.EffectiveFrom.Should().Be(new DateOnly(2020, 1, 1));
        w.EffectiveTo.Should().BeNull();   // currently in force
        w.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(4000, 0.03, 120.00)]
    [InlineData(1234.55, 0.05, 61.73)]   // half-up at 2dp
    [InlineData(1000, 0.0075, 7.50)]
    public void Wht_amount_is_base_times_rate_half_up_2dp(
        decimal @base, decimal rate, decimal expected) =>
        Math.Round(@base * rate, 2, MidpointRounding.AwayFromZero)
            .Should().Be(expected);
}
