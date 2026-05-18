using FluentValidation;

namespace Accounting.Application.Ledger;

public sealed record JournalLineInput(
    long AccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    string? Description,
    string? Reference,
    string? DimensionsJson);

public sealed record CreateJournalRequest(
    DateOnly DocDate,
    DateOnly PostingDate,
    string Description,
    string? Reference,
    string CurrencyCode,
    decimal ExchangeRate,
    IReadOnlyList<JournalLineInput> Lines);

public sealed record JournalPostedResult(long JournalId, string DocNo, DateTimeOffset PostedAt);

public sealed class CreateJournalValidator : AbstractValidator<CreateJournalRequest>
{
    public CreateJournalValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count >= 2)
            .WithMessage("A journal needs at least 2 lines (debit + credit).");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountId).GreaterThan(0);
            line.RuleFor(l => l.DebitAmount).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.CreditAmount).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l)
                .Must(l => (l.DebitAmount > 0) ^ (l.CreditAmount > 0))
                .WithMessage("Each line must be either pure debit or pure credit (not both, not neither).");
        });

        RuleFor(x => x.Lines).Must(lines =>
            lines.Sum(l => l.DebitAmount) == lines.Sum(l => l.CreditAmount))
            .WithMessage("Total debit must equal total credit.");
    }
}
