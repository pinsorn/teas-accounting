using Accounting.Domain.Enums;
using FluentValidation;

namespace Accounting.Application.Sales;

public sealed record CreateTaxAdjustmentNoteRequest(
    TaxAdjustmentNoteType NoteType,
    DateOnly DocDate,
    long     OriginalTaxInvoiceId,
    string   ReasonCode,
    string   Reason,
    decimal  AdjustmentSubtotal,
    decimal  TaxRate,
    string   CurrencyCode,
    decimal  ExchangeRate,
    string?  Notes,
    int? BusinessUnitId = null);   // Sprint 8 — typically inherited from the original TI

public sealed record TaxAdjustmentNotePostedResult(
    long NoteId, string DocNo, DateTimeOffset PostedAt,
    TaxAdjustmentNoteType NoteType, decimal TotalAmount, decimal TaxAmount);

public sealed class CreateTaxAdjustmentNoteValidator : AbstractValidator<CreateTaxAdjustmentNoteRequest>
{
    public CreateTaxAdjustmentNoteValidator()
    {
        RuleFor(x => x.OriginalTaxInvoiceId).GreaterThan(0);
        RuleFor(x => x.ReasonCode).NotEmpty()
            .Must((req, code) => req.NoteType == TaxAdjustmentNoteType.Credit
                ? Enum.TryParse<Accounting.Domain.Enums.CreditNoteReasonCode>(code, out _)
                : Enum.TryParse<Accounting.Domain.Enums.DebitNoteReasonCode>(code, out _))
            .WithMessage("ReasonCode must be a valid code for the note type.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("Reason is mandatory per ม.86/9 / ม.86/10.");
        RuleFor(x => x.AdjustmentSubtotal).GreaterThan(0);
        RuleFor(x => x.TaxRate).InclusiveBetween(0m, 1m);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public interface ITaxAdjustmentNoteService
{
    Task<long> CreateDraftAsync(CreateTaxAdjustmentNoteRequest req, CancellationToken ct);
    Task<TaxAdjustmentNotePostedResult> PostAsync(long noteId, CancellationToken ct);
    /// <param name="noteType">"CREDIT" | "DEBIT" | null = both.</param>
    Task<CursorPage<AdjustmentNoteListItem>> ListAsync(
        string? noteType, long? cursor, int limit, CancellationToken ct,
        int? businessUnitId = null, bool includeUnspecified = false);
    Task<AdjustmentNoteDetail?> GetDetailAsync(long noteId, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long noteId, CancellationToken ct);
}
