using Accounting.Api.Authorization;
using Accounting.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/vat-register", async (
            [FromQuery] int year, [FromQuery] int month,
            IVatReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetRegisterAsync(year, month, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.VatRegisterRead);

        group.MapGet("/pnd30", async (
            [FromQuery] int year, [FromQuery] int month,
            IVatReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetPnd30Async(year, month, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd30Read);

        // Sprint 8.6 — AR-side WHT reports (ภ.ง.ด.50 credit + chase 50ทวิ).
        group.MapGet("/wht-receivable-register", async (
            [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetRegisterAsync(fromDate, toDate, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        group.MapGet("/wht-receivable-aging", async (
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetAgingAsync(ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        // Sprint 13j-tail — receipts missing the customer 50ทวิ cert (chase per period).
        group.MapGet("/wht-receivable-missing-cert", async (
            [FromQuery] int period,
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetMissingCertAsync(period, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        // Sprint 9 Part A — financial reports. (report.financial.read maps to
        // the existing granular TrialBalance/ProfitLoss perms — mechanism note.)
        group.MapGet("/trial-balance", async (
            [FromQuery] DateOnly? asOfDate, [FromQuery] bool? includeInactive,
            IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.TrialBalanceAsync(
                    asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    includeInactive ?? false, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);

        // C-C — งบแสดงฐานะการเงิน (feeds ภ.ง.ด.50 + DBD; locked decision #3).
        group.MapGet("/balance-sheet", async (
            [FromQuery] DateOnly? asOfDate, IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.BalanceSheetAsync(
                    asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow), ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);

        group.MapGet("/profit-loss", async (
            [FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] int? businessUnitId, [FromQuery] bool? includeUnspecified,
            IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.ProfitLossAsync(
                    from, to, businessUnitId, includeUnspecified ?? false, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        group.MapGet("/sales-summary", async (
            [FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] string? groupBy, IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.SalesSummaryAsync(from, to, groupBy ?? "customer", ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        // 2026-06-13 — monthly tax summary dashboard (revenue/expense + VAT + WHT
        // paid/received per month). Year defaults to the current Asia/Bangkok year.
        group.MapGet("/tax-summary", async (
            [FromQuery] int? year, [FromQuery] int? businessUnitId, ITaxSummaryService svc,
            Accounting.Application.Abstractions.IClock clock, CancellationToken ct) =>
                Results.Ok(await svc.GetAsync(year ?? clock.TodayInBangkok().Year, ct, businessUnitId)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        // Number-gap audit (CLAUDE.md §4.3 / plan §17.6). Empty result = compliant.
        group.MapGet("/number-gaps", async (
            [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? doc_type,
            INumberGapReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetGapsAsync(year, month, doc_type, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.AuditRead);

        return app;
    }
}
