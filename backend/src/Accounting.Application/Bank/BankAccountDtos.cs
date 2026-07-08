using FluentValidation;

namespace Accounting.Application.Bank;

// Bank reconciliation (specs/bank-reconciliation.md B1.4) — bank-account master CRUD DTOs.
// GlCashAccountId is optional on create (defaults to the company's 1120 account, D6) and
// required on update.

public sealed record CreateBankAccountRequest(
    string BankCode, string BankName, string AccountNo, string? AccountName,
    string? AccountType, long? GlCashAccountId, string? Currency);

public sealed record UpdateBankAccountRequest(
    string BankName, string? AccountName, string? AccountType,
    long GlCashAccountId, string Currency, bool IsActive);

public sealed record BankAccountListItem(
    int BankAccountId, string BankCode, string BankName, string AccountNo,
    string? AccountName, long GlCashAccountId, string Currency, bool IsActive);

public sealed record BankAccountDetail(
    int BankAccountId, string BankCode, string BankName, string AccountNo,
    string? AccountName, string? AccountType, long GlCashAccountId, string Currency, bool IsActive);

public interface IBankAccountService
{
    Task<int> CreateAsync(CreateBankAccountRequest req, CancellationToken ct);
    Task UpdateAsync(int id, UpdateBankAccountRequest req, CancellationToken ct);
    Task DeactivateAsync(int id, CancellationToken ct);   // soft (is_active=false)
    Task<IReadOnlyList<BankAccountListItem>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<BankAccountDetail?> GetAsync(int id, CancellationToken ct);
}

public sealed class CreateBankAccountValidator : AbstractValidator<CreateBankAccountRequest>
{
    public CreateBankAccountValidator()
    {
        // Messages are i18n keys (FE resolves to TH/EN) — mirrors CreateBusinessUnitValidator.
        RuleFor(x => x.BankCode).NotEmpty().WithMessage("validation.required")
            .MaximumLength(20).WithMessage("validation.maxLength");
        RuleFor(x => x.BankName).NotEmpty().WithMessage("validation.required")
            .MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.AccountNo).NotEmpty().WithMessage("validation.required")
            .MaximumLength(50).WithMessage("validation.maxLength");
        RuleFor(x => x.AccountName).MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.AccountType).MaximumLength(50).WithMessage("validation.maxLength");
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrWhiteSpace(x.Currency))
            .WithMessage("validation.currency");
    }
}

public sealed class UpdateBankAccountValidator : AbstractValidator<UpdateBankAccountRequest>
{
    public UpdateBankAccountValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().WithMessage("validation.required")
            .MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.AccountName).MaximumLength(255).WithMessage("validation.maxLength");
        RuleFor(x => x.AccountType).MaximumLength(50).WithMessage("validation.maxLength");
        RuleFor(x => x.Currency).NotEmpty().WithMessage("validation.required")
            .Length(3).WithMessage("validation.currency");
        RuleFor(x => x.GlCashAccountId).GreaterThan(0).WithMessage("validation.required");
    }
}
