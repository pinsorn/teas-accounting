using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// HTTP tests for the first-run setup endpoint POST /system/setup/instance-keys. Covers the
/// security surface the feature exists for: super-admin-only authz, key/lifetime validation, and
/// the first-run-only 409 on an already-set MFA key. Success/409 cases point the secrets file at a
/// per-test temp path (InstanceSecrets:Path) so they NEVER write into the source tree and stay
/// repeatable across the 2× gate; the 403/400 cases return before any write.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InstanceSetupEndpointTests
{
    private readonly PostgresFixture _fx;
    public InstanceSetupEndpointTests(PostgresFixture fx) => _fx = fx;

    private const string Route = "/system/setup/instance-keys";

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static string Token(bool isSuper, int companyId = 0) =>
        Issuer().Issue(new TokenClaims(
            UserId: 1, Username: "admin", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: isSuper, Roles: [], Permissions: [])).Token;

    private static string ValidKey() => Convert.ToBase64String(new byte[32]);

    private static HttpRequestMessage Post(string token, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [SkippableFact]
    public async Task Non_super_admin_is_forbidden()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Post(Token(isSuper: false, companyId: 1),
            new { mfaAesKeyBase64 = ValidKey(), jwtAccessTokenMinutes = 60 });
        using var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a normal tenant user must never overwrite the instance MFA key");
    }

    [SkippableFact]
    public async Task Super_admin_with_bad_base64_key_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Post(Token(isSuper: true),
            new { mfaAesKeyBase64 = "not-base64!!!", jwtAccessTokenMinutes = 60 });
        using var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Super_admin_with_wrong_length_key_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // 16 bytes (AES-128) — must be exactly 32 (AES-256).
        using var req = Post(Token(isSuper: true),
            new { mfaAesKeyBase64 = Convert.ToBase64String(new byte[16]), jwtAccessTokenMinutes = 60 });
        using var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Super_admin_with_out_of_range_minutes_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Post(Token(isSuper: true),
            new { mfaAesKeyBase64 = ValidKey(), jwtAccessTokenMinutes = 4 });
        using var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Super_admin_first_run_writes_secrets_then_second_attempt_conflicts()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Per-test temp secrets file → no source-tree pollution; fresh each run (2× safe).
        var tempPath = Path.Combine(
            Path.GetTempPath(), $"teas-setup-{Guid.NewGuid():N}.json");
        try
        {
            await using var factory = new RbacApiFactory(_fx.ConnectionString)
                .WithWebHostBuilder(b => b.UseSetting("InstanceSecrets:Path", tempPath));
            using var client = factory.CreateClient();

            // First run — accepted, file written.
            using (var resp1 = await client.SendAsync(Post(Token(isSuper: true),
                new { mfaAesKeyBase64 = ValidKey(), jwtAccessTokenMinutes = 90 })))
            {
                resp1.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            File.Exists(tempPath).Should().BeTrue();
            var written = await File.ReadAllTextAsync(tempPath);
            written.Should().Contain("MfaAesKeyBase64").And.Contain("AccessTokenMinutes");

            // Second run — MFA key already set; rotation is destructive → 409.
            using var resp2 = await client.SendAsync(Post(Token(isSuper: true),
                new { mfaAesKeyBase64 = ValidKey(), jwtAccessTokenMinutes = 120 }));
            resp2.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "the instance MFA key is first-run only — overwriting would orphan enrolled users");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort */ }
        }
    }
}
