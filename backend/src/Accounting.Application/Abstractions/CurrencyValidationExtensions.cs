using FluentValidation;

namespace Accounting.Application.Abstractions;

/// <summary>
/// Multi-currency is DEFERRED (not in scope — see code-review 2026-06-17, findings 05-C1 / 05-H1):
/// the GL posting and the ภ.พ.30 / VAT registers read the raw document-currency amounts with no
/// THB conversion, so any non-THB document would silently misstate the statutory VAT return and the
/// financial statements. Until FX conversion is built, every fiscal-document create path is restricted
/// to THB at the document's functional currency (CLAUDE.md §5: functional currency = THB).
/// </summary>
public static class CurrencyValidationExtensions
{
    public const string ThbOnlyCode = "currency.thb_only";

    /// <summary>
    /// Restrict a fiscal-document create request to THB only: <c>CurrencyCode == "THB"</c> AND
    /// <c>ExchangeRate == 1</c>. Call this in every fiscal-document create validator. Replaces the
    /// looser <c>CurrencyCode.NotEmpty().Length(3)</c> + <c>ExchangeRate.GreaterThan(0)</c> pair.
    /// </summary>
    public static void ThbOnly<T>(
        this AbstractValidator<T> validator,
        Func<T, string> currencyCode,
        Func<T, decimal> exchangeRate)
    {
        validator.RuleFor(x => currencyCode(x))
            .Equal("THB")
            .WithErrorCode(ThbOnlyCode)
            .WithMessage("Only THB is supported (multi-currency is not yet available).");
        validator.RuleFor(x => exchangeRate(x))
            .Equal(1m)
            .WithErrorCode(ThbOnlyCode)
            .WithMessage("ExchangeRate must be 1 (THB only; multi-currency is not yet available).");
    }
}
