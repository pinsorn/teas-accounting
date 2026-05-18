using Accounting.Api.Authorization;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/receipts").WithTags("Receipts");

        group.MapPost("/", async (
            [FromBody] CreateReceiptRequest req,
            IValidator<CreateReceiptRequest> validator,
            IReceiptService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/receipts/{id}", new { receipt_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        group.MapPost("/{id:long}/post", async (long id, IReceiptService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptPost);

        group.MapGet("/", async ([FromQuery] long? cursor, [FromQuery] int? limit,
            [FromQuery] int? businessUnitId, [FromQuery] bool? includeUnspecified,
            IReceiptService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct,
                    businessUnitId, includeUnspecified ?? false)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        // Sprint 8.6 — WHT base/rate/type auto-suggest for the Receipt form.
        group.MapGet("/wht-base-suggest", async (
            [FromQuery] string taxInvoiceIds, [FromQuery] long customerId,
            IReceiptService svc, CancellationToken ct) =>
        {
            var ids = (taxInvoiceIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : 0L)
                .Where(v => v > 0).ToList();
            return Results.Ok(await svc.SuggestWhtBaseAsync(ids, customerId, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        group.MapGet("/{id:long}", async (long id, IReceiptService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        group.MapGet("/{id:long}/pdf", async (long id, IReceiptService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPdfAsync(id, ct), "application/pdf", $"receipt-{id}.pdf"))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        return app;
    }
}
