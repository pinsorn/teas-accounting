namespace Accounting.Domain.Entities.Identity;

/// <summary>
/// M1 (MCP) — the two API-key profiles. Stored as the <c>kind</c> text column on
/// <c>sys.api_keys</c>. <see cref="Integration"/> is the default (full scopes incl
/// <c>.post</c>); <see cref="Mcp"/> keys are read + <c>.create</c> only and are
/// structurally barred from holding any <c>.post</c> scope (compliance belt — an
/// AI agent key cannot post).
/// </summary>
public static class ApiKeyKinds
{
    public const string Integration = "integration";
    public const string Mcp = "mcp";

    public static bool IsValid(string? kind) =>
        kind is Integration or Mcp;
}
