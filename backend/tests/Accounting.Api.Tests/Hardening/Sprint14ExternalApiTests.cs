using System.Security.Claims;
using Accounting.Api.Authorization;
using Accounting.Api.Tenancy;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 14 — ApiKey generator/resolver/service + idempotency store. Full
/// HTTP behaviours (auth scheme, scope 403, idempotency replay/409, BU-lock
/// 409, auth isolation v1↔root, error envelope) are covered end-to-end by the
/// external-api-microservice e2e.
/// </summary>
public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Mint_then_bcrypt_verify_roundtrips_and_prefix_is_stable()
    {
        var m = ApiKeyGenerator.New();
        m.Plaintext.Should().StartWith("key_").And.HaveLength(44);
        m.KeyPrefix.Should().Be(m.Plaintext[..16]);
        BCrypt.Net.BCrypt.Verify(m.Plaintext, m.KeyHash).Should().BeTrue();
        ApiKeyGenerator.PrefixOf(m.Plaintext).Should().Be(m.KeyPrefix);
        ApiKeyGenerator.PrefixOf("garbage").Should().BeNull();
    }

    // M1 (§4.8) — key-sourced writes previously logged a null actor (UserId null,
    // Username null). The auth handler now emits ClaimTypes.Name = the key name,
    // so HttpTenantContext.Username (the ActivityRecorder actor) is the key name,
    // never null. Target the real mapping (HttpTenantContext over an ApiKey
    // principal), NOT the StubTenant used by the DB tests.
    [Fact]
    public void HttpTenantContext_uses_api_key_name_as_audit_actor()
    {
        const string keyName = "Reptify-mcp-agent";
        var claims = new List<Claim>
        {
            new(TenantClaims.CompanyId, "7"),
            new(TenantClaims.ApiKeyId,  "42"),
            new(TenantClaims.IsApiKey,  "true"),
            new(ClaimTypes.Name,        keyName),
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, ApiKeyAuthenticationHandler.SchemeName));
        var http = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = http };

        var tenant = new HttpTenantContext(accessor);

        tenant.IsAuthenticated.Should().BeTrue();
        tenant.ApiKeyId.Should().Be(42);
        tenant.UserId.Should().BeNull();           // no human user
        tenant.Username.Should().Be(keyName);      // actor = the key name, not null
    }
}

