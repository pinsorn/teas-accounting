using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// Business partner on the AP side (supplier). Same shape as Customer but separate table because
/// (a) WHT defaults differ by relationship, (b) credit-limit/payment-term semantics differ.
/// </summary>
public class Vendor : ITenantOwned
{
    public long VendorId { get; set; }
    public int CompanyId { get; set; }

    public required string VendorCode { get; set; }
    public CustomerType VendorType { get; set; }

    public string? TaxId { get; set; }
    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }

    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    public bool VatRegistered { get; set; }

    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public int PaymentTermDays { get; set; }
    public string DefaultCurrency { get; set; } = "THB";

    /// <summary>Default WHT type code applied when creating a Payment Voucher (overridable).</summary>
    public string? DefaultWhtTypeCode { get; set; }

    // Sprint 8.7 — foreign-vendor / VAT-D support. Existing VatRegistered is
    // reused as the spec's "is_vat_registered" (domestic non-VAT flag) — no
    // duplicate column (Report-Backend13 mechanism note). CHECK: a foreign
    // vendor is always VatRegistered=true (VAT/no-VAT flows via HasThaiVatDReg
    // + ภ.พ.36 reverse charge); HasThaiVatDReg implies IsForeign.
    public bool    IsForeign       { get; set; }
    public bool    HasThaiVatDReg  { get; set; }   // พรบ. e-Service 2564
    public string? CountryCode     { get; set; }   // ISO 3166-1 alpha-2

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
