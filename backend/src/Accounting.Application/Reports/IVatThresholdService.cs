namespace Accounting.Application.Reports;

/// <summary>
/// Sprint 8.5 — VAT-registration threshold state for a non-VAT company.
/// ม.85/1: a business must register for VAT within 30 days of crossing
/// 1.8M THB revenue. We warn early using a rolling-12-month window (more
/// conservative than the official ปีปฏิทิน rule — see plan §16.x note).
/// </summary>
public enum RevenueThresholdStatus
{
    /// <summary>Company is already VAT-registered (companies.vat_registered=true) — no warning.</summary>
    NotApplicable,
    /// <summary>Rolling-12-mo revenue &lt; 1.5M — fine.</summary>
    Ok,
    /// <summary>≥ 1.5M and &lt; 1.8M — approaching the registration threshold.</summary>
    Approaching,
    /// <summary>≥ 1.8M — must register for VAT within 30 days (ม.85/1).</summary>
    Exceeded,
}

public sealed record VatThresholdStatusResult(RevenueThresholdStatus Status);

public interface IVatThresholdService
{
    /// <summary>Rolling-12-month posted-TI revenue (TotalAmountThb) vs the
    /// 1.5M / 1.8M bands. Returns <see cref="RevenueThresholdStatus.NotApplicable"/>
    /// immediately when the company is VAT-registered.</summary>
    Task<RevenueThresholdStatus> CheckAsync(CancellationToken ct);
}
