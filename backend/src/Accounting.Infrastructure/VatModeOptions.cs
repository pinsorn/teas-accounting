namespace Accounting.Infrastructure;

/// <summary>
/// Sprint 8.5 — Infrastructure-layer view of the VAT-mode switch + non-VAT document
/// labels, bound from the same <c>Tax</c> appsettings section as the API-layer
/// <c>TaxConfig</c>. A separate class is required because <c>TaxConfig</c> lives in
/// the API assembly and the PDF builders live in Infrastructure (Clean Architecture
/// forbids Infrastructure → API). Same config source, same values — see
/// Report-Backend11 mechanism note.
/// </summary>
public sealed class VatModeOptions
{
    /// <summary>Company is VAT-registered (env <c>Tax:VatMode</c>). Default true to
    /// preserve current behavior if the section is absent.</summary>
    public bool VatMode { get; init; } = true;

    public string NonVatDocLabelTh { get; init; } = "ใบส่งของ";
    public string NonVatDocLabelEn { get; init; } = "Delivery Order";

    /// <summary>Sprint 9 B5 — ภ.พ.30 submission mode (env <c>Tax:Pnd30SubmissionMode</c>):
    /// "manual" (default) = generate file for manual upload; "auto" = RD Open API
    /// (Phase-1 stubbed). Same Tax section / value as API-layer TaxConfig.</summary>
    public string Pnd30SubmissionMode { get; init; } = "manual";
}
