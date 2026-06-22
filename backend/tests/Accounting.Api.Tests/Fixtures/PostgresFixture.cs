using System.IO;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Accounting.Api.Tests.Fixtures;

/// <summary>
/// Provides a Postgres-backed schema for integration tests. Resolution order:
/// 1. <c>TEAS_TEST_PG</c> env var — a ready connection string to ANY Postgres
///    (native install, shared dev box, remote). No Docker required.
/// 2. Testcontainers — spins up postgres:16-alpine (requires Docker).
/// 3. Neither available → <see cref="SkipReason"/> is set; integration tests
///    that use [SkippableFact] report Skipped rather than failing the suite.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _connectionString;

    public string? SkipReason { get; private set; }
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException(SkipReason ?? "Postgres not initialised.");

    /// <summary>A NON-superuser, NOBYPASSRLS role provisioned for tests that must exercise REAL
    /// Postgres RLS (the test connection is a superuser that bypasses it — masking a whole class
    /// of prod-only bugs, e.g. the empty-token-at-login bug). Tests `SET ROLE` to this on their
    /// own connection. See <see cref="RlsRoleSkip"/>.</summary>
    public const string RlsTestRole = "teas_rls_test";
    /// <summary>Non-null when the RLS test role could NOT be provisioned (e.g. the test user lacks
    /// CREATEROLE) — RLS-dependent tests should <c>Skip.If</c> on it.</summary>
    public string? RlsRoleSkip { get; private set; } = "RLS test role not initialised.";

    public async Task InitializeAsync()
    {
        var envConn = Environment.GetEnvironmentVariable("TEAS_TEST_PG");
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            _connectionString = envConn;
        }
        else
        {
            try
            {
                _container = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .WithUsername("test")
                    .WithPassword("test")
                    .WithDatabase("teas_test")
                    .Build();
                await _container.StartAsync();
                _connectionString = _container.GetConnectionString();
            }
            catch (Exception ex)
            {
                SkipReason =
                    "No Postgres available. Set TEAS_TEST_PG to a Postgres connection string " +
                    $"or start Docker for Testcontainers. ({ex.GetType().Name}: {ex.Message})";
                return;
            }
        }

        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS pgcrypto; CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");

        // Bootstrap the EF migrations history so MigrateAsync works on a DB that was
        // previously initialized by DbInitializer (SQL scripts, not EF migrations).
        // Strategy: if the schema already exists (master.companies present) but the EF
        // history table has no InitialCreate entry, mark it as applied so MigrateAsync
        // only runs subsequent migrations (e.g. AddPerfIndexes). Without this, MigrateAsync
        // tries to run InitialCreate which fails because the tables already exist.
        // ponytail: EF history table = sys.__ef_migrations (MigrationsHistoryTable config in DI).
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS sys;
            CREATE TABLE IF NOT EXISTS sys.__ef_migrations (
                migration_id    character varying(150) NOT NULL,
                product_version character varying(32)  NOT NULL,
                CONSTRAINT pk___ef_migrations PRIMARY KEY (migration_id)
            );
            """);
        // If schema was bootstrapped via SQL scripts, mark InitialCreate as applied.
        var schemaExists = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'master' AND table_name = 'companies'")
            .FirstOrDefaultAsync();
        if (schemaExists > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sys.__ef_migrations(migration_id, product_version) " +
                "VALUES ('20260616130322_InitialCreate', '10.0.0') ON CONFLICT DO NOTHING");
        }

        await db.Database.MigrateAsync();

        // RLS + triggers + append-only + seed, in lexical order — applied ONCE per DB, tracked in
        // sys.applied_sql_scripts (mirrors DbInitializer). teas_test persists across `dotnet test`
        // invocations, so replaying scripts every run is both wasteful and, post per-company-RBAC
        // conversion (510), incorrect: 110's ON CONFLICT (role_code) would fail once 510 has
        // swapped the global role_code unique for a per-company one. Apply-once matches prod.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS sys;
            CREATE TABLE IF NOT EXISTS sys.applied_sql_scripts (
                script_name TEXT PRIMARY KEY,
                applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """);
        var applied = (await db.Database
                .SqlQueryRaw<string>("SELECT script_name AS \"Value\" FROM sys.applied_sql_scripts")
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Bootstrap scripts can be heavy (e.g. 510 fans per-company roles out to every company;
        // a long-lived shared teas_test accumulates hundreds of test companies). Allow well past
        // the 30s default so the one-time conversion never trips the command timeout.
        db.Database.SetCommandTimeout(300);

        var scriptsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts");
        foreach (var path in Directory.GetFiles(scriptsDir, "*.sql").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (applied.Contains(name)) continue;
            var sql = await File.ReadAllTextAsync(path);
            await db.Database.ExecuteSqlRawAsync(sql);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sys.applied_sql_scripts(script_name) VALUES ({0})", name);
        }

        // Provision a NON-superuser, NOBYPASSRLS role so RLS-dependent tests can reproduce prod
        // behaviour (the test connection is a superuser that bypasses RLS). Idempotent + best-effort:
        // if the test user can't CREATE ROLE, leave RlsRoleSkip set so those tests Skip instead of
        // failing. No curly braces in the SQL (EF ExecuteSqlRaw treats them as format placeholders).
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                DO $rls$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'teas_rls_test') THEN
                        CREATE ROLE teas_rls_test NOLOGIN NOBYPASSRLS;
                    END IF;
                END $rls$;
                GRANT USAGE ON SCHEMA sys, master, audit TO teas_rls_test;
                GRANT SELECT ON ALL TABLES IN SCHEMA sys, master, audit TO teas_rls_test;
                """);
            RlsRoleSkip = null;
        }
        catch (Exception ex)
        {
            RlsRoleSkip = $"RLS test role '{RlsTestRole}' unavailable ({ex.GetType().Name}: {ex.Message}). " +
                          "Needs a TEAS_TEST_PG user with CREATEROLE/superuser.";
        }
    }

    public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    public ServiceProvider BuildServiceProvider(ITenantContext? tenant = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AccountingDbContext>(opt =>
            opt.UseNpgsql(ConnectionString, npg =>
            {
                // ponytail: must match DependencyInjection.cs so MigrateAsync uses sys.__ef_migrations
                npg.MigrationsHistoryTable("__ef_migrations", "sys");
            })
            .UseSnakeCaseNamingConvention());
        if (tenant is not null) services.AddSingleton(tenant);
        return services.BuildServiceProvider();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>Tiny test double for <see cref="ITenantContext"/>.</summary>
public sealed class StubTenant : ITenantContext
{
    public int CompanyId { get; init; }
    public int BranchId { get; init; }
    public long? UserId { get; init; }
    public string? Username { get; init; }
    public bool IsSuperAdmin { get; init; }
    public long? ApiKeyId { get; init; }
    public string? ApiKeyName { get; init; }
    public int? ApiKeyDefaultBusinessUnitId { get; init; }
    public bool IsAuthenticated => UserId is not null || ApiKeyId is not null;
}
