using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Microsoft.AspNetCore.Mvc;
using Accounting.Api.Authorization;

namespace Accounting.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest req,
            ILoginService login,
            CancellationToken ct) =>
        {
            var result = await login.LoginAsync(req, ct);
            return result.MfaRequired
                ? Results.Ok(new { mfa_required = true })
                : Results.Ok(new
                {
                    access_token = result.Token.Token,
                    expires_at   = result.Token.ExpiresAt,
                    token_type   = "Bearer",
                });
        })
        .WithName("Login")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .AllowAnonymous()  // ponytail: explicit — FallbackPolicy now requires auth by default
        .RequireRateLimiting("login"); // ponytail: 10 req/min/IP fixed-window (Program.cs)

        // Onboarding-switcher spec (2026-06-16) — SUPER-ADMIN ONLY. Re-scopes the caller's session
        // to another company by re-issuing the JWT (RLS is pinned at the DB session, so a new token
        // is the only way to move tenant). Gated by the super-admin-only Master.CompanyManage
        // permission (declared so the RBAC audit map/matrix classify it as a Perm endpoint, like
        // /companies). anonymous → 401; authenticated-without-CompanyManage → 403 at the policy; the
        // explicit IsSuperAdmin check in the handler + service stays as defence in depth.
        group.MapPost("/switch-company/{companyId:int}", async (
            int companyId,
            ITenantContext tenant,
            ICompanySwitchService switcher,
            CancellationToken ct) =>
        {
            if (!tenant.IsSuperAdmin)
                return Results.Problem(
                    title: "auth.forbidden",
                    detail: "Only a super-admin may switch company.",
                    statusCode: StatusCodes.Status403Forbidden);

            // company.not_found (missing or inactive) → 404 via DomainExceptionMiddleware.
            var token = await switcher.SwitchAsync(companyId, ct);
            return Results.Ok(new
            {
                access_token = token.Token,
                expires_at   = token.ExpiresAt,
                token_type   = "Bearer",
            });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyManage)
        .WithName("SwitchCompany")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
