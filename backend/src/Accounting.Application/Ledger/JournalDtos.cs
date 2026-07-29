using Accounting.Application.Abstractions;
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

// ── JE detail (first read endpoint — GET /journals/{id}) ───────────────────────
public sealed record JournalDetailLine(
    int LineNo, long AccountId, string AccountCode, string AccountNameTh,
    string? Description, string? Reference, decimal Debit, decimal Credit, int? BusinessUnitId);

public sealed record JournalDetail(
    long JournalId, string? DocNo, DateOnly DocDate, DateOnly PostingDate,
    string Description, string? Reference, string Status, DateTimeOffset? PostedAt,
    long? ReversalOfId, IReadOnlyList<JournalDetailLine> Lines,
    decimal TotalDebit, decimal TotalCredit);

// ── Manual JV (specs/manual-jv-and-coa-management.md §B0/B3) — create-and-post in one call.
// No draft state: see §B0 for the rationale (immutability invariant, period-close draft gate).
public sealed record ManualJournalLineInput(
    long AccountId, decimal DebitAmount, decimal CreditAmount, string? Description, int? BusinessUnitId);

public sealed record CreateManualJournalRequest(
    DateOnly DocDate, string Description, string? Reference,
    IReadOnlyList<ManualJournalLineInput> Lines);

public sealed record JournalListItem(
    long JournalId, string? DocNo, DateOnly DocDate, string Description, string? Reference,
    string Status, decimal TotalDebit, decimal TotalCredit, bool IsClosingEntry);

public sealed class CreateManualJournalValidator : AbstractValidator<CreateManualJournalRequest>
{
    public CreateManualJournalValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Reference).MaximumLength(255);
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count >= 2)
            .WithMessage("A journal needs at least 2 lines (debit + credit).");
        // Tier-2 F3 — a sane upper bound; nothing in the domain needs an unbounded line count
        // and an unbounded array is its own (mild) abuse surface.
        RuleFor(x => x.Lines).Must(l => l.Count <= 200)
            .WithMessage("A journal cannot have more than 200 lines.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountId).GreaterThan(0);
            line.RuleFor(l => l.DebitAmount).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.CreditAmount).GreaterThanOrEqualTo(0);
            // Tier-2 F3 — the column is HasMaxLength(500) (matches the header Description
            // rule above); an unbounded line description passed validation and came back as a
            // raw Postgres 22001 instead of a 400.
            line.RuleFor(l => l.Description).MaximumLength(500);
            line.RuleFor(l => l).Must(l => (l.DebitAmount > 0) ^ (l.CreditAmount > 0))
                .WithMessage("Each line must be either pure debit or pure credit (not both, not neither).");
            // THB is a 2-decimal currency; the columns are numeric(19,4) so a 3rd/4th decimal
            // would be STORED and would make ΣDr==ΣCr pass on invisible satang. Reject at the edge.
            line.RuleFor(l => l).Must(l => decimal.Round(l.DebitAmount, 2) == l.DebitAmount
                                        && decimal.Round(l.CreditAmount, 2) == l.CreditAmount)
                .WithMessage("Amounts must have at most 2 decimal places.");
        });

        RuleFor(x => x.Lines).Must(l => l.Sum(x => x.DebitAmount) == l.Sum(x => x.CreditAmount))
            .WithMessage("Total debit must equal total credit.");
    }
}

public sealed class CreateJournalValidator : AbstractValidator<CreateJournalRequest>
{
    public CreateJournalValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
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
