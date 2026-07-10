using Accounting.Api.Authorization;
using Accounting.Application.Expense;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class ExpenseClaimEndpoints
{
    public static IEndpointRouteBuilder MapExpenseClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/expense-claims").WithTags("Expense Claims");

        group.MapPost("/", async (
            [FromBody] CreateExpenseClaimRequest req,
            IValidator<CreateExpenseClaimRequest> validator,
            IExpenseClaimService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateDraftAsync(req, ct);
            return Results.Created($"/expense-claims/{id}", new { expense_claim_id = id });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimCreate);

        group.MapPut("/{id:long}", async (long id,
            [FromBody] UpdateExpenseClaimRequest req,
            IValidator<UpdateExpenseClaimRequest> validator,
            IExpenseClaimService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            await service.UpdateDraftAsync(id, req, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimCreate);

        group.MapPost("/{id:long}/submit", async (long id, IExpenseClaimService service, CancellationToken ct) =>
        {
            await service.SubmitAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimCreate);

        group.MapPost("/{id:long}/approve", async (long id, IExpenseClaimService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimApprove);

        group.MapPost("/{id:long}/reject", async (long id,
            [FromBody] RejectExpenseClaimRequest req,
            IValidator<RejectExpenseClaimRequest> validator,
            IExpenseClaimService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            await service.RejectAsync(id, req, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimApprove);

        group.MapPost("/{id:long}/pay", async (long id,
            [FromBody] PayExpenseClaimRequest req,
            IValidator<PayExpenseClaimRequest> validator,
            IExpenseClaimService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await service.PayAsync(id, req, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimPay);

        group.MapPost("/{id:long}/cancel", async (long id, IExpenseClaimService service, CancellationToken ct) =>
        {
            await service.CancelAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimCreate);

        group.MapGet("/", async (
            [FromQuery] string? status, [FromQuery] long? employeeId,
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            IExpenseClaimService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(status, employeeId, from, to, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimRead);

        group.MapGet("/{id:long}", async (long id, IExpenseClaimService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Expense.ClaimRead);

        return app;
    }
}
