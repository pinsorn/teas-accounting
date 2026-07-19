using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — 626_reconcile_number_sequences.sql is the
/// money/footgun SQL (Fable writes + reviews it personally). This runs the ACTUAL prod script file
/// under a non-owner-bypassing, non-superuser role with FORCE ROW LEVEL SECURITY in effect — the
/// same shape as the NOBYPASSRLS prod app role — to catch the exact class of 42501/silent-no-op bug
/// that a superuser test connection would mask (memory: rls-masked-by-superuser-tests; v1.22.0 died
/// on 625 running as superuser). Mirrors the established `SET ROLE pg_database_owner` trick
/// (SalesChainRlsTests / ExpenseCategoryBackfillRlsTests) rather than `teas_rls_test`, which SKIPs
/// on a login without CREATEROLE (troubles-wiki.md).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceReconcileScriptTests
{
    private readonly PostgresFixture _fx;
    public NumberSequenceReconcileScriptTests(PostgresFixture fx) => _fx = fx;

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
    public async Task Script626_lifts_a_drifted_bucket_to_true_max_and_is_idempotent_under_RLS()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // Directly-seeded "historical drift" rows — 3 contiguous JV doc_nos with NO matching
        // sys.number_sequences bucket at all (the "missing bucket" flavour of drift), on the
        // bypass (superuser) connection, exactly how the real drift-origin seeds/resets populate
        // these tables.
        string DocNo(int seq) => $"{today:MM}-{today:yyyy}-JV-{seq:D4}";
        for (var seq = 1; seq <= 3; seq++)
        {
            await using var cmd = new NpgsqlCommand($@"
                INSERT INTO gl.journal_entries
                    (company_id, branch_id, prefix_code, doc_no, doc_date, posting_date, description,
                     currency_code, exchange_rate, total_debit, total_credit, status, posted_at, posted_by,
                     is_closing_entry, created_at, updated_at, version)
                VALUES
                    ({t.CompanyId}, {t.BranchId}, 'JV', '{DocNo(seq)}', '{today:yyyy-MM-dd}', '{today:yyyy-MM-dd}',
                     'reconcile seed', 'THB', 1, 100, 100, 'POSTED', now(), 1,
                     false, now(), now(), 0)", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // Grant the non-bypass role exactly what the prod app role needs for this script (accounting
        // owns every table; grants are idempotent, run while still the bypass role).
        await ExecAsync(conn,
            "GRANT USAGE ON SCHEMA sys, gl, purchase, sales, payroll, expense, fixedasset, tax, master TO pg_database_owner; " +
            "GRANT SELECT ON master.companies TO pg_database_owner; " +
            "GRANT SELECT, INSERT, UPDATE ON sys.number_sequences TO pg_database_owner; " +
            "GRANT SELECT ON gl.journal_entries, purchase.purchase_orders, purchase.vendor_invoices, " +
            "purchase.payment_vouchers, sales.tax_invoices, sales.receipts, sales.quotations, " +
            "sales.sales_orders, sales.delivery_orders, sales.billing_notes, sales.tax_adjustment_notes, " +
            "payroll.payroll_runs, expense.expense_claims, fixedasset.fixed_assets, tax.wht_certificates " +
            "TO pg_database_owner;");

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts", "626_reconcile_number_sequences.sql");
        File.Exists(scriptPath).Should().BeTrue($"script not found at {scriptPath}");
        var sql = await File.ReadAllTextAsync(scriptPath);

        try
        {
            await ExecAsync(conn, "SET ROLE pg_database_owner");
            // The ACTUAL prod script, run under a non-owner-bypassing, non-superuser role with
            // FORCE ROW LEVEL SECURITY in effect. teas_test is long-lived/shared (bloated with many
            // companies), and the script's per-company loop scans master.companies once per
            // iteration — raised timeout.
            await using var scriptCmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
            await scriptCmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecAsync(conn, "RESET ROLE");
        }

        var currentValue = await ScalarAsync(conn,
            $"SELECT current_value FROM sys.number_sequences WHERE company_id={t.CompanyId} " +
            $"AND branch_id={t.BranchId} AND prefix_code='JV' AND sub_prefix='' " +
            $"AND period_year={today.Year} AND period_month={today.Month}");
        currentValue.Should().Be(3, "reconcile must insert the MISSING bucket at the true max (3), " +
            "not leave it absent");

        // Idempotency: bump one MORE real doc (seq 4) in between runs, then re-run. The bucket must
        // land at the NEW true max (4) and never regress below whatever it already holds.
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO gl.journal_entries
                (company_id, branch_id, prefix_code, doc_no, doc_date, posting_date, description,
                 currency_code, exchange_rate, total_debit, total_credit, status, posted_at, posted_by,
                 is_closing_entry, created_at, updated_at, version)
            VALUES
                ({t.CompanyId}, {t.BranchId}, 'JV', '{DocNo(4)}', '{today:yyyy-MM-dd}', '{today:yyyy-MM-dd}',
                 'reconcile seed 2', 'THB', 1, 100, 100, 'POSTED', now(), 1,
                 false, now(), now(), 0)", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            await ExecAsync(conn, "SET ROLE pg_database_owner");
            await using var scriptCmd2 = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
            await scriptCmd2.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecAsync(conn, "RESET ROLE");
        }

        var afterSecondRun = await ScalarAsync(conn,
            $"SELECT current_value FROM sys.number_sequences WHERE company_id={t.CompanyId} " +
            $"AND branch_id={t.BranchId} AND prefix_code='JV' AND sub_prefix='' " +
            $"AND period_year={today.Year} AND period_month={today.Month}");
        afterSecondRun.Should().Be(4, "a second run must lift to the NEW true max (4), " +
            "proving it is safe to re-run and never regresses a bucket");
    }
}
