namespace Accounting.Domain.Entities.Master;

/// <summary>
/// Sprint 13d P6 — 1:1 extension of <see cref="Company"/> holding the
/// document-rendering profile. Hybrid lock model (Phase 1):
///
///  * HARD fields (legal name, tax id, registered address, VAT reg date,
///    branch code) — read-only via the UI in Phase 1. Embedded on every Tax
///    Invoice and must match ภ.พ.20; changing them requires ภ.พ.09 +
///    (Phase 2) a 2-person approval flow. The API rejects hard edits (501).
///  * SOFT fields (trade name, logo, contact, banking) — admin-editable.
///
/// One row per company_id (PK == FK to companies).
/// </summary>
public class CompanyProfile
{
    public int CompanyId { get; set; }

    // ---- HARD fields (read-only via UI in Phase 1) ----
    public required string LegalName { get; set; }
    public required string TaxId { get; set; }                 // 13-digit
    public string? RegistrationNumber { get; set; }            // 13-digit (usually == TaxId)
    public required string RegisteredAddressLine1 { get; set; }
    public string? RegisteredAddressLine2 { get; set; }
    public string? RegisteredSubdistrict { get; set; }
    public string? RegisteredDistrict { get; set; }
    public required string RegisteredProvince { get; set; }
    public required string RegisteredPostalCode { get; set; }  // 5-digit
    public DateOnly? VatRegistrationDate { get; set; }
    public string BranchCode { get; set; } = "00000";          // head office

    // ---- SOFT fields (admin-editable) ----
    public string? TradeName { get; set; }
    public string? LogoUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ContactName { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountName { get; set; }

    // ---- Audit ----
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedByUserId { get; set; }
}
