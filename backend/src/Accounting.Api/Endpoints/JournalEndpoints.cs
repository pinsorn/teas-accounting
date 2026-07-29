using Accounting.Api.Authorization;
using Accounting.Application.Ledger;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class JournalEndpoints
{
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/journals").WithTags("Journals");

        group.MapPost("/", async (
            [FromBody] CreateJournalRequest req,
            IValidator<CreateJournalRequest> validator,
            IJournalService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/journals/{id}", new { journal_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalCreate);

        group.MapPost("/{id:long}/post", async (long id, IJournalService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalPost);

        // First JE read endpoint (GL drill-down target) — 404 for not-found/other-tenant alike.
        group.MapGet("/{id:long}", async (long id, IJournalService service, CancellationToken ct) =>
            Results.Ok(await service.GetDetailAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalRead);

        // Manual JV — create AND post in one call (specs/manual-jv-and-coa-management.md §B0).
        // Gated on gl.journal.post, NOT .create: with no draft state, POST is the only act, and
        // posting arbitrary journals is the most powerful write in the product (see §B6).
        group.MapPost("/manual", async (
            [FromBody] CreateManualJournalRequest req,
            IValidator<CreateManualJournalRequest> validator,
            IJournalService service, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            return Results.Ok(await service.CreateAndPostManualAsync(req, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalPost);

        // Optional query params MUST be nullable (MasterEndpoints.cs:75-77 — the minimal-API
        // binder rejects a param-less call before the handler body otherwise).
        group.MapGet("/", async (
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IJournalService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(from, to, search, page ?? 1, pageSize ?? 50, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.JournalRead);

        return app;
    }
}
