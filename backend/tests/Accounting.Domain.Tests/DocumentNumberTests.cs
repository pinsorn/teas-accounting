using Accounting.Domain.ValueObjects;
using FluentAssertions;

namespace Accounting.Domain.Tests;

public class DocumentNumberTests
{
    [Theory]
    [InlineData("05-2026-TI-0001",  2026, 5, "TI", null,   1)]
    [InlineData("12-2026-CN-0042",  2026, 12, "CN", null,  42)]
    [InlineData("05-2026-PV-RENT-0007", 2026, 5, "PV", "RENT", 7)]
    // B1 (2026-06-19) — hyphen-joined sub-prefix (PV with BOTH business-unit AND category).
    [InlineData("05-2026-PV-MKT-RENT-0001", 2026, 5, "PV", "MKT-RENT", 1)]
    public void Parses_valid_numbers(string input, int year, int month, string prefix, string? sub, int seq)
    {
        DocumentNumber.TryParse(input, out var doc).Should().BeTrue();
        doc.Year.Should().Be(year);
        doc.Month.Should().Be(month);
        doc.Prefix.Should().Be(prefix);
        doc.SubPrefix.Should().Be(sub);
        doc.Sequence.Should().Be(seq);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("13-2026-TI-0001")]  // bad month
    [InlineData("05-26-TI-0001")]    // 2-digit year
    public void Rejects_invalid(string input)
        => DocumentNumber.TryParse(input, out _).Should().BeFalse();

    [Fact]
    public void Build_produces_canonical_form_with_subprefix()
        => DocumentNumber.Build(2026, 5, "PV", "RENT", 7).Value.Should().Be("05-2026-PV-RENT-0007");

    [Fact]
    public void Build_omits_subprefix_when_null()
        => DocumentNumber.Build(2026, 5, "TI", null, 1).Value.Should().Be("05-2026-TI-0001");
}
