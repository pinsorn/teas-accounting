using System.Text.RegularExpressions;
using Accounting.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Tier-2 R3 (specs/mcp-manual-journal.md §10) — pins the frontend's api-keys scope picker
/// (frontend/app/(dashboard)/settings/api-keys/page.tsx) in sync with the backend MCP scope
/// catalog (McpScopes.All). Reads the FE source directly (RbacAuthMap pattern — TEAS_REPO_ROOT),
/// no DB needed. Caught, once fixed: `sales.sales_order.manage` was pre-selected by
/// MCP_DEFAULT_SCOPES yet absent from ALL_SCOPES (couldn't be re-picked if deselected+reselected),
/// and would have caught `gl.journal.create` missing from ALL_SCOPES the same way.
/// </summary>
public sealed class McpScopeFrontendParityTests
{
    private static string ReadApiKeysPageSource()
    {
        var path = Path.Combine(RbacTestPaths.RepoRoot(),
            "frontend", "app", "(dashboard)", "settings", "api-keys", "page.tsx");
        File.Exists(path).Should().BeTrue($"expected the FE api-keys page at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Extracts every single-quoted scope string out of the `const NAME = [ ... ]`
    /// array literal (stops at the FIRST closing bracket — neither array nests brackets).</summary>
    private static IReadOnlyList<string> ExtractScopeArray(string source, string constName)
    {
        var marker = $"const {constName} = [";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"expected to find `{marker}` in the FE file");
        var bodyStart = start + marker.Length;
        var end = source.IndexOf(']', bodyStart);
        var body = source.Substring(bodyStart, end - bodyStart);
        return Regex.Matches(body, "'([a-z0-9_.]+)'")
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }

    [Fact]
    public void Mcp_default_scopes_are_a_subset_of_all_scopes()
    {
        var source = ReadApiKeysPageSource();
        var allScopes = ExtractScopeArray(source, "ALL_SCOPES");
        var mcpDefaults = ExtractScopeArray(source, "MCP_DEFAULT_SCOPES");

        mcpDefaults.Should().BeSubsetOf(allScopes,
            "every scope pre-selected for a default mcp key must also be pickable in ALL_SCOPES " +
            "— otherwise a user who deselects then reselects it can never get it back");
    }

    [Fact]
    public void Every_mcp_default_scope_normalizes_cleanly_through_McpScopes()
    {
        var source = ReadApiKeysPageSource();
        var mcpDefaults = ExtractScopeArray(source, "MCP_DEFAULT_SCOPES");

        var normalized = McpScopes.Normalize(mcpDefaults);
        normalized.Should().BeEquivalentTo(mcpDefaults,
            "every FE default-mcp-key scope must be in the backend's McpScopes.All catalog — a " +
            "drifted entry here is silently dropped for every real mcp-kind key created with " +
            "the FE's defaults, with no error surfaced anywhere");
    }
}
