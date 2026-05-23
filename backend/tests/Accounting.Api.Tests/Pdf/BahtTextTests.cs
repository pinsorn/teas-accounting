using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

// Sprint 13j-PDF — guards the C# baht-text port against the FE bath-text.ts.
// Pure unit test (no DB), so it runs even where Testcontainers/Postgres is absent.
public class BahtTextTests
{
    [Theory]
    [InlineData(0, "ศูนย์บาทถ้วน")]
    [InlineData(1, "หนึ่งบาทถ้วน")]
    [InlineData(11, "สิบเอ็ดบาทถ้วน")]
    [InlineData(21, "ยี่สิบเอ็ดบาทถ้วน")]
    [InlineData(25, "ยี่สิบห้าบาทถ้วน")]
    [InlineData(1070, "หนึ่งพันเจ็ดสิบบาทถ้วน")]
    [InlineData(1000000, "หนึ่งล้านบาทถ้วน")]
    public void Of_whole_baht(double n, string expected) =>
        BahtText.Of((decimal)n).Should().Be(expected);

    [Fact]
    public void Of_with_satang() =>
        BahtText.Of(1234.50m).Should().Be("หนึ่งพันสองร้อยสามสิบสี่บาทห้าสิบสตางค์");

    [Fact]
    public void Of_single_satang_unit() =>
        BahtText.Of(0.25m).Should().Be("ศูนย์บาทยี่สิบห้าสตางค์");
}
