using Accounting.Api.Authorization;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class TaxInvoiceEndpoints
{
    public static IEndpointRouteBuilder MapTaxInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tax-invoices").WithTags("Tax Invoices");

        group.MapPost("/", async (
            [FromBody] CreateTaxInvoiceRequest req,
            IValidator<CreateTaxInvoiceRequest> validator,
            ITaxInvoiceService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/tax-invoices/{id}", new { tax_invoice_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceCreate);

        group.MapPost("/{id:long}/post", async (long id, ITaxInvoiceService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoicePost);

        // ── Sprint 2 read surface ──────────────────────────────────────────────
        group.MapGet("/", async (
            [AsParameters] TaxInvoiceListQueryParams qp,
            ITaxInvoiceService service, CancellationToken ct) =>
        {
            var page = await service.ListAsync(new TaxInvoiceListQuery(
                qp.DateFrom, qp.DateTo, qp.CustomerId, qp.Status, qp.Cursor, qp.Limit ?? 25,
                qp.BusinessUnitId, qp.IncludeUnspecified ?? false,
                qp.Search, qp.Unpaid ?? false), ct);
            return Results.Ok(page);
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapGet("/{id:long}", async (long id, ITaxInvoiceService service, CancellationToken ct) =>
        {
            var d = await service.GetDetailAsync(id, ct);
            return d is null ? Results.NotFound() : Results.Ok(d);
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapGet("/{id:long}/xml", async (long id, ITaxInvoiceService service, CancellationToken ct) =>
        {
            var xml = await service.BuildXmlAsync(id, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(xml),
                "application/xml", $"tax-invoice-{id}.xml");
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapGet("/{id:long}/pdf", async (long id, [FromQuery] bool? copy, ITaxInvoiceService service, CancellationToken ct) =>
        {
            var pdf = await service.BuildPdfAsync(id, ct, copy ?? false);
            return Results.File(pdf, "application/pdf", $"tax-invoice-{id}.pdf");
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapPost("/{id:long}/resend", async (long id, ITaxInvoiceService service, CancellationToken ct) =>
            Results.Ok(await service.ResendAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoicePost);

        return app;
    }
}

/// <summary>Query-string binding for GET /tax-invoices (cursor list + filters).</summary>
public sealed record TaxInvoiceListQueryParams(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    long?     CustomerId,
    string?   Status,
    long?     Cursor,
    int?      Limit,
    int?      BusinessUnitId,
    bool?     IncludeUnspecified,
    string?   Search,
    bool?     Unpaid);
