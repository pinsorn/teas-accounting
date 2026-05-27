using Accounting.Api.Authorization;
using Accounting.Application.Purchase;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class PurchaseOrderEndpoints
{
    public sealed record ReasonBody(string Reason);

    public static IEndpointRouteBuilder MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var create  = PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PurchaseOrderCreate;
        var approve = PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PurchaseOrderApprove;
        var read    = PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PurchaseOrderRead;
        var cancel  = PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PurchaseOrderCancel;

        var g = app.MapGroup("/purchase-orders").WithTags("Purchase Orders");

        g.MapPost("/", async ([FromBody] CreatePurchaseOrderRequest req,
            IValidator<CreatePurchaseOrderRequest> v, IPurchaseOrderService svc,
            CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/purchase-orders/{id}", new { purchase_order_id = id });
        }).RequireAuthorization(create);

        g.MapPut("/{id:long}", async (long id, [FromBody] CreatePurchaseOrderRequest req,
            IValidator<CreatePurchaseOrderRequest> v, IPurchaseOrderService svc,
            CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(create);

        g.MapPost("/{id:long}/approve", async (long id, IPurchaseOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ApproveAsync(id, ct))).RequireAuthorization(approve);

        g.MapPost("/{id:long}/mark-sent", async (long id, IPurchaseOrderService s, CancellationToken ct) =>
            { await s.MarkSentAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(create);

        g.MapPost("/{id:long}/close", async (long id, IPurchaseOrderService s, CancellationToken ct) =>
            { await s.CloseAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(cancel);

        g.MapPost("/{id:long}/cancel", async (long id, [FromBody] ReasonBody b,
            IPurchaseOrderService s, CancellationToken ct) =>
            { await s.CancelAsync(id, b.Reason, ct); return Results.NoContent(); })
            .RequireAuthorization(cancel);

        g.MapGet("/", async ([FromQuery] string? status, [FromQuery] long? vendorId,
            IPurchaseOrderService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(status, vendorId, ct))).RequireAuthorization(read);

        g.MapGet("/{id:long}", async (long id, IPurchaseOrderService s, CancellationToken ct) =>
            { var d = await s.GetDetailAsync(id, ct); return d is null ? Results.NotFound() : Results.Ok(d); })
            .RequireAuthorization(read);

        // ?copy=true → "สำเนา" watermark; default/false → "ต้นฉบับ" (bound as bool?,
        // FE sends ?copy=true). Print tracking is a separate POST /mark-printed
        // (PrintEndpoints) mirroring the shipped Sales pattern — see Phase C deviation.
        g.MapGet("/{id:long}/pdf", async (long id, [FromQuery] bool? copy,
            IPurchaseOrderService s, CancellationToken ct) =>
            Results.File(await s.BuildPdfAsync(id, ct, copy ?? false), "application/pdf",
                $"purchase-order-{id}.pdf"))
            .RequireAuthorization(read);

        app.MapGet("/reports/outstanding-po", async (
            [FromQuery(Name = "as_of")] DateOnly? asOf, [FromQuery] long? vendorId,
            [FromQuery(Name = "overdue_only")] bool? overdueOnly,
            IPurchaseOrderService s, CancellationToken ct) =>
            Results.Ok(await s.OutstandingAsync(
                asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), vendorId,
                overdueOnly ?? false, ct)))
            .RequireAuthorization(read).WithTags("Purchase Orders");

        // Sprint 13j-PURCH Phase B — AP Aging report (deviation D1: no ReportEndpoints.cs;
        // mapped here next to outstanding-po, SAME auth policy = PurchaseOrderRead).
        // asOf default = Bangkok today (Asia/Bangkok = UTC+7), never user-trusted (§10).
        app.MapGet("/reports/ap-aging", async (
            [FromQuery] DateOnly? asOf, [FromQuery] long? vendorId,
            Accounting.Application.Reports.IApAgingService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(
                asOf ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), vendorId, ct)))
            .RequireAuthorization(read).WithTags("Purchase Orders");

        return app;
    }
}
