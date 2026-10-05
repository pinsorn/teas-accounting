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

        // draft-edit-receipt-taxinvoice — Draft-only full replace; same policy/validator/DTO as create
        // (mirrors SalesChainEndpoints quotation PUT). 204. Posted -> 422 rc.cannot_edit_after_post.
        group.MapPut("/{id:long}", async (long id, [FromBody] CreateReceiptRequest req,
            IValidator<CreateReceiptRequest> validator, IReceiptService service, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            await service.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(createPol);

        // The exact CreateReceiptRequest that reproduces this Draft - the edit form's prefill.
        group.MapGet("/{id:long}/draft-input", async (long id, IReceiptService svc, CancellationToken ct) =>
            await svc.GetDraftInputAsync(id, ct) is { } r ? Results.Ok(r) : Results.NotFound())
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

        // cont.121 — canonical paper DTO (JSON twin of /pdf) for the FE PaperDocument.
        group.MapGet("/{id:long}/paper", async (long id, [FromQuery] bool? copy, IReceiptService svc, CancellationToken ct) =>
            Results.Ok(await svc.BuildPaperAsync(id, ct, copy ?? false)))
        .RequireAuthorization(readPol);

        // cancel-reissue (spec 3.7). Reissue variants need BOTH .cancel and .create.
        var cancelPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCancel;
        group.MapPost("/{id:long}/cancel", async (long id, [FromBody] CancelDocumentBody b,
            IReceiptService svc, CancellationToken ct) =>
            Results.Ok(await svc.CancelAsync(id, b.ReasonCode, SalesChainEndpoints.RequireReason(b.Reason), ct)))
        .RequireAuthorization(cancelPol);

        group.MapPost("/{id:long}/cancel-and-reissue", async (long id, [FromBody] CancelDocumentBody b,
            IReceiptService svc, CancellationToken ct) =>
            Results.Ok(await svc.CancelAndReissueAsync(id, b.ReasonCode, SalesChainEndpoints.RequireReason(b.Reason), ct)))
        .RequireAuthorization(cancelPol, createPol);

        group.MapPost("/{id:long}/reissue", async (long id, IReceiptService svc, CancellationToken ct) =>
            Results.Ok(new { replacementReceiptId = await svc.ReissueAsync(id, ct) }))
        .RequireAuthorization(cancelPol, createPol);

        // Replacement DRAFTS only (rc.delete_not_allowed otherwise).
        group.MapDelete("/{id:long}", async (long id, IReceiptService svc, CancellationToken ct) =>
        {
            await svc.DiscardReplacementAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(cancelPol);

        return app;
    }
}
