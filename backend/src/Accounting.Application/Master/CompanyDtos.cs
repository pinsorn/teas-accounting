using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateCompanyRequest(string TaxId, string NameTh, string? NameEn, LegalEntityType LegalEntityType,
    DateOnly? RegistrationDate, bool VatRegistered, DateOnly? VatRegisterDate, short FiscalYearStartMonth,
    string? AddressTh, string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Phone, string? Email, decimal? PaidUpCapital = null,
    decimal VatRate = 0.07m, string Pnd30SubmissionMode = "manual");
public sealed record UpdateCompanyRequest(string NameTh, string? NameEn, bool VatRegistered, DateOnly? VatRegisterDate,
    string? AddressTh, string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Phone, string? Email, bool IsActive, decimal? PaidUpCapital = null,
    decimal VatRate = 0.07m, string Pnd30SubmissionMode = "manual");
public sealed record CompanyDto(int CompanyId, string TaxId, string NameTh, string? NameEn, LegalEntityType LegalEntityType,
    bool VatRegistered, string BaseCurrency, bool IsActive, decimal? PaidUpCapital = null,
    decimal VatRate = 0.07m, string Pnd30SubmissionMode = "manual");
/// <summary>Full row for the super-admin edit form — PUT /companies/{id} replaces every
/// updatable field, so the FE must load all of them first (address would be blanked otherwise).</summary>
public sealed record CompanyDetailDto(int CompanyId, string TaxId, string NameTh, string? NameEn,
    LegalEntityType LegalEntityType, DateOnly? RegistrationDate, bool VatRegistered, DateOnly? VatRegisterDate,
    short FiscalYearStartMonth, string? AddressTh, string? SubDistrict, string? District, string? Province,
    string? PostalCode, string? Phone, string? Email, bool IsActive, decimal? PaidUpCapital,
    decimal VatRate, string Pnd30SubmissionMode);

public sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyValidator()
    {
        RuleFor(x => x.TaxId).Must(t => ThaiTaxId.TryParse(t, out _))
            .WithMessage("TaxId must be 13 digits with valid checksum.");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FiscalYearStartMonth).InclusiveBetween((short)1, (short)12);
        RuleFor(x => x.PaidUpCapital).GreaterThanOrEqualTo(0m).When(x => x.PaidUpCapital.HasValue);
        RuleFor(x => x.VatRate).InclusiveBetween(0m, 1m);
        RuleFor(x => x.Pnd30SubmissionMode).Must(m => m is "manual" or "auto")
            .WithMessage("Pnd30SubmissionMode must be 'manual' or 'auto'.");
    }
}

public sealed class UpdateCompanyValidator : AbstractValidator<UpdateCompanyRequest>
{
    public UpdateCompanyValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.PaidUpCapital).GreaterThanOrEqualTo(0m).When(x => x.PaidUpCapital.HasValue);
        RuleFor(x => x.VatRate).InclusiveBetween(0m, 1m);
        RuleFor(x => x.Pnd30SubmissionMode).Must(m => m is "manual" or "auto")
            .WithMessage("Pnd30SubmissionMode must be 'manual' or 'auto'.");
    }
}

public interface ICompanyService
{
    Task<int> CreateAsync(CreateCompanyRequest req, CancellationToken ct);
    Task UpdateAsync(int companyId, UpdateCompanyRequest req, CancellationToken ct);
    Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct);
    Task<CompanyDetailDto> GetAsync(int companyId, CancellationToken ct);
}
