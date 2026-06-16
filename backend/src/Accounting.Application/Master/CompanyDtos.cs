using System.Linq;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateCompanyRequest(string TaxId, string NameTh, string? NameEn, LegalEntityType LegalEntityType,
    DateOnly? RegistrationDate, bool VatRegistered, DateOnly? VatRegisterDate, short FiscalYearStartMonth,
    string? AddressTh, string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Phone, string? Email, decimal? PaidUpCapital = null,
    decimal VatRate = 0.07m, string Pnd30SubmissionMode = "manual",
    // Founding registered-address parts (ม.86/4) — written ONCE into the new company's
    // company_profile.Reg* fields so every RD form (ภ.พ.30 / ภ.ง.ด.3/53/54 / 50ทวิ) renders
    // each address box. Subdistrict/District/Province/PostalCode reuse the fields above.
    // Street-level parts are additive & optional; Province + PostalCode are required (validator).
    string? RegHouseNo = null, string? RegMoo = null, string? RegSoi = null, string? RegStreet = null,
    string? RegBuilding = null, string? RegRoomNo = null, string? RegFloor = null, string? RegVillage = null);
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
        // ม.86/4 — the founding registered address is the company's tax identity; Province +
        // PostalCode are mandatory so the new company_profile (and every RD form) has a real
        // address. Enforced at the API boundary (browser-side checks alone are bypassable).
        RuleFor(x => x.Province).NotEmpty().WithMessage("Province is required (founding registered address, ม.86/4).");
        RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{5}$")
            .WithMessage("PostalCode must be 5 digits (founding registered address, ม.86/4).");
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

/// <summary>
/// Single source of truth for composing the legacy free-text registered address line
/// (<c>company_profile.RegisteredAddressLine1</c> / <c>companies.AddressTh</c>) from the
/// granular Thai address parts. Both the founding path (company creation) and the ภ.พ.09
/// hard-edit path call this so the founding and edited address format IDENTICALLY — divergent
/// formatting between the two is exactly what a สรรพากร audit would flag (ม.86/4).
/// </summary>
public static class ThaiRegisteredAddress
{
    public static string ComposeLine1(
        string? houseNo, string? building, string? roomNo, string? floor,
        string? village, string? moo, string? soi, string? street)
    {
        var parts = new[]
        {
            houseNo,
            string.IsNullOrWhiteSpace(building) ? null : $"อาคาร{building}",
            string.IsNullOrWhiteSpace(roomNo) ? null : $"ห้อง {roomNo}",
            string.IsNullOrWhiteSpace(floor) ? null : $"ชั้น {floor}",
            string.IsNullOrWhiteSpace(village) ? null : $"หมู่บ้าน{village}",
            string.IsNullOrWhiteSpace(moo) ? null : $"หมู่ {moo}",
            string.IsNullOrWhiteSpace(soi) ? null : $"ซ.{soi}",
            string.IsNullOrWhiteSpace(street) ? null : $"ถ.{street}",
        }.Where(s => !string.IsNullOrWhiteSpace(s));
        var line = string.Join(" ", parts).Trim();
        return line.Length == 0 ? "-" : line;
    }
}

public interface ICompanyService
{
    Task<int> CreateAsync(CreateCompanyRequest req, CancellationToken ct);
    Task UpdateAsync(int companyId, UpdateCompanyRequest req, CancellationToken ct);
    Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct);
    Task<CompanyDetailDto> GetAsync(int companyId, CancellationToken ct);
}
