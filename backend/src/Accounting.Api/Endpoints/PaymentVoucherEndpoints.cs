using Accounting.Api.Authorization;
using Accounting.Application.Purchase;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class PaymentVoucherEndpoints
{
    public static IEndpointRouteBuilder MapPaymentVoucherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payment-vouchers").WithTags("Payment Vouchers");

        group.MapPost("/", async (
            [FromBody] CreatePaymentVoucherRequest req,
            IValidator<CreatePaymentVoucherRequest> validator,
            IPaymentVoucherService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/payment-vouchers/{id}", new { payment_voucher_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherCreate);

        group.MapPost("/{id:long}/approve", async (long id, IPaymentVoucherService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherApprove);

        group.MapPost("/{id:long}/post", async (long id, IPaymentVoucherService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherPost);

        // cont.76 — guided "create บันทึกใบกำกับภาษีซื้อ off this PV" path: pre-fills a VI draft
        // from the PV and links it back. Requires the VI-create permission (it creates a VI).
        group.MapPost("/{id:long}/vendor-invoice", async (long id,
            [FromBody] CreateViFromPvRequest req,
            IValidator<CreateViFromPvRequest> validator,
            IPaymentVoucherService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var viId = await service.CreateVendorInvoiceFromPvAsync(id, req, ct);
            return Results.Created($"/vendor-invoices/{viId}", new { vendor_invoice_id = viId });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceCreate);

        group.MapGet("/", async ([FromQuery] long? cursor, [FromQuery] int? limit,
            [FromQuery] bool? incompleteOnly, IPaymentVoucherService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct, incompleteOnly ?? false)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherRead);

        group.MapGet("/{id:long}", async (long id, IPaymentVoucherService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherRead);

        // ?copy=true → "สำเนา" watermark; default/false → "ต้นฉบับ". Print tracking is
        // a separate POST /mark-printed (PrintEndpoints), mirroring the Sales pattern.
        group.MapGet("/{id:long}/pdf", async (long id, [FromQuery] bool? copy,
            IPaymentVoucherService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPdfAsync(id, ct, copy ?? false), "application/pdf",
                $"payment-voucher-{id}.pdf"))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.PaymentVoucherRead);

        return app;
    }
}
