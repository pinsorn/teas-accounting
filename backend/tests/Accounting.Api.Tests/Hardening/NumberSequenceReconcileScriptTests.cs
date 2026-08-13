using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private static async Task<string> ReadScriptAsync(string fileName)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts", fileName);
        File.Exists(scriptPath).Should().BeTrue($"script not found at {scriptPath}");
        return await File.ReadAllTextAsync(scriptPath);
    }

    private static async Task RunUnderAppRoleAsync(NpgsqlConnection conn, string sql)
    {
        try
        {
            await ExecAsync(conn, "SET ROLE pg_database_owner");
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecAsync(conn, "RESET ROLE");
        }
    }

    private const string AppRoleGrants =
        "GRANT USAGE ON SCHEMA sys, gl, purchase, sales, payroll, expense, fixedasset, tax, master TO pg_database_owner; " +
        "GRANT SELECT ON master.companies TO pg_database_owner; " +
        "GRANT SELECT, INSERT, UPDATE ON sys.number_sequences TO pg_database_owner; " +
        "GRANT SELECT ON gl.journal_entries, purchase.purchase_orders, purchase.vendor_invoices, " +
        "purchase.payment_vouchers, sales.tax_invoices, sales.receipts, sales.quotations, " +
        "sales.sales_orders, sales.delivery_orders, sales.billing_notes, sales.tax_adjustment_notes, " +
        "payroll.payroll_runs, expense.expense_claims, fixedasset.fixed_assets, tax.wht_certificates " +
        "TO pg_database_owner;";

    /// <summary>H1 (specs/fix-duplicate-tax-doc-numbers.md) T4/I2 — 634_reconcile_number_sequences_
    /// company_wide.sql touches ONLY sys.number_sequences (WP-2). It must never renumber an existing
    /// document — not one row anywhere is legally permitted to change (F8/F9/F11). Snapshot every
    /// doc_no on this company before/after and assert set equality.</summary>
    [SkippableFact]
    public async Task Script634_changes_no_doc_no()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        string DocNo(int seq) => $"{today:MM}-{today:yyyy}-JV-{seq:D4}";
        for (var seq = 1; seq <= 3; seq++)
        {
            await using var cmd = new NpgsqlCommand($@"
                INSERT INTO gl.journal_entries
                    (company_id, branch_id, prefix_code, doc_no, doc_date, posting_date, description,
                     currency_code, exchange_rate, total_debit, total_credit, status, posted_at, posted_by,
                     is_closing_entry, created_at, updated_at, version)
                VALUES
                    ({t.CompanyId}, {(seq <= 1 ? 0 : t.BranchId)}, 'JV', '{DocNo(seq)}', '{today:yyyy-MM-dd}',
                     '{today:yyyy-MM-dd}', 'T4 seed', 'THB', 1, 100, 100, 'POSTED', now(), 1,
                     false, now(), now(), 0)", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<List<string>> SnapshotAsync()
        {
            var list = new List<string>();
            await using var cmd = new NpgsqlCommand(
                $"SELECT doc_no FROM gl.journal_entries WHERE company_id={t.CompanyId} ORDER BY doc_no", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        var before = await SnapshotAsync();

        await ExecAsync(conn, AppRoleGrants);
        var sql = await ReadScriptAsync("634_reconcile_number_sequences_company_wide.sql");
        await RunUnderAppRoleAsync(conn, sql);

        var after = await SnapshotAsync();
        after.Should().Equal(before, "the reconcile touches sys.number_sequences ONLY — no doc_no anywhere may change");
    }

    /// <summary>H1 T5/I3 — a company with TWO branch series (pre-H1-fix shape: some documents minted
    /// under branch 0, some under a real branch) must show NO gap after WP-1+WP-2 collapse the
    /// sequence to company-wide, and the next number issued must be one past the highest number the
    /// company has EVER shown a human (§3.3's "no restart, no gap" claim) — here 5, so the next
    /// allocation must be 6. gl.journal_entries already carries a COMPANY-WIDE unique doc_no index
    /// (one of the 8 "already correct" tables, §1.2), so its own rows can never literally collide —
    /// modelled here as 5 distinct company-wide-unique JV numbers, 3 minted while "branch 0" was the
    /// active counter and 2 more while "branch t.BranchId" was — exactly how the retry-guarded JE
    /// path always behaves pre- and post-fix. The literal cross-branch DUPLICATE condition (the same
    /// STRING minted twice) only exists on the seven branch-scoped tables and is T1/T3's job, not this
    /// one's — this test is scoped to WP-2's arithmetic and the no-gap invariant.</summary>
    [SkippableFact]
    public async Task Company_with_two_branch_series_has_no_gaps_after_collapse()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        string DocNo(int seq) => $"{today:MM}-{today:yyyy}-JV-{seq:D4}";
        // 3 issued while branch 0 was "active", 2 more while t.BranchId was — one continuous
        // company-wide-unique series underneath, exactly as gl.journal_entries already enforces.
        for (var seq = 1; seq <= 5; seq++)
        {
            var branch = seq <= 3 ? 0 : t.BranchId;
            await using var cmd = new NpgsqlCommand($@"
                INSERT INTO gl.journal_entries
                    (company_id, branch_id, prefix_code, doc_no, doc_date, posting_date, description,
                     currency_code, exchange_rate, total_debit, total_credit, status, posted_at, posted_by,
                     is_closing_entry, created_at, updated_at, version)
                VALUES
                    ({t.CompanyId}, {branch}, 'JV', '{DocNo(seq)}', '{today:yyyy-MM-dd}', '{today:yyyy-MM-dd}',
                     'T5 seed', 'THB', 1, 100, 100, 'POSTED', now(), 1,
                     false, now(), now(), 0)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        // The two branch series' OWN counters, as they would have sat pre-collapse.
        await ExecAsync(conn,
            "INSERT INTO sys.number_sequences " +
            "(company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at) " +
            $"VALUES ({t.CompanyId},0,'JV','',{today.Year},{today.Month},3,now())");
        await ExecAsync(conn,
            "INSERT INTO sys.number_sequences " +
            "(company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at) " +
            $"VALUES ({t.CompanyId},{t.BranchId},'JV','',{today.Year},{today.Month},5,now())");

        await ExecAsync(conn, AppRoleGrants);
        var sql = await ReadScriptAsync("634_reconcile_number_sequences_company_wide.sql");
        await RunUnderAppRoleAsync(conn, sql);

        // Post through the REAL (post-WP-1, company-wide) allocator — must land on 6.
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);
        await using var s = sp.CreateAsyncScope();
        var numbers = s.ServiceProvider.GetRequiredService<INumberSequenceService>();
        var next = await numbers.NextAsync(t.CompanyId, "JV", null, today, default);
        next.Sequence.Should().Be(6,
            "the next number must be one past the highest number the company has EVER shown a human, " +
            "not a restart and not a gap");

        // No gap: tax.v_number_gaps must be empty for this company (it already covers gl.journal_entries).
        await using var gapCmd = new NpgsqlCommand(
            $"SELECT count(*) FROM tax.v_number_gaps WHERE company_id={t.CompanyId}", conn);
        var gapCount = Convert.ToInt64(await gapCmd.ExecuteScalarAsync());
        gapCount.Should().Be(0, "1..max must be fully populated after the collapse — no new gaps");
    }

    /// <summary>Tier-2 FIX 2 (specs/fix-duplicate-tax-doc-numbers.md, F21/troubles-wiki 22003) — a
    /// garbage doc_no with an 11+ digit trailing run overflows int4 on the seq_str::int cast, rolls
    /// the WHOLE script back, and (because a failed script is never recorded in
    /// sys.applied_sql_scripts) makes it retry on EVERY boot. Proves the length(seq_str) &lt;= 9 guard
    /// actually fires two ways: (1) a bucket with a legitimate sibling excludes the garbage row from
    /// MAX(seq) without corrupting the true max; (2) a bucket that is ENTIRELY garbage (no legitimate
    /// sibling at all) must not surface as max_seq = NULL — sys.number_sequences.current_value is
    /// NOT NULL, so that would fail the INSERT with 23502 instead of 22003, the same
    /// never-recorded/retries-every-boot failure mode by a different SQLSTATE.</summary>
    [SkippableFact]
    public async Task Script634_survives_a_garbage_doc_no_with_an_overlong_digit_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // JV bucket: one legitimate doc (true max should end up here) plus one garbage doc whose
        // trailing digit run is 20 characters — shape-valid (MM-YYYY-PREFIX-digits) so it reaches
        // the cast, but far past both int4 (10 digits) and even bigint (19 digits) range.
        // QT bucket: garbage ONLY, no legitimate sibling at all — the all-NULL-bucket case.
        // Seeded as DRAFT, not POSTED (626/634's raw_docs CTE has no status filter, so a DRAFT row
        // is picked up identically): troubles-wiki's 22003 entry documents that a POSTED doc_no is
        // a PERMANENT pollution of the shared teas_test DB (020_journal_immutability.sql blocks
        // both UPDATE of doc_no and DELETE once status <> 'DRAFT') — the earlier draft of this test
        // learned that the hard way. DRAFT keeps the finally-block cleanup below a normal DELETE.
        var legitDocNo = $"{today:MM}-{today:yyyy}-JV-0001";
        var garbageJvDocNo = $"{today:MM}-{today:yyyy}-JV-12345678901234567890";
        var garbageQtDocNo = $"{today:MM}-{today:yyyy}-QT-98765432109876543210";
        foreach (var (docNo, prefix, branch) in new[]
        {
            (legitDocNo, "JV", 0),
            (garbageJvDocNo, "JV", t.BranchId),
            (garbageQtDocNo, "QT", 0),
        })
        {
            await using var cmd = new NpgsqlCommand($@"
                INSERT INTO gl.journal_entries
                    (company_id, branch_id, prefix_code, doc_no, doc_date, posting_date, description,
                     currency_code, exchange_rate, total_debit, total_credit, status,
                     is_closing_entry, created_at, updated_at, version)
                VALUES
                    ({t.CompanyId}, {branch}, '{prefix}', '{docNo}', '{today:yyyy-MM-dd}', '{today:yyyy-MM-dd}',
                     'overflow-guard seed', 'THB', 1, 100, 100, 'DRAFT',
                     false, now(), now(), 0)", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await ExecAsync(conn, AppRoleGrants);
        var sql = await ReadScriptAsync("634_reconcile_number_sequences_company_wide.sql");

        try
        {
            // Must complete without a 22003 (integer out of range) or 23502 (not-null violation)
            // exception.
            var act = async () => await RunUnderAppRoleAsync(conn, sql);
            await act.Should().NotThrowAsync("the length(seq_str) <= 9 guard must exclude every " +
                "garbage row BEFORE the int cast — including an entirely-garbage bucket, which " +
                "must emit no row rather than a NULL current_value");

            var currentValue = await ScalarAsync(conn,
                $"SELECT current_value FROM sys.number_sequences WHERE company_id={t.CompanyId} " +
                $"AND branch_id=0 AND prefix_code='JV' AND sub_prefix='' " +
                $"AND period_year={today.Year} AND period_month={today.Month}");
            currentValue.Should().Be(1, "the garbage row must be excluded from MAX(seq) entirely — " +
                "the bucket lands on the legitimate row's true max (1), not on the garbage digit run");

            await using var qtCmd = new NpgsqlCommand(
                $"SELECT count(*) FROM sys.number_sequences WHERE company_id={t.CompanyId} " +
                $"AND prefix_code='QT' AND period_year={today.Year} AND period_month={today.Month}", conn);
            var qtRows = Convert.ToInt64(await qtCmd.ExecuteScalarAsync());
            qtRows.Should().Be(0, "an entirely-garbage bucket (no legitimate sibling) must emit " +
                "NO row at all — never a row with a NULL/garbage current_value");
        }
        finally
        {
            // teas_test is long-lived and shared (troubles-wiki: "apply-once, bloated ~629
            // companies") — 626_reconcile_number_sequences.sql is untouched/unguarded (out of
            // scope, "do not edit 626") and scans EVERY company's gl.journal_entries on every run.
            // Leaving this garbage doc_no behind would poison every future run of 626 (and 634)
            // against this DB, in this test class and any other, with the exact 22003 this test
            // exists to catch. Clean up regardless of pass/fail.
            await ExecAsync(conn,
                $"DELETE FROM gl.journal_entries WHERE company_id={t.CompanyId} " +
                $"AND doc_no IN ('{legitDocNo}', '{garbageJvDocNo}', '{garbageQtDocNo}')");
        }
    }
}
