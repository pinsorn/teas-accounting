using Accounting.Api.Authorization;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class SalesChainEndpoints
{
    public static IEndpointRouteBuilder MapSalesChainEndpoints(this IEndpointRouteBuilder app)
    {
        var qPol  = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.QuotationManage;
        var soPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.SalesOrderManage;
        var doPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.DeliveryOrderManage;

        // ── Quotations ──────────────────────────────────────────────────────
        var q = app.MapGroup("/quotations").WithTags("Quotations").RequireAuthorization(qPol);
        q.MapPost("/", async ([FromBody] CreateQuotationRequest req,
            IValidator<CreateQuotationRequest> v, IQuotationService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/quotations/{id}", new { quotation_id = id });
        });
        q.MapPost("/{id:long}/send", async (long id, IQuotationService s, CancellationToken ct) =>
            { await s.SendAsync(id, ct); return Results.NoContent(); });
        q.MapPost("/{id:long}/accept", async (long id, IQuotationService s, CancellationToken ct) =>
            { await s.AcceptAsync(id, ct); return Results.NoContent(); });
        q.MapPost("/{id:long}/reject", async (long id, [FromBody] ReasonBody b,
            IQuotationService s, CancellationToken ct) =>
            { await s.RejectAsync(id, b.Reason, ct); return Results.NoContent(); });
        q.MapPost("/{id:long}/cancel", async (long id, [FromBody] ReasonBody b,
            IQuotationService s, CancellationToken ct) =>
            { await s.CancelAsync(id, b.Reason, ct); return Results.NoContent(); });
        q.MapPost("/{id:long}/convert-to-so", async (long id, IQuotationService s, CancellationToken ct) =>
            Results.Ok(new { sales_order_id = await s.ConvertToSalesOrderAsync(id, ct) }));
        q.MapGet("/", async ([FromQuery] string? status, IQuotationService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct)));
        q.MapGet("/{id:long}", async (long id, IQuotationService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); });
        q.MapGet("/{id:long}/pdf", async (long id, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.QuotationPdfAsync(id, ct), "application/pdf", $"quotation-{id}.pdf"));

        // ── Sales Orders ────────────────────────────────────────────────────
        var so = app.MapGroup("/sales-orders").WithTags("Sales Orders").RequireAuthorization(soPol);
        so.MapPost("/", async ([FromBody] CreateSalesOrderRequest req,
            IValidator<CreateSalesOrderRequest> v, ISalesOrderService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/sales-orders/{id}", new { sales_order_id = id });
        });
        so.MapPost("/{id:long}/post", async (long id, ISalesOrderService s, CancellationToken ct) =>
            { await s.PostAsync(id, ct); return Results.NoContent(); });
        so.MapPost("/{id:long}/delivery-orders", async (long id,
            [FromBody] CreateDeliveryOrderRequest req, ISalesOrderService s, CancellationToken ct) =>
            Results.Ok(new { delivery_order_id = await s.CreateDeliveryOrderAsync(id, req, ct) }));
        so.MapGet("/", async ([FromQuery] string? status, ISalesOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct)));
        so.MapGet("/{id:long}", async (long id, ISalesOrderService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); });
        so.MapGet("/{id:long}/pdf", async (long id, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.SalesOrderPdfAsync(id, ct), "application/pdf", $"sales-order-{id}.pdf"));

        // ── Delivery Orders ─────────────────────────────────────────────────
        var d0 = app.MapGroup("/delivery-orders").WithTags("Delivery Orders").RequireAuthorization(doPol);
        d0.MapPost("/", async ([FromBody] CreateDeliveryOrderRequest req,
            IDeliveryOrderService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/delivery-orders/{id}", new { delivery_order_id = id });
        });
        d0.MapPost("/{id:long}/post", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            { await s.PostAsync(id, ct); return Results.NoContent(); });
        d0.MapPost("/{id:long}/create-ti", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            Results.Ok(new { tax_invoice_id = await s.CreateTaxInvoiceAsync(id, ct) }));
        d0.MapGet("/", async ([FromQuery] string? status, IDeliveryOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, ct)));
        d0.MapGet("/{id:long}", async (long id, IDeliveryOrderService s, CancellationToken ct) =>
            { var d = await s.GetAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); });
        d0.MapGet("/{id:long}/pdf", async (long id, ISalesChainPdfService pdf, CancellationToken ct) =>
            Results.File(await pdf.DeliveryOrderPdfAsync(id, ct), "application/pdf", $"delivery-order-{id}.pdf"));

        return app;
    }

    public sealed record ReasonBody(string Reason);
}
