using Accounting.Api.Scheduling;
using Accounting.Api.Tenancy;
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

namespace Accounting.Api.Tests.Scheduling;

/// <summary>
/// H2 (review 2026-07-04) — the (now-deleted) separate <c>Accounting.Workers</c> host
/// registered NO <c>ITenantContext</c> anywhere, so <see cref="VatRegisterSnapshotJob"/>'s EF
/// query filter silently no-op'd: under the suite's bypass-role connection it would blend every
/// company's VAT; under prod NOBYPASSRLS the fail-closed <c>company_isolation</c> policy on
/// <c>sales.tax_invoices</c> (040_tax_invoice_immutability.sql) returns ZERO rows instead. This
/// test exercises the fix's riskiest piece (Codex flaw #3):
/// <see cref="VatRegisterSnapshotJob.RunSnapshotAsync"/>'s LOCAL-in-transaction
/// <c>set_config('app.company_id', …, true)</c> pin, the same pattern as
/// <c>PermissionLookup.cs</c>/<c>ApiKeyResolverRlsTests</c>. <c>SET ROLE pg_database_owner</c>
/// (rolbypassrls=false — the repo's non-bypass-role trick, see
/// <see cref="SalesChainRlsTests"/>/<see cref="ReviewHardeningRlsTests"/>)
/// makes RLS actually enforced, so a false-green via the suite's superuser bypass connection is
/// impossible here. Two companies each get one POSTED tax invoice with DISTINCT amounts —
/// A: 100/7, B: 300/21 — so A-only, B-only, blended (400/28), and fail-closed-zero (0/0) are
/// all distinguishable outcomes. (Tier-2 Codex correction 2026-07-04: identical A/B amounts
/// could not tell "isolated to A" from "isolated to the WRONG company, B" apart — distinct
/// values close that gap.)
///
/// Move-jobs-to-api (2026-07-04): moved from the deleted <c>Accounting.Workers</c> project
/// (the job is now <c>Accounting.Api.Scheduling.VatRegisterSnapshotJob</c>) and the retired
/// <c>WorkerTenantContext</c> replaced by <see cref="AmbientTenantContext"/>'s settable
/// fallback (used here with no <c>IHttpContextAccessor.HttpContext</c> set, exactly the
/// condition a real background-job DI scope is in).
///
/// Tier-2 (Codex) review correction 2026-07-04 (DEFECT — RLS bypass via leftover session flag):
/// the <c>company_isolation</c> policy is <c>company_id = current_setting('app.company_id') OR
/// is_super_admin</c>. Pinning only <c>app.company_id</c> left a pre-existing
/// <c>app.is_super_admin</c> leftover on the pooled connection (e.g. from the known L4
/// <c>TenantMiddleware</c> reset-failure gap) free to satisfy the policy's bypass arm regardless
/// of the company pin. This test now deliberately sets <c>app.is_super_admin='true'</c>
/// SESSION-scoped BEFORE calling <see cref="VatRegisterSnapshotJob.RunSnapshotAsync"/> —
/// simulating exactly that leftover — and asserts isolation still holds (proving
/// <c>RunSnapshotAsync</c>'s own LOCAL <c>is_super_admin='false'</c> pin, added alongside the
/// company_id pin, shields the job's queries from it).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class VatRegisterSnapshotJobRlsTests
{
    private readonly PostgresFixture _fx;
    public VatRegisterSnapshotJobRlsTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Mirrors the H2 registration in Api/Program.cs exactly: AmbientTenantContext
    /// scoped + ITenantContext mapped to the SAME scoped instance. No HttpContext is ever set
    /// on the accessor here, so AmbientTenantContext resolves in its fallback (job) mode.</summary>
    private static ServiceProvider BuildJobProvider(string connectionString)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddInfrastructure(cfg);
        s.AddHttpContextAccessor();
        s.AddScoped<AmbientTenantContext>();
        s.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
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

        await using var sp = BuildJobProvider(_fx.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        // Exactly what the job's per-company loop does: resolve the scope's AmbientTenantContext
        // (no HttpContext bound anywhere in this provider, so it's in fallback/job mode) and pin
        // it to the ONE company this iteration owns.
        var tenant = scope.ServiceProvider.GetRequiredService<AmbientTenantContext>();
        tenant.SetCompany(a.CompanyId);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var report = scope.ServiceProvider.GetRequiredService<IVatReportService>();

        // Keep ONE physical connection open for the whole block so SET ROLE (session-scoped)
        // sticks across RunSnapshotAsync's own internal transaction — same technique as
        // ApiKeyResolverRlsTests.
        await db.Database.OpenConnectionAsync();
        try
        {
            // Reproduce the exact prod pre-pin condition: RLS enforced (NOBYPASSRLS role),
            // app.company_id UNSET. Tier-2 (Codex) review correction 2026-07-04: instead of
            // clearing app.is_super_admin, deliberately set it to 'true' SESSION-scoped (NOT
            // LOCAL) here — simulating a LEFTOVER super-admin flag left on this pooled
            // connection by some prior request (the known L4 gap: TenantMiddleware's
            // best-effort session reset can fail). The company_isolation RLS policy is
            // `company_id = current_setting('app.company_id') OR is_super_admin`, so this
            // leftover 'true' alone would satisfy the policy's bypass arm for EVERY company
            // unless RunSnapshotAsync's own LOCAL transaction pin also forces is_super_admin
            // back to 'false' for its own queries (the fix below).
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', '', false)");
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.is_super_admin', 'true', false)");
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var summary = await VatRegisterSnapshotJob.RunSnapshotAsync(
                db, report, a.CompanyId, today.Year, today.Month, default);

            // Company A's OWN, SPECIFIC figures (100/7) — not just "non-zero" or "not-doubled".
            // Distinct A/B amounts mean this also rules out the "isolated to the WRONG company"
            // failure mode: if the pin instead pinned to B (or ignored the requested company),
            // this would read 300/21 and fail here. With the SESSION-level is_super_admin
            // leftover simulated above, this ALSO proves the fix: without RunSnapshotAsync's own
            // LOCAL is_super_admin='false' pin, the leftover 'true' would satisfy the RLS
            // policy's bypass arm and blend in EVERY company (B included) regardless of the
            // company_id pin — this assertion would then see 400.00/28.00 (A+B blended), not
            // A's isolated 100.00/7.00.
            summary.Sales.Should().Be(100.00m, "this must be company A's own TI amount");
            summary.OutputVat.Should().Be(7.00m, "this must be company A's own tax amount");

            // Explicitly rule out B's distinct figures leaking in (blend or wrong-company swap).
            summary.Sales.Should().NotBe(300.00m, "company B's distinct Sales figure must not appear in A's snapshot");
            summary.OutputVat.Should().NotBe(21.00m, "company B's distinct OutputVat figure must not appear in A's snapshot");

            // The leftover flag was SESSION-scoped (is_local=false); RunSnapshotAsync's own pin
            // is LOCAL (auto-reverts at its transaction's commit), so the pre-existing leftover
            // must still read 'true' here, proving the fix is a per-transaction override, not a
            // permanent session-level change that would just mask the leftover elsewhere.
            var stillLeftover = (string?)await db.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.is_super_admin', true) AS \"Value\"").SingleAsync();
            stillLeftover.Should().Be("true", "the SESSION-scoped leftover must survive the job's own LOCAL-scoped override");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Tier-2 (Codex) review correction 2026-07-04 — the test above proves the JOB's real
    /// end-to-end isolation, but its assertions go through <see cref="IVatReportService"/>, whose
    /// queries are ALSO independently restricted by EF's own C# global query filter
    /// (<see cref="AmbientTenantContext.IsSuperAdmin"/> is hardcoded <c>false</c> in job/fallback
    /// mode, so <c>e.CompanyId == tenant.CompanyId</c> is baked into the generated SQL regardless
    /// of what RLS's session-level GUCs say) — so it can't by itself prove that
    /// <see cref="VatRegisterSnapshotJob.RunSnapshotAsync"/>'s OWN pin actually includes
    /// <c>app.is_super_admin='false'</c> (a future edit could silently drop it and this file's
    /// other test would still pass, masking the regression). This test drives
    /// <c>RunSnapshotAsync</c> directly with a spy <see cref="IVatReportService"/> that captures
    /// the live GUC values at the exact moment the job's query would run — deterministic, no
    /// seeded companies/RLS role-switch needed, and it WOULD fail if the is_super_admin pin were
    /// ever removed from the production method (verified: reverting the source fix and re-running
    /// this test makes it fail with <c>CapturedIsSuperAdmin == "true"</c>).
    /// </summary>
    [SkippableFact]
    public async Task RunSnapshotAsync_pins_is_super_admin_false_for_the_duration_of_its_own_query()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = BuildJobProvider(_fx.ConnectionString);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var spy = new GucCapturingReportService(db);

        await db.Database.OpenConnectionAsync();
        try
        {
            // Simulate the exact leftover this defect is about: some PRIOR request/tx left
            // is_super_admin='true' on this pooled connection (SESSION-scoped, not LOCAL).
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.is_super_admin', 'true', false)");

            await VatRegisterSnapshotJob.RunSnapshotAsync(db, spy, companyId: 999999, year: 2026, month: 1, default);

            spy.CapturedIsSuperAdmin.Should().Be("false",
                "RunSnapshotAsync must force is_super_admin='false' for its own query window, " +
                "regardless of a leftover 'true' on the pooled connection (Tier-2 Codex defect)");
            spy.CapturedCompanyId.Should().Be("999999", "the company_id pin must still carry the requested company");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.is_super_admin', 'false', false)");
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Captures the live <c>app.company_id</c>/<c>app.is_super_admin</c> GUCs at the
    /// moment <see cref="VatRegisterSnapshotJob.RunSnapshotAsync"/> calls
    /// <see cref="IVatReportService.GetPnd30Async"/> — lets the test above observe
    /// <c>RunSnapshotAsync</c>'s own pin directly, decoupled from <c>VatReportService</c>'s
    /// (irrelevant here) EF query-filter behavior.</summary>
    private sealed class GucCapturingReportService(AccountingDbContext db) : IVatReportService
    {
        public string? CapturedCompanyId;
        public string? CapturedIsSuperAdmin;

        public async Task<Pnd30Summary> GetPnd30Async(int year, int month, CancellationToken ct, int? businessUnitId = null)
        {
            CapturedCompanyId = (string?)await db.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.company_id', true) AS \"Value\"").SingleAsync(ct);
            CapturedIsSuperAdmin = (string?)await db.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.is_super_admin', true) AS \"Value\"").SingleAsync(ct);
            return new Pnd30Summary(year, month, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        public Task<VatRegisterPeriod> GetRegisterAsync(int year, int month, CancellationToken ct, int? businessUnitId = null)
            => throw new NotSupportedException("Not exercised by RunSnapshotAsync's pin-capture test.");
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
