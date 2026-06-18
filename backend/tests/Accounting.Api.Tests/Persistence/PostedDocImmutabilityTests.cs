using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// §4.2 / ม.86/9-10 — DB-level posting immutability on Receipts and Tax Adjustment
/// Notes (CN/DN). SqlScripts 570/571 add BEFORE UPDATE/DELETE triggers that block
/// mutation of the posted snapshot once status = POSTED, mirroring TI (040) / VI (060).
/// Triggers fire regardless of the connection's rolbypassrls, so these run on the
/// normal connection. Each test also asserts a DRAFT row STILL mutates — proving the
/// trigger blocks only the posted snapshot, not legitimate draft edits.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostedDocImmutabilityTests
{
    private readonly PostgresFixture _fx;
    public PostedDocImmutabilityTests(PostgresFixture fx) => _fx = fx;

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

    private static string Today => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    private static async Task<long> InsertReceiptAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId, string status)
    {
        var docNo = "IMMRC-" + Guid.NewGuid().ToString("N")[..10];
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.receipts
                (company_id, branch_id, customer_id, doc_no, doc_date, status,
                 customer_name, customer_address, payment_method, currency_code, exchange_rate,
                 amount, total_amount, total_amount_thb, wht_amount, cash_received,
                 created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},{customerId},'{docNo}','{Today}','{status}',
                 'ลูกค้า','99 ถ.ทดสอบ','CASH','THB',1, 100,100,100,0,100, now(), now(), 0, 0)
            RETURNING receipt_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> InsertAdjNoteAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId, string status)
    {
        var tiDocNo = "IMMTI-" + Guid.NewGuid().ToString("N")[..10];
        var tiId = await SalesChainRlsTests.InsertMinimalTaxInvoiceAsync(
            c, companyId, branchId, customerId, tiDocNo, Today);

        var docNo = "IMMCN-" + Guid.NewGuid().ToString("N")[..10];
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.tax_adjustment_notes
                (company_id, branch_id, customer_id, doc_no, prefix_code, note_type,
                 doc_date, tax_point_date, original_tax_invoice_id, reason,
                 customer_name, customer_address, customer_vat_registered, currency_code, exchange_rate,
                 subtotal_amount, tax_amount, total_amount, total_amount_thb, tax_rate,
                 status, created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},{customerId},'{docNo}','CN','CREDIT',
                 '{Today}','{Today}', {tiId}, 'ทดสอบ',
                 'ลูกค้า','99 ถ.ทดสอบ', true, 'THB',1, 100,7,107,107,0.07,
                 '{status}', now(), now(), 0, 0)
            RETURNING note_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // ---- Receipts (§4.2) ----

    [SkippableFact]
    public async Task Posted_receipt_cannot_be_updated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertReceiptAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "POSTED");

        var act = async () => await ExecAsync(c,
            $"UPDATE sales.receipts SET total_amount = total_amount + 1 WHERE receipt_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514"); // check_violation raised by the trigger
    }

    [SkippableFact]
    public async Task Posted_receipt_cannot_be_deleted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertReceiptAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "POSTED");

        var act = async () => await ExecAsync(c, $"DELETE FROM sales.receipts WHERE receipt_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514");
    }

    [SkippableFact]
    public async Task Draft_receipt_can_still_be_updated_and_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertReceiptAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "DRAFT");

        // Editing a draft (incl. the post transition) must NOT be blocked.
        var act = async () => await ExecAsync(c,
            $"UPDATE sales.receipts SET total_amount = 200, status = 'POSTED' WHERE receipt_id = {id}");
        await act.Should().NotThrowAsync();
    }

    // ---- Tax Adjustment Notes / CN-DN (§4.2, ม.86/9-10) ----

    [SkippableFact]
    public async Task Posted_adjustment_note_cannot_be_updated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertAdjNoteAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "POSTED");

        var act = async () => await ExecAsync(c,
            $"UPDATE sales.tax_adjustment_notes SET total_amount = total_amount + 1 WHERE note_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514");
    }

    [SkippableFact]
    public async Task Posted_adjustment_note_cannot_be_deleted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertAdjNoteAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "POSTED");

        var act = async () => await ExecAsync(c,
            $"DELETE FROM sales.tax_adjustment_notes WHERE note_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514");
    }

    [SkippableFact]
    public async Task Draft_adjustment_note_can_still_be_updated_and_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var c = await OpenAsync();
        var id = await InsertAdjNoteAsync(c, co.CompanyId, co.BranchId, co.CustomerId, "DRAFT");

        var act = async () => await ExecAsync(c,
            $"UPDATE sales.tax_adjustment_notes SET total_amount = 200, status = 'POSTED' WHERE note_id = {id}");
        await act.Should().NotThrowAsync();
    }
}
