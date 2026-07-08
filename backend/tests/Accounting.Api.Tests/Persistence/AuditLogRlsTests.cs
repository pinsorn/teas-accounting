using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// M12 (review 2026-07-04) — RLS backstop for <c>audit.activity_log</c> (585_audit_log_rls.sql),
/// deferred until AFTER H2/M3 landed. <c>company_id</c> is NULLABLE (system-wide rows), so the
/// policy is <c>company_id = pinned OR company_id IS NULL OR bypass</c> — this is ALSO the
/// INSERT WITH-CHECK (no separate WITH CHECK given). <c>SET ROLE pg_database_owner</c>
/// (rolbypassrls=false — the repo's non-bypass-role trick, see <see cref="SalesChainRlsTests"/>)
/// exercises the real FORCE RLS policy instead of the suite's bypass-role connection.
/// Migrated 2026-07-08 (superadmin-tenant-scope.md): the bypass arm is now the LOCAL-only
/// <c>app.bypass_rls</c> GUC (600_superadmin_scoped_rls.sql, G3) — <c>app.is_super_admin</c> is
/// retired and no longer referenced by this policy.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AuditLogRlsTests
{
    private readonly PostgresFixture _fx;
    public AuditLogRlsTests(PostgresFixture fx) => _fx = fx;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Company_A_row_visible_to_A_invisible_to_B_and_legitimate_writes_succeed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = await OpenAsync();
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA audit TO pg_database_owner; " +
            "GRANT SELECT, INSERT ON audit.activity_log TO pg_database_owner;");

        // Seed one row per company (bypass connection — same as sibling RLS tests).
        var aRowId = await InsertRowAsync(c, a.CompanyId, "seed_a");
        var bRowId = await InsertRowAsync(c, b.CompanyId, "seed_b");

        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            await ExecAsync(c,
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), " +
                "set_config('app.bypass_rls', 'false', false)");

            // (a) two-directional visibility check.
            (await ScalarAsync(c, $"SELECT count(*) FROM audit.activity_log WHERE activity_id = {aRowId}"))
                .Should().Be(1, "company A must see its own audit row (grant + pin live)");
            (await ScalarAsync(c, $"SELECT count(*) FROM audit.activity_log WHERE activity_id = {bRowId}"))
                .Should().Be(0, "the RLS policy must hide company B's audit row from company A");

            // (b) a pinned tenant-row INSERT succeeds (WITH-CHECK allows company_id == pinned).
            var pinnedInsertId = await InsertRowAsync(c, a.CompanyId, "pinned_tenant_row");
            (await ScalarAsync(c, $"SELECT count(*) FROM audit.activity_log WHERE activity_id = {pinnedInsertId}"))
                .Should().Be(1, "an audit row whose company_id matches the pinned app.company_id must insert cleanly");

            // (b) a NULL-company "system" row INSERT ALSO succeeds while a tenant is pinned —
            // proves the nullable-company arm doesn't block legitimate system-wide audit writes.
            var systemInsertId = await InsertRowAsync(c, companyId: null, "system_row");
            (await ScalarAsync(c, $"SELECT count(*) FROM audit.activity_log WHERE activity_id = {systemInsertId} AND company_id IS NULL"))
                .Should().Be(1, "a NULL-company system audit row must insert cleanly even while a tenant is pinned");

            // Bonus negative-space check (two-directional discipline, mirrors SalesChainRlsTests):
            // a MISMATCHED tenant row (company B's id while pinned to A) must be BLOCKED by the
            // same WITH-CHECK — proves the policy isn't simply wide open.
            var mismatched = () => InsertRowAsync(c, b.CompanyId, "mismatched_row");
            (await mismatched.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("42501",
                    "an audit row whose company_id does NOT match the pinned app.company_id must be rejected by RLS WITH CHECK");
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }
    }

    private static async Task<long> InsertRowAsync(NpgsqlConnection c, int? companyId, string activityType)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO audit.activity_log (company_id, activity_at, activity_type)
            VALUES (@companyId, now(), @activityType)
            RETURNING activity_id", c);
        cmd.Parameters.AddWithValue("companyId", (object?)companyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activityType", activityType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
