using Accounting.Application.Abstractions;
using Accounting.Infrastructure;
using Accounting.Infrastructure.ETax;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.ETax;

/// <summary>
/// HIGH-03 fail-closed gate (2026-09-04): <c>RdApi:Provider</c> other than
/// 'Mock' must fail options validation rather than silently resolving the
/// Tier 2/3 HTTP skeleton (no response parsing). Pure DI-registration test —
/// no DB required (a dummy connection string satisfies AddInfrastructure's
/// eager `ConnectionStrings:Postgres` check; nothing here opens a connection).
/// </summary>
public sealed class RdApiProviderGateTests
{
    private const string DummyPg = "Host=localhost;Port=5432;Database=dummy;Username=x;Password=x";

    private static ServiceProvider Build(string? provider)
    {
        var dict = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = DummyPg };
        if (provider is not null) dict["RdApi:Provider"] = provider;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new ServiceCollection().AddInfrastructure(cfg).BuildServiceProvider();
    }

    [Fact]
    public void Missing_provider_key_resolves_mock_client()
    {
        using var sp = Build(null);
        sp.GetRequiredService<IOptions<RdApiOptions>>().Value.Provider.Should().Be("Mock");
        sp.GetRequiredService<IRdEfilingClient>().Should().BeOfType<MockRdEfilingClient>();
    }

    [Theory]
    [InlineData("mock")]
    [InlineData("")]      // I1 — empty (as distinct from missing key) also gates to Mock
    [InlineData(" Mock ")] // whitespace-padded, case-insensitive
    public void Mock_variants_resolve_mock_client(string provider)
    {
        using var sp = Build(provider);
        sp.GetRequiredService<IOptions<RdApiOptions>>().Value.Should().NotBeNull();
        sp.GetRequiredService<IRdEfilingClient>().Should().BeOfType<MockRdEfilingClient>();
    }

    [Fact]
    public void Unsupported_provider_fails_options_validation()
    {
        using var sp = Build("RdUat");
        var act = () => _ = sp.GetRequiredService<IOptions<RdApiOptions>>().Value;
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RdUat*not supported*");

        // ValidateOnStart wires an IStartupValidator that a real host invokes at startup
        // (Program.cs never resolves IOptions<RdApiOptions> eagerly on its own) — assert
        // that path throws too, not just a manual IOptions<>.Value pull.
        var startupValidator = sp.GetService<IStartupValidator>();
        if (startupValidator is not null)
            FluentActions.Invoking(startupValidator.Validate).Should().Throw<OptionsValidationException>();
    }
}
