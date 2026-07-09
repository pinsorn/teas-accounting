using Accounting.Api.Authorization;
using Accounting.Application.Bank;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Bank reconciliation matching + inline JE (specs/bank-reconciliation.md §Matching + inline JE,
/// B4.3) + the reconciliation report (B5.2). Matching routes gated by
/// <see cref="Permissions.Bank.Reconcile"/> — suggestions are read-only but share the same gate
/// as confirm/unmatch/journal/ignore per the spec's own wording ("suggestions readable with
/// bank.reconcile"). The report route is gated by <see cref="Permissions.Bank.ReportRead"/>.
/// </summary>
public static class BankReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapBankReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var pol = PermissionPolicyProvider.PolicyPrefix + Permissions.Bank.Reconcile;
        var g = app.MapGroup("/bank-accounts/{bankAccountId:int}/lines/{lineId:long}")
            .WithTags("Bank Reconciliation")
            .RequireAuthorization(pol);

        g.MapGet("/suggestions", async (
            int bankAccountId, long lineId, IBankReconciliationService svc, CancellationToken ct) =>
            Results.Ok(await svc.SuggestAsync(lineId, ct)));

        g.MapPost("/match", async (
            int bankAccountId, long lineId, ConfirmMatchRequest req,
            IBankReconciliationService svc, CancellationToken ct) =>
        {
            await svc.ConfirmMatchAsync(lineId, req, ct);
            return Results.NoContent();
        });

        g.MapPost("/unmatch", async (
            int bankAccountId, long lineId, IBankReconciliationService svc, CancellationToken ct) =>
        {
            await svc.UnmatchAsync(lineId, ct);
            return Results.NoContent();
        });

        g.MapPost("/journal", async (
            int bankAccountId, long lineId, CreateInlineJournalRequest req,
            IBankReconciliationService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateJournalAsync(lineId, req, ct)));

        g.MapPost("/ignore", async (
            int bankAccountId, long lineId, IBankReconciliationService svc, CancellationToken ct) =>
        {
            await svc.IgnoreAsync(lineId, ct);
            return Results.NoContent();
        });

        g.MapPost("/unignore", async (
            int bankAccountId, long lineId, IBankReconciliationService svc, CancellationToken ct) =>
        {
            await svc.UnignoreAsync(lineId, ct);
            return Results.NoContent();
        });

        // B5.2 — the reconciliation report.
        var reportPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Bank.ReportRead;
        app.MapGet("/bank-accounts/{bankAccountId:int}/reconciliation", async (
            int bankAccountId, DateOnly from, DateOnly to,
            IBankReconciliationReportService reportSvc, CancellationToken ct) =>
            Results.Ok(await reportSvc.GetAsync(bankAccountId, from, to, ct)))
            .WithTags("Bank Reconciliation")
            .RequireAuthorization(reportPol);

        return app;
    }
}
