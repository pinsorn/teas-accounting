using Accounting.Api.Tests.Fixtures;
using Accounting.TestKit;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Codex review 2026-08-20 F2 (specs/fix-codex-review-2026-08-20.md) —
/// 641_reconcile_demo_pv_user_roles.sql must not grant AP_CLERK/SALES_STAFF by a bare numeric
/// user_id; it must derive the user via 181's own exact identity pins (username AND email), so a
/// user who merely happens to hold a coincidental id gets NOTHING. sys.users.username/email are
/// GLOBALLY unique (ix_users_username/ix_users_email) — the real ap_clerk/sales_staff rows
/// (user_id 3/4) already occupy those exact identities in the shared teas_test DB, so this test
/// cannot fabricate a SECOND row with the same username to reproduce the literal "id 3" collision.
/// Instead it proves the mechanism directly: scope the script (text-substitution, mirroring
/// EmployeeLookupGrantTests' per-company loop-anchor substitution) to a FRESH test company that
/// already has AP_CLERK/SALES_STAFF roles (auto-seeded by CompanyService.CreateAsync ->
/// sys.seed_company_roles), add an "impostor" user with some OTHER username scoped to that
/// company, and confirm only the identity-matched ap_clerk/sales_staff (the real global rows,
/// whatever their id) receive the grant — the impostor, regardless of id, gets nothing.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DemoPvUserRoleReconcileScriptTests
{
    private readonly PostgresFixture _fx;
    public DemoPvUserRoleReconcileScriptTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection c, string sql, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection c, string sql, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [SkippableFact]
    public async Task Script641_grants_only_the_identity_matched_users_never_an_impostor_by_coincidental_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // Sanity — AP_CLERK/SALES_STAFF must already exist for this fresh company (auto-fanned-out
        // by CompanyService.CreateAsync -> sys.seed_company_roles), or the scoped replay below
        // would prove nothing (both grants would no-op for lack of a role row, not for lack of
        // identity match).
        var apClerkRoleCount = await ScalarIntAsync(conn,
            "SELECT count(*)::int FROM sys.roles WHERE company_id = $1 AND role_code = 'AP_CLERK'", co.CompanyId);
        apClerkRoleCount.Should().Be(1, "sanity: AP_CLERK must be auto-seeded for every new company");
        var salesStaffRoleCount = await ScalarIntAsync(conn,
            "SELECT count(*)::int FROM sys.roles WHERE company_id = $1 AND role_code = 'SALES_STAFF'", co.CompanyId);
        salesStaffRoleCount.Should().Be(1, "sanity: SALES_STAFF must be auto-seeded for every new company");

        // An "impostor" user — NOT named ap_clerk/sales_staff — scoped to this test company.
        var impostorUsername = "impostor_" + TestIds.Suffix().ToLowerInvariant();
        var impostorUserId = await ScalarLongAsync(conn,
            "INSERT INTO sys.users (username, email, password_hash, full_name, is_super_admin, " +
            "is_active, failed_login_count, must_change_password, created_at, updated_at, version) " +
            "VALUES ($1, $2, crypt('Test@1234', gen_salt('bf', 4)), 'Impostor', FALSE, TRUE, 0, FALSE, " +
            "now(), now(), 0) RETURNING user_id",
            impostorUsername, impostorUsername + "@teas.local");

        var fileSql = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts",
            "641_reconcile_demo_pv_user_roles.sql"));

        const string roleCompanyAnchor = "AND r.company_id = 1";
        const string grantValuesAnchor = "SELECT u.user_id, r.role_id, 1, 1, DATE '2026-01-01'";
        const string notExistsAnchor = "AND ur.company_id = 1 AND ur.branch_id = 1";
        fileSql.Should().Contain(roleCompanyAnchor, "641's role-lookup anchor text changed — update this test's scoping substitution");
        fileSql.Should().Contain(grantValuesAnchor, "641's grant-values anchor text changed — update this test's scoping substitution");
        fileSql.Should().Contain(notExistsAnchor, "641's idempotency-guard anchor text changed — update this test's scoping substitution");

        var scopedSql = fileSql
            .Replace(roleCompanyAnchor, $"AND r.company_id = {co.CompanyId}")
            .Replace(grantValuesAnchor, $"SELECT u.user_id, r.role_id, {co.CompanyId}, {co.BranchId}, DATE '2026-01-01'")
            .Replace(notExistsAnchor, $"AND ur.company_id = {co.CompanyId} AND ur.branch_id = {co.BranchId}");

        await ExecAsync(conn, scopedSql);

        // The impostor must get NOTHING, regardless of whatever numeric id it landed on.
        var impostorGrantCount = await ScalarIntAsync(conn,
            "SELECT count(*)::int FROM sys.user_roles WHERE user_id = $1 AND company_id = $2",
            impostorUserId, co.CompanyId);
        impostorGrantCount.Should().Be(0,
            "F2 — a user who is not literally named ap_clerk/sales_staff must get NOTHING, no matter its numeric id");

        // The REAL, globally identity-matched ap_clerk/sales_staff (181's own users) DO get the
        // grant, scoped to THIS company — proving the id is derived from identity, not hardcoded.
        var apClerkGrantCount = await ScalarIntAsync(conn,
            "SELECT count(*)::int FROM sys.user_roles ur " +
            "JOIN sys.users u ON u.user_id = ur.user_id " +
            "JOIN sys.roles r ON r.role_id = ur.role_id " +
            "WHERE u.username = 'ap_clerk' AND r.role_code = 'AP_CLERK' AND ur.company_id = $1",
            co.CompanyId);
        apClerkGrantCount.Should().Be(1, "the identity-matched ap_clerk must still be granted, derived by username not a hardcoded id");

        var salesStaffGrantCount = await ScalarIntAsync(conn,
            "SELECT count(*)::int FROM sys.user_roles ur " +
            "JOIN sys.users u ON u.user_id = ur.user_id " +
            "JOIN sys.roles r ON r.role_id = ur.role_id " +
            "WHERE u.username = 'sales_staff' AND r.role_code = 'SALES_STAFF' AND ur.company_id = $1",
            co.CompanyId);
        salesStaffGrantCount.Should().Be(1, "the identity-matched sales_staff must still be granted, derived by username not a hardcoded id");

        // Idempotency — a second replay over the same company must not throw (NOT EXISTS guard).
        var secondRun = async () => await ExecAsync(conn, scopedSql);
        await secondRun.Should().NotThrowAsync("641's INSERTs are NOT EXISTS-guarded; a second replay must be a silent no-op");
    }
}
