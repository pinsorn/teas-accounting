using System.Reflection;

namespace Accounting.Api;

/// <summary>
/// Build/version metadata for the running API. The informational version is stamped by
/// MinVer at build time from the git tag (<c>vX.Y.Z</c>); we strip the <c>+&lt;sha&gt;</c>
/// build-metadata suffix for a clean display value (e.g. "1.0.0", "1.0.1-alpha.0.3").
/// </summary>
internal static class AppBuildInfo
{
    public static readonly string Version = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(AppBuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        return typeof(AppBuildInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
