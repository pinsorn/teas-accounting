using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Bootstrap;

/// <summary>
/// First-run-bootstrap spec (2026-06-17). Verifies the two halves of the fresh-clone story END TO END
/// against a PRISTINE ephemeral database (created per test, dropped in finally) so the assertions are
/// not contaminated by the shared teas_test fixture (which seeds demo on purpose):
///
///   1. Database:SeedDemoData=false yields a SYSTEM-only database — RBAC roles/permissions exist, but
///      there is NO seeded 'admin'/'setup-admin' user and NO demo companies (co2/co3).
///   2. POST /system/setup/bootstrap-admin SUCCEEDS on that empty system (creates the first super-admin),
///      and the SECOND call REFUSES with 409 because a user now exists — the zero-users gate that makes
///      the anonymous endpoint safe on a live system.
///
/// These run on their own DB (not the shared one), so they are inherently collision-free across the 2× gate.
/// </summary>
public sealed class FirstRunBootstrapTests
{
    private static string? BaseConn => Environment.GetEnvironmentVariable("TEAS_TEST_PG");

    private static (string ephemeralConn, string dbName, string maintenanceConn) DeriveEphemeral()
    {
        var b = new NpgsqlConnectionStringBuilder(BaseConn) { IncludeErrorDetail = true };
        var dbName = $"teas_boot_{Guid.NewGuid():N}".Substring(0, 30);
        var maintenance = new NpgsqlConnectionStringBuilder(b.ConnectionString) { Database = "postgres" };
        b.Database = dbName;
        return (b.ConnectionString, dbName, maintenance.ConnectionString);
    }

    private static ServiceProvider BuildProvider(string conn, bool seedDemo)
    {
        var services = new ServiceCollection();
        services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:SeedDemoData"] = seedDemo ? "true" : "false",
            }).Build());
        services.AddDbContext<AccountingDbContext>(opt =>
            opt.UseNpgsql(conn).UseSnakeCaseNamingConvention());
        return services.BuildServiceProvider();
    }

    private static async Task CreateDbAsync(string maintenanceConn, string dbName)
    {
        await using var c = new NpgsqlConnection(maintenanceConn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDbAsync(string maintenanceConn, string dbName)
    {
        await using var c = new NpgsqlConnection(maintenanceConn);
        await c.OpenAsync();
        // Terminate any lingering sessions, then drop. Best-effort.
        await using (var term = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @d AND pid <> pg_backend_pid()", c))
        {
            term.Parameters.AddWithValue("d", dbName);
            await term.ExecuteNonQueryAsync();
        }
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\"", c);
        await drop.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task SeedDemoData_false_seeds_no_admin_and_no_demo_companies()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseConn), "TEAS_TEST_PG not set.");
        var (conn, dbName, maint) = DeriveEphemeral();
        await CreateDbAsync(maint, dbName);
        try
        {
            // Bootstrap a SYSTEM-only DB (no demo).
            await using (var sp = BuildProvider(conn, seedDemo: false))
            {
                await DbInitializer.InitializeAsync(sp);

                await using var scope = sp.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

                // No seeded users at all (admin/approver/e2e/setup-admin are all demo).
                var userCount = await db.Users.IgnoreQueryFilters().CountAsync();
                userCount.Should().Be(0, "a fresh SeedDemoData=false install seeds NO placeholder users");

                // No demo companies — co2 (id 2) and co3 (id 3) come from demo seeds only.
                var companyCount = await db.Database
                    .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM master.companies")
                    .SingleAsync();
                companyCount.Should().Be(0, "no demo companies on a fresh install — the first company is created at onboarding");

                // SYSTEM data IS present — roles exist so onboarding's seed_company_roles fan-out works.
                var roleCount = await db.Database
                    .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM sys.roles")
                    .SingleAsync();
                roleCount.Should().BeGreaterThan(0, "RBAC roles are SYSTEM data and must always seed");
            }
        }
        finally
        {
            await DropDbAsync(maint, dbName);
        }
    }

    [SkippableFact]
    public async Task BootstrapAdmin_succeeds_on_empty_then_409_once_a_user_exists()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseConn), "TEAS_TEST_PG not set.");
        var (conn, dbName, maint) = DeriveEphemeral();
        await CreateDbAsync(maint, dbName);
        try
        {
            // System-only bootstrap (no demo, so zero users → first-run open).
            await using (var sp = BuildProvider(conn, seedDemo: false))
            {
                await DbInitializer.InitializeAsync(sp);
            }

            await using var factory = new RbacApiFactory(conn);
            using var client = factory.CreateClient();

            // First call: succeeds, creates the first super-admin.
            using (var resp1 = await client.PostAsJsonAsync("/system/setup/bootstrap-admin",
                new { username = "owner", password = "Sup3rSecret!2026", email = (string?)null, fullName = "Owner" }))
            {
                resp1.StatusCode.Should().Be(HttpStatusCode.OK, "the first-run bootstrap must succeed on an empty system");
            }

            // Second call: a user now exists → the gate refuses with 409.
            using (var resp2 = await client.PostAsJsonAsync("/system/setup/bootstrap-admin",
                new { username = "intruder", password = "An0therSecret!99", email = (string?)null, fullName = "Nope" }))
            {
                resp2.StatusCode.Should().Be(HttpStatusCode.Conflict,
                    "once any user exists the anonymous bootstrap must refuse — it can never mint an admin on a live system");
            }

            // Sanity: exactly one user, and it is a super-admin.
            await using var sp2 = BuildProvider(conn, seedDemo: false);
            await using var scope = sp2.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var users = await db.Users.IgnoreQueryFilters().ToListAsync();
            users.Should().ContainSingle();
            users[0].IsSuperAdmin.Should().BeTrue();
            users[0].Username.Should().Be("owner");
        }
        finally
        {
            await DropDbAsync(maint, dbName);
        }
    }
}
