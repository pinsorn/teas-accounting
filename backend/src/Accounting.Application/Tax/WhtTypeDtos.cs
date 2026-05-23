using FluentValidation;

namespace Accounting.Application.Tax;

public sealed record CreateWhtTypeRequest(
    string Code, string NameTh, string? NameEn,
    string IncomeTypeCode, string FormType, decimal Rate);

/// <summary>Non-rate edit only — rate changes go through ChangeRate (effective-date).</summary>
public sealed record UpdateWhtTypeRequest(
    string NameTh, string? NameEn, string IncomeTypeCode, string FormType);

public sealed record ChangeWhtRateRequest(decimal NewRate, DateOnly EffectiveFrom);

public sealed record WhtTypeListItem(
    int WhtTypeId, string Code, string NameTh, string? NameEn,
    decimal Rate, string FormType, string IncomeTypeCode,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record WhtTypeDetail(
    int WhtTypeId, string Code, string NameTh, string? NameEn,
    decimal Rate, string FormType, string IncomeTypeCode,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public interface IWhtTypeService
{
    Task<int> CreateAsync(CreateWhtTypeRequest req, CancellationToken ct);
    Task UpdateAsync(int id, UpdateWhtTypeRequest req, CancellationToken ct);
    Task DeactivateAsync(int id, CancellationToken ct);
    Task ReactivateAsync(int id, CancellationToken ct);   // Sprint 13f P2
    Task<IReadOnlyList<WhtTypeListItem>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<WhtTypeDetail?> GetAsync(int id, CancellationToken ct);

    /// <summary>Effective-date rate change: close the in-force row
    /// (EffectiveTo = newFrom-1d) + insert a new open row at the new rate.
    /// Posted documents keep their snapshot.</summary>
    Task ChangeRateAsync(int id, ChangeWhtRateRequest req, CancellationToken ct);

    /// <summary>Resolve the rate row in force for <paramref name="code"/> at
    /// <paramref name="docDate"/> (used by Receipt/PV). Null if none.</summary>
    Task<WhtTypeDetail?> ResolveAtDateAsync(string code, DateOnly docDate, CancellationToken ct);
}

public sealed class CreateWhtTypeValidator : AbstractValidator<CreateWhtTypeRequest>
{
    public CreateWhtTypeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches(@"^[A-Z0-9\-]+$")
            .WithMessage("Code must be uppercase letters/digits/hyphen, ≤20 chars.");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
        RuleFor(x => x.IncomeTypeCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.FormType).NotEmpty().Must(f => f is "PND1" or "PND3" or "PND53")
            .WithMessage("FormType must be PND1 / PND3 / PND53.");
        RuleFor(x => x.Rate).InclusiveBetween(0m, 1m);
    }
}

public sealed class UpdateWhtTypeValidator : AbstractValidator<UpdateWhtTypeRequest>
{
    public UpdateWhtTypeValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
        RuleFor(x => x.IncomeTypeCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.FormType).NotEmpty().Must(f => f is "PND1" or "PND3" or "PND53");
    }
}

public sealed class ChangeWhtRateValidator : AbstractValidator<ChangeWhtRateRequest>
{
    public ChangeWhtRateValidator()
    {
        RuleFor(x => x.NewRate).InclusiveBetween(0m, 1m);
    }
}
