using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// H5 (review 2026-07-04) — <see cref="Accounting.Infrastructure.Identity.ApiKeyResolver"/>
/// resolves an incoming X-Api-Key BEFORE TenantMiddleware pins <c>app.company_id</c> (it
/// resolves the tenant FROM the key). <c>sys.api_keys</c>/<c>master.branches</c> carry FORCE
/// RLS (010_rls_policies.sql, fail-closed on unset company_id); the suite's <c>accounting</c>
/// login is a SUPERUSER (rolbypassrls=true), so
/// <c>Sprint14ExternalApiTests.Create_then_resolve_roundtrips_scopes_and_company</c> is a
/// FALSE-GREEN — it never exercises the DB policy. This test <c>SET ROLE pg_database_owner</c>
/// (rolbypassrls=false, membership implicit for the DB owner — the same non-bypass-role trick
/// as <see cref="Persistence.SalesChainRlsTests"/>/<see cref="Persistence.ReviewHardeningRlsTests"/>;
/// unlike <c>teas_rls_test</c> it needs no CREATEROLE grant on the test DB user) with
/// <c>app.company_id</c> UNSET — the exact prod pre-auth condition — and exercises the resolver
/// directly (the technique from <see cref="PermissionLookupRlsTests"/>, which tests the sibling
/// <c>PermissionLookup</c> this fix mirrors). Without the fix (pinning the LOCAL-only,
/// never-user-derived <c>app.bypass_rls='true'</c> to each lookup's own transaction) the
/// fail-closed policy hides every row and this returns null/<c>auth.invalid_api_key</c> (red).
/// Migrated 2026-07-08 (superadmin-tenant-scope.md): the resolver's GUC was renamed
/// <c>app.is_super_admin</c> → <c>app.bypass_rls</c> (that GUC is now RETIRED from every
/// <c>company_isolation</c> policy — it no longer has ANY data-scope effect).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ApiKeyResolverRlsTests
{
    private readonly PostgresFixture _fx;
    public ApiKeyResolverRlsTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task AuthenticateAsync_resolves_key_under_NOBYPASSRLS_role_with_company_unset()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        // Mint a real key via the real create path (bypass-role scope — RLS is not under test here).
        string plaintext;
        long expectedApiKeyId;
        await using (var spSeed = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1))
        await using (var sSeed = spSeed.CreateAsyncScope())
        {
            var svc = sSeed.ServiceProvider.GetRequiredService<IApiKeyService>();
            var r = await svc.CreateAsync(
                new CreateApiKeyRequest(TestIds.Name("H5Probe"), ["sales.tax_invoice.read"]), default);
            plaintext = r.Plaintext;
            expectedApiKeyId = r.ApiKeyId;
        }

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Keep ONE physical connection open for the whole block so SET ROLE (session-scoped)
        // actually sticks across the resolver's internal transactions — same technique as
        // PermissionLookupRlsTests.
        await db.Database.OpenConnectionAsync();
        try
        {
            // Grant the non-bypass role read on the two tables the resolver queries (accounting
            // owns them). Idempotent, run while still the bypass role (accounting).
            await db.Database.ExecuteSqlRawAsync(
                "GRANT USAGE ON SCHEMA sys, master TO pg_database_owner; " +
                "GRANT SELECT ON sys.api_keys TO pg_database_owner; " +
                "GRANT SELECT ON master.branches TO pg_database_owner;");

            // Reproduce the exact pre-auth condition: RLS ENFORCED (NOBYPASSRLS role) +
            // app.company_id UNSET. Also explicitly clear app.bypass_rls (SESSION-scoped,
            // is_local=false) — a pooled connection that happened to retain 'true' from a prior
            // test would let even the PRE-FIX code pass here for the wrong reason (false green).
            // Clearing both GUCs up front means only the fix's own LOCAL (per-transaction) pin
            // can satisfy the policy.
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', '', false)");
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'false', false)");
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var result = await scope.ServiceProvider.GetRequiredService<IApiKeyResolver>()
                .AuthenticateAsync(plaintext, default);

            // With the fix, both IgnoreQueryFilters() reads (ApiKeys by prefix, Branches by
            // company) pin app.bypass_rls='true' LOCAL to their own transaction so RLS lets
            // them read despite app.company_id being unset. Without the fix, the fail-closed
            // policy hides every row → Key is null.
            result.Key.Should().NotBeNull("the fix must let the pre-tenant lookup read under RLS");
            result.Key!.ApiKeyId.Should().Be(expectedApiKeyId);
            result.Key.CompanyId.Should().Be(co.CompanyId);
            result.Key.HeadOfficeBranchId.Should().Be(co.BranchId);

            // Security-critical property: the LOCAL pin must not have leaked onto the pooled
            // SESSION setting. Each fix transaction sets app.bypass_rls='true' with
            // is_local=true, which Postgres guarantees reverts at COMMIT — so after both lookups
            // have committed, the SESSION-level value must still read back as the 'false' this
            // test set before calling AuthenticateAsync.
            var stillFalse = (string?)await db.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.bypass_rls', true) AS \"Value\"").SingleAsync();
            (stillFalse is null or "" or "false").Should().BeTrue(
                $"the transaction-LOCAL bypass pin must reset and never leak onto the pooled session (was '{stillFalse}')");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }
}
