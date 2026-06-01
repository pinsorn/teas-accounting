using FluentValidation;

namespace Accounting.Application.Master;

// Sprint 13d P6 — full profile (hard + soft) for document rendering + the
// /settings/company page. Hard fields are read-only via UI in Phase 1.
public sealed record CompanyProfileDto(
    int CompanyId,
    // hard
    string LegalName,
    string TaxId,
    string? RegistrationNumber,
    string RegisteredAddressLine1,
    string? RegisteredAddressLine2,
    // structured registered address (each part in its own field — RD forms)
    string? RegBuilding,
    string? RegRoomNo,
    string? RegFloor,
    string? RegVillage,
    string? RegHouseNo,
    string? RegMoo,
    string? RegSoi,
    string? RegStreet,
    string? RegisteredSubdistrict,
    string? RegisteredDistrict,
    string RegisteredProvince,
    string RegisteredPostalCode,
    DateOnly? VatRegistrationDate,
    string BranchCode,
    // soft
    string? TradeName,
    string? LogoUrl,
    string? Phone,
    string? Email,
    string? Website,
    string? ContactName,
    string? BankName,
    string? BankAccountNo,
    string? BankAccountName);

// Only soft fields are accepted. Hard fields are never in this request —
// editing them is rejected by the /hard endpoint (501, Phase 2).
public sealed record UpdateCompanyProfileSoftRequest(
    string? TradeName,
    string? LogoUrl,
    string? Phone,
    string? Email,
    string? Website,
    string? ContactName,
    string? BankName,
    string? BankAccountNo,
    string? BankAccountName);

// Registered address (HARD) edit — allowed only after the user confirms (FE modal) that the
// change has been filed with DBD (บอจ.1 + บอจ.4) and, for a VAT registrant, สรรพากร (ภ.พ.09).
// Every change is audited. Building/street are the structured RD-form parts; Line1 is kept in sync.
public sealed record UpdateRegisteredAddressRequest(
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Street,
    string? Subdistrict, string? District, string Province, string PostalCode);

public interface ICompanyProfileService
{
    /// <summary>Profile for the current tenant's company. Any authenticated
    /// role may read (drives the invoice header / nav branding).</summary>
    Task<CompanyProfileDto?> GetAsync(CancellationToken ct);

    /// <summary>Update SOFT fields only (admin / master.company.manage).</summary>
    Task UpdateSoftAsync(UpdateCompanyProfileSoftRequest req, CancellationToken ct);

    /// <summary>Update the HARD registered address (admin-confirmed DBD/ภ.พ.09 filing). Audited.</summary>
    Task UpdateRegisteredAddressAsync(UpdateRegisteredAddressRequest req, CancellationToken ct);

    /// <summary>Sprint 13h P10 — upload a company logo. Stores the file via
    /// the polymorphic attachment service (parent_type=COMPANY_PROFILE) and
    /// rewrites <see cref="CompanyProfile.LogoUrl"/> to the new download URL.
    /// Returns the new URL.</summary>
    Task<string> UpdateLogoAsync(
        string fileName, string mimeType, long sizeBytes, Stream content,
        CancellationToken ct);
}

public sealed class UpdateCompanyProfileSoftValidator
    : AbstractValidator<UpdateCompanyProfileSoftRequest>
{
    public UpdateCompanyProfileSoftValidator()
    {
        // Sprint 13d P5 — i18n-key messages (FE resolves TH/EN).
        RuleFor(x => x.TradeName).MaximumLength(200).WithMessage("validation.maxLength");
        RuleFor(x => x.LogoUrl).MaximumLength(500).WithMessage("validation.maxLength");
        RuleFor(x => x.Phone).MaximumLength(50).WithMessage("validation.maxLength");
        RuleFor(x => x.Email).MaximumLength(200).WithMessage("validation.maxLength")
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("validation.email");
        RuleFor(x => x.Website).MaximumLength(200).WithMessage("validation.maxLength");
        RuleFor(x => x.ContactName).MaximumLength(200).WithMessage("validation.maxLength");
        RuleFor(x => x.BankName).MaximumLength(100).WithMessage("validation.maxLength");
        RuleFor(x => x.BankAccountNo).MaximumLength(50).WithMessage("validation.maxLength");
        RuleFor(x => x.BankAccountName).MaximumLength(200).WithMessage("validation.maxLength");
    }
}
