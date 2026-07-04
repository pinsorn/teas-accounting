using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// Review 2026-07-04 schema hardening (SqlScripts 581–584):
///   H1 (581) — RLS on 8 previously-unprotected ITenantOwned tables.
///   H7 (582) — posted-line triggers now block re-parenting a line away from a posted doc.
///   M2 (583) — Tax Invoice header trigger blocks un-posting (POSTED→DRAFT) + guards VOIDED.
///   M1 (584) — audit.activity_log TRUNCATE is blocked (append-only, §4.8).
///
/// RLS behavioural tests use <c>SET ROLE pg_database_owner</c> (rolbypassrls=false) — the same
/// non-bypass-role trick as <see cref="SalesChainRlsTests"/>, because the suite's <c>accounting</c>
/// login bypasses RLS and would be a false-green. Triggers (H7/M2/M1) fire for every role, so those
/// run on the normal connection.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReviewHardeningRlsTests
{
    private readonly PostgresFixture _fx;
    public ReviewHardeningRlsTests(PostgresFixture fx) => _fx = fx;

    private static readonly string[] H1Tables =
    {
        "master.products", "tax.wht_types", "tax.wht_certificates", "tax.tax_filings",
        "sys.idempotency_keys", "sys.attachments", "gl.accounting_periods", "etax.submissions",
    };

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

    // ── H1: every one of the 8 tables has RLS enabled + the company_isolation policy (581) ──
    [SkippableFact]
    public async Task H1_all_eight_tables_have_rls_and_company_isolation_policy()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var c = await OpenAsync();

        foreach (var qualified in H1Tables)
        {
            var parts = qualified.Split('.');
            var (schema, table) = (parts[0], parts[1]);

            var rlsOn = await ScalarAsync(c,
                $"SELECT count(*) FROM pg_class cl JOIN pg_namespace n ON n.oid = cl.relnamespace " +
                $"WHERE n.nspname = '{schema}' AND cl.relname = '{table}' AND cl.relrowsecurity");
            rlsOn.Should().Be(1, $"{qualified} must have ROW LEVEL SECURITY enabled (581)");

            var policy = await ScalarAsync(c,
                $"SELECT count(*) FROM pg_policies WHERE schemaname = '{schema}' " +
                $"AND tablename = '{table}' AND policyname = 'company_isolation'");
            policy.Should().Be(1, $"{qualified} must carry the company_isolation policy (581)");
        }
    }

    // ── H1 behavioural: company A cannot see company B's products under the policy ──
    [SkippableFact]
    public async Task H1_products_policy_hides_other_tenants_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = await OpenAsync();
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA master TO pg_database_owner; " +
            "GRANT SELECT ON master.products TO pg_database_owner;");

        var aId = await InsertProductAsync(c, a.CompanyId);
        var bId = await InsertProductAsync(c, b.CompanyId);

        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            await ExecAsync(c,
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), " +
                "set_config('app.is_super_admin', 'false', false)");

            (await ScalarAsync(c, $"SELECT count(*) FROM master.products WHERE product_id = {aId}"))
                .Should().Be(1, "company A must see its own product");
            (await ScalarAsync(c, $"SELECT count(*) FROM master.products WHERE product_id = {bId}"))
                .Should().Be(0, "the RLS policy must hide company B's product from company A");
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }
    }

    // ── H7: a posted TI's line cannot be re-parented onto a draft TI (582) ──
    [SkippableFact]
    public async Task H7_reparenting_a_posted_line_onto_a_draft_is_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();

        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var postedTiId = await SalesChainRlsTests.InsertMinimalTaxInvoiceAsync(
            c, t.CompanyId, t.BranchId, t.CustomerId, "H7P-" + Guid.NewGuid().ToString("N")[..8], today);
        var draftTiId = await SalesChainRlsTests.InsertMinimalTaxInvoiceAsync(
            c, t.CompanyId, t.BranchId, t.CustomerId, "H7D-" + Guid.NewGuid().ToString("N")[..8], today);

        // Flip the first TI to POSTED (bypass conn — trigger only guards UPDATE of the line, not the header status here).
        await ExecAsync(c, $"UPDATE sales.tax_invoices SET status = 'POSTED' WHERE tax_invoice_id = {postedTiId}");
        var lineId = await InsertTiLineAsync(c, postedTiId);

        // Re-parent the posted TI's line onto the draft TI → 582 must block (OLD parent is POSTED).
        var act = () => ExecAsync(c,
            $"UPDATE sales.tax_invoice_lines SET tax_invoice_id = {draftTiId} WHERE line_id = {lineId}");
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514", "re-parenting a posted line must raise check_violation (H7/582)");
    }

    // ── M2: a posted Tax Invoice cannot be un-posted (POSTED→DRAFT) via direct UPDATE (583) ──
    [SkippableFact]
    public async Task M2_unposting_a_posted_tax_invoice_is_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();

        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var tiId = await SalesChainRlsTests.InsertMinimalTaxInvoiceAsync(
            c, t.CompanyId, t.BranchId, t.CustomerId, "M2-" + Guid.NewGuid().ToString("N")[..8], today);
        await ExecAsync(c, $"UPDATE sales.tax_invoices SET status = 'POSTED' WHERE tax_invoice_id = {tiId}");

        var act = () => ExecAsync(c, $"UPDATE sales.tax_invoices SET status = 'DRAFT' WHERE tax_invoice_id = {tiId}");
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514", "un-posting a POSTED Tax Invoice must raise check_violation (M2/583)");
    }

    // ── M1: audit.activity_log cannot be TRUNCATEd (584) ──
    [SkippableFact]
    public async Task M1_truncating_the_audit_log_is_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var c = await OpenAsync();

        // Runs in a transaction so a (hypothetical) success can't wipe the shared audit table.
        await ExecAsync(c, "BEGIN");
        try
        {
            var act = () => ExecAsync(c, "TRUNCATE audit.activity_log");
            (await act.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("23514", "TRUNCATE of the append-only audit log must be blocked (M1/584)");
        }
        finally
        {
            await ExecAsync(c, "ROLLBACK");
        }
    }

    private static async Task<long> InsertProductAsync(NpgsqlConnection c, int companyId)
    {
        var code = "RLSP-" + Guid.NewGuid().ToString("N")[..10];
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO master.products
                (company_id, product_code, name_th, product_type, is_active,
                 created_at, updated_at, version)
            VALUES ({companyId}, '{code}', 'สินค้า', 'GOOD', true,
                 now(), now(), 0)
            RETURNING product_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // uom_id / tax_code_id are NOT-NULL snapshot columns with NO FK — any value satisfies them.
    private static async Task<long> InsertTiLineAsync(NpgsqlConnection c, long tiId)
    {
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.tax_invoice_lines
                (tax_invoice_id, line_no, product_type, description_th, quantity,
                 uom_id, uom_text, unit_price, discount_percent, discount_amount,
                 line_amount, tax_code_id, tax_code, tax_rate, tax_amount, total_amount)
            VALUES ({tiId}, 1, 'GOODS', 'รายการ', 1,
                 1, 'ชิ้น', 100, 0, 0,
                 100, 1, 'V7', 0.07, 7, 107)
            RETURNING line_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
