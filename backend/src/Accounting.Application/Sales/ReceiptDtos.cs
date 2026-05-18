using Accounting.Domain.Enums;
using FluentValidation;

namespace Accounting.Application.Sales;

public sealed record ReceiptApplicationInput(long TaxInvoiceId, decimal AppliedAmount);

public sealed record CreateReceiptRequest(
    DateOnly DocDate,
    long CustomerId,
    PaymentMethod PaymentMethod,
    string?  ChequeNo,
    DateOnly? ChequeDate,
    long?    BankAccountId,
    string   CurrencyCode,
    decimal  ExchangeRate,
    string?  Notes,
    IReadOnlyList<ReceiptApplicationInput> Applications,
    int? BusinessUnitId = null,   // Sprint 8 — recomputed at post (cross-BU → NULL)
    // Sprint 8.6 — AR-side WHT (customer withheld). All optional; WhtAmount=0 = none.
    decimal WhtAmount = 0,
    int? WhtTypeId = null,
    string? CustomerWhtCertNo = null,
    DateOnly? CustomerWhtCertDate = null);

public sealed record ReceiptPostedResult(
    long ReceiptId, string DocNo, DateTimeOffset PostedAt, decimal Amount,
    bool CrossesBusinessUnits = false,
    decimal CashReceived = 0m, decimal WhtAmount = 0m);

/// <summary>Sprint 8.6 — WHT base auto-suggest. Sprint 10 A4: Product master
/// exists → ServiceSubtotal/GoodsSubtotal split by Product.ProductType and
/// SuggestedWhtBase now defaults to ServiceSubtotal (was full ex-VAT). Old
/// fields unchanged — additive. User still override-able.</summary>
public sealed record WhtBaseSuggestion(
    decimal AppliedSubtotalExVat,
    int?    SuggestedWhtTypeId,
    string? SuggestedWhtTypeCode,
    decimal SuggestedWhtRate,
    decimal SuggestedWhtBase,
    decimal SuggestedWhtAmount,
    string  Explanation,
    decimal ServiceSubtotal,   // Sprint 10 A4 (NEW)
    decimal GoodsSubtotal);    // Sprint 10 A4 (NEW)

public sealed class CreateReceiptValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        // Sprint 8.6 — AR-side WHT.
        RuleFor(x => x.WhtAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WhtTypeId).NotNull()
            .When(x => x.WhtAmount > 0)
            .WithMessage("WhtTypeId is required when WhtAmount > 0.");
        RuleFor(x => x.CustomerWhtCertNo).NotEmpty()
            .When(x => x.WhtAmount > 0)
            .WithMessage("Customer WHT certificate no. is required when WhtAmount > 0.");
        RuleFor(x => x.Applications).NotEmpty();
        RuleForEach(x => x.Applications).ChildRules(a =>
        {
            a.RuleFor(x => x.TaxInvoiceId).GreaterThan(0);
            a.RuleFor(x => x.AppliedAmount).GreaterThan(0);
        });
        When(x => x.PaymentMethod == PaymentMethod.Cheque, () =>
        {
            RuleFor(x => x.ChequeNo).NotEmpty();
            RuleFor(x => x.ChequeDate).NotNull();
        });
    }
}

public interface IReceiptService
{
    Task<long> CreateDraftAsync(CreateReceiptRequest req, CancellationToken ct);
    Task<ReceiptPostedResult> PostAsync(long receiptId, CancellationToken ct);
    Task<CursorPage<ReceiptListItem>> ListAsync(long? cursor, int limit, CancellationToken ct,
        int? businessUnitId = null, bool includeUnspecified = false);
    Task<ReceiptDetail?> GetDetailAsync(long receiptId, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long receiptId, CancellationToken ct);

    /// <summary>Sprint 8.6 — WHT base/rate/type auto-suggest for the Receipt form
    /// (R-B1a: base = full ex-VAT applied subtotal; user overrides to service-only).</summary>
    Task<WhtBaseSuggestion> SuggestWhtBaseAsync(
        IReadOnlyList<long> taxInvoiceIds, long customerId, CancellationToken ct);
}
