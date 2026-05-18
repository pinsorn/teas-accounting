using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Accounting.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement requirement)
    {
        // Super admins bypass per-permission checks (CLAUDE.md §4.1).
        var isSuperAdmin = string.Equals(
            ctx.User.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        if (isSuperAdmin ||
            ctx.User.HasClaim(c => c.Type == TenantClaims.Permission && c.Value == requirement.Permission))
        {
            ctx.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
