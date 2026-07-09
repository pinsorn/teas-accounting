using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Bank;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md T8) — DB-level RLS backstop on the three
/// bank.* tables (614_bank_reconciliation_rls.sql). Mirrors SalesChainRlsTests: the `accounting`
/// login this suite runs as has rolbypassrls=true, so a normal connection (and the EF global
/// query filter) would be a FALSE-GREEN. SET ROLE pg_database_owner exercises the real DB policy.
/// Each assertion is two-directional: company A's own row IS visible, company B's is NOT.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BankReconciliationRlsTests
{
    private readonly PostgresFixture _fx;
    public BankReconciliationRlsTests(PostgresFixture fx) => _fx = fx;

    private async Task<int> SeedBankAccountAsync(string connectionString, TestCompanyFactory.SeededCompany co)
    {
        var sp = TestCompanyFactory.BuildProvider(connectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var bankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", $"999-9-{co.CompanyId}-9", null, null, null, "THB"), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var import = new StatementImport
        {
            CompanyId = co.CompanyId, BankAccountId = bankAccountId, AdapterCode = "TEST",
            SourceFileName = "rls-test.csv", PeriodStart = DateOnly.FromDateTime(DateTime.Today),
            PeriodEnd = DateOnly.FromDateTime(DateTime.Today), OpeningBalance = 0m, ClosingBalance = 0m,
            LineCount = 1, Status = ImportStatus.Parsed, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = 1,
        };
        db.StatementImports.Add(import);
        await db.SaveChangesAsync();

        db.StatementLines.Add(new StatementLine
        {
            CompanyId = co.CompanyId, StatementImportId = import.StatementImportId, BankAccountId = bankAccountId,
            LineNo = 1, TxnDate = DateOnly.FromDateTime(DateTime.Today), Direction = StatementDirection.MoneyIn,
            Amount = 1m, RunningBalance = 1m, Channel = "TEST", TxnType = "TEST", Description = "rls test",
            MatchStatus = MatchStatus.Unmatched,
        });
        await db.SaveChangesAsync();

        return bankAccountId;
    }

    [SkippableTheory]
    [InlineData("bank_accounts", "bank_account_id")]
    [InlineData("statement_imports", "statement_import_id")]
    [InlineData("statement_lines", "statement_line_id")]
    public async Task Company_A_cannot_see_company_B_rows_under_the_policy(string table, string pk)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        // Seed real rows via EF (bypass connection, same as the app's own writes) — reuses the
        // already-tested B1/B2 services rather than hand-writing per-table INSERT SQL.
        var bankAccountA = await SeedBankAccountAsync(_fx.ConnectionString, a);
        var bankAccountB = await SeedBankAccountAsync(_fx.ConnectionString, b);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        async Task ExecAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        async Task<long> ScalarAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        await ExecAsync($"GRANT USAGE ON SCHEMA bank TO pg_database_owner; " +
                         $"GRANT SELECT ON bank.{table} TO pg_database_owner;");

        var aPk = await ScalarAsync(
            table == "bank_accounts"
                ? $"SELECT {pk} FROM bank.{table} WHERE company_id = {a.CompanyId} AND bank_account_id = {bankAccountA}"
                : $"SELECT {pk} FROM bank.{table} WHERE company_id = {a.CompanyId} ORDER BY {pk} LIMIT 1");
        var bPk = await ScalarAsync(
            table == "bank_accounts"
                ? $"SELECT {pk} FROM bank.{table} WHERE company_id = {b.CompanyId} AND bank_account_id = {bankAccountB}"
                : $"SELECT {pk} FROM bank.{table} WHERE company_id = {b.CompanyId} ORDER BY {pk} LIMIT 1");

        try
        {
            await ExecAsync("SET ROLE pg_database_owner");
            await ExecAsync(
                $"SELECT set_config('app.company_id', '{a.CompanyId}', false), " +
                "set_config('app.is_super_admin', 'false', false)");

            var ownVisible = await ScalarAsync($"SELECT count(*) FROM bank.{table} WHERE {pk} = {aPk}");
            ownVisible.Should().Be(1, $"company A must see its own {table} row (grant + tenant pin live)");

            var foreignVisible = await ScalarAsync($"SELECT count(*) FROM bank.{table} WHERE {pk} = {bPk}");
            foreignVisible.Should().Be(0, $"the RLS policy must hide company B's {table} row from company A");
        }
        finally
        {
            await ExecAsync("RESET ROLE");
        }
    }
}
