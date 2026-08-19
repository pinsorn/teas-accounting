using Accounting.Api.Authorization;
using Accounting.Application.Bank;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Bank statement import (specs/bank-reconciliation.md B2.6). All three routes gated by
/// <see cref="Permissions.Bank.StatementImport"/> — B4 introduces the dedicated matching-screen
/// endpoints under <see cref="Permissions.Bank.Reconcile"/>.
/// </summary>
public static class StatementImportEndpoints
{
    public static IEndpointRouteBuilder MapStatementImportEndpoints(this IEndpointRouteBuilder app)
    {
        var pol = PermissionPolicyProvider.PolicyPrefix + Permissions.Bank.StatementImport;
        var g = app.MapGroup("/bank-accounts/{bankAccountId:int}/imports").WithTags("Bank Statement Imports");

        g.MapPost("/", async (
            int bankAccountId, HttpRequest req, IStatementImportService svc, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { detail = "multipart/form-data required." });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { detail = "file is required." });
            // Optional — reserved for the K-Plus PDF adapter (B3); the CSV adapter ignores it.
            var password = string.IsNullOrWhiteSpace(form["password"]) ? null : form["password"].ToString();

            await using var s = file.OpenReadStream();
            var result = await svc.ImportAsync(
                bankAccountId, file.FileName, file.ContentType, file.Length, s, password, ct);
            return Results.Created($"/bank-accounts/{bankAccountId}/imports/{result.StatementImportId}", result);
        }).RequireAuthorization(pol).DisableAntiforgery();

        g.MapGet("/", async (int bankAccountId, IStatementImportService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(bankAccountId, ct)))
            .RequireAuthorization(pol);

        g.MapGet("/{importId:long}/lines", async (
            long importId, IStatementImportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLinesAsync(importId, ct)))
            .RequireAuthorization(pol);

        // L2-3 superseded-import remediation (PLAN-fix-findings-r2.md §U3.2) — same permission
        // as import creation (this group's `pol`), not a new one.
        g.MapDelete("/{importId:long}", async (
            long importId, IStatementImportService svc, CancellationToken ct) =>
        {
            await svc.DeleteImportAsync(importId, ct);
            return Results.NoContent();
        }).RequireAuthorization(pol);

        return app;
    }
}
