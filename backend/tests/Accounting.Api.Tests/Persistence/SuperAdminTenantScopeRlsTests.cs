using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// Bugfix 2026-07-08 — super admin saw the UNION of all companies' data (prod: Repttown vs
/// พงศ์สันต์ quotations/customers/dashboard identical regardless of the switcher). Root cause:
/// BOTH the EF global query filter (<c>AccountingDbContext.cs</c> <c>_tenant.IsSuperAdmin ||</c>)
/// AND every <c>company_isolation</c> RLS policy (<c>OR current_setting('app.is_super_admin')</c>)
/// exempted super admins from tenant scoping, and <c>TenantMiddleware</c> pinned that GUC straight
/// from the logged-in user's flag — so an actual super-admin REQUEST (company_id = selected
/// company, is_super_admin = true) leaked every OTHER company's rows regardless of which company
/// was selected.
///
/// This test reproduces that exact session shape at the DB layer: <c>SET ROLE pg_database_owner</c>
/// (same non-bypass-role trick as <see cref="SalesChainRlsTests"/>/<see cref="ReviewHardeningRlsTests"/> —
/// the suite's <c>accounting</c> login bypasses RLS and would be a false-green). It pins
/// <c>app.company_id = company B</c> AND <c>app.is_super_admin = 'true'</c> — precisely what
/// <c>TenantMiddleware</c> used to pin for an authenticated super admin who had switched to B —
/// and asserts ONLY B's rows are visible. NO <c>app.bypass_rls</c> is set (that GUC doesn't exist
/// pre-fix, and must have zero bearing post-fix).
///
/// Before <c>600_superadmin_scoped_rls.sql</c> this FAILS: company A's row leaks through the
/// <c>OR is_super_admin</c> arm. After 600 retires that arm from the policy entirely,
/// <c>is_super_admin</c> has no bearing on RLS and the test passes.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SuperAdminTenantScopeRlsTests
{
    private readonly PostgresFixture _fx;
    public SuperAdminTenantScopeRlsTests(PostgresFixture fx) => _fx = fx;

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
    public async Task Super_admin_pinned_to_company_B_sees_only_B_quotations_and_customers()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Two fresh tenants via the real onboarding path (TestCompanyFactory already seeds one
        // customer per company — that satisfies "a customer in each").
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = await OpenAsync();
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA sales, master TO pg_database_owner; " +
            "GRANT SELECT ON sales.quotations TO pg_database_owner; " +
            "GRANT SELECT ON master.customers TO pg_database_owner;");

        // Insert one quotation per tenant on the bypass (accounting) connection — same approach
        // SalesChainRlsTests uses for seeding rows the RLS-bound role will later be tested against.
        var aQuotationId = await InsertQuotationAsync(c, a.CompanyId, a.BranchId, a.CustomerId);
        var bQuotationId = await InsertQuotationAsync(c, b.CompanyId, b.BranchId, b.CustomerId);

        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            // Exactly what TenantMiddleware used to pin for an authenticated super admin who has
            // switched to company B: company_id = B, is_super_admin = true. Deliberately NO
            // app.bypass_rls — the new GUC doesn't exist pre-fix and must not matter post-fix.
            await ExecAsync(c,
                $"SELECT set_config('app.company_id', '{b.CompanyId}', false), " +
                "set_config('app.is_super_admin', 'true', false)");

            (await ScalarAsync(c, $"SELECT count(*) FROM sales.quotations WHERE quotation_id = {bQuotationId}"))
                .Should().Be(1, "company B's own quotation must be visible while pinned to B");
            (await ScalarAsync(c, $"SELECT count(*) FROM sales.quotations WHERE quotation_id = {aQuotationId}"))
                .Should().Be(0, "a super admin pinned to B must NOT see company A's quotation " +
                                "(this is the reported leak — fails before 600, passes after)");

            (await ScalarAsync(c, $"SELECT count(*) FROM master.customers WHERE customer_id = {b.CustomerId}"))
                .Should().Be(1, "company B's own customer must be visible while pinned to B");
            (await ScalarAsync(c, $"SELECT count(*) FROM master.customers WHERE customer_id = {a.CustomerId}"))
                .Should().Be(0, "a super admin pinned to B must NOT see company A's customer");
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }
    }

    /// <summary>D5 — letterhead symptom. Fetching a KNOWN company-A document id while pinned to B
    /// must return zero rows (the app-layer detail endpoint then maps "not found" to 404/403
    /// instead of ever rendering A's data under B's letterhead).</summary>
    [SkippableFact]
    public async Task Cross_company_quotation_detail_fetch_returns_zero_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = await OpenAsync();
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA sales TO pg_database_owner; " +
            "GRANT SELECT ON sales.quotations TO pg_database_owner;");

        var aQuotationId = await InsertQuotationAsync(c, a.CompanyId, a.BranchId, a.CustomerId);

        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            await ExecAsync(c,
                $"SELECT set_config('app.company_id', '{b.CompanyId}', false), " +
                "set_config('app.is_super_admin', 'true', false)");

            (await ScalarAsync(c, $"SELECT count(*) FROM sales.quotations WHERE quotation_id = {aQuotationId}"))
                .Should().Be(0, "a known company-A quotation id must be invisible while pinned to B " +
                                "(app layer maps 0 rows -> 404, never renders A under B's letterhead)");
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }
    }

    /// <summary>Minimal DRAFT quotation insert — mirrors <c>SalesChainRlsTests</c>'s private
    /// per-table insert (not reusable across classes) for just the one table this suite needs.</summary>
    private static async Task<long> InsertQuotationAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId)
    {
        var docNo = "SATS-" + Guid.NewGuid().ToString("N")[..10];
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.quotations
                (company_id, branch_id, customer_id, doc_no, doc_date, valid_until_date, status,
                 customer_name, customer_type, currency_code, exchange_rate,
                 subtotal_amount, vat_amount, total_amount, show_wht_note,
                 created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','{today}','DRAFT',
                 'ลูกค้า','CORPORATE','THB',1, 100,7,107, false, now(), now(), 0, 0)
            RETURNING quotation_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
