using FluentValidation;

namespace Accounting.Application.Abstractions;

/// <summary>
/// R1/C1 — THB is a 2-decimal currency; several document lines were stored at 4dp and could reach
/// the immutable ledger (JournalEntry.MarkPosted is the durable backstop — this is the fail-fast
/// validator layer on top, for a better error message at the API edge). Mirrors
/// <see cref="CurrencyValidationExtensions.ThbOnly{T}"/>'s shape.
/// </summary>
public static class MoneyValidationExtensions
{
    /// <summary>Reject a money amount carrying more than 2 decimal places (THB has 2, satang).</summary>
    public static IRuleBuilderOptions<T, decimal> Satang<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(v => decimal.Round(v, 2) == v)
            .WithMessage("Amounts must have at most 2 decimal places (THB has 2).");
}
