using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// fix-r2-u2 (L6-4 — specs/fix-r2-u2-billing-tax-integrity.md §3.3/T6) — the
/// 639_repair_foreign_tax_code_id_on_sales_lines.sql prod hotfix must actually repair rows
/// under REAL RLS, not just when run by the test suite's superuser connection (which bypasses
/// RLS entirely and would make a broken per-company GUC loop pass anyway — the exact "RLS
/// masked by superuser tests" class of bug: troubles-wiki.md "Startup SqlScript … RLS'd tables
/// fails 42501 or silently no-ops on prod"). Mirrors the established
/// <c>SET ROLE pg_database_owner</c> trick (ExpenseCategoryBackfillRlsTests and siblings)
/// rather than <c>teas_rls_test</c>, which SKIPs on a login without CREATEROLE.
///
/// sales.{quotations,sales_orders,delivery_orders,billing_notes} and tax.tax_codes are G1
/// tables (ENABLE + FORCE ROW LEVEL SECURITY, plain company_isolation, NO bypass arm —
/// 600_superadmin_scoped_rls.sql), so a role that is NOT the table owner AND not a superuser is
/// genuinely RLS-bound here. The four sales.*_lines UPDATE targets carry no RLS at all — the
/// GRANTs below are what makes them reachable by a non-owner role, exactly mirroring 639's own
/// runtime security context table.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SalesLineTaxCodeRepairRlsTests
{
    private readonly PostgresFixture _fx;
    public SalesLineTaxCodeRepairRlsTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection c, string sql, params object[] pars)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var p in pars) cmd.Parameters.AddWithValue(p);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    // Doc date in the CURRENT month so the accounting period is open (memory
    // relative-date-seed-temporal-tests — avoid the 1st/last day edge either way).
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    [SkippableFact]
    public async Task Script639_repairs_foreign_tax_code_id_under_RLS_per_company_loop()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Two companies: `co` is the tenant under test, `other` only sources a REAL tax_code_id
        // that is genuinely foreign to `co` — exactly co3's shape (co1's VAT7 id stranded under
        // co3's row).
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var other = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var ownVat7Id = await ScalarIntAsync(conn,
            "SELECT tax_code_id FROM tax.tax_codes WHERE company_id = $1 AND code = 'VAT7'", co.CompanyId);
        var foreignId = await ScalarIntAsync(conn,
            "SELECT tax_code_id FROM tax.tax_codes WHERE company_id = $1 AND code = 'VAT7'", other.CompanyId);

        // Real onboarding path — a Billing Note with two lines, created through the service
        // (so both lines legitimately resolve to `co`'s own VAT7 id and rate 0.07 before the
        // seed corrupts them).
        long bnId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId))
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today(), Today(),
                co.CustomerId, null, null, null, "THB", 1m, null, null,
                [
                    new BillingLineInput(null, null, "บริการ 1", 1m, "ครั้ง", 1000m, 0m, null, null, 0m),
                    new BillingLineInput(null, null, "บริการ 2", 1m, "ครั้ง", 2000m, 0m, null, null, 0m),
                ]), default);
        }

        // Seed the F13-predates-this-line shape directly on the bypassing connection — the
        // seed MUST rewrite BOTH columns (a service-created line already stores tax_code =
        // 'VAT7', which IS in `co`'s own master, so rewriting only the id would exercise rule
        // (a) regardless of which branch is intended).
        // Line 1 → rule (a): foreign id, but the code string DOES match `co`'s own master →
        // recovered to `co`'s own VAT7 id.
        await ExecAsync(conn,
            $"UPDATE sales.billing_note_lines SET tax_code_id = {foreignId}, tax_code = 'VAT7' " +
            $"WHERE billing_note_id = {bnId} AND line_no = 1");
        // Line 2 → rule (b): foreign id, code string absent from `co`'s master too (the exact
        // co3 shape) → SYNTHETIC_TAX_CODE_ID.
        await ExecAsync(conn,
            $"UPDATE sales.billing_note_lines SET tax_code_id = {foreignId}, tax_code = 'VAT0' " +
            $"WHERE billing_note_id = {bnId} AND line_no = 2");

        await ExecAsync(conn,
            "GRANT USAGE ON SCHEMA sales, tax, master TO pg_database_owner; " +
            "GRANT SELECT ON master.companies, tax.tax_codes, sales.billing_notes, " +
            "sales.quotations, sales.sales_orders, sales.delivery_orders TO pg_database_owner; " +
            "GRANT SELECT, UPDATE ON sales.billing_note_lines, sales.quotation_lines, " +
            "sales.sales_order_lines, sales.delivery_order_lines TO pg_database_owner;");

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts",
            "639_repair_foreign_tax_code_id_on_sales_lines.sql");
        File.Exists(scriptPath).Should().BeTrue($"script not found at {scriptPath}");
        var sql = await File.ReadAllTextAsync(scriptPath);

        try
        {
            await ExecAsync(conn, "SET ROLE pg_database_owner");
            // The ACTUAL prod script, run under a non-owner-bypassing, non-superuser role with
            // FORCE ROW LEVEL SECURITY in effect. Raised timeout: teas_test is long-lived and
            // shared (memory: "bloated ~629 companies"), and the per-company loop scans
            // master.companies once per statement × 4 statements.
            await using var scriptCmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 300 };
            await scriptCmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecAsync(conn, "RESET ROLE");
        }

        await using var readCmd = new NpgsqlCommand(
            "SELECT line_no, tax_code_id, tax_code, tax_rate, line_amount, tax_amount, total_amount " +
            "FROM sales.billing_note_lines WHERE billing_note_id = $1 ORDER BY line_no", conn);
        readCmd.Parameters.AddWithValue(bnId);
        await using var reader = await readCmd.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(1).Should().Be(ownVat7Id,
            "rule (a): the per-company GUC-loop repair must recover a foreign id by the code string, " +
            "not silently no-op the way a bare multi-company UPDATE would with no app.company_id GUC set");
        reader.GetString(2).Should().Be("VAT7", "the string snapshot is never rewritten");
        reader.GetDecimal(3).Should().Be(0.07m, "I1 — only tax_code_id changes");
        var line1Amount = reader.GetDecimal(4);
        var line1Tax = reader.GetDecimal(5);
        var line1Total = reader.GetDecimal(6);
        line1Amount.Should().Be(1000m);
        line1Tax.Should().Be(70m);
        line1Total.Should().Be(1070m);

        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(1).Should().Be(0, "rule (b): no id and no code-string match — SYNTHETIC_TAX_CODE_ID");
        reader.GetString(2).Should().Be("VAT0", "the string snapshot is never rewritten");
        reader.GetDecimal(3).Should().Be(0.07m, "I1 — only tax_code_id changes");
        reader.GetDecimal(4).Should().Be(2000m);
        reader.GetDecimal(5).Should().Be(140m);
        reader.GetDecimal(6).Should().Be(2140m);
    }
}
