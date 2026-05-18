using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 10 A1 — a default WHT type may only sit on a SERVICE /
/// EXEMPT_SERVICE product (goods don't attract service withholding).
/// </summary>
public sealed class ProductValidationTests
{
    private static Product Make(ProductType type, int? whtTypeId) => new()
    {
        CompanyId = 1, ProductCode = "P1", NameTh = "x",
        ProductType = type, DefaultWhtTypeId = whtTypeId,
    };

    [Theory]
    [InlineData(ProductType.Good)]
    [InlineData(ProductType.ExemptGood)]
    public void Wht_type_on_goods_is_rejected(ProductType t)
    {
        var act = () => Make(t, whtTypeId: 12).EnsureValid();
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("product.wht_on_goods");
    }

    [Theory]
    [InlineData(ProductType.Service)]
    [InlineData(ProductType.ExemptService)]
    public void Wht_type_on_service_is_allowed(ProductType t)
    {
        var act = () => Make(t, whtTypeId: 12).EnsureValid();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ProductType.Good)]
    [InlineData(ProductType.Service)]
    public void No_wht_type_is_always_valid(ProductType t)
    {
        var act = () => Make(t, whtTypeId: null).EnsureValid();
        act.Should().NotThrow();
    }

    [Fact]
    public void IsService_tracks_the_type()
    {
        Make(ProductType.Service, null).IsService.Should().BeTrue();
        Make(ProductType.ExemptService, null).IsService.Should().BeTrue();
        Make(ProductType.Good, null).IsService.Should().BeFalse();
        Make(ProductType.ExemptGood, null).IsService.Should().BeFalse();
    }
}
