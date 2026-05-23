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

        // Sprint 13i B1 — per-endpoint auth split: GET → read, POST → create/post.
        var readPol   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptRead;
        var createPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate;
        var postPol   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptPost;

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
        .RequireAuthorization(createPol);

        group.MapPost("/{id:long}/post", async (long id, IReceiptService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(postPol);

        // Sprint 13j-FE — supply the customer 50ทวิ number/date after posting
        // (receipt posted with "ขาดใบทวิ 50"). Attach the scan via the attachments API.
        group.MapPost("/{id:long}/wht-cert", async (
            long id, [FromBody] SetWhtCertRequest req, IReceiptService service, CancellationToken ct) =>
        {
            await service.SetWhtCertAsync(id, req.CertNo, req.CertDate, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(createPol);

        group.MapGet("/", async ([FromQuery] long? cursor, [FromQuery] int? limit,
            [FromQuery] int? businessUnitId, [FromQuery] bool? includeUnspecified,
            IReceiptService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct,
                    businessUnitId, includeUnspecified ?? false)))
        .RequireAuthorization(readPol);

        // Sprint (multi-category WHT) — per-income-type WHT auto-suggest for the
        // Receipt form. POST (not GET) because it needs the applied amounts in the
        // body to pro-rate partial payments across the applied TIs' service lines.
        group.MapPost("/wht-base-suggest", async (
            [FromBody] WhtSuggestRequest req, IReceiptService svc, CancellationToken ct) =>
                Results.Ok(await svc.SuggestWhtBaseAsync(
                    req.Applications ?? [], req.CustomerId, ct)))
        .RequireAuthorization(readPol);

        group.MapGet("/{id:long}", async (long id, IReceiptService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(readPol);

        group.MapGet("/{id:long}/pdf", async (long id, [FromQuery] bool? copy, IReceiptService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPdfAsync(id, ct, copy ?? false), "application/pdf", $"receipt-{id}.pdf"))
        .RequireAuthorization(readPol);

        return app;
    }
}
