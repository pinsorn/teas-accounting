using System;
using Microsoft.Extensions.Options;

namespace Accounting.Api.Tests.Fixtures;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns a fixed value. Used by unit tests that
/// construct services (e.g. JwtTokenIssuer) directly — those services moved from IOptions to
/// IOptionsMonitor so the first-run setup endpoint's reloadOnChange secrets take effect live.
/// </summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
