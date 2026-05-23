using FluentValidation;

namespace Accounting.Application.Sales;

// Sprint 10 Part B — Q → SO → DO chain. Shared line shape across the three docs.
public sealed record ChainLineInput(
    long?  ProductId,
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    int    TaxCodeId,
    string TaxCode,
    decimal TaxRate,
    string? ProductType = null);  // Sprint 13h P7 — snapshot from picker

public sealed record CreateQuotationRequest(
    DateOnly DocDate,
    DateOnly ValidUntilDate,
    long CustomerId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<ChainLineInput> Lines);

public sealed record CreateSalesOrderRequest(
    DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate,
    long CustomerId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    long? FromQuotationId,                 // optional clone source
    IReadOnlyList<ChainLineInput> Lines);

public sealed record CreateDeliveryOrderRequest(
    DateOnly DocDate,
    long CustomerId,
    int? BusinessUnitId,
    bool IsCombinedWithTi,
    string? Notes,
    long? FromSalesOrderId,
    IReadOnlyList<DeliveryLineInput> Lines);

/// <summary>A delivery line optionally references the SO line it fulfils
/// (partial-delivery tracking).</summary>
public sealed record DeliveryLineInput(
    long?  SalesOrderLineId,
    long?  ProductId,
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    int    TaxCodeId,
    string TaxCode,
    decimal TaxRate,
    string? ProductType = null);  // Sprint 13h P7

public sealed record ChainLineDto(
    int LineNo, long? ProductId, string? ProductCode, string DescriptionTh,
    decimal Quantity, string UomText, decimal UnitPrice, decimal LineAmount,
    decimal TaxAmount, decimal TotalAmount);

public sealed record QuotationListItem(
    long QuotationId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly ValidUntilDate, string CustomerName, decimal TotalAmount,
    long? ConvertedToSoId);

public sealed record QuotationDetail(
    long QuotationId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly ValidUntilDate, long CustomerId, string CustomerName,
    int? BusinessUnitId, string CurrencyCode, decimal SubtotalAmount,
    decimal VatAmount, decimal TotalAmount, bool ShowWhtNote,
    long? ConvertedToSoId, string? Notes, IReadOnlyList<ChainLineDto> Lines);

public sealed record SalesOrderListItem(
    long SalesOrderId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, decimal TotalAmount, long? QuotationId);

public sealed record SalesOrderDetail(
    long SalesOrderId, string? DocNo, string Status, DateOnly DocDate,
    long CustomerId, string CustomerName, int? BusinessUnitId,
    decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    long? QuotationId, IReadOnlyList<ChainLineDto> Lines);

public sealed record DeliveryOrderListItem(
    long DeliveryOrderId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, bool IsCombinedWithTi, long? TaxInvoiceId, long? SalesOrderId,
    // Non-VAT receipt apply-to-DO (cont. 68): the DO picker scopes by customer and
    // prefills the applied amount, so the list item must carry both.
    long CustomerId = 0, decimal TotalAmount = 0);

public sealed record DeliveryOrderDetail(
    long DeliveryOrderId, string? DocNo, string Status, DateOnly DocDate,
    long CustomerId, string CustomerName, int? BusinessUnitId,
    bool IsCombinedWithTi, long? TaxInvoiceId, long? SalesOrderId,
    decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    IReadOnlyList<ChainLineDto> Lines,
    // cont.69 — the Invoice created from this DO (one-per-DO); FE hides the
    // "create Invoice" button once set.
    long? BillingNoteId = null);

public interface IQuotationService
{
    Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct);
    // Sprint 13h P4 — Draft-only edits / hard-delete.
    Task UpdateDraftAsync(long id, CreateQuotationRequest req, CancellationToken ct);
    Task DeleteDraftAsync(long id, CancellationToken ct);
    Task SendAsync(long id, CancellationToken ct);
    Task AcceptAsync(long id, CancellationToken ct);
    Task RejectAsync(long id, string reason, CancellationToken ct);
    Task CancelAsync(long id, string reason, CancellationToken ct);
    Task<long> ConvertToSalesOrderAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<QuotationListItem>> ListAsync(string? status, CancellationToken ct);
    Task<QuotationDetail?> GetAsync(long id, CancellationToken ct);
}

public interface ISalesOrderService
{
    Task<long> CreateDraftAsync(CreateSalesOrderRequest req, CancellationToken ct);
    Task PostAsync(long id, CancellationToken ct);
    Task<long> CreateDeliveryOrderAsync(long salesOrderId, CreateDeliveryOrderRequest req, CancellationToken ct);
    Task<IReadOnlyList<SalesOrderListItem>> ListAsync(string? status, CancellationToken ct);
    Task<SalesOrderDetail?> GetAsync(long id, CancellationToken ct);
}

public interface IDeliveryOrderService
{
    Task<long> CreateDraftAsync(CreateDeliveryOrderRequest req, CancellationToken ct);
    // Sprint 13h P9: 4-state machine. Issue allocates doc_no without firing TI;
    // MarkDelivered transitions Issued→Delivered AND triggers the linked TI.
    Task IssueAsync(long id, CancellationToken ct);
    Task MarkDeliveredAsync(long id, CancellationToken ct);
    Task<long> CreateTaxInvoiceAsync(long deliveryOrderId, CancellationToken ct);
    Task<IReadOnlyList<DeliveryOrderListItem>> ListAsync(string? status, CancellationToken ct);
    Task<DeliveryOrderDetail?> GetAsync(long id, CancellationToken ct);
}

public sealed class CreateQuotationValidator : AbstractValidator<CreateQuotationRequest>
{
    public CreateQuotationValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.ValidUntilDate).GreaterThanOrEqualTo(x => x.DocDate);
    }
}

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
    }
}