[Collection(nameof(PostgresCollection))]
public sealed class Sprint14ExternalApiTests
{
    private readonly PostgresFixture _fx;
    public Sprint14ExternalApiTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(long userId = 1)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Create_then_resolve_roundtrips_scopes_and_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        string plaintext;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            var r = await svc.CreateAsync(new CreateApiKeyRequest(
                "Reptify", ["sales.tax_invoice.create", "sales.tax_invoice.read"]), default);
            plaintext = r.Plaintext;
            r.KeyPrefix.Should().HaveLength(16);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var res = await s.ServiceProvider.GetRequiredService<IApiKeyResolver>()
                .AuthenticateAsync(plaintext, default);
            res.Key.Should().NotBeNull();
            res.Key!.CompanyId.Should().Be(1);
            res.Key.ScopesCsv.Should().Contain("sales.tax_invoice.create");
        }
    }

    [SkippableFact]
    public async Task Resolver_carries_head_office_branch_for_numbering()
    {
        // M13 — the ApiKey principal had no branch claim → tenant.BranchId = 0 →
        // JE numbering allocated from a fresh (branch-0) sequence whose values
        // collide with the head office's on ix_journal_entries_company_id_doc_no.
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        string plaintext;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            plaintext = (await svc.CreateAsync(
                new CreateApiKeyRequest("hq-branch", ["x"]), default)).Plaintext;
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var res = await s.ServiceProvider.GetRequiredService<IApiKeyResolver>()
                .AuthenticateAsync(plaintext, default);
            res.Key!.HeadOfficeBranchId.Should().Be(1);   // company 1's seeded head office
        }
    }

    [SkippableFact]
    public async Task Revoked_key_fails_with_revoked_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        string plaintext; long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            var r = await svc.CreateAsync(new CreateApiKeyRequest("k", ["x"]), default);
            plaintext = r.Plaintext; id = r.ApiKeyId;
            await svc.RevokeAsync(id, default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var res = await s.ServiceProvider.GetRequiredService<IApiKeyResolver>()
                .AuthenticateAsync(plaintext, default);
            res.Key.Should().BeNull();
            res.FailCode.Should().Be("auth.revoked_api_key");
        }
    }

    [SkippableFact]
    public async Task Rotate_invalidates_the_old_secret()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        string oldKey, newKey;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            var c = await svc.CreateAsync(new CreateApiKeyRequest("k", ["x"]), default);
            oldKey = c.Plaintext;
            newKey = (await svc.RotateAsync(c.ApiKeyId, default)).Plaintext;
        }
        oldKey.Should().NotBe(newKey);
        await using (var s = sp.CreateAsyncScope())
        {
            var resolver = s.ServiceProvider.GetRequiredService<IApiKeyResolver>();
            (await resolver.AuthenticateAsync(oldKey, default)).Key.Should().BeNull();
            (await resolver.AuthenticateAsync(newKey, default)).Key.Should().NotBeNull();
        }
    }

    [SkippableFact]
    public async Task Create_writes_a_secret_free_audit_row()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        var r = await svc.CreateAsync(new CreateApiKeyRequest("Audited", ["x"]), default);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var log = db.ActivityLogs.OrderByDescending(a => a.ActivityId)
            .First(a => a.EntityType == "ApiKey" && a.EntityId == r.ApiKeyId);
        log.ActivityType.Should().Be("api_key.create");
        (log.AfterValueJson ?? "").Should().NotContain(r.Plaintext);
    }

    [SkippableFact]
    public async Task Idempotency_store_get_save_race_and_purge()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var store = s.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var now = DateTimeOffset.UtcNow;
        var k = "shopify-order-" + Guid.NewGuid().ToString("N")[..8];

        (await store.GetAsync(1, 99, k, default)).Should().BeNull();
        (await store.TrySaveAsync(1, 99, k, "h1", 201, "{\"id\":1}", now, default)).Should().BeTrue();
        // Same key again → unique violation → race lost.
        (await store.TrySaveAsync(1, 99, k, "h1", 201, "{\"id\":1}", now, default)).Should().BeFalse();
        var got = await store.GetAsync(1, 99, k, default);
        got!.RequestHash.Should().Be("h1");
        got.ResponseStatus.Should().Be(201);

        // Expired row is purged + not returned.
        var expKey = "exp-" + Guid.NewGuid().ToString("N")[..8];
        await store.TrySaveAsync(1, 99, expKey, "h", 200, "{}", now.AddHours(-48), default);
        (await store.GetAsync(1, 99, expKey, default)).Should().BeNull();   // expires_at in the past
        (await store.PurgeExpiredAsync(now, default)).Should().BeGreaterThanOrEqualTo(1);
    }

    [SkippableFact]
    public async Task Create_with_inactive_bu_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        var act = () => svc.CreateAsync(
            new CreateApiKeyRequest("bad-bu", ["x"], DefaultBusinessUnitId: 999_999), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("api_key.invalid_business_unit");
    }

    // ── M1 (MCP) — api_keys.kind ─────────────────────────────────────────────

    [SkippableFact]
    public async Task Kind_defaults_to_integration_when_unspecified()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            // Existing positional ctor (no Kind) → must default to integration.
            id = (await svc.CreateAsync(
                new CreateApiKeyRequest(TestIds.Name("key"), ["sales.tax_invoice.read"]), default)).ApiKeyId;
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var list = await s.ServiceProvider.GetRequiredService<IApiKeyService>().ListAsync(default);
            list.First(k => k.ApiKeyId == id).Kind.Should().Be(ApiKeyKinds.Integration);
        }
    }

    [SkippableFact]
    public async Task Mcp_kind_persists_and_lists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
            id = (await svc.CreateAsync(new CreateApiKeyRequest(
                TestIds.Name("mcp"),
                ["sales.tax_invoice.read", "sales.tax_invoice.create"],
                Kind: ApiKeyKinds.Mcp), default)).ApiKeyId;
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var list = await s.ServiceProvider.GetRequiredService<IApiKeyService>().ListAsync(default);
            list.First(k => k.ApiKeyId == id).Kind.Should().Be(ApiKeyKinds.Mcp);
        }
    }

    [SkippableFact]
    public async Task Mcp_key_with_read_and_create_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        var act = () => svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-ok"),
            ["sales.tax_invoice.read", "sales.tax_invoice.create", "sales.receipt.create"],
            Kind: ApiKeyKinds.Mcp), default);
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task Mcp_key_with_post_scope_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        var act = () => svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-bad"),
            ["sales.tax_invoice.create", "sales.tax_invoice.post"],
            Kind: ApiKeyKinds.Mcp), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("api_key.mcp_cannot_post");
    }

    // BE-2b (2026-06-19) — the mcp no-post guard now rejects ALL state-advancing suffixes,
    // not only .post (.approve/.issue/.send/.void/.cancel/.reject). An mcp key may hold only
    // .read/.create/.manage. (No catalog mcp scope ends in these suffixes today.)
    [SkippableTheory]
    [InlineData("sales.tax_invoice.approve")]
    [InlineData("sales.tax_invoice.issue")]
    [InlineData("sales.quotation.send")]
    [InlineData("sales.tax_invoice.void")]
    [InlineData("purchase.purchase_order.cancel")]
    [InlineData("sales.quotation.reject")]
    public async Task Mcp_key_with_state_advancing_scope_is_rejected(string offendingScope)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        var act = () => svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-adv"),
            ["sales.tax_invoice.create", offendingScope],
            Kind: ApiKeyKinds.Mcp), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("api_key.mcp_cannot_post");
    }

    [SkippableFact]
    public async Task Integration_key_with_post_scope_still_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApiKeyService>();
        // Integration keys (M2M) keep full scopes incl post — guard must not fire.
        var act = () => svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("integration"),
            ["sales.tax_invoice.create", "sales.tax_invoice.post"],
            Kind: ApiKeyKinds.Integration), default);
        await act.Should().NotThrowAsync();
    }
}
