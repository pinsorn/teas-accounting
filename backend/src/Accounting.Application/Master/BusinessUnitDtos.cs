using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateBusinessUnitRequest(
    string Code, string NameTh, string? NameEn, long? DefaultRevenueAccountId);

public sealed record UpdateBusinessUnitRequest(
    string NameTh, string? NameEn, long? DefaultRevenueAccountId, bool IsActive);

public sealed record BusinessUnitListItem(
    int BusinessUnitId, string Code, string NameTh, string? NameEn, bool IsActive);

public sealed record BusinessUnitDetail(
    int BusinessUnitId, string Code, string NameTh, string? NameEn,
    long? DefaultRevenueAccountId, bool IsActive);

public interface IBusinessUnitService
{
    Task<int> CreateAsync(CreateBusinessUnitRequest req, CancellationToken ct);
    Task UpdateAsync(int id, UpdateBusinessUnitRequest req, CancellationToken ct);
    Task DeactivateAsync(int id, CancellationToken ct);   // soft (is_active=false)
    Task<IReadOnlyList<BusinessUnitListItem>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<BusinessUnitDetail?> GetAsync(int id, CancellationToken ct);

    /// <summary>Current tenant's company.requires_business_unit opt-in flag
    /// (also the source the frontend reads to drive required-asterisk on forms —
    /// Answer-Sana-Backend9 §7.2 "company context").</summary>
    Task<bool> GetCompanyRequiresBuAsync(CancellationToken ct);
    Task SetCompanyRequiresBuAsync(bool value, CancellationToken ct);
}

public sealed record CompanyBuSetting(bool RequiresBusinessUnit);

public sealed class CreateBusinessUnitValidator : AbstractValidator<CreateBusinessUnitRequest>
{
    public CreateBusinessUnitValidator()
    {
        // Sprint 13d P5 — messages are i18n keys (FE resolves to TH/EN), not
        // hardcoded English. Resolved via frontend/lib/i18n/validation.ts.
        RuleFor(x => x.Code).NotEmpty().WithMessage("validation.required")
            .MaximumLength(20).WithMessage("validation.maxLength")
            .Matches(@"^[A-Z0-9]+$").WithMessage("validation.code.format");
        RuleFor(x => x.NameTh).NotEmpty().WithMessage("validation.required")
            .MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.NameEn).MaximumLength(255).WithMessage("validation.maxLength");
    }
}

public sealed class UpdateBusinessUnitValidator : AbstractValidator<UpdateBusinessUnitRequest>
{
    public UpdateBusinessUnitValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().WithMessage("validation.required")
            .MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.NameEn).MaximumLength(255).WithMessage("validation.maxLength");
    }
}
