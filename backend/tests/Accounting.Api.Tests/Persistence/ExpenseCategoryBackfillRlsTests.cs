using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// WP1.5b (F20 — specs/fix-purchase-ux-findings-2026-07-14.md) — the
/// 623_backfill_expense_category_accounts.sql prod hotfix must actually update rows under REAL
/// RLS, not just when run by the test suite's superuser connection (which bypasses RLS
/// entirely and would make a broken per-company GUC loop pass anyway — the exact "RLS masked
/// by superuser tests" class of bug: troubles-wiki.md "Startup SqlScript … RLS'd tables fails
/// 42501 or silently no-ops on prod"). Mirrors the established `SET ROLE pg_database_owner`
/// trick (SalesChainRlsTests/ReviewHardeningRlsTests/SuperAdminTenantScopeRlsTests) rather than
/// `teas_rls_test`, which SKIPs on a login without CREATEROLE (troubles-wiki.md "New RLS test
/// SKIPs with teas_rls_test unavailable").
///
/// sys.expense_categories + master.chart_of_accounts are G1 tables (ENABLE + FORCE ROW LEVEL
/// SECURITY, plain company_isolation, NO bypass arm — 600_superadmin_scoped_rls.sql), so a
/// role that is NOT the table owner AND not a superuser is genuinely RLS-bound here.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExpenseCategoryBackfillRlsTests
{
    private readonly PostgresFixture _fx;
    public ExpenseCategoryBackfillRlsTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    [SkippableFact]
    public async Task Script623_fills_null_default_account_under_RLS_per_company_loop()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Real onboarding path — every default account this script could resolve to already
        // exists (DefaultChartOfAccounts, incl. "5200").
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // Simulate an existing tenant's F20 gap (hand-created category, no default account) —
        // on the bypass (superuser) connection, same as how other RLS tests seed fixture rows.
        int categoryId; // sys.expense_categories.category_id is int (ExpenseCategory.CategoryId)
        await using (var seedCmd = new NpgsqlCommand(
            "INSERT INTO sys.expense_categories " +
            "(company_id, category_code, name_th, default_is_recoverable_vat, is_capex, is_cogs, is_active, created_at) " +
            "VALUES ($1, 'GAPTEST', 'ทดสอบช่องว่าง', TRUE, FALSE, FALSE, TRUE, now()) RETURNING category_id", conn))
        {
            seedCmd.Parameters.AddWithValue(co.CompanyId);
            categoryId = (int)(await seedCmd.ExecuteScalarAsync())!;
        }

        await ExecAsync(conn,
            "GRANT USAGE ON SCHEMA sys, master TO pg_database_owner; " +
            "GRANT SELECT, UPDATE ON sys.expense_categories TO pg_database_owner; " +
            "GRANT SELECT ON master.chart_of_accounts TO pg_database_owner; " +
            "GRANT SELECT ON master.companies TO pg_database_owner;");

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts",
            "623_backfill_expense_category_accounts.sql");
        File.Exists(scriptPath).Should().BeTrue($"script not found at {scriptPath}");
        var sql = await File.ReadAllTextAsync(scriptPath);

        try
        {
            await ExecAsync(conn, "SET ROLE pg_database_owner");
            // The ACTUAL prod script, run under a non-owner-bypassing, non-superuser role with
            // FORCE ROW LEVEL SECURITY in effect — exactly the prod app-role shape. Raised
            // timeout: teas_test is a long-lived shared DB (memory: "bloated ~629 companies"),
            // and this script's per-company loop scans master.companies once per iteration.
            await using var scriptCmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
            await scriptCmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecAsync(conn, "RESET ROLE");
        }

        var acctId = await ScalarAsync(conn,
            $"SELECT default_expense_account_id FROM sys.expense_categories WHERE category_id = {categoryId}");
        acctId.Should().NotBeNull(
            "the per-company GUC-loop backfill must actually fill the NULL default under RLS, " +
            "not silently no-op the way a bare multi-company UPDATE would with no app.company_id GUC set");

        var acctCode = await ScalarAsync(conn,
            $"SELECT account_code FROM master.chart_of_accounts WHERE account_id = {acctId}");
        acctCode.Should().Be("5200", "universal fallback — every CreateAsync company has 5200, none has a dedicated COGS account");
    }
}
