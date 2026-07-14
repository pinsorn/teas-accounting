using Accounting.Application.Master;
using Accounting.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// WP1.4 (F13, specs/fix-purchase-ux-findings-2026-07-14.md) — a domestic VAT-registered
/// vendor must carry a valid 13-digit Thai Tax ID (required to claim input VAT / ภ.พ.30).
/// Foreign vendors are force VAT-registered (MasterDataServices.cs VendorService) but have no
/// Thai taxId, so the rule scopes to !IsForeign. Pure FluentValidation unit tests (mirrors the
/// CreatePaymentVoucherValidator tests in Sprint87ForeignVendorTests) — no DB needed. Both
/// CreateVendorValidator and UpdateVendorValidator are wired at the REST endpoint
/// (MasterEndpoints.cs) AND the MCP tool (TeasMcpTools.cs), so a pure-unit pass here covers
/// both entry points without an HTTP-level test.
/// </summary>
public sealed class VendorVatTaxIdValidatorTests
{
    // Valid mod-11 Thai Tax ID already used elsewhere in this test suite (Sprint55/Sprint87
    // vendor seeds, TestCompanyFactory's demo customer) — reused here for consistency.
    private const string ValidTaxId = "0105556123453";

    private static CreateVendorRequest CreateReq(bool vatRegistered, bool isForeign, string? taxId) =>
        new(
            VendorCode: "V-" + Guid.NewGuid().ToString("N")[..8], VendorType: CustomerType.Corporate,
            NameTh: "ผู้ขายทดสอบ", NameEn: null, TaxId: taxId, BranchCode: null, BranchName: null,
            VatRegistered: vatRegistered, Address: null, ContactPerson: null, Phone: null, Email: null,
            PaymentTermDays: 30, DefaultCurrency: "THB", DefaultWhtTypeCode: null,
            IsForeign: isForeign, HasThaiVatDReg: false, CountryCode: isForeign ? "US" : null);

    private static UpdateVendorRequest UpdateReq(bool vatRegistered, bool isForeign, string? taxId) =>
        new(
            NameTh: "ผู้ขายทดสอบ", NameEn: null, TaxId: taxId, BranchCode: null, BranchName: null,
            VatRegistered: vatRegistered, Address: null, ContactPerson: null, Phone: null, Email: null,
            PaymentTermDays: 30, DefaultCurrency: "THB", DefaultWhtTypeCode: null, IsActive: true,
            IsForeign: isForeign, HasThaiVatDReg: false, CountryCode: isForeign ? "US" : null);

    [Fact]
    public void Create_DomesticVatRegistered_NoTaxId_Fails()
    {
        var r = new CreateVendorValidator().Validate(CreateReq(vatRegistered: true, isForeign: false, taxId: null));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage == "vendor.vat_registered_requires_taxid");
    }

    [Fact]
    public void Create_DomesticVatRegistered_ValidTaxId_Passes()
    {
        var r = new CreateVendorValidator().Validate(CreateReq(vatRegistered: true, isForeign: false, taxId: ValidTaxId));
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_ForeignVatRegistered_NoThaiTaxId_Passes()
    {
        var r = new CreateVendorValidator().Validate(CreateReq(vatRegistered: true, isForeign: true, taxId: null));
        r.IsValid.Should().BeTrue("foreign vendors are force VAT-registered but have no Thai taxId");
    }

    [Fact]
    public void Create_NonVatRegistered_NoTaxId_Passes()
    {
        var r = new CreateVendorValidator().Validate(CreateReq(vatRegistered: false, isForeign: false, taxId: null));
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_DomesticVatRegistered_NoTaxId_Fails()
    {
        var r = new UpdateVendorValidator().Validate(UpdateReq(vatRegistered: true, isForeign: false, taxId: null));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage == "vendor.vat_registered_requires_taxid");
    }

    [Fact]
    public void Update_DomesticVatRegistered_ValidTaxId_Passes()
    {
        var r = new UpdateVendorValidator().Validate(UpdateReq(vatRegistered: true, isForeign: false, taxId: ValidTaxId));
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_ForeignVatRegistered_NoThaiTaxId_Passes()
    {
        var r = new UpdateVendorValidator().Validate(UpdateReq(vatRegistered: true, isForeign: true, taxId: null));
        r.IsValid.Should().BeTrue();
    }
}
