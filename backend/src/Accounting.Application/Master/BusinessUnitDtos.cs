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
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches(@"^[A-Z0-9]+$")
            .WithMessage("Code must be uppercase letters/digits, ≤20 chars.");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
    }
}

public sealed class UpdateBusinessUnitValidator : AbstractValidator<UpdateBusinessUnitRequest>
{
    public UpdateBusinessUnitValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
    }
}
