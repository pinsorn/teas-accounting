using Accounting.Application.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// F3 (Tier-2 round 1, specs/pnd2-filing.md §10) — CreateWhtTypeValidator/UpdateWhtTypeValidator
/// only accepted PND1/PND3/PND53, so the 632-seeded INT-IND (FormType=PND2) row 400'd on save in
/// the WHT-types settings UI. Pure FluentValidation unit tests — no DB needed (mirrors
/// VendorVatTaxIdValidatorTests). PND54 carries the same pre-existing hole (FOR-SVC/FOR-ROYAL);
/// intentionally NOT fixed here — logged in troubles-wiki.md instead.
/// </summary>
public sealed class WhtTypeFormValidatorTests
{
    private static CreateWhtTypeRequest CreateReq(string formType) =>
        new(Code: "TST", NameTh: "ทดสอบ", NameEn: null,
            IncomeTypeCode: "4", FormType: formType, Rate: 0.15m);

    private static UpdateWhtTypeRequest UpdateReq(string formType) =>
        new(NameTh: "ทดสอบ", NameEn: null, IncomeTypeCode: "4", FormType: formType);

    [Fact]
    public void Create_accepts_pnd2()
        => new CreateWhtTypeValidator().Validate(CreateReq("PND2")).IsValid.Should().BeTrue();

    [Fact]
    public void Update_accepts_pnd2()
        => new UpdateWhtTypeValidator().Validate(UpdateReq("PND2")).IsValid.Should().BeTrue();

    [Fact]
    public void Create_still_rejects_pnd54()
        => new CreateWhtTypeValidator().Validate(CreateReq("PND54")).IsValid.Should().BeFalse(
            "PND54 (FOR-SVC/FOR-ROYAL) is a separate pre-existing gap, not fixed by F3");
}
