namespace Accounting.Api.Endpoints;

/// <summary>cancel-reissue spec 3.7 - body of the TI / Receipt cancel and cancel-and-reissue routes.</summary>
public sealed record CancelDocumentBody(string ReasonCode, string Reason);

/// <summary>Invoice (billing note) cancel body. Separate from the shared SalesChainEndpoints.ReasonBody,
/// which Quotation / SO / PO cancel still use.</summary>
public sealed record BillingNoteCancelBody(string ReasonCode, string Reason);
