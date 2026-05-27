using Accounting.Api.Authorization;
using Accounting.Application.Master;
using Accounting.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// CRUD endpoints for non-Customer master data. Customer lives in <see cref="CustomerEndpoints"/>.
/// All groups are gated by per-resource permissions registered in <see cref="Permissions"/>.
/// </summary>
public static class MasterEndpoints
{
    public static IEndpointRouteBuilder MapMasterEndpoints(this IEndpointRouteBuilder app)
    {
        MapBranches(app);
        MapVendors(app);
        MapAccounts(app);
        MapCompanies(app);
        MapDocumentPrefixes(app);
        MapExpenseCategories(app);
        return app;
    }

    // ------------------------- Branches -------------------------
    private static void MapBranches(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/branches").WithTags("Branches")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.BranchManage);

        g.MapPost("/", async ([FromBody] CreateBranchRequest req, IValidator<CreateBranchRequest> v,
            IBranchService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/branches/{await svc.CreateAsync(req, ct)}", null);
        });
        g.MapPut("/{id:int}", async (int id, [FromBody] UpdateBranchRequest req, IBranchService svc, CancellationToken ct)
            => { await svc.UpdateAsync(id, req, ct); return Results.NoContent(); });
        g.MapGet("/", async (IBranchService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
    }

    // ------------------------- Vendors -------------------------
    private static void MapVendors(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/vendors").WithTags("Vendors")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.VendorManage);

        g.MapPost("/", async ([FromBody] CreateVendorRequest req, IValidator<CreateVendorRequest> v,
            IVendorService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/vendors/{await svc.CreateAsync(req, ct)}", null);
        });
        g.MapPut("/{id:long}", async (long id, [FromBody] UpdateVendorRequest req, IVendorService svc, CancellationToken ct)
            => { await svc.UpdateAsync(id, req, ct); return Results.NoContent(); });
        // Optional query params MUST be nullable — minimal-API binder rejects a
        // param-less call before the handler body if they're non-nullable
        // (runtime-gotchas §2). Frontend selectors call /vendors with no page.
        g.MapGet("/", async ([FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
            IVendorService svc, CancellationToken ct)
            => Results.Ok(await svc.ListAsync(search, page ?? 1, pageSize ?? 50, ct)));
        g.MapGet("/{id:long}", async (long id, IVendorService svc, CancellationToken ct)
            => await svc.GetByIdAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
    }

    // ------------------------- Chart of Accounts -------------------------
    private static void MapAccounts(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/accounts").WithTags("Chart of Accounts")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CoaManage);

        g.MapPost("/", async ([FromBody] CreateAccountRequest req, IValidator<CreateAccountRequest> v,
            IChartOfAccountService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/accounts/{await svc.CreateAsync(req, ct)}", null);
        });
        g.MapPut("/{id:long}", async (long id, [FromBody] UpdateAccountRequest req, IChartOfAccountService svc, CancellationToken ct)
            => { await svc.UpdateAsync(id, req, ct); return Results.NoContent(); });
        g.MapGet("/", async ([FromQuery] AccountType? type, [FromQuery] bool activeOnly,
            IChartOfAccountService svc, CancellationToken ct)
            => Results.Ok(await svc.ListAsync(type, activeOnly, ct)));
    }

    // ------------------------- Companies (super-admin) -------------------------
    private static void MapCompanies(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/companies").WithTags("Companies")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyManage);

        g.MapPost("/", async ([FromBody] CreateCompanyRequest req, IValidator<CreateCompanyRequest> v,
            ICompanyService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/companies/{await svc.CreateAsync(req, ct)}", null);
        });
        g.MapPut("/{id:int}", async (int id, [FromBody] UpdateCompanyRequest req, ICompanyService svc, CancellationToken ct)
            => { await svc.UpdateAsync(id, req, ct); return Results.NoContent(); });
        g.MapGet("/", async (ICompanyService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
    }

    // ------------------------- Document Prefixes -------------------------
    private static void MapDocumentPrefixes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/document-prefixes").WithTags("Document Prefixes")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.DocPrefixManage);

        g.MapPost("/", async ([FromBody] CreateDocumentPrefixRequest req, IValidator<CreateDocumentPrefixRequest> v,
            IDocumentPrefixService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/document-prefixes/{await svc.CreateAsync(req, ct)}", null);
        });
        g.MapGet("/", async (IDocumentPrefixService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
    }

    // ------------------------- Expense Categories -------------------------
    private static void MapExpenseCategories(IEndpointRouteBuilder app)
    {
        // BP-01 (RV2): split auth — write needs `manage`, read-only listing needs the lighter
        // `read` (PV/VI-creating roles need the picker but must not manage the master).
        var g = app.MapGroup("/expense-categories").WithTags("Expense Categories");

        g.MapPost("/", async ([FromBody] CreateExpenseCategoryRequest req, IValidator<CreateExpenseCategoryRequest> v,
            IExpenseCategoryService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/expense-categories/{await svc.CreateAsync(req, ct)}", null);
        }).RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.ExpenseCatManage);

        g.MapGet("/", async (IExpenseCategoryService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)))
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.ExpenseCatRead);
    }
}
