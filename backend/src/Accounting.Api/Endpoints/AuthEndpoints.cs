using System.Security.Claims;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
        //
        // 2026-07 review fix F-A — switch-company now enforces the SAME absolute session cap as
        // /auth/refresh (AbsoluteSessionCap.CheckOrThrow, called authoritatively inside
        // CompanySwitchService before it mints the new token) and carries the ORIGINAL auth_time
        // forward onto the re-issued token, so repeated switches can no longer reset the cap clock.
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

            AccessToken token;
            try
            {
                // company.not_found (missing or inactive) → 404 via DomainExceptionMiddleware.
                token = await switcher.SwitchAsync(companyId, ct);
            }
            catch (SessionAbsoluteCapExceededException ex)
            {
                return Results.Problem(
                    title: AbsoluteSessionCap.ProblemCode,
                    detail: ex.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
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

        // WP2.1 (F16, D6 sliding re-issue) — re-issue a fresh access token for a STILL-VALID
        // caller, same claims/company/branch (mirrors switch-company, just re-targeting the
        // caller's own current company). Auth is the [Authorize] fallback policy: the JWT bearer
        // handler already rejects an EXPIRED token before this handler ever runs (ValidateLifetime
        // in Program.cs), so an expired session naturally 401s here with no extra code — sliding
        // only ever extends an ACTIVE session, never resurrects a dead one.
        //
        // Security notes (design mandatory, all four enforced below):
        //  (1) cookie policy is unchanged (BFF route owns httpOnly/secure/sameSite).
        //  (2) ABSOLUTE cap — auth_time (stamped once at login, carried forward by AuthTime below,
        //      never reset by a refresh) rejects past AbsoluteSessionCapHours (403), forcing a
        //      full re-login regardless of how often the token slides.
        //  (3) idle timeout is FE-owned (useSessionKeepAlive stops calling this endpoint after N
        //      idle minutes) — nothing to enforce server-side beyond the absolute cap.
        //  (4) re-validates the user is active/not-locked via a fresh DB read — never blindly
        //      re-signs a since-disabled account just because its old token still parses.
        //  (5) scope never widens: company/branch/super-admin are copied from the CALLER's
        //      current claims (not re-derived), and roles/permissions are RELOADED for that same
        //      company (so a REVOKED grant takes effect on refresh — tightening only, never a
        //      widening path).
        group.MapPost("/refresh", async (
            ClaimsPrincipal principal,
            ITenantContext tenant,
            IUserRepository users,
            IPermissionLookup permissions,
            IJwtTokenIssuer tokens,
            IOptionsMonitor<JwtOptions> jwtOptions,
            IClock clock,
            CancellationToken ct) =>
        {
            if (tenant.UserId is not { } userId)
                return Results.Problem(
                    title: "auth.unauthenticated", detail: "No session.",
                    statusCode: StatusCodes.Status401Unauthorized);

            DateTimeOffset authTime;
            try
            {
                authTime = AbsoluteSessionCap.CheckOrThrow(
                    principal, clock, jwtOptions.CurrentValue.AbsoluteSessionCapHours);
            }
            catch (SessionAbsoluteCapExceededException ex)
            {
                return Results.Problem(
                    title: AbsoluteSessionCap.ProblemCode,
                    detail: ex.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var user = await users.FindByIdAsync(userId, ct);
            if (user is null || !user.IsActive || user.IsLocked(clock.UtcNow))
                return Results.Problem(
                    title: "auth.account_disabled",
                    detail: "Account is no longer active.",
                    statusCode: StatusCodes.Status403Forbidden);

            var (roles, perms) = await permissions.LoadAsync(userId, tenant.CompanyId, ct);

            var token = tokens.Issue(new TokenClaims(
                UserId: userId,
                Username: tenant.Username ?? user.Username,
                CompanyId: tenant.CompanyId,
                BranchId: tenant.BranchId,
                IsSuperAdmin: tenant.IsSuperAdmin,
                Roles: roles,
                Permissions: perms,
                AuthTime: authTime));

            return Results.Ok(new
            {
                access_token = token.Token,
                expires_at   = token.ExpiresAt,
                token_type   = "Bearer",
            });
        })
        .RequireAuthorization()
        .WithName("RefreshToken")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
