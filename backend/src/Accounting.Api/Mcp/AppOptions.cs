namespace Accounting.Api.Mcp;

/// <summary>
/// M2 (MCP) — binds the <c>App</c> config section. <see cref="BaseUrl"/> is the
/// public frontend origin used to build the human-approval deep-link a create-draft
/// MCP tool returns (<c>{BaseUrl}/&lt;route&gt;/{id}?action=approve</c>). Dev value lives in
/// appsettings; set per-environment in production.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public frontend base URL (no trailing slash needed). Dev = http://localhost:3000.</summary>
    public string BaseUrl { get; init; } = "http://localhost:3000";
}
