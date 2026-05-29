using System.Text.RegularExpressions;
using Accounting.TestKit;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>Sprint 14.5 — meta-tests for the §14 anti-collision helper.</summary>
public sealed class TestIdsTests
{
    [Fact]
    public void Suffix_is_8_char_lowercase_alphanumeric()
        => Regex.IsMatch(TestIds.Suffix(), "^[0-9a-f]{8}$").Should().BeTrue();

    [Fact]
    public void CustomerCode_format_is_prefix_dash_suffix()
        => Regex.IsMatch(TestIds.CustomerCode(), "^CUST-[0-9a-f]{8}$").Should().BeTrue();

    [Fact]
    public void Thousand_calls_produce_thousand_unique_values()
    {
        var set = new HashSet<string>();
        for (var i = 0; i < 1000; i++) set.Add(TestIds.VendorCode());
        set.Should().HaveCount(1000);
    }

    [Fact]
    public void TaxId_is_13_digits_starting_0000()
    {
        var id = TestIds.TaxId();
        id.Should().MatchRegex("^0000[0-9]{9}$").And.HaveLength(13);
    }

    [Fact]
    public void FuturePeriod_is_yyyymm_at_least_12_months_out()
    {
        var p = TestIds.FuturePeriod();
        var now = DateTime.UtcNow;
        var floor = now.AddMonths(12);
        var floorYm = floor.Year * 100 + floor.Month;
        p.Should().BeGreaterThanOrEqualTo(floorYm);
        (p % 100).Should().BeInRange(1, 12);   // valid month component
    }

    [Fact]
    public void BusinessUnitCode_respects_20_char_max()
    {
        var c = TestIds.BusinessUnitCode();
        c.Length.Should().BeLessThanOrEqualTo(20);
        c.Should().MatchRegex("^BU[0-9A-F]{8}$");   // cont.75 — widened 3→8 to kill shared-DB collisions
    }
}
