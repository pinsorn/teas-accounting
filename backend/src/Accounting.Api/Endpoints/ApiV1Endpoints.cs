using Accounting.Api.Authorization;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 14 — external <c>/api/v1/*</c> surface. ADDITIVE: existing root
/// routes (BFF/JWT) are untouched. The group is ApiKey-scheme-only
/// (<see cref="ApiKeyOnlyPolicy"/>); EACH endpoint additionally requires a
/// scope (P6 — <see cref="PermissionHandler"/> checks the key's ScopesJson).
/// Handlers DELEGATE to the same service interfaces as root — zero
/// business-logic duplication. Subset = "what a microservice bills with".
/// </summary>
public static class ApiV1Endpoints
{
    public const string ApiKeyOnlyPolicy = "ApiKeyOnly";

    // ApiKey-scheme-pinned + scope (P6). Root JWT routes keep "perm:" — the
    // scheme split IS the auth isolation (X-Api-Key can't satisfy "perm:";
    // a JWT can't satisfy "apiperm:").
    private static string P(string scope) => PermissionPolicyProvider.ApiKeyPolicyPrefix + scope;

    public static IEndpointRouteBuilder MapExternalApiV1(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1").WithTags("External API v1");

        // ── Tax Invoices ─────────────────────────────────────────────────────
        v1.MapPost("/tax-invoices", async (
            [FromBody] CreateTaxInvoiceRequest req, IValidator<CreateTaxInvoiceRequest> v,
            ITaxInvoiceService svc, CancellationToken ct) =>
        {
            await v.ValidateAndThrowAsync(req, ct);   // → validation_error envelope (P5)
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/api/v1/tax-invoices/{id}", new { tax_invoice_id = id });
        }).RequireAuthorization(P("sales.tax_invoice.create"));
        v1.MapPost("/tax-invoices/{id:long}/post", async (
            long id, ITaxInvoiceService svc, CancellationToken ct) =>
            Results.Ok(await svc.PostAsync(id, ct)))
            .RequireAuthorization(P("sales.tax_invoice.post"));
        v1.MapGet("/tax-invoices/{id:long}", async (
            long id, ITaxInvoiceService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(P("sales.tax_invoice.read"));
        v1.MapGet("/tax-invoices", async (
            [AsParameters] TaxInvoiceListQueryParams qp,
            ITaxInvoiceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(new TaxInvoiceListQuery(
                qp.DateFrom, qp.DateTo, qp.CustomerId, qp.Status, qp.Cursor, qp.Limit ?? 25,
                qp.BusinessUnitId, qp.IncludeUnspecified ?? false), ct)))
            .RequireAuthorization(P("sales.tax_invoice.read"));

        // ── Receipts ─────────────────────────────────────────────────────────
        v1.MapPost("/receipts", async (
            [FromBody] CreateReceiptRequest req, IValidator<CreateReceiptRequest> v,
            IReceiptService svc, CancellationToken ct) =>
        {
            await v.ValidateAndThrowAsync(req, ct);
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/api/v1/receipts/{id}", new { receipt_id = id });
        }).RequireAuthorization(P("sales.receipt.create"));
        v1.MapPost("/receipts/{id:long}/post", async (
            long id, IReceiptService svc, CancellationToken ct) =>
            Results.Ok(await svc.PostAsync(id, ct)))
            .RequireAuthorization(P("sales.receipt.post"));
        v1.MapGet("/receipts/{id:long}", async (
            long id, IReceiptService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(P("sales.receipt.read"));
        v1.MapGet("/receipts", async (
            [FromQuery] long? cursor, [FromQuery] int? limit,
            IReceiptService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct, null, false)))
            .RequireAuthorization(P("sales.receipt.read"));

        // ── Quotations (Sprint 10) ──────────────────────────────────────────
        v1.MapPost("/quotations", async (
            [FromBody] CreateQuotationRequest req, IValidator<CreateQuotationRequest> v,
            IQuotationService svc, CancellationToken ct) =>
        {
            await v.ValidateAndThrowAsync(req, ct);
            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/api/v1/quotations/{id}", new { quotation_id = id });
        }).RequireAuthorization(P("sales.quotation.create"));
        v1.MapPost("/quotations/{id:long}/send", async (
            long id, IQuotationService svc, CancellationToken ct) =>
            { await svc.SendAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(P("sales.quotation.send"));
        v1.MapGet("/quotations/{id:long}", async (
            long id, IQuotationService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(P("sales.quotation.read"));
        v1.MapGet("/quotations", async (
            [FromQuery] string? status, IQuotationService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(status, ct)))
            .RequireAuthorization(P("sales.quotation.read"));

        // ── Customers (resolve/create for integrations) ─────────────────────
        v1.MapPost("/customers", async (
            [FromBody] CreateCustomerRequest req, IValidator<CreateCustomerRequest> v,
            ICustomerService svc, CancellationToken ct) =>
        {
            await v.ValidateAndThrowAsync(req, ct);
            var id = await svc.CreateAsync(req, ct);
            return Results.Created($"/api/v1/customers/{id}", new { customer_id = id });
        }).RequireAuthorization(P("master.customer.manage"));
        v1.MapGet("/customers/{id:long}", async (
            long id, ICustomerService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } c ? Results.Ok(c) : Results.NotFound())
            .RequireAuthorization(P("master.customer.read"));
        v1.MapGet("/customers", async (
            [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
            ICustomerService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(search, page is null or 0 ? 1 : page.Value,
                pageSize is null or 0 ? 50 : pageSize.Value, ct)))
            .RequireAuthorization(P("master.customer.read"));

        // ── Products (map external SKU → product_id) ────────────────────────
        v1.MapGet("/products/{id:long}", async (
            long id, IProductService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireAuthorization(P("master.product.read"));
        v1.MapGet("/products", async (
            [FromQuery] bool? includeInactive, [FromQuery] string? search,
            IProductService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeInactive ?? false, search, ct)))
            .RequireAuthorization(P("master.product.read"));

        // ── System info ─────────────────────────────────────────────────────
        v1.MapGet("/system/info", (IConfiguration cfg) => Results.Ok(new
        {
            version = typeof(Program).Assembly.GetName().Version?.ToString(),
            vat_mode = cfg.GetValue<bool>("Tax:VatMode"),
            vat_rate = cfg.GetValue<decimal>("Tax:VatRate"),
            pnd30_submission_mode = cfg.GetValue<string>("Tax:Pnd30SubmissionMode"),
            document_number_format = "MM-YYYY-PREFIX-NNNN",
            timezone = "Asia/Bangkok",
        })).RequireAuthorization(P("sys.system_info.read"));

        return app;
    }
}
