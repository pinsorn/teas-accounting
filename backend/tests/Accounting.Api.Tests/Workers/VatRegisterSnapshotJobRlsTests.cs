// Aliased (see .csproj) — both Accounting.Api and Accounting.Workers have top-level-statement
// Program.cs, which the compiler places in the global namespace; an unaliased reference here
// would make the global `Program` ambiguous for the existing WebApplicationFactory<Program>
// usages elsewhere in this test project.
extern alias Workers;

using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Persistence;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Workers::Accounting.Workers.Jobs;
using Workers::Accounting.Workers.Tenancy;

namespace Accounting.Api.Tests.Workers;

/// <summary>
/// H2 (review 2026-07-04) — <c>Accounting.Workers</c> registered NO <c>ITenantContext</c>
/// anywhere, so <see cref="VatRegisterSnapshotJob"/>'s EF query filter silently no-op'd: under
/// the suite's bypass-role connection it would blend every company's VAT; under prod
/// NOBYPASSRLS the fail-closed <c>company_isolation</c> policy on <c>sales.tax_invoices</c>
/// (040_tax_invoice_immutability.sql) returns ZERO rows instead. This test exercises the fix's
/// riskiest piece (Codex flaw #3): <see cref="VatRegisterSnapshotJob.RunSnapshotAsync"/>'s
/// LOCAL-in-transaction <c>set_config('app.company_id', …, true)</c> pin, the same pattern as
/// <c>PermissionLookup.cs</c>/<c>ApiKeyResolverRlsTests</c>. <c>SET ROLE pg_database_owner</c>
/// (rolbypassrls=false — the repo's non-bypass-role trick, see
/// <see cref="SalesChainRlsTests"/>/<see cref="ReviewHardeningRlsTests"/>)
/// makes RLS actually enforced, so a false-green via the suite's superuser bypass connection is
/// impossible here. Two companies each get one POSTED tax invoice with DISTINCT amounts —
/// A: 100/7, B: 300/21 — so A-only, B-only, blended (400/28), and fail-closed-zero (0/0) are
/// all distinguishable outcomes. (Tier-2 Codex correction 2026-07-04: identical A/B amounts
/// could not tell "isolated to A" from "isolated to the WRONG company, B" apart — distinct
/// values close that gap.)
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class VatRegisterSnapshotJobRlsTests
{
    private readonly PostgresFixture _fx;
    public VatRegisterSnapshotJobRlsTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Mirrors the H2 registration in Accounting.Workers/Program.cs exactly:
    /// WorkerTenantContext scoped + ITenantContext mapped to the SAME scoped instance.</summary>
    private static ServiceProvider BuildWorkerProvider(string connectionString)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddInfrastructure(cfg);
        s.AddScoped<WorkerTenantContext>();
        s.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());
        return s.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task RunSnapshotAsync_isolates_company_A_from_company_B_under_NOBYPASSRLS()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayStr = today.ToString("yyyy-MM-dd");

        // Seed one POSTED TI per company (bypass connection — accounting owns the tables,
        // same as ReviewHardeningRlsTests/SalesChainRlsTests) with DISTINCT amounts per
        // company (see class doc) — a local helper, not the shared
        // SalesChainRlsTests.InsertMinimalTaxInvoiceAsync (which hardcodes 100/7/107 for every
        // caller), so other RLS tests that depend on those fixed values are untouched.
        await using (var seed = new NpgsqlConnection(_fx.ConnectionString))
        {
            await seed.OpenAsync();
            var aTiId = await InsertTaxInvoiceAsync(
                seed, a.CompanyId, a.BranchId, a.CustomerId, "H2A-" + Guid.NewGuid().ToString("N")[..8], todayStr,
                subtotal: 100m, tax: 7m);
            var bTiId = await InsertTaxInvoiceAsync(
                seed, b.CompanyId, b.BranchId, b.CustomerId, "H2B-" + Guid.NewGuid().ToString("N")[..8], todayStr,
                subtotal: 300m, tax: 21m);

            await using var flip = new NpgsqlCommand(
                $"UPDATE sales.tax_invoices SET status = 'POSTED' WHERE tax_invoice_id IN ({aTiId}, {bTiId})", seed);
            await flip.ExecuteNonQueryAsync();

            // GetRegisterAsync also reads tax_adjustment_notes + purchase.vendor_invoices (both
            // empty for these test companies, but the role still needs USAGE/SELECT to even
            // attempt the query — otherwise it 42501s before RLS gets a chance to filter).
            await using var grant = new NpgsqlCommand(
                "GRANT USAGE ON SCHEMA sales, purchase TO pg_database_owner; " +
                "GRANT SELECT ON sales.tax_invoices TO pg_database_owner; " +
                "GRANT SELECT ON sales.tax_adjustment_notes TO pg_database_owner; " +
                "GRANT SELECT ON purchase.vendor_invoices TO pg_database_owner;", seed);
            await grant.ExecuteNonQueryAsync();
        }

        await using var sp = BuildWorkerProvider(_fx.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        // Exactly what the job's per-company loop does: resolve the scope's WorkerTenantContext
        // and set CompanyId to the ONE company this iteration owns.
        var tenant = scope.ServiceProvider.GetRequiredService<WorkerTenantContext>();
        tenant.CompanyId = a.CompanyId;

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var report = scope.ServiceProvider.GetRequiredService<IVatReportService>();

        // Keep ONE physical connection open for the whole block so SET ROLE (session-scoped)
        // sticks across RunSnapshotAsync's own internal transaction — same technique as
        // ApiKeyResolverRlsTests.
        await db.Database.OpenConnectionAsync();
        try
        {
            // Reproduce the exact prod pre-pin condition: RLS enforced (NOBYPASSRLS role),
            // app.company_id UNSET, app.is_super_admin explicitly cleared (a pooled connection
            // could otherwise retain 'true' from a prior test and let even unfixed code pass
            // via the policy's super-admin bypass clause — false green).
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', '', false)");
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.is_super_admin', 'false', false)");
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var summary = await VatRegisterSnapshotJob.RunSnapshotAsync(
                db, report, a.CompanyId, today.Year, today.Month, default);

            // Company A's OWN, SPECIFIC figures (100/7) — not just "non-zero" or "not-doubled".
            // Distinct A/B amounts mean this also rules out the "isolated to the WRONG company"
            // failure mode: if the pin instead pinned to B (or ignored the requested company),
            // this would read 300/21 and fail here.
            summary.Sales.Should().Be(100.00m, "this must be company A's own TI amount");
            summary.OutputVat.Should().Be(7.00m, "this must be company A's own tax amount");

            // Explicitly rule out B's distinct figures leaking in (blend or wrong-company swap).
            summary.Sales.Should().NotBe(300.00m, "company B's distinct Sales figure must not appear in A's snapshot");
            summary.OutputVat.Should().NotBe(21.00m, "company B's distinct OutputVat figure must not appear in A's snapshot");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Same minimal-POSTED-TI shape as
    /// <see cref="SalesChainRlsTests.InsertMinimalTaxInvoiceAsync"/>, but with a caller-supplied
    /// subtotal/tax so company A and company B can carry DISTINCT amounts (that shared helper
    /// hardcodes 100/7/107 for every caller — kept as-is so other RLS tests are unaffected).</summary>
    private static async Task<long> InsertTaxInvoiceAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId,
        string docNo, string today, decimal subtotal, decimal tax)
    {
        var total = subtotal + tax;
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
                 true, {subtotal},0,{subtotal}, 0,{tax},{total},{total}, false,0,false,false,
                 now(), now(), 0, 0)
            RETURNING tax_invoice_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
