using Accounting.Api.Tests.Fixtures;
using Accounting.TestKit;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// Codex review 2026-08-20 F1 (specs/fix-codex-review-2026-08-20.md) — 637/638 must not launder a
/// REAL tenant's all-zero placeholder Tax ID into the fictional-but-checksum-valid 0105000000012;
/// only the demo company's own stable seeded identity (name_th = 'Demo Company (เดโม)', see
/// 120_seed_demo_company.sql) may be repaired. Mirrors SalesLineTaxCodeRepairRlsTests' read-the-
/// actual-script-from-disk-and-replay-it pattern. No RLS gymnastics needed here — both
/// master.companies and master.company_profile have relrowsecurity = false (confirmed in 637/638's
/// own headers), unlike the sales.* tables that test covers.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DemoTaxIdRepairScriptTests
{
    private readonly PostgresFixture _fx;
    public DemoTaxIdRepairScriptTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql, NpgsqlTransaction? txn, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c, txn);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task ExecAsync(NpgsqlConnection c, string sql, params object[] pars) =>
        ExecAsync(c, sql, null, pars);

    private static async Task<string> ScalarStringAsync(NpgsqlConnection c, string sql, NpgsqlTransaction? txn, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c, txn);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<string> ScalarStringAsync(NpgsqlConnection c, string sql, params object[] pars) =>
        ScalarStringAsync(c, sql, null, pars);

    private static async Task<string> ReadScriptAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts", fileName);
        File.Exists(path).Should().BeTrue($"script not found at {path}");
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>Positive case — the REAL demo company (company_id 1, the only row in the shared
    /// teas_test DB that legitimately matches the identity predicate) gets repaired. Company 1
    /// already holds the repaired value '0105000000012' from a prior real run of this script — a
    /// synthetic second row can never reach that SAME literal value too (ix_companies_tax_id is a
    /// single GLOBAL unique index, not scoped by name), so this scenario is exercised directly
    /// against company 1 itself inside a transaction that is ALWAYS rolled back: nothing is ever
    /// committed, so no concurrent reader (e.g. the live API on :5080) can observe the transient
    /// flip back to the placeholder, and company 1's real row is provably unchanged afterward.</summary>
    [SkippableFact]
    public async Task Script637_repairs_the_real_demo_company_row()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        var originalTaxId = await ScalarStringAsync(conn,
            "SELECT tax_id FROM master.companies WHERE company_id = 1");

        await using var txn = await conn.BeginTransactionAsync();
        await ExecAsync(conn, "UPDATE master.companies SET tax_id = '0000000000000' WHERE company_id = 1", txn);

        var sql = await ReadScriptAsync("637_repair_all_zero_company_tax_id.sql");
        await ExecAsync(conn, sql, txn);

        var repaired = await ScalarStringAsync(conn,
            "SELECT tax_id FROM master.companies WHERE company_id = 1", txn);
        repaired.Should().Be("0105000000012",
            "the demo company's own stable seeded identity (name_th = 'Demo Company (เดโม)') must still be repaired");

        await txn.RollbackAsync();

        var afterRollback = await ScalarStringAsync(conn,
            "SELECT tax_id FROM master.companies WHERE company_id = 1");
        afterRollback.Should().Be(originalTaxId, "the rollback must leave company 1's real row untouched");
    }

    /// <summary>Negative case (F1's actual finding) — a genuine OTHER tenant that also happens to
    /// still be sitting on the all-zero placeholder must NOT be laundered into the fictional Tax
    /// ID, because its name does not match the demo identity.</summary>
    [SkippableFact]
    public async Task Script637_does_not_repair_a_real_tenant_coincidentally_holding_the_placeholder()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var real = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // `finally` always moves `real` OFF the unique placeholder value before the test ends
        // (pass or fail), so this test never leaves the global unique slot occupied for a later
        // run to collide with.
        try
        {
            await ExecAsync(conn,
                "UPDATE master.companies SET tax_id = '0000000000000' WHERE company_id = $1",
                real.CompanyId);

            var sql = await ReadScriptAsync("637_repair_all_zero_company_tax_id.sql");
            await ExecAsync(conn, sql);

            var realTaxId = await ScalarStringAsync(conn,
                "SELECT tax_id FROM master.companies WHERE company_id = $1", real.CompanyId);
            realTaxId.Should().Be("0000000000000",
                "F1 — a real tenant coincidentally holding the all-zero placeholder must NOT be " +
                "laundered into a fictional valid Tax ID; the filing/WHT guards must keep refusing it");
        }
        finally
        {
            await ExecAsync(conn,
                "UPDATE master.companies SET tax_id = $2 WHERE company_id = $1 AND tax_id = '0000000000000'",
                real.CompanyId, TestIds.TaxId());
        }
    }

    [SkippableFact]
    public async Task Script638_repairs_only_the_demo_company_profile_identity_not_a_real_tenant()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var demo = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var real = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // company_profile.tax_id carries NO unique index (confirmed in 638's own header), so both
        // rows can hold the placeholder simultaneously — no sequencing trick needed here.
        await ExecAsync(conn,
            "UPDATE master.companies SET name_th = 'Demo Company (เดโม)' WHERE company_id = $1",
            demo.CompanyId);
        await ExecAsync(conn,
            "UPDATE master.company_profile SET tax_id = '0000000000000' WHERE company_id = $1",
            demo.CompanyId);
        await ExecAsync(conn,
            "UPDATE master.company_profile SET tax_id = '0000000000000' WHERE company_id = $1",
            real.CompanyId);

        var sql = await ReadScriptAsync("638_repair_all_zero_company_profile_tax_id.sql");
        await using (var cmd = new NpgsqlCommand(sql, conn)) await cmd.ExecuteNonQueryAsync();

        var demoTaxId = await ScalarStringAsync(conn,
            "SELECT tax_id FROM master.company_profile WHERE company_id = $1", demo.CompanyId);
        var realTaxId = await ScalarStringAsync(conn,
            "SELECT tax_id FROM master.company_profile WHERE company_id = $1", real.CompanyId);

        demoTaxId.Should().Be("0105000000012",
            "the demo company's own company_profile row must still be repaired (joined via master.companies identity)");
        realTaxId.Should().Be("0000000000000",
            "F1 — a real tenant's company_profile placeholder must NOT be laundered into a fictional valid Tax ID");
    }
}
