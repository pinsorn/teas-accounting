using Accounting.Api.Authorization;
using Accounting.Application.FixedAsset;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class FixedAssetEndpoints
{
    public static IEndpointRouteBuilder MapFixedAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fixed-assets").WithTags("Fixed Assets");

        group.MapGet("/", async (
            [FromQuery] string? status, [FromQuery] string? category,
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            IFixedAssetService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(status, category, from, to, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        group.MapGet("/reports/register", async (
            [FromQuery] DateOnly asOf, IFixedAssetService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetRegisterReportAsync(asOf, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        group.MapGet("/reports/accumulated-depreciation", async (
            [FromQuery] int year, IFixedAssetService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetAccumulatedDepreciationReportAsync(year, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        group.MapGet("/{id:long}", async (long id, IFixedAssetService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        group.MapPost("/", async (
            [FromBody] CreateFixedAssetRequest req,
            IValidator<CreateFixedAssetRequest> validator,
            IFixedAssetService svc, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await svc.CreateDraftAsync(req, ct);
            return Results.Created($"/fixed-assets/{id}", new { fixed_asset_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Manage);

        group.MapPut("/{id:long}", async (long id,
            [FromBody] UpdateFixedAssetRequest req,
            IValidator<UpdateFixedAssetRequest> validator,
            IFixedAssetService svc, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            await svc.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Manage);

        group.MapPost("/{id:long}/activate", async (long id, IFixedAssetService svc, CancellationToken ct) =>
        {
            await svc.ActivateAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Manage);

        group.MapPost("/{id:long}/cancel", async (long id, IFixedAssetService svc, CancellationToken ct) =>
        {
            await svc.CancelAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Manage);

        group.MapPost("/{id:long}/dispose", async (long id,
            [FromBody] DisposeFixedAssetRequest req,
            IValidator<DisposeFixedAssetRequest> validator,
            IFixedAssetService svc, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await svc.DisposeAsync(id, req, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Dispose);

        group.MapPost("/{id:long}/write-off", async (long id,
            [FromBody] WriteOffFixedAssetRequest req,
            IValidator<WriteOffFixedAssetRequest> validator,
            IFixedAssetService svc, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await svc.WriteOffAsync(id, req, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Dispose);

        var runs = app.MapGroup("/depreciation-runs").WithTags("Fixed Assets");

        runs.MapPost("/", async (
            [FromBody] GenerateDepreciationRequest req,
            IValidator<GenerateDepreciationRequest> validator,
            IFixedAssetService svc, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await svc.GenerateDepreciationAsync(req.Year, req.Month, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.DepreciationRun);

        runs.MapGet("/", async (IFixedAssetService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListDepreciationRunsAsync(ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        runs.MapGet("/{year:int}/{month:int}", async (int year, int month, IFixedAssetService svc, CancellationToken ct) =>
            await svc.GetDepreciationRunAsync(year, month, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.FixedAsset.Read);

        return app;
    }
}
