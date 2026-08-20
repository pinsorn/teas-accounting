using Accounting.Api.Tests.Fixtures;
using Accounting.TestKit;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Codex UI review 2026-08-20 R1 (specs/fix-codex-review-2026-08-20.md) —
/// 160_seed_approver_user.sql must not make the demo `approver` user a SUPER_ADMIN (defeats the
/// Segregation-of-Duties flow it exists to demonstrate); 642_demote_approver_from_super_admin.sql
/// reconciles an already-applied database where 160 ran under the old content. Mirrors 641's
/// exact identity discipline (username AND email, never a bare id).
///
/// `sys.users.username`/`email` are GLOBALLY unique (ix_users_username/ix_users_email) — the real
/// `approver` (user_id=2) already occupies that identity in the shared teas_test DB, and other
/// tests/tools may assume it stays SUPER_ADMIN today (this fix's OWN real demotion only happens
/// for real when 642 runs through DbInitializer on an actual boot, not as a side effect of this
/// test suite). So the "demote happens" scenario is exercised inside a transaction that is ALWAYS
/// rolled back — mirrors DemoTaxIdRepairScriptTests' company-1 technique.
///
/// 160's own fresh-install ROLE assignment (targeting the GLOBAL, pre-510 APPROVER role row) is
/// NOT literally replayable here: `ck_roles_company_required` (added by 510) now forbids any
/// non-SUPER_ADMIN global role row from existing in this already-510-migrated database (confirmed
/// via psql — only SUPER_ADMIN remains global). 160's IS_SUPER_ADMIN flip is still faithfully
/// replayable (that INSERT doesn't depend on sys.roles at all) — its role-code reference is
/// verified via a text assertion on the file content instead, mirroring F2's own
/// "assert-new-behavior only" fallback for an analogous infeasibility.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ApproverDemotionScriptTests
{
    private readonly PostgresFixture _fx;
    public ApproverDemotionScriptTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql, NpgsqlTransaction? txn = null)
    {
        await using var cmd = new NpgsqlCommand(sql, c, txn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection c, string sql, NpgsqlTransaction? txn, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c, txn);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadScriptAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts", fileName);
        File.Exists(path).Should().BeTrue($"script not found at {path}");
        return await File.ReadAllTextAsync(path);
    }

    // ── 160 (fresh installs) ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Script160_no_longer_targets_super_admin_and_creates_a_non_super_user()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts", "160_seed_approver_user.sql");
        var text = File.ReadAllText(path);

        text.Should().Contain("WHERE r.role_code = 'APPROVER'",
            "160's fresh-install role grant must target APPROVER, not SUPER_ADMIN");
        text.Should().NotContain("WHERE r.role_code = 'SUPER_ADMIN'",
            "160 must no longer bind the demo approver user to the SUPER_ADMIN role");
        // is_super_admin is the 6th value in the VALUES(...) tuple — assert the literal is FALSE
        // (line-ending-agnostic: match on the value list alone, not surrounding whitespace).
        text.Should().Contain("FALSE, TRUE, 0, FALSE,",
            "160 must seed the approver user with is_super_admin = FALSE");
        text.Should().NotContain("TRUE, TRUE, 0, FALSE,",
            "160 must no longer seed the approver user with is_super_admin = TRUE");
    }

    [SkippableFact]
    public async Task Script160_is_super_admin_flip_replays_correctly_in_isolation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // Confirm the real approver identity this script targets — sanity, not mutated outside
        // the rolled-back transaction below.
        var realUsername = await ScalarAsync<string>(conn,
            "SELECT username FROM sys.users WHERE user_id = 2", null);
        realUsername.Should().Be("approver", "sanity — 160's hardcoded user_id=2 must still be the real approver identity");
        // Captured, not hardcoded — 642 may already have run for real (via DbInitializer on a
        // live boot) and correctly demoted this row before this test ever runs, so "the real row's
        // current state" is whatever it actually is right now, not an assumption baked into the test.
        var originalIsSuperAdmin = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", null);

        await using var txn = await conn.BeginTransactionAsync();
        // Simulate a fresh install for the INSERT's own purposes — 160's INSERT is
        // ON CONFLICT (user_id) DO NOTHING, so it would silently no-op against the EXISTING row
        // without ever writing FALSE. Delete it first, entirely inside this rolled-back
        // transaction, so the fix's actual VALUES(...) tuple gets exercised.
        await ExecAsync(conn, "DELETE FROM sys.user_roles WHERE user_id = 2", txn);
        await ExecAsync(conn, "DELETE FROM sys.users WHERE user_id = 2", txn);

        var sql = await ReadScriptAsync("160_seed_approver_user.sql");
        await ExecAsync(conn, sql, txn);

        var isSuperAdmin = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", txn);
        isSuperAdmin.Should().BeFalse("160's re-seeded approver row must not be a super admin");

        await txn.RollbackAsync();

        // The real, live approver row must be completely untouched by this test — restored to
        // whatever its real, current state actually was before this test ran.
        var afterRollback = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", null);
        afterRollback.Should().Be(originalIsSuperAdmin, "the rollback must leave the real approver row untouched");
    }

    // ── 642 (existing/already-applied DBs) ────────────────────────────────────────────────

    [SkippableFact]
    public async Task Script642_demotes_the_real_approver_and_swaps_super_admin_for_the_approver_role()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var originalIsSuperAdmin = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", null);

        await using var txn = await conn.BeginTransactionAsync();

        // Reconstruct the exact "160 ran under the OLD SUPER_ADMIN content" scenario 642 exists
        // to repair — entirely inside this rolled-back transaction, regardless of whatever the
        // real row's CURRENT state already is (642 may already have run for real via a live
        // DbInitializer boot and correctly demoted it, which would make running 642 again here a
        // no-op that proves nothing without first putting the row back into the broken shape).
        await ExecAsync(conn, "UPDATE sys.users SET is_super_admin = TRUE WHERE user_id = 2", txn);
        await ExecAsync(conn,
            "DELETE FROM sys.user_roles WHERE user_id = 2 AND company_id = 1 AND branch_id = 1", txn);
        await ExecAsync(conn,
            "INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from) " +
            "SELECT 2, role_id, 1, 1, DATE '2026-01-01' FROM sys.roles WHERE role_code = 'SUPER_ADMIN'", txn);

        var sql = await ReadScriptAsync("642_demote_approver_from_super_admin.sql");

        // First run — demote happens.
        await ExecAsync(conn, sql, txn);

        var isSuperAdmin = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", txn);
        isSuperAdmin.Should().BeFalse("642 must demote the real approver's is_super_admin flag");

        var hasSuperAdminRole = await ScalarAsync<int>(conn,
            "SELECT count(*)::int FROM sys.user_roles ur JOIN sys.roles r ON r.role_id = ur.role_id " +
            "WHERE ur.user_id = 2 AND r.role_code = 'SUPER_ADMIN' AND ur.company_id = 1", txn);
        hasSuperAdminRole.Should().Be(0, "642 must remove the SUPER_ADMIN role grant 160 originally created");

        var hasApproverRole = await ScalarAsync<int>(conn,
            "SELECT count(*)::int FROM sys.user_roles ur JOIN sys.roles r ON r.role_id = ur.role_id " +
            "WHERE ur.user_id = 2 AND r.role_code = 'APPROVER' AND ur.company_id = 1", txn);
        hasApproverRole.Should().Be(1, "642 must grant the APPROVER role in its place");

        // Second run, same open transaction — idempotency (no throw, no change).
        var secondRun = async () => await ExecAsync(conn, sql, txn);
        await secondRun.Should().NotThrowAsync("642's DELETE/INSERT are safely repeatable");

        var hasApproverRoleAfterSecondRun = await ScalarAsync<int>(conn,
            "SELECT count(*)::int FROM sys.user_roles ur JOIN sys.roles r ON r.role_id = ur.role_id " +
            "WHERE ur.user_id = 2 AND r.role_code = 'APPROVER' AND ur.company_id = 1", txn);
        hasApproverRoleAfterSecondRun.Should().Be(1, "a second replay must not duplicate the grant");

        await txn.RollbackAsync();

        // The real, live approver row must be completely untouched by this test — the real
        // demotion happens for real only when 642 runs through DbInitializer on an actual boot.
        var afterRollback = await ScalarAsync<bool>(conn,
            "SELECT is_super_admin FROM sys.users WHERE user_id = 2", null);
        afterRollback.Should().Be(originalIsSuperAdmin, "the rollback must leave the real approver row untouched");
    }

    [SkippableFact]
    public async Task Script642_does_not_touch_a_super_admin_user_who_is_not_identity_matched_to_approver()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var impostorUsername = "impostor_" + TestIds.Suffix().ToLowerInvariant();
        var impostorUserId = await ScalarAsync<long>(conn,
            "INSERT INTO sys.users (username, email, password_hash, full_name, is_super_admin, " +
            "is_active, failed_login_count, must_change_password, created_at, updated_at, version) " +
            "VALUES ($1, $2, crypt('Test@1234', gen_salt('bf', 4)), 'Impostor Super Admin', TRUE, " +
            "TRUE, 0, FALSE, now(), now(), 0) RETURNING user_id", null,
            impostorUsername, impostorUsername + "@teas.local");

        try
        {
            var sql = await ReadScriptAsync("642_demote_approver_from_super_admin.sql");
            await ExecAsync(conn, sql);

            var stillSuperAdmin = await ScalarAsync<bool>(conn,
                "SELECT is_super_admin FROM sys.users WHERE user_id = $1", null, impostorUserId);
            stillSuperAdmin.Should().BeTrue(
                "R1 — a user who is not literally named/emailed as the seeded approver must NOT be demoted, no matter its is_super_admin flag");

            var gotApproverRole = await ScalarAsync<int>(conn,
                "SELECT count(*)::int FROM sys.user_roles WHERE user_id = $1", null, impostorUserId);
            gotApproverRole.Should().Be(0, "the impostor must not be granted the APPROVER role either");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM sys.users WHERE user_id = $1", conn);
            cleanup.Parameters.AddWithValue(impostorUserId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
