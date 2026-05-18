using Accounting.Api.Authorization;
using Accounting.Application.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 14 — ApiKey admin CRUD (BFF/JWT, NOT the external surface).
/// Perm <c>sys.api_key.manage</c> (SUPER_ADMIN + COMPANY_ADMIN). Plaintext is
/// returned ONCE on create/rotate.
/// </summary>
public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var manage = PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.ApiKeyManage;
        var g = app.MapGroup("/api-keys").WithTags("API Keys").RequireAuthorization(manage);

        g.MapGet("/", async (IApiKeyService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(ct)));

        g.MapPost("/", async ([FromBody] CreateApiKeyRequest req,
            IValidator<CreateApiKeyRequest> v, IApiKeyService s, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var res = await s.CreateAsync(req, ct);
            return Results.Created($"/api-keys/{res.ApiKeyId}", res);   // plaintext ONCE
        });

        g.MapDelete("/{id:long}", async (long id, IApiKeyService s, CancellationToken ct) =>
            { await s.RevokeAsync(id, ct); return Results.NoContent(); });

        g.MapPost("/{id:long}/rotate", async (long id, IApiKeyService s, CancellationToken ct) =>
            Results.Ok(await s.RotateAsync(id, ct)));   // new plaintext ONCE

        return app;
    }
}
