using Accounting.Domain.ValueObjects;
using FluentAssertions;

namespace Accounting.Domain.Tests;

public class BranchCodeTests
{
    [Theory]
    [InlineData("00000")]
    [InlineData("00001")]
    [InlineData("99999")]
    public void Valid_code_parses(string input)
    {
        BranchCode.TryParse(input, out var c).Should().BeTrue();
        c.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("000000")]
    [InlineData("0000A")]
    [InlineData(null)]
    public void Invalid_code_rejected(string? input)
        => BranchCode.TryParse(input, out _).Should().BeFalse();

    [Fact]
    public void Head_office_label()
    {
        BranchCode.Parse("00000").IsHeadOffice.Should().BeTrue();
        BranchCode.Parse("00000").ToThaiLabel().Should().Be("สำนักงานใหญ่");
    }

    [Fact]
    public void Branch_label_formats_correctly()
    {
        BranchCode.Parse("00007").ToThaiLabel().Should().Be("สาขาที่ 00007");
    }
}
