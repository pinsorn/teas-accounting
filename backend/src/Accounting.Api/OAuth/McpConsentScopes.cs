namespace Accounting.Api.OAuth;

/// <summary>
/// H4/M11 — filters a normalized MCP grant down to the scopes the CONSENTING/REFRESHING user is
/// actually authorized for, by mapping each scope to the RBAC permission that authorizes it and
/// keeping only the scopes whose mapped permission the user holds. Most scopes ARE their permission
/// code (identity); the exceptions are enumerated (RBAC has no granular quotation perm; system_info
/// is a public read). A super-admin is NOT filtered here — callers short-circuit that (see spec §0).
/// </summary>
internal static class McpConsentScopes
{
    /// <param name="grantedScopes">Already ∩ McpScopes.All (i.e. the result of McpScopes.Normalize).</param>
    /// <param name="userPermissions">The user's EFFECTIVE permission codes for the target company
    /// (IPermissionLookup.LoadAsync .Permissions), as an Ordinal set.</param>
    public static IReadOnlyList<string> FilterToRbac(
        IReadOnlyList<string> grantedScopes, IReadOnlySet<string> userPermissions) =>
        grantedScopes
            .Where(s => RequiredPermission(s) is not { } perm || userPermissions.Contains(perm))
            .ToArray();

    /// <summary>The RBAC permission a user must hold to be granted <paramref name="scope"/>.
    /// <c>null</c> ⇒ no permission required (public read).</summary>
    private static string? RequiredPermission(string scope) => scope switch
    {
        "sales.quotation.read"   => "sales.quotation.manage",
        "sales.quotation.create" => "sales.quotation.manage",
        "sys.system_info.read"   => null,
        _                        => scope,   // identity: scope code == permission code
    };
}
