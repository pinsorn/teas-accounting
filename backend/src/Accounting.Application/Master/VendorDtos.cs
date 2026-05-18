using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateVendorRequest(string VendorCode, CustomerType VendorType, string NameTh, string? NameEn,
    string? TaxId, string? BranchCode, string? BranchName, bool VatRegistered, string? Address,
    string? ContactPerson, string? Phone, string? Email, int PaymentTermDays, string DefaultCurrency,
    string? DefaultWhtTypeCode,
    bool IsForeign = false, bool HasThaiVatDReg = false, string? CountryCode = null);
public sealed record UpdateVendorRequest(string NameTh, string? NameEn, string? TaxId, string? BranchCode, string? BranchName,
    bool VatRegistered, string? Address, string? ContactPerson, string? Phone, string? Email,
    int PaymentTermDays, string DefaultCurrency, string? DefaultWhtTypeCode, bool IsActive,
    bool IsForeign = false, bool HasThaiVatDReg = false, string? CountryCode = null);
public sealed record VendorDto(long VendorId, string VendorCode, CustomerType VendorType, string NameTh, string? TaxId, bool VatRegistered, bool IsActive);
public sealed record VendorDetailDto(long VendorId, string VendorCode, CustomerType VendorType,
    string NameTh, string? NameEn, string? TaxId, string? BranchCode, string? BranchName,
    bool VatRegistered, string? Address, string? ContactPerson, string? Phone, string? Email,
    int PaymentTermDays, string DefaultCurrency, string? DefaultWhtTypeCode, bool IsActive,
    bool IsForeign, bool HasThaiVatDReg, string? CountryCode);

/// <summary>Sprint 8.7 — small allowlist of common ISO 3166-1 alpha-2 codes
/// (full DTA list is Phase 2). Validation only; storage is the 2-char code.</summary>
public static class CountryCodes
{
    public static readonly IReadOnlySet<string> Common = new HashSet<string>
    {
        "US","SG","IE","JP","GB","DE","AU","CN","IN","NL","CA","FR",
        "HK","KR","TW","MY","VN","ID","PH","CH","SE","NZ","AE","LU",
    };
}

public sealed class CreateVendorValidator : AbstractValidator<CreateVendorRequest>
{
    public CreateVendorValidator()
    {
        RuleFor(x => x.VendorCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.TaxId).Must(t => string.IsNullOrEmpty(t) || ThaiTaxId.TryParse(t, out _))
            .WithMessage("Invalid Thai Tax ID (13 digits + checksum).");
        RuleFor(x => x.BranchCode).Must(b => string.IsNullOrEmpty(b) || BranchCode.TryParse(b, out _))
            .WithMessage("BranchCode must be exactly 5 digits.");
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
        // Sprint 8.7 — foreign-vendor flag rules (mirror the DB CHECKs).
        RuleFor(x => x.HasThaiVatDReg).Must((r, v) => !v || r.IsForeign)
            .WithMessage("VAT-D registration requires a foreign vendor.");
        RuleFor(x => x.VatRegistered).Must((r, v) => !r.IsForeign || v)
            .WithMessage("Foreign vendors must be VAT-registered (VAT flows via VAT-D / ภ.พ.36).");
        RuleFor(x => x.CountryCode)
            .Must(c => !string.IsNullOrEmpty(c) && c.Length == 2 && c == c.ToUpperInvariant()
                       && CountryCodes.Common.Contains(c))
            .When(x => x.IsForeign)
            .WithMessage("A valid ISO 3166-1 alpha-2 country code is required for foreign vendors.");
    }
}

public sealed class UpdateVendorValidator : AbstractValidator<UpdateVendorRequest>
{
    public UpdateVendorValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.TaxId).Must(t => string.IsNullOrEmpty(t) || ThaiTaxId.TryParse(t, out _))
            .WithMessage("Invalid Thai Tax ID (13 digits + checksum).");
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
        RuleFor(x => x.HasThaiVatDReg).Must((r, v) => !v || r.IsForeign)
            .WithMessage("VAT-D registration requires a foreign vendor.");
        RuleFor(x => x.VatRegistered).Must((r, v) => !r.IsForeign || v)
            .WithMessage("Foreign vendors must be VAT-registered (VAT flows via VAT-D / ภ.พ.36).");
        RuleFor(x => x.CountryCode)
            .Must(c => !string.IsNullOrEmpty(c) && c.Length == 2 && c == c.ToUpperInvariant()
                       && CountryCodes.Common.Contains(c))
            .When(x => x.IsForeign)
            .WithMessage("A valid ISO 3166-1 alpha-2 country code is required for foreign vendors.");
    }
}

public interface IVendorService
{
    Task<long> CreateAsync(CreateVendorRequest req, CancellationToken ct);
    Task UpdateAsync(long vendorId, UpdateVendorRequest req, CancellationToken ct);
    Task<IReadOnlyList<VendorDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<VendorDetailDto?> GetByIdAsync(long vendorId, CancellationToken ct);
}
