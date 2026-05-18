using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 9 B1 (R-Q3) — Category is DERIVED from IsExempt/IsZeroRated (single
/// source of truth, no category column); exempt+zero-rated is rejected.
/// </summary>
public sealed class TaxCodeCategoryTests
{
    private static TaxCode Make(bool exempt, bool zero) => new()
    {
        Code = "X", NameTh = "x", CompanyId = 1,
        TaxType = TaxType.Vat, Direction = TaxDirection.Output,
        IsExempt = exempt, IsZeroRated = zero,
    };

    [Theory]
    [InlineData(false, false, "TAXABLE")]
    [InlineData(false, true,  "ZERO_RATED")]
    [InlineData(true,  false, "EXEMPT")]
    public void Category_is_derived_from_booleans(bool exempt, bool zero, string expected)
        => Make(exempt, zero).Category.Should().Be(expected);

    [Fact]
    public void Exempt_and_zero_rated_together_is_rejected()
    {
        var act = () => Make(exempt: true, zero: true).EnsureValid();
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("tax_code.exempt_zerorated_conflict");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Valid_combinations_pass(bool exempt, bool zero)
    {
        var act = () => Make(exempt, zero).EnsureValid();
        act.Should().NotThrow();
    }
}
