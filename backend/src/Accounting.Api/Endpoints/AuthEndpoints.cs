using Accounting.Application.Identity;
using Microsoft.AspNetCore.Mvc;

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
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
