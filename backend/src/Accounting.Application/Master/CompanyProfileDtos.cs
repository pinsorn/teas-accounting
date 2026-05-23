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

public interface ICompanyProfileService
{
    /// <summary>Profile for the current tenant's company. Any authenticated
    /// role may read (drives the invoice header / nav branding).</summary>
    Task<CompanyProfileDto?> GetAsync(CancellationToken ct);

    /// <summary>Update SOFT fields only (admin / master.company.manage).</summary>
    Task UpdateSoftAsync(UpdateCompanyProfileSoftRequest req, CancellationToken ct);

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
