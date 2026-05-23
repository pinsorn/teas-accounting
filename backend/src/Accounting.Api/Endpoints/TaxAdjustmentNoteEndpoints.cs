using Accounting.Api.Authorization;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class TaxAdjustmentNoteEndpoints
{
    public static IEndpointRouteBuilder MapTaxAdjustmentNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tax-adjustment-notes").WithTags("Credit/Debit Notes");

        group.MapPost("/", async (
            [FromBody] CreateTaxAdjustmentNoteRequest req,
            IValidator<CreateTaxAdjustmentNoteRequest> validator,
            ITaxAdjustmentNoteService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/tax-adjustment-notes/{id}", new { note_id = id });
        })
        .RequireAuthorization(ctx =>
            ctx.RequireAuthenticatedUser().RequireAssertion(c =>
                c.User.HasClaim("perm", Permissions.Sales.CreditNoteCreate) ||
                c.User.HasClaim("perm", Permissions.Sales.DebitNoteCreate) ||
                c.User.HasClaim(TenantClaimsHelper.IsSuperAdmin, "true")));

        group.MapPost("/{id:long}/post", async (long id, ITaxAdjustmentNoteService svc, CancellationToken ct) =>
            Results.Ok(await svc.PostAsync(id, ct)))
        .RequireAuthorization(ctx =>
            ctx.RequireAuthenticatedUser().RequireAssertion(c =>
                c.User.HasClaim("perm", Permissions.Sales.CreditNotePost) ||
                c.User.HasClaim("perm", Permissions.Sales.DebitNotePost) ||
                c.User.HasClaim(TenantClaimsHelper.IsSuperAdmin, "true")));

        // Sprint 13i B1 — dedicated read perms added so read-tier roles
        // (AUDITOR, SALES_STAFF) can view CN/DN without create/post grants.
        static bool CanRead(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext c) =>
            c.User.HasClaim("perm", Permissions.Sales.CreditNoteRead) ||
            c.User.HasClaim("perm", Permissions.Sales.DebitNoteRead) ||
            c.User.HasClaim("perm", Permissions.Sales.CreditNoteCreate) ||
            c.User.HasClaim("perm", Permissions.Sales.DebitNoteCreate) ||
            c.User.HasClaim("perm", Permissions.Sales.CreditNotePost) ||
            c.User.HasClaim("perm", Permissions.Sales.DebitNotePost) ||
            c.User.HasClaim(TenantClaimsHelper.IsSuperAdmin, "true");

        group.MapGet("/", async ([FromQuery] string? noteType, [FromQuery] long? cursor,
            [FromQuery] int? limit, [FromQuery] int? businessUnitId,
            [FromQuery] bool? includeUnspecified,
            ITaxAdjustmentNoteService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(noteType, cursor, limit ?? 25, ct,
                    businessUnitId, includeUnspecified ?? false)))
        .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanRead));

        group.MapGet("/{id:long}", async (long id, ITaxAdjustmentNoteService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanRead));

        group.MapGet("/{id:long}/pdf", async (long id, [FromQuery] bool? copy, ITaxAdjustmentNoteService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPdfAsync(id, ct, copy ?? false), "application/pdf", $"note-{id}.pdf"))
        .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanRead));

        return app;
    }
}

internal static class TenantClaimsHelper
{
    public const string IsSuperAdmin = "is_super_admin";
}
