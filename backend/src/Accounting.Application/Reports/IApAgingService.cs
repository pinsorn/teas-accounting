namespace Accounting.Application.Reports;

/// <summary>
/// AP Aging report — outstanding posted vendor invoices bucketed by age as of a date.
/// Tenant-scoped (mandatory company_id filter, CLAUDE.md §4.7).
/// </summary>
public interface IApAgingService
{
    Task<ApAgingReport> GetAsync(DateOnly asOf, long? vendorId, CancellationToken ct);
}
