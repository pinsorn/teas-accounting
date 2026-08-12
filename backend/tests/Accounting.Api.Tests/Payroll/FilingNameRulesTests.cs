using Accounting.Domain.Common;
using Accounting.Infrastructure.Payroll;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Payroll;

/// <summary>
/// R2/H9 — FilingNameRules.EnsureFilable pure unit tests (no DB, no Postgres fixture). Locks the
/// two failure modes (blank, unencodable-in-cp874) and confirms ordinary Thai input — and an
/// over-length one — pass through untouched. §3.5(c): length/truncation is deliberately NOT this
/// helper's concern (that stays SpsBatchFormat.Txt's job); the last test pins that boundary.
/// </summary>
public sealed class FilingNameRulesTests
{
    [Fact]
    public void Ordinary_thai_name_passes_without_throwing()
    {
        var act = () => FilingNameRules.EnsureFilable("สมชาย", "ชื่อ", "EMP-001");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_value_is_refused(string? value)
    {
        var act = () => FilingNameRules.EnsureFilable(value, "ชื่อ", "EMP-001");
        act.Should().Throw<DomainException>().Which.Code.Should().Be("sso_batch.unencodable_name");
    }

    [Fact]
    public void A_non_cp874_character_is_refused_and_the_code_point_is_named()
    {
        // U+09AE is Bengali "MA" — visually near-identical to, but NOT, Thai ม (U+0E21). Written as
        // the C# escape below (never the literal glyph, per this repo's pre-commit glyph grep). It
        // is a real corruption repro: co5 shipped 37 '?' bytes as an insured person's name because
        // a character in this family silently replaced under the OLD default encoder fallback.
        var name = "สม\u09AEชาย";
        var act = () => FilingNameRules.EnsureFilable(name, "ชื่อ", "EMP-001");
        var ex = act.Should().Throw<DomainException>().Which;
        ex.Code.Should().Be("sso_batch.unencodable_name");
        ex.Message.Should().Contain("EMP-001", "the offending employee must be identifiable");
        ex.Message.Should().Contain("U+09AE", "the offending character's code point must be named");
    }

    [Fact]
    public void An_over_length_name_is_not_refused_here()
    {
        // §3.5(c) — encodability only, NO length rule. A 45-char Thai name is well over every
        // สปส.1-10 field width (30/35/45) but is fully cp874-encodable, so EnsureFilable must let
        // it through; SpsBatchFormat.Txt truncates it downstream, deliberately, not this helper.
        var name = new string('ก', 45);
        var act = () => FilingNameRules.EnsureFilable(name, "ชื่อ", "EMP-001");
        act.Should().NotThrow();
    }
}
