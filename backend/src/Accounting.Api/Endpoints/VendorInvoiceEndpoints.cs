using Accounting.Api.Authorization;
using Accounting.Application.Purchase;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class VendorInvoiceEndpoints
{
    public static IEndpointRouteBuilder MapVendorInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vendor-invoices").WithTags("Vendor Invoices");

        group.MapPost("/", async (
            [FromBody] CreateVendorInvoiceRequest req,
            IValidator<CreateVendorInvoiceRequest> validator,
            IVendorInvoiceService service,
            CancellationToken ct) =>
        {
            var v = await validator.ValidateAsync(req, ct);
            if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/vendor-invoices/{id}", new { vendor_invoice_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceCreate);

        group.MapPut("/{id:long}", async (long id,
            [FromBody] CreateVendorInvoiceRequest req,
            IValidator<CreateVendorInvoiceRequest> validator,
            IVendorInvoiceService service, CancellationToken ct) =>
        {
            var v = await validator.ValidateAsync(req, ct);
            if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
            await service.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceCreate);

        group.MapPost("/{id:long}/claim-period", async (long id,
            [FromBody] SetClaimPeriodRequest req,
            IVendorInvoiceService service, CancellationToken ct) =>
        {
            await service.SetClaimPeriodAsync(id, req.VatClaimPeriod, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceCreate);

        group.MapPost("/{id:long}/post", async (long id, IVendorInvoiceService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoicePost);

        group.MapGet("/", async ([FromQuery] long? cursor, [FromQuery] int? limit,
            IVendorInvoiceService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceRead);

        group.MapGet("/{id:long}", async (long id, IVendorInvoiceService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceRead);

        return app;
    }
}
