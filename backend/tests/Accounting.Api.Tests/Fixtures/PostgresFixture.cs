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
        await db.Database.MigrateAsync();

        // RLS + triggers + append-only + seed, in lexical order.
        var scriptsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts");
        foreach (var path in Directory.GetFiles(scriptsDir, "*.sql").OrderBy(p => p, StringComparer.Ordinal))
        {
            var sql = await File.ReadAllTextAsync(path);
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    public ServiceProvider BuildServiceProvider(ITenantContext? tenant = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AccountingDbContext>(opt =>
            opt.UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention());
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
    public bool IsSuperAdmin { get; init; }
    public long? ApiKeyId { get; init; }
    public int? ApiKeyDefaultBusinessUnitId { get; init; }
    public bool IsAuthenticated => UserId is not null || ApiKeyId is not null;
}
