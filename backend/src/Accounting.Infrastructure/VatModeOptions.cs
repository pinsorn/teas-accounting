namespace Accounting.Infrastructure;

/// <summary>
/// Sprint 8.5 — Infrastructure-layer non-VAT document labels, bound from the
/// <c>Tax</c> appsettings section. ONLY the cosmetic labels remain here: since the
/// per-company-vat-mode spec (2026-06-11) the VAT mode / rate / ภ.พ.30 submission
/// mode live on the <c>companies</c> row and are read via
/// <c>ICompanyTaxConfigService</c>. A separate class (vs the API-layer
/// <c>TaxConfig</c>) is required because the PDF builders live in Infrastructure
/// and Clean Architecture forbids Infrastructure → API.
/// </summary>
public sealed class VatModeOptions
{
    public string NonVatDocLabelTh { get; init; } = "ใบส่งของ";
    public string NonVatDocLabelEn { get; init; } = "Delivery Order";
}
