using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateCompanyRequest(string TaxId, string NameTh, string? NameEn, LegalEntityType LegalEntityType,
    DateOnly? RegistrationDate, bool VatRegistered, DateOnly? VatRegisterDate, short FiscalYearStartMonth,
    string? AddressTh, string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Phone, string? Email);
public sealed record UpdateCompanyRequest(string NameTh, string? NameEn, bool VatRegistered, DateOnly? VatRegisterDate,
    string? AddressTh, string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Phone, string? Email, bool IsActive);
public sealed record CompanyDto(int CompanyId, string TaxId, string NameTh, string? NameEn, LegalEntityType LegalEntityType,
    bool VatRegistered, string BaseCurrency, bool IsActive);

public sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyValidator()
    {
        RuleFor(x => x.TaxId).Must(t => ThaiTaxId.TryParse(t, out _))
            .WithMessage("TaxId must be 13 digits with valid checksum.");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FiscalYearStartMonth).InclusiveBetween((short)1, (short)12);
    }
}

public interface ICompanyService
{
    Task<int> CreateAsync(CreateCompanyRequest req, CancellationToken ct);
    Task UpdateAsync(int companyId, UpdateCompanyRequest req, CancellationToken ct);
    Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct);
}
