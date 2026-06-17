using System;
using System.IO;
using System.Threading;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// Proves the highest-risk part of the first-run-secrets feature: a value written to a
/// reloadOnChange JSON file (the same shape the setup endpoint writes) is picked up LIVE by
/// IOptionsMonitor — no restart. This is the "write target == read source + reload fires"
/// guarantee. JwtTokenIssuer/OtpNetTotpService now depend on IOptionsMonitor for exactly this.
/// </summary>
public sealed class InstanceSecretsReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "teas-secrets-" + Guid.NewGuid().ToString("N"));

    public InstanceSecretsReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    [Fact]
    public void Jwt_AccessTokenMinutes_reloads_live_from_secrets_file()
    {
        var path = Path.Combine(_dir, "appsettings.Secrets.json");
        AtomicWrite(path, """
            { "Jwt": { "Issuer": "i", "Audience": "a",
                       "SigningKey": "0123456789-0123456789-0123456789-XYZ",
                       "AccessTokenMinutes": 30 } }
            """);

        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt"));
        services.AddSingleton<IConfiguration>(config);
        using var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<JwtOptions>>();

        monitor.CurrentValue.AccessTokenMinutes.Should().Be(30);

        // Rewrite the SAME path the config watches → the monitor must reflect the new value.
        AtomicWrite(path, """
            { "Jwt": { "Issuer": "i", "Audience": "a",
                       "SigningKey": "0123456789-0123456789-0123456789-XYZ",
                       "AccessTokenMinutes": 120 } }
            """);

        // File-watch is debounced; poll briefly rather than asserting immediately.
        var reloaded = false;
        for (var i = 0; i < 50 && !reloaded; i++)
        {
            if (monitor.CurrentValue.AccessTokenMinutes == 120) { reloaded = true; break; }
            Thread.Sleep(100);
        }

        reloaded.Should().BeTrue("IOptionsMonitor must reflect the rewritten secrets file (live reload)");
    }
}
