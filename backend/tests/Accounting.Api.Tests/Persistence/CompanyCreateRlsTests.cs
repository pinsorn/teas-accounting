using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Audit;
using Accounting.Application.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Master;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// specs/fix-company-create-rls-atomic.md — <see cref="CompanyService.CreateAsync"/> writes rows
/// FOR the brand-new company (branch "00000", company_profile, WHT types, CoA, tax codes,
/// expense categories, <c>sys.seed_company_roles</c>) while the caller's DB session is pinned to
/// the CALLER'S OWN (existing) company. <c>600_superadmin_scoped_rls.sql</c> (2026-07-08) retired
/// the <c>is_super_admin</c> data-scope arm from every <c>company_isolation</c> policy, so every
/// one of those inserts now 42501s unless <c>CreateAsync</c> itself re-pins
/// <c>app.company_id</c> LOCAL to the brand-new id inside its own transaction — the same idiom
/// already proven at <c>VatRegisterSnapshotJob.cs:95-98</c>.
///
/// Reproduces the exact session shape <c>TenantMiddleware</c> pins for an authenticated caller:
/// <c>SET ROLE pg_database_owner</c> (the portable NON-bypass trick — <c>teas_rls_test</c> SKIPs
/// in this environment, see <see cref="SuperAdminTenantScopeRlsTests"/>) + <c>app.company_id</c>
/// = the caller's OWN company, SESSION-scoped (<c>is_local=false</c>), then calls the REAL
/// <see cref="ICompanyService.CreateAsync"/> for a brand-new company on that exact
/// connection/session (not a bypass connection).
///
/// Before the fix (2026-07-18 attempt log): fails with 42501 on <c>master.branches</c> (the
/// first RLS-governed table <c>CreateAsync</c> writes to after <c>master.companies</c>, which
/// carries no RLS at all) and leaves an ORPHAN companies row behind — no wrapping transaction
/// means the first <c>SaveChangesAsync</c> (companies only) commits before the second
/// (branches + company_profile) throws.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyCreateRlsTests
{
    private readonly PostgresFixture _fx;
    public CompanyCreateRlsTests(PostgresFixture fx) => _fx = fx;

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

    /// <summary>CreateAsync never calls IActivityRecorder.Record (that's UpdateAsync only) — a
    /// no-op is enough; wiring a real ActivityRecorder would need its own ITenantContext.</summary>
    private sealed class NoopActivityRecorder : IActivityRecorder
    {
        public void Record(
            string entityType, long entityId, string? docNo, int companyId,
            string action, string? fromStatus = null, string? toStatus = null,
            string? note = null, string module = "sales")
        { }
    }

    [SkippableFact]
    public async Task CreateAsync_seeds_new_tenant_while_session_pinned_to_callers_own_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Caller's OWN existing company — created on the bypass (superuser) connection, exactly
        // like every other fixture-seeded company in this suite (TestCompanyFactory itself calls
        // the real ICompanyService.CreateAsync, unaffected by RLS on that connection).
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();

        // pg_database_owner starts with ZERO privileges (it's an empty pseudo-role Postgres
        // provides for admins to grant into) — grant exactly the schemas CreateAsync's seeding
        // touches, issued on the bypass role BEFORE switching (same ordering as
        // SuperAdminTenantScopeRlsTests). Table-level GRANTs do NOT bypass RLS — the
        // company_isolation policies below still gate every row by app.company_id.
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA master, sys, tax TO pg_database_owner; " +
            "GRANT ALL ON ALL TABLES IN SCHEMA master, sys, tax TO pg_database_owner; " +
            "GRANT ALL ON ALL SEQUENCES IN SCHEMA master, sys, tax TO pg_database_owner; " +
            "GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA sys TO pg_database_owner;");

        var before = await ScalarAsync(c, "SELECT count(*) FROM master.companies");

        var taxId = TestIds.TaxId();
        var nameTh = $"บริษัทใหม่ {TestIds.Suffix()}";
        var req = new CreateCompanyRequest(
            TaxId: taxId, NameTh: nameTh, NameEn: null,
            LegalEntityType: LegalEntityType.LimitedCompany,
            RegistrationDate: null, VatRegistered: true, VatRegisterDate: new DateOnly(2024, 1, 1),
            FiscalYearStartMonth: 1,
            AddressTh: null, SubDistrict: "x", District: "y",
            Province: "กรุงเทพมหานคร", PostalCode: "10110", Phone: null, Email: null,
            PaidUpCapital: null, VatRate: 0.07m, Pnd30SubmissionMode: "manual");

        int newCompanyId = 0;
        Exception? thrown = null;
        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            // Exactly what TenantMiddleware pins for an authenticated caller: app.company_id =
            // their OWN company, SESSION-scoped (is_local=false) — matching a real
            // HTTP-request-scoped connection, NOT the id of the company being created.
            await ExecAsync(c, $"SELECT set_config('app.company_id', '{a.CompanyId}', false)");

            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseNpgsql(c).UseSnakeCaseNamingConvention().Options;
            // tenant: null — CreateAsync never relies on the EF-layer query filter (its one
            // read uses IgnoreQueryFilters explicitly); this is the RLS layer under test.
            await using var db = new AccountingDbContext(options, tenant: null);
            var svc = new CompanyService(db, new NoopActivityRecorder());

            newCompanyId = await svc.CreateAsync(req, default);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }

        var after = await ScalarAsync(c, "SELECT count(*) FROM master.companies");
        // Attempt-log evidence, captured regardless of which assertion below fails/passes.
        Console.WriteLine(
            $"[CompanyCreateRlsTests] thrown={thrown} before={before} after={after} " +
            $"delta={after - before} newCompanyId={newCompanyId}");

        thrown.Should().BeNull(
            "CreateAsync must succeed while the session is pinned to the caller's OWN " +
            $"company, not the new one (was: {thrown})");
        (after - before).Should().Be(1,
            "the fix wraps every insert in one transaction — no orphan/dup company row " +
            "regardless of outcome");

        // Read-back on the plain bypass connection — RLS was already exercised above on the
        // WRITE path; this just verifies the seeded rows, same style as
        // SuperAdminTenantScopeRlsTests / OnboardingFoundingAddressTests.
        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var rdb = new AccountingDbContext(opts, tenant: null);

        var branches = await rdb.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.CompanyId == newCompanyId).ToListAsync();
        branches.Should().HaveCount(1, "CreateAsync must seed exactly one HQ branch");
        branches.Single().BranchCode.Should().Be("00000");

        var profileExists = await rdb.CompanyProfiles.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(p => p.CompanyId == newCompanyId);
        profileExists.Should().BeTrue("the founding company_profile row (ม.86/4) must be seeded");

        // Every default-seeded set must match company A's — both were created via the IDENTICAL
        // CreateAsync code path moments apart, so their counts must be equal regardless of the
        // exact default lists (spec: 12 tax codes, 19 expense categories, 15 WHT types [13
        // domestic + 2 foreign] — asserted dynamically here instead of hardcoded so this test
        // never drifts from MasterDataServices.cs's own default arrays).
        var coaA = await rdb.ChartOfAccounts.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == a.CompanyId);
        var coaNew = await rdb.ChartOfAccounts.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == newCompanyId);
        coaNew.Should().Be(coaA).And.BeGreaterThan(0, "full default chart of accounts must be seeded");

        var taxA = await rdb.TaxCodes.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == a.CompanyId);
        var taxNew = await rdb.TaxCodes.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == newCompanyId);
        taxNew.Should().Be(taxA).And.Be(12, "12 default VAT tax codes (spec)");

        var whtA = await rdb.WhtTypes.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == a.CompanyId);
        var whtNew = await rdb.WhtTypes.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == newCompanyId);
        whtNew.Should().Be(whtA).And.BeGreaterThan(0, "default WHT types must be seeded");

        var expA = await rdb.ExpenseCategories.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == a.CompanyId);
        var expNew = await rdb.ExpenseCategories.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == newCompanyId);
        expNew.Should().Be(expA).And.Be(19, "19 default expense categories (spec)");

        // sys.seed_company_roles(newCompanyId) fan-out — at least one role cloned.
        var rolesNew = await rdb.Set<Accounting.Domain.Entities.Identity.Role>()
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(r => r.CompanyId == newCompanyId);
        rolesNew.Should().BeGreaterThan(0, "sys.seed_company_roles must clone the template role set");
    }
}
