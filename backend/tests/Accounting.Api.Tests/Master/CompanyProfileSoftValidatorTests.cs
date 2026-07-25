using Accounting.Application.Master;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// O12 (specs/fix-army-findings-2026-07-22.md) — `เลขที่บัญชี` (10-digit SSO employer
/// registration number) printed blank because there was nowhere to validate it before it fed
/// Sps110FormFiller/SpsBatchFormat (both already exist and both strip non-digits — see
/// Sps110FormFillerTests/SpsBatchFormatTests). The storage field (CompanyProfile.
/// SsoEmployerAccountNo) and FE settings field already existed; this locks in the
/// digits-only/exactly-10/optional rule added to UpdateCompanyProfileSoftValidator. No DB
/// needed — FluentValidation validators are pure.
/// </summary>
public sealed class CompanyProfileSoftValidatorTests
{
    private static UpdateCompanyProfileSoftRequest Req(string? ssoAccountNo) => new(
        TradeName: null, LogoUrl: null, Phone: null, Email: null, Website: null,
        ContactName: null, BankName: null, BankAccountNo: null, BankAccountName: null,
        SsoEmployerAccountNo: ssoAccountNo);

    [Fact]
    public void Null_is_valid_optional_field()
    {
        new UpdateCompanyProfileSoftValidator().Validate(Req(null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_string_is_valid_optional_field()
    {
        new UpdateCompanyProfileSoftValidator().Validate(Req("")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Exactly_10_digits_is_valid()
    {
        new UpdateCompanyProfileSoftValidator().Validate(Req("1001849434")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("123456789")]     // 9 digits — too short
    [InlineData("12345678901")]   // 11 digits — too long
    [InlineData("100184943a")]    // non-digit
    [InlineData("100-184-9434")]  // punctuation, even though it's 10 digits' worth
    public void Anything_other_than_exactly_10_digits_is_rejected(string value)
    {
        var r = new UpdateCompanyProfileSoftValidator().Validate(Req(value));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateCompanyProfileSoftRequest.SsoEmployerAccountNo)
            && e.ErrorMessage == "validation.sso10Digits");
    }
}
