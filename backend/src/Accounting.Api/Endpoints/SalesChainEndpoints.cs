using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class SalesChainEndpoints
{
    public static IEndpointRouteBuilder MapSalesChainEndpoints(this IEndpointRouteBuilder app)
    {
        // WP6 (specs/fix-swarm-findings-all.md) — read/manage split: list/get/PDF/paper need only
        // .read; every write/lifecycle route keeps .manage. No group-level RequireAuthorization
        // (mirrors CustomerEndpoints/BankAccountEndpoints — a group-level policy would AND with the
        // per-route one, wrongly requiring BOTH manage and read on a read route).
        var qManage  = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.QuotationManage;
        var qRead    = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.QuotationRead;
        var soManage = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.SalesOrderManage;
        var soRead   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.SalesOrderRead;
        var doManage = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.DeliveryOrderManage;
        var doRead   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.DeliveryOrderRead;

        // ── Quotations ──────────────────────────────────────────────────────
        var q = app.MapGroup("/quotations").WithTags("Quotations");
        q.MapPost("/", async ([FromBody] CreateQuotationRequest req,
            IValidator<CreateQuotationRequest> v, IQuotationService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/quotations/{id}", new { quotation_id = id });
        }).RequireAuthorization(qManage);
        // Sprint 13h P4 — Draft-only edit + hard-delete.
        q.MapPut("/{id:long}", async (long id, [FromBody] CreateQuotationRequest req,
            IValidator<CreateQuotationRequest> v, IQuotationService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(qManage);
        q.MapDelete("/{id:long}", async (long id, IQuotationService s, CancellationToken ct) =>
            { await s.DeleteDraftAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(qManage);
        q.MapPost("/{id:long}/send", async (long id, IQuotationService s, CancellationToken ct) =>
            { await s.SendAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(qManage);
        q.MapPost("/{id:long}/accept", async (long id, IQuotationService s, CancellationToken ct) =>
            { await s.AcceptAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(qManage);
        q.MapPost("/{id:long}/reject", async (long id, [FromBody] ReasonBody b,
            IQuotationService s, CancellationToken ct) =>
            { await s.RejectAsync(id, b.Reason, ct); return Results.NoContent(); }).RequireAuthorization(qManage);
        q.MapPost("/{id:long}/cancel", async (long id, [FromBody] ReasonBody b,
            IQuotationService s, CancellationToken ct) =>
            { await s.CancelAsync(id, b.Reason, ct); return Results.NoContent(); }).RequireAuthorization(qManage);
        q.MapPost("/{id:long}/convert-to-so", async (long id, IQuotationService s, CancellationToken ct) =>
            Results.Ok(new { sales_order_id = await s.ConvertToSalesOrderAsync(id, ct) })).RequireAuthorization(qManage);
        q.MapGet("/", async ([FromQuery] string? status, IQuotationService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct))).RequireAuthorization(qRead);
        q.MapGet("/{id:long}", async (long id, IQuotationService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); })
            .RequireAuthorization(qRead);
        q.MapGet("/{id:long}/pdf", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.QuotationPdfAsync(id, ct, copy ?? false), "application/pdf", $"quotation-{id}.pdf"))
            .RequireAuthorization(qRead);
        // cont.121 — canonical paper DTO (JSON twin of /pdf) for the FE PaperDocument.
        q.MapGet("/{id:long}/paper", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.Ok(await pdf.QuotationPaperAsync(id, ct, copy ?? false))).RequireAuthorization(qRead);

        // ── Sales Orders ────────────────────────────────────────────────────
        var so = app.MapGroup("/sales-orders").WithTags("Sales Orders");
        so.MapPost("/", async ([FromBody] CreateSalesOrderRequest req,
            IValidator<CreateSalesOrderRequest> v, ISalesOrderService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/sales-orders/{id}", new { sales_order_id = id });
        }).RequireAuthorization(soManage);
        // S15 (2026-07-16 fix) — Draft-only edit, same auth/permission shape as the create
        // above and the same shape as the Quotation PUT.
        so.MapPut("/{id:long}", async (long id, [FromBody] CreateSalesOrderRequest req,
            IValidator<CreateSalesOrderRequest> v, ISalesOrderService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(soManage);
        so.MapPost("/{id:long}/post", async (long id, ISalesOrderService s, CancellationToken ct) =>
            { await s.PostAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(soManage);
        so.MapPost("/{id:long}/delivery-orders", async (long id,
            [FromBody] CreateDeliveryOrderRequest req, ISalesOrderService s, CancellationToken ct) =>
            Results.Ok(new { delivery_order_id = await s.CreateDeliveryOrderAsync(id, req, ct) }))
            .RequireAuthorization(soManage);
        // mcp-document-chain (D9) — SO → Invoice, direct (service-only skip-DO path, §A2).
        // Polymorphic by company VAT mode (CRUX-1), mirroring create_invoice_draft's MCP-side
        // polymorphism exactly — same reused service methods, one FK response field set.
        so.MapPost("/{id:long}/create-invoice", async (long id,
            IBillingNoteService bnSvc, ITaxInvoiceService tiSvc, ICompanyTaxConfigService taxCfg,
            CancellationToken ct) =>
        {
            var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
            return vatMode
                ? Results.Ok(new { tax_invoice_id = await tiSvc.CreateFromSalesOrderAsync(id, ct), billing_note_id = (long?)null })
                : Results.Ok(new { billing_note_id = await bnSvc.CreateFromSalesOrderAsync(id, ct), tax_invoice_id = (long?)null });
        }).RequireAuthorization(soManage);
        so.MapGet("/", async ([FromQuery] string? status, ISalesOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct))).RequireAuthorization(soRead);
        so.MapGet("/{id:long}", async (long id, ISalesOrderService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); })
            .RequireAuthorization(soRead);
        so.MapGet("/{id:long}/pdf", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.SalesOrderPdfAsync(id, ct, copy ?? false), "application/pdf", $"sales-order-{id}.pdf"))
            .RequireAuthorization(soRead);
        so.MapGet("/{id:long}/paper", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.Ok(await pdf.SalesOrderPaperAsync(id, ct, copy ?? false))).RequireAuthorization(soRead);

        // ── Delivery Orders ─────────────────────────────────────────────────
        var d0 = app.MapGroup("/delivery-orders").WithTags("Delivery Orders");
        d0.MapPost("/", async ([FromBody] CreateDeliveryOrderRequest req,
            IDeliveryOrderService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/delivery-orders/{id}", new { delivery_order_id = id });
        }).RequireAuthorization(doManage);
        // Sprint 13h P9 — 4-state machine. /post replaced by /issue + /mark-delivered.
        d0.MapPost("/{id:long}/issue", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            { await s.IssueAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(doManage);
        d0.MapPost("/{id:long}/mark-delivered", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            { await s.MarkDeliveredAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(doManage);
        d0.MapPost("/{id:long}/create-ti", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            Results.Ok(new { tax_invoice_id = await s.CreateTaxInvoiceAsync(id, ct) })).RequireAuthorization(doManage);
        // cont.69 Phase 1 — DO → Invoice (ใบแจ้งหนี้), manual.
        d0.MapPost("/{id:long}/create-invoice", async (long id, IBillingNoteService s, CancellationToken ct) =>
            Results.Ok(new { billing_note_id = await s.CreateFromDeliveryOrderAsync(id, ct) })).RequireAuthorization(doManage);
        d0.MapGet("/", async ([FromQuery] string? status, IDeliveryOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct))).RequireAuthorization(doRead);
        d0.MapGet("/{id:long}", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); })
            .RequireAuthorization(doRead);
        d0.MapGet("/{id:long}/pdf", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.DeliveryOrderPdfAsync(id, ct, copy ?? false), "application/pdf", $"delivery-order-{id}.pdf"))
            .RequireAuthorization(doRead);
        d0.MapGet("/{id:long}/paper", async (long id, bool? copy, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.Ok(await pdf.DeliveryOrderPaperAsync(id, ct, copy ?? false))).RequireAuthorization(doRead);

        return app;
    }

    public sealed record ReasonBody(string Reason);
}
