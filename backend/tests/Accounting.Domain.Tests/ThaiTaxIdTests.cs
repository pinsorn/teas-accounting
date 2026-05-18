using Accounting.Domain.ValueObjects;
using FluentAssertions;

namespace Accounting.Domain.Tests;

public class ThaiTaxIdTests
{
    // Known-valid Thai Tax IDs — last digit is the mod-11 checksum of the first 12.
    // 010555612345 -> check 3 ; 310060018147 -> check 6
    [Theory]
    [InlineData("0105556123453")]
    [InlineData("3100600181476")]
    public void Valid_TaxId_Parses(string input)
    {
        var ok = ThaiTaxId.TryParse(input, out var id);
        ok.Should().BeTrue();
        id.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0000000000000")]      // checksum fails
    [InlineData("0105556123456")]      // checksum fails (correct check is 3)
    [InlineData("notdigits1234")]
    public void Invalid_TaxId_DoesNotParse(string input)
    {
        ThaiTaxId.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Strips_Non_Digit_Separators()
    {
        var ok = ThaiTaxId.TryParse("0-1055-56123-45-3", out var id);
        ok.Should().BeTrue();
        id.Value.Should().Be("0105556123453");
    }

    [Fact]
    public void Display_Formats_With_Hyphens()
    {
        var id = ThaiTaxId.Parse("0105556123453");
        id.ToDisplay().Should().Be("0-1055-56123-45-3");
    }
}
