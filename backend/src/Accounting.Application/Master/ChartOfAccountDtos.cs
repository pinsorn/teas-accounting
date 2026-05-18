using Accounting.Domain.Enums;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateAccountRequest(string AccountCode, string AccountNameTh, string? AccountNameEn,
    AccountType AccountType, long? ParentId, bool IsHeader, NormalBalance NormalBalance);
public sealed record UpdateAccountRequest(string AccountNameTh, string? AccountNameEn, bool IsHeader, bool IsActive);
public sealed record AccountDto(long AccountId, string AccountCode, string AccountNameTh, string? AccountNameEn,
    AccountType AccountType, bool IsHeader, NormalBalance NormalBalance, bool IsActive);

public sealed class CreateAccountValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountValidator()
    {
        RuleFor(x => x.AccountCode).NotEmpty().MaximumLength(20).Matches(@"^\d{2,10}$");
        RuleFor(x => x.AccountNameTh).NotEmpty().MaximumLength(255);
    }
}

public interface IChartOfAccountService
{
    Task<long> CreateAsync(CreateAccountRequest req, CancellationToken ct);
    Task UpdateAsync(long accountId, UpdateAccountRequest req, CancellationToken ct);
    Task<IReadOnlyList<AccountDto>> ListAsync(AccountType? type, bool activeOnly, CancellationToken ct);
}
