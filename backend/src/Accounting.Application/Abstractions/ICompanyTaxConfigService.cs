namespace Accounting.Application.Abstractions;

/// <summary>
/// Per-company tax configuration (per-company-vat-mode spec, 2026-06-11).
/// VAT mode / rate / ภ.พ.30 submission mode live on the company row (master.companies)
/// so one multi-tenant instance can host VAT and non-VAT companies side by side.
/// Replaces the env-level Tax-section VatMode / VatRate / Pnd30SubmissionMode
/// reads (§4.6 amendment — changes are audited via
/// activity_log on PUT /companies, never a user-facing settings UI).
/// Non-VAT doc labels remain instance-level config (cosmetic).
/// </summary>
public sealed record CompanyTaxConfig(
    bool VatMode,
    decimal VatRate,
    string Pnd30SubmissionMode,
    string NonVatDocLabelTh,
    string NonVatDocLabelEn);

public interface ICompanyTaxConfigService
{
    /// <summary>Tax config of the current tenant's company. Cached for the request lifetime.</summary>
    Task<CompanyTaxConfig> GetAsync(CancellationToken ct);
}
