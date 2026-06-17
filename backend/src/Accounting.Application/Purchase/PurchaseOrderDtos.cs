using FluentValidation;

namespace Accounting.Application.Purchase;

public sealed record PurchaseOrderLineInput(
    long?  ProductId,
    string DescriptionTh,
    decimal Quantity,
    string? UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    int?   TaxCodeId,
    string? TaxCode,
    decimal TaxRate,
    string? Notes);

public sealed record CreatePurchaseOrderRequest(
    DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate,
    long VendorId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

public sealed record PurchaseOrderApprovedResult(long PurchaseOrderId, string DocNo, long ApprovedBy, DateTimeOffset ApprovedAt);
public sealed record PurchaseOrderLineDto(
    int LineNo, long? ProductId, string? ProductCode, string DescriptionTh,
    decimal Quantity, string? UomText, decimal UnitPrice, decimal LineAmount,
    decimal TaxAmount, decimal TotalAmount,
    // cont.94d — product taxonomy (GOOD/SERVICE/EXEMPT_*) so a PV prefill derives the
    // correct VAT. Trailing-defaulted so other positional call-sites keep compiling.
    string ProductType = "GOOD");

public sealed record PurchaseOrderListItem(
    long PurchaseOrderId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate, string VendorName, decimal TotalAmount,
    int? BusinessUnitId);

public sealed record LinkedViDto(long VendorInvoiceId, string? DocNo, decimal TotalAmount);

public sealed record PurchaseOrderDetail(
    long PurchaseOrderId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate, long VendorId, string VendorName,
    int? BusinessUnitId, string CurrencyCode, decimal SubtotalAmount,
    decimal VatAmount, decimal TotalAmount, string? Notes, string? InternalNotes,
    DateTimeOffset? ApprovedAt, long? ApprovedBy, DateTimeOffset? SentToVendorAt,
    DateTimeOffset? ClosedAt, string? CancellationReason,
    decimal LinkedViTotal, decimal Remaining,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    IReadOnlyList<LinkedViDto> LinkedVis,
    string? BusinessUnitCode = null,   // cont.79 — BU display (id already present above)
    string? BusinessUnitName = null);

public sealed record OutstandingPoRow(
    long PoId, string? DocNo, string VendorName, DateOnly? ExpectedDeliveryDate,
    int DaysOverdue, string AgingBucket, decimal PoTotal,
    int LinkedViCount, decimal LinkedViTotal, decimal Remaining);

public sealed record OutstandingPoReport(DateOnly AsOf, IReadOnlyList<OutstandingPoRow> Rows);

public interface IPurchaseOrderService
{
    Task<long> CreateDraftAsync(CreatePurchaseOrderRequest req, CancellationToken ct);
    Task UpdateDraftAsync(long id, CreatePurchaseOrderRequest req, CancellationToken ct);
    Task<PurchaseOrderApprovedResult> ApproveAsync(long id, CancellationToken ct);
    Task MarkSentAsync(long id, CancellationToken ct);
    Task CloseAsync(long id, CancellationToken ct);
    Task CancelAsync(long id, string reason, CancellationToken ct);
    Task<IReadOnlyList<PurchaseOrderListItem>> ListAsync(string? status, long? vendorId, CancellationToken ct);
    Task<PurchaseOrderDetail?> GetDetailAsync(long id, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false);
    Task<OutstandingPoReport> OutstandingAsync(DateOnly asOf, long? vendorId, bool overdueOnly, CancellationToken ct);
}

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
    }
}
