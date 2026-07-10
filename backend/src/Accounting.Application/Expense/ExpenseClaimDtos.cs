using FluentValidation;

namespace Accounting.Application.Expense;

public sealed record ExpenseClaimLineInput(
    int      ExpenseCategoryId,
    long?    ExpenseAccountId,   // null -> resolve from the category's default
    string   Description,
    DateOnly? ExpenseDate,
    decimal  Amount,
    int?     TaxCodeId,
    decimal  VatRate,
    bool     IsRecoverableVat);

public sealed record CreateExpenseClaimRequest(
    long    EmployeeId,
    DateOnly ClaimDate,
    string? Title,
    string? Notes,
    IReadOnlyList<ExpenseClaimLineInput> Lines,
    int?    BusinessUnitId = null);

public sealed record UpdateExpenseClaimRequest(
    long    EmployeeId,
    DateOnly ClaimDate,
    string? Title,
    string? Notes,
    IReadOnlyList<ExpenseClaimLineInput> Lines,
    int?    BusinessUnitId = null);

public sealed record PayExpenseClaimRequest(string PaymentMethod, int? BankAccountId);

public sealed record RejectExpenseClaimRequest(string Reason);

public sealed record ExpenseClaimApprovedResult(long ExpenseClaimId, long ApprovedBy, DateTimeOffset ApprovedAt);

public sealed record ExpenseClaimPaidResult(
    long ExpenseClaimId, string DocNo, DateTimeOffset PaidAt, decimal TotalAmount, long JournalEntryId);

public sealed record ExpenseClaimListItem(
    long ExpenseClaimId, string? DocNo, long EmployeeId, string EmployeeName,
    DateOnly ClaimDate, string Status, decimal TotalAmount);

public sealed record ExpenseClaimLineDetail(
    long ExpenseClaimLineId, int LineNo, int ExpenseCategoryId, string CategoryName,
    long ExpenseAccountId, string Description, DateOnly? ExpenseDate,
    decimal Amount, int? TaxCodeId, decimal VatRate, decimal VatAmount,
    bool IsRecoverableVat, decimal LineTotal);

public sealed record ExpenseClaimDetail(
    long ExpenseClaimId, string? DocNo, long EmployeeId, string EmployeeName,
    DateOnly ClaimDate, string? Title, string Status,
    string? PaymentMethod, int? BankAccountId,
    decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    long? JournalEntryId, string? RejectReason, string? Notes,
    int? BusinessUnitId, long Version,
    IReadOnlyList<ExpenseClaimLineDetail> Lines);

public sealed class ExpenseClaimLineInputValidator : AbstractValidator<ExpenseClaimLineInput>
{
    public ExpenseClaimLineInputValidator()
    {
        RuleFor(x => x.ExpenseCategoryId).GreaterThan(0);
        RuleFor(x => x.ExpenseAccountId).Must(v => v is null || v > 0)
            .WithMessage("ExpenseAccountId must be null (use category default) or > 0.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.VatRate).InclusiveBetween(0m, 1m);
    }
}

public sealed class CreateExpenseClaimValidator : AbstractValidator<CreateExpenseClaimRequest>
{
    public CreateExpenseClaimValidator()
    {
        RuleFor(x => x.EmployeeId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new ExpenseClaimLineInputValidator());
    }
}

public sealed class UpdateExpenseClaimValidator : AbstractValidator<UpdateExpenseClaimRequest>
{
    public UpdateExpenseClaimValidator()
    {
        RuleFor(x => x.EmployeeId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new ExpenseClaimLineInputValidator());
    }
}

public sealed class PayExpenseClaimValidator : AbstractValidator<PayExpenseClaimRequest>
{
    public PayExpenseClaimValidator()
    {
        RuleFor(x => x.PaymentMethod).NotEmpty()
            .Must(m => m == "CASH" || m == "TRANSFER")
            .WithMessage("paymentMethod must be CASH or TRANSFER.");
        RuleFor(x => x.BankAccountId).NotNull().When(x => x.PaymentMethod == "TRANSFER")
            .WithMessage("TRANSFER payment requires a bankAccountId.");
    }
}

public sealed class RejectExpenseClaimValidator : AbstractValidator<RejectExpenseClaimRequest>
{
    public RejectExpenseClaimValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
