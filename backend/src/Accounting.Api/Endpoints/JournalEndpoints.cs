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

        return app;
    }
}
