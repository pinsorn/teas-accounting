using Accounting.Application.Abstractions;
using FluentValidation;

namespace Accounting.Application.Sales;

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล).

public sealed record BillingLineInput(
    long?  ProductId,
    long?  TaxInvoiceId,        // optional source TI for this rolled-up line
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    int    TaxCodeId,
    string TaxCode,
    decimal TaxRate,
    string? ProductType = null);

public sealed record CreateBillingNoteRequest(
    DateOnly DocDate,
    DateOnly DueDate,
    long CustomerId,
    int? BusinessUnitId,
    long? QuotationId,
    long[]? TaxInvoiceIds,       // BN may group N TIs for the same customer
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<BillingLineInput> Lines);

public sealed record BillingNoteListItem(
    long BillingNoteId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly DueDate, string CustomerName, decimal TotalAmount,
    long? QuotationId,
    // Sprint 13i C3 — for client-side BU/customer filtering on the list page.
    long CustomerId, int? BusinessUnitId);

// Sprint 13i C7 — a TaxInvoice grouped by a BN, surfaced from the join table.
public sealed record BillingNoteTaxInvoiceRef(
    long TaxInvoiceId, string? DocNo, decimal AppliedAmount);

public sealed record BillingNoteDetail(
    long BillingNoteId, string? DocNo, string Status, DateOnly DocDate, DateOnly DueDate,
    long CustomerId, string CustomerName, int? BusinessUnitId,
    long? QuotationId, IReadOnlyList<BillingNoteTaxInvoiceRef> TaxInvoices,
    string CurrencyCode, decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    string? Notes, IReadOnlyList<ChainLineDto> Lines);

public interface IBillingNoteService
{
    Task<long> CreateDraftAsync(CreateBillingNoteRequest req, CancellationToken ct);

    /// <summary>cont.69 Phase 1 — create a Draft Invoice (ใบแจ้งหนี้) from a Delivery
    /// Order. Copies the DO's lines + customer snapshot into a new BillingNote with
    /// <c>DeliveryOrderId</c> set. Returns the new billing_note_id.</summary>
    Task<long> CreateFromDeliveryOrderAsync(long deliveryOrderId, CancellationToken ct);

    Task UpdateDraftAsync(long id, CreateBillingNoteRequest req, CancellationToken ct);
    Task DeleteDraftAsync(long id, CancellationToken ct);
    Task IssueAsync(long id, CancellationToken ct);
    Task CancelAsync(long id, string reason, CancellationToken ct);
    Task MarkSettledAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<BillingNoteListItem>> ListAsync(string? status, CancellationToken ct);
    Task<BillingNoteDetail?> GetAsync(long id, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false);   // Sprint 13j-PDF; cont.69 P4 copy=สำเนา
}

public sealed class CreateBillingNoteValidator : AbstractValidator<CreateBillingNoteRequest>
{
    public CreateBillingNoteValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.DocDate);
    }
}
