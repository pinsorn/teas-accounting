using Accounting.Api.Authorization;
using Accounting.Application.Bank;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Bank-account master CRUD (specs/bank-reconciliation.md B1.6). Read gated by
/// <see cref="Permissions.Bank.AccountRead"/>; create/update/deactivate gated by
/// <see cref="Permissions.Bank.AccountManage"/> (mirrors CustomerEndpoints' read/manage split).
/// </summary>
public static class BankAccountEndpoints
{
    public static IEndpointRouteBuilder MapBankAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bank-accounts").WithTags("Bank Accounts");

        var readPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Bank.AccountRead;
        var managePol = PermissionPolicyProvider.PolicyPrefix + Permissions.Bank.AccountManage;

        group.MapPost("/", async (
            [FromBody] CreateBankAccountRequest req,
            IValidator<CreateBankAccountRequest> validator,
            IBankAccountService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateAsync(req, ct);
            return Results.Created($"/bank-accounts/{id}", new { bank_account_id = id });
        }).RequireAuthorization(managePol);

        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] UpdateBankAccountRequest req,
            IValidator<UpdateBankAccountRequest> validator,
            IBankAccountService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            await service.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        group.MapDelete("/{id:int}", async (int id, IBankAccountService service, CancellationToken ct) =>
        {
            await service.DeactivateAsync(id, ct);   // soft-deactivate, not hard delete
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        group.MapGet("/{id:int}", async (int id, IBankAccountService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(readPol);

        group.MapGet("/", async (
            [FromQuery] bool? includeInactive, IBankAccountService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(includeInactive ?? false, ct)))
            .RequireAuthorization(readPol);

        return app;
    }
}
