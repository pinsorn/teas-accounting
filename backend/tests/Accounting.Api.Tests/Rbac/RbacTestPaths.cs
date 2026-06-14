using System.IO;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Resolves the repo root for writing generated RBAC docs. Climbing <c>..</c> from the
/// test bin fails on this dev box because W:/U: are <c>subst</c> drives whose root clamps
/// the walk at <c>backend/</c>; so honour <c>TEAS_REPO_ROOT</c> first (set by the dev/CI
/// runner), then fall back to climbing for the <c>CLAUDE.md</c>+<c>docs/</c> marker (works
/// on real paths / CI checkouts).
/// </summary>
public static class RbacTestPaths
{
    public static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("TEAS_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs", "superpowers")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the TEAS repo root. Set TEAS_REPO_ROOT to the repo root " +
            "(the folder holding CLAUDE.md and docs/).");
    }

    public static string RbacDocsDir()
    {
        var dir = Path.Combine(RepoRoot(), "docs", "rbac");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
