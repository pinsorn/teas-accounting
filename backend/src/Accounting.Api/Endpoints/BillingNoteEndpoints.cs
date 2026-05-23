using Accounting.Api.Authorization;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) endpoints.
public static class BillingNoteEndpoints
{
    public static IEndpointRouteBuilder MapBillingNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var readPol   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.BillingNoteRead;
        var managePol = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.BillingNoteManage;

        var g = app.MapGroup("/billing-notes").WithTags("Billing Notes");

        g.MapPost("/", async ([FromBody] CreateBillingNoteRequest req,
            IValidator<CreateBillingNoteRequest> v, IBillingNoteService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/billing-notes/{id}", new { billing_note_id = id });
        }).RequireAuthorization(managePol);

        g.MapPut("/{id:long}", async (long id, [FromBody] CreateBillingNoteRequest req,
            IValidator<CreateBillingNoteRequest> v, IBillingNoteService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        g.MapDelete("/{id:long}", async (long id, IBillingNoteService s, CancellationToken ct) =>
            { await s.DeleteDraftAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(managePol);

        g.MapPost("/{id:long}/issue", async (long id, IBillingNoteService s, CancellationToken ct) =>
            { await s.IssueAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(managePol);

        g.MapPost("/{id:long}/cancel", async (long id, [FromBody] SalesChainEndpoints.ReasonBody b,
            IBillingNoteService s, CancellationToken ct) =>
            { await s.CancelAsync(id, b.Reason, ct); return Results.NoContent(); })
            .RequireAuthorization(managePol);

        g.MapPost("/{id:long}/mark-settled", async (long id, IBillingNoteService s, CancellationToken ct) =>
            { await s.MarkSettledAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(managePol);

        // cont.69 Phase 1 — Invoice → Tax Invoice (manual, VAT only). Throws
        // ti.non_vat_blocked (422) for a non-VAT company.
        g.MapPost("/{id:long}/create-tax-invoice", async (long id, ITaxInvoiceService s, CancellationToken ct) =>
            Results.Ok(new { tax_invoice_id = await s.CreateFromBillingNoteAsync(id, ct) }))
            .RequireAuthorization(managePol);

        g.MapGet("/", async ([FromQuery] string? status, IBillingNoteService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct)))
            .RequireAuthorization(readPol);

        g.MapGet("/{id:long}", async (long id, IBillingNoteService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); })
            .RequireAuthorization(readPol);

        // Sprint 13j-PDF — A4 PDF (PaperDocument mirror).
        g.MapGet("/{id:long}/pdf", async (long id, bool? copy, IBillingNoteService s, CancellationToken ct) =>
            Results.File(await s.BuildPdfAsync(id, ct, copy ?? false), "application/pdf", $"billing-note-{id}.pdf"))
            .RequireAuthorization(readPol);

        return app;
    }
}
