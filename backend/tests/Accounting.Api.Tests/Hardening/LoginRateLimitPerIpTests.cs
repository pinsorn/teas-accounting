using System.Net;
using System.Net.Http.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// M4 + M5 (review 2026-07-04) — <c>/auth/login</c>'s rate limiter used to be a single GLOBAL
/// fixed-window bucket (<c>AddFixedWindowLimiter("login", ...)</c> with no partition-key
/// function), so one client's burst locked out every legitimate user. Fixed by partitioning
/// per client IP (mirroring the <c>/api/v1</c> policy) PLUS <c>UseForwardedHeaders</c> so the
/// real caller IP (recovered from <c>X-Forwarded-For</c>) is what gets partitioned, not the
/// Cloudflare→Next passthrough address. Real HTTP pipeline via <see cref="RbacApiFactory"/> —
/// exercises the actual <c>AddRateLimiter</c> + <c>UseForwardedHeaders</c> wiring in Program.cs,
/// not a reflected approximation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class LoginRateLimitPerIpTests
{
    private readonly PostgresFixture _fx;
    public LoginRateLimitPerIpTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Login_rate_limit_partitions_per_client_ip_not_globally()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Distinct TEST-NET-3 (RFC 5737) addresses — never a real client, safe to hardcode.
        const string ipA = "203.0.113.10";
        const string ipB = "203.0.113.21";

        // Exhaust IP-A's 10-request/min window. None of these 10 should be 429 yet.
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 10; i++)
        {
            last = await LoginAsync(client, ipA);
            last.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"request {i + 1}/10 from IP-A is still within its own 10/min window");
        }

        // The 11th request from the SAME ip must now be rate-limited.
        var eleventh = await LoginAsync(client, ipA);
        eleventh.Should().Be(HttpStatusCode.TooManyRequests,
            "the 11th request from IP-A in the same window must hit the per-IP limit (M4)");

        // A DIFFERENT client IP must have its OWN bucket — proving per-IP partitioning, not the
        // pre-fix single GLOBAL bucket (which would also 429 here, locking out every other user).
        var firstB = await LoginAsync(client, ipB);
        firstB.Should().NotBe(HttpStatusCode.TooManyRequests,
            "a different client IP must not be blocked by IP-A's exhausted bucket (M4+M5)");
    }

    private static async Task<HttpStatusCode> LoginAsync(HttpClient client, string clientIp)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { username = "rate-limit-probe", password = "wrong-password" }),
        };
        // Simulates the Cloudflare→Next hop's X-Forwarded-For; UseForwardedHeaders (M5) must
        // translate this into Connection.RemoteIpAddress for the "login" policy (M4) to partition on.
        req.Headers.Add("X-Forwarded-For", clientIp);
        using var resp = await client.SendAsync(req);
        return resp.StatusCode;
    }
}
