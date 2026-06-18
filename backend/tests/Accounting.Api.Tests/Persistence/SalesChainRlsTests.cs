using System.Data;
using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// §4.7 — DB-level Row-Level-Security backstop on the sales chain
/// (quotations / sales_orders / delivery_orders / receipts + tax_adjustment_notes).
/// SqlScripts 570/571/572 add ENABLE + FORCE RLS + a tenant policy keyed on
/// <c>current_setting('app.company_id')</c>.
///
/// WHY A SPECIAL ROLE: the <c>accounting</c> login this suite runs as has
/// <c>rolbypassrls=true</c> — so a normal connection (and the EF global query
/// filter) would make this a FALSE-GREEN: it never exercises the DB policy. To
/// hit the policy we <c>SET ROLE pg_database_owner</c> (membership is implicit
/// for the DB owner, and that role has <c>rolbypassrls=false</c>), after granting
/// it SELECT on the tables (accounting owns them). Each assertion is TWO-DIRECTIONAL:
/// company A's own row IS visible (proves the grant + the app.company_id pin work)
/// AND company B's row is invisible (proves the policy filters). If the policy were
/// dropped, the B-invisible assertion would fail — i.e. this test really tests ④.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SalesChainRlsTests
{
    private readonly PostgresFixture _fx;
    public SalesChainRlsTests(PostgresFixture fx) => _fx = fx;

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

    [SkippableTheory]
    [InlineData("quotations", "quotation_id")]
    [InlineData("sales_orders", "sales_order_id")]
    [InlineData("delivery_orders", "delivery_order_id")]
    [InlineData("receipts", "receipt_id")]
    [InlineData("tax_adjustment_notes", "note_id")]
    public async Task Company_A_cannot_see_company_B_rows_under_the_policy(string table, string pk)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Two fresh, real tenants via the onboarding path (their own company_id rows).
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = await OpenAsync();

        // Grant the non-bypass role read on the table (accounting owns it). Idempotent.
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA sales TO pg_database_owner; " +
            $"GRANT SELECT ON sales.{table} TO pg_database_owner;");

        // Insert one known row per tenant directly (bypass connection — accounting bypasses RLS,
        // so the INSERT is never blocked; this is exactly how the seeds populate these tables).
        var aId = await InsertRowAsync(c, table, a.CompanyId, a.BranchId, a.CustomerId);
        var bId = await InsertRowAsync(c, table, b.CompanyId, b.BranchId, b.CustomerId);

        // Now switch to the policy-bound role and pin the tenant to A.
        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            await ExecAsync(c,
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), " +
                "set_config('app.is_super_admin', 'false', false)");

            var ownVisible = await ScalarAsync(c,
                $"SELECT count(*) FROM sales.{table} WHERE {pk} = {aId}");
            ownVisible.Should().Be(1,
                $"company A must see its own {table} row (grant + tenant pin live)");

            var foreignVisible = await ScalarAsync(c,
                $"SELECT count(*) FROM sales.{table} WHERE {pk} = {bId}");
            foreignVisible.Should().Be(0,
                $"the RLS policy must hide company B's {table} row from company A");
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }
    }

    /// <summary>Inserts a single minimal row carrying company_id and returns its PK.
    /// Only the policy key (company_id) and the NOT-NULL-no-default columns are set.
    /// A bypass connection (accounting) is used so the INSERT is never RLS-blocked —
    /// the same path the seeds use.</summary>
    private static async Task<long> InsertRowAsync(
        NpgsqlConnection c, string table, int companyId, int branchId, long customerId)
    {
        var docNo = "RLS-" + Guid.NewGuid().ToString("N")[..10];
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        string sql = table switch
        {
            "quotations" => $@"
                INSERT INTO sales.quotations
                    (company_id, branch_id, customer_id, doc_no, doc_date, valid_until_date, status,
                     customer_name, customer_type, currency_code, exchange_rate,
                     subtotal_amount, vat_amount, total_amount, show_wht_note,
                     created_at, updated_at, version, print_count)
                VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','{today}','DRAFT',
                     'ลูกค้า','CORPORATE','THB',1, 100,7,107, false, now(), now(), 0, 0)
                RETURNING quotation_id",
            "sales_orders" => $@"
                INSERT INTO sales.sales_orders
                    (company_id, branch_id, customer_id, doc_no, doc_date, status,
                     customer_name, customer_type, currency_code, exchange_rate,
                     subtotal_amount, vat_amount, total_amount,
                     created_at, updated_at, version, print_count)
                VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','DRAFT',
                     'ลูกค้า','CORPORATE','THB',1, 100,7,107, now(), now(), 0, 0)
                RETURNING sales_order_id",
            "delivery_orders" => $@"
                INSERT INTO sales.delivery_orders
                    (company_id, branch_id, customer_id, doc_no, doc_date, status,
                     customer_name, customer_type, is_combined_with_ti, currency_code, exchange_rate,
                     subtotal_amount, vat_amount, total_amount,
                     created_at, updated_at, version, print_count)
                VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','DRAFT',
                     'ลูกค้า','CORPORATE', false, 'THB',1, 100,7,107, now(), now(), 0, 0)
                RETURNING delivery_order_id",
            "receipts" => $@"
                INSERT INTO sales.receipts
                    (company_id, branch_id, customer_id, doc_no, doc_date, status,
                     customer_name, customer_address, payment_method, currency_code, exchange_rate,
                     amount, total_amount, total_amount_thb, wht_amount, cash_received,
                     created_at, updated_at, version, print_count)
                VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','DRAFT',
                     'ลูกค้า','99 ถ.ทดสอบ','CASH','THB',1, 100,100,100,0,100, now(), now(), 0, 0)
                RETURNING receipt_id",
            "tax_adjustment_notes" => await InsertAdjNoteAsync(c, companyId, branchId, customerId, docNo, today),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };
        if (table == "tax_adjustment_notes") return long.Parse(sql);  // already the PK
        await using var cmd = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>tax_adjustment_notes.original_tax_invoice_id is NOT NULL with an FK to
    /// sales.tax_invoices — so insert a minimal TI for the tenant first, then the note.</summary>
    private static async Task<string> InsertAdjNoteAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId, string docNo, string today)
    {
        var tiDocNo = "RLSTI-" + Guid.NewGuid().ToString("N")[..10];
        var tiId = await InsertMinimalTaxInvoiceAsync(c, companyId, branchId, customerId, tiDocNo, today);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.tax_adjustment_notes
                (company_id, branch_id, customer_id, doc_no, prefix_code, note_type,
                 doc_date, tax_point_date, original_tax_invoice_id, reason,
                 customer_name, customer_address, customer_vat_registered, currency_code, exchange_rate,
                 subtotal_amount, tax_amount, total_amount, total_amount_thb, tax_rate,
                 status, created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},{customerId},'{docNo}','CN','CREDIT',
                 '{today}','{today}', {tiId}, 'ทดสอบ',
                 'ลูกค้า','99 ถ.ทดสอบ', true, 'THB',1, 100,7,107,107,0.07,
                 'DRAFT', now(), now(), 0, 0)
            RETURNING note_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()).ToString();
    }

    /// <summary>Minimal DRAFT tax_invoice for the tenant — only needed to satisfy the
    /// tax_adjustment_notes.original_tax_invoice_id FK. supplier_* are NOT NULL fixed-length.</summary>
    internal static async Task<long> InsertMinimalTaxInvoiceAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId, string docNo, string today)
    {
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.tax_invoices
                (company_id, branch_id, doc_no, doc_date, tax_point_date, status, is_substitute,
                 supplier_tax_id, supplier_branch_code, supplier_branch_name,
                 supplier_name, supplier_address, customer_id, customer_name, customer_address,
                 customer_vat_registered, subtotal_amount, discount_amount, taxable_amount,
                 non_taxable_amount, tax_amount, total_amount, total_amount_thb,
                 is_tax_inclusive, amount_paid, is_e_tax, delivered_to_customer,
                 created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},'{docNo}','{today}','{today}','DRAFT', false,
                 '0000000000000','00000','สำนักงานใหญ่',
                 'ผู้ขาย','99 ถ.ทดสอบ',{customerId},'ลูกค้า','99 ถ.ทดสอบ',
                 true, 100,0,100, 0,7,107,107, false,0,false,false,
                 now(), now(), 0, 0)
            RETURNING tax_invoice_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
