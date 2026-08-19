using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// specs/fix-r2-u6-employee-lookup.md (L4-1) — 640_seed_employee_lookup_perm.sql grants
/// master.employee.lookup to every role that holds expense.claim.create (derived dynamically,
/// not hardcoded), so the claim form's Employee picker (GET /employees/lookup) stops 403-ing for
/// ACCOUNTANT/CHIEF_ACCOUNTANT/COMPANY_ADMIN without loosening the payroll-sensitive
/// /employees group (still gated by master.employee.manage). Mirrors
/// ReadManageSplitGrantTests / FixedAssetPermissionTests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class EmployeeLookupGrantTests
{
    private readonly PostgresFixture _fx;
    public EmployeeLookupGrantTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    /// <summary>Real grants for a role in the given company — read straight from
    /// sys.role_permissions (the EFFECTIVE grants), not hand-typed, so a token issued from this
    /// set can only pass 200 if 640's seed actually landed the grant.</summary>
    private static async Task<List<string>> RolePermissionsAsync(AccountingDbContext db, int companyId, string roleCode) =>
        await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.Role!.CompanyId == companyId && rp.Role.RoleCode == roleCode)
            .Select(rp => rp.Permission!.PermissionCode)
            .ToListAsync();

    [SkippableFact]
    public async Task Accountant_shaped_token_gets_200_on_lookup_but_stays_403_on_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var accountantPerms = await RolePermissionsAsync(db, co.CompanyId, "ACCOUNTANT");
        accountantPerms.Should().Contain(Permissions.Master.EmployeeLookup,
            "640's per-company sync must have granted lookup to this freshly-cloned company's ACCOUNTANT role");
        accountantPerms.Should().NotContain(Permissions.Master.EmployeeManage,
            "sanity — ACCOUNTANT must NOT hold the full manage grant (that's the whole point of L4-1)");

        var token = Issuer().Issue(new TokenClaims(
            UserId: 1, Username: "rbac-accountant-lookup", CompanyId: co.CompanyId, BranchId: co.BranchId,
            IsSuperAdmin: false, Roles: ["ACCOUNTANT"], Permissions: accountantPerms)).Token;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var lookupReq = new HttpRequestMessage(HttpMethod.Get, "/employees/lookup");
        lookupReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var lookupResp = await client.SendAsync(lookupReq);
        lookupResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "ACCOUNTANT holds expense.claim.create -> 640 grants master.employee.lookup -> the claim form's picker must work");

        using var manageReq = new HttpRequestMessage(HttpMethod.Get, "/employees");
        manageReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var manageResp = await client.SendAsync(manageReq);
        manageResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the full payroll-data /employees group must stay gated by EmployeeManage — L4-1 never loosens it");
    }

    [SkippableFact]
    public async Task Lookup_response_never_leaks_salary_national_id_or_bank_fields()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var empSvc = seedScope.ServiceProvider.GetRequiredService<IEmployeeService>();
            await empSvc.CreateAsync(new CreateEmployeeRequest(
                "EMP-L4-1", "นาย", "ทดสอบ", "การรั่วไหล", null, null, null,
                "1103700000001", null, null,
                new DateOnly(2020, 1, 1), null, 45000m,
                "ธนาคารทดสอบ", "1234567890", "ทดสอบ การรั่วไหล",
                true, "1234567890",
                "SINGLE", false, 0), default);
        }

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var accountantPerms = await RolePermissionsAsync(db, co.CompanyId, "ACCOUNTANT");

        var token = Issuer().Issue(new TokenClaims(
            UserId: 1, Username: "rbac-accountant-dtoleak", CompanyId: co.CompanyId, BranchId: co.BranchId,
            IsSuperAdmin: false, Roles: ["ACCOUNTANT"], Permissions: accountantPerms)).Token;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/employees/lookup");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var items = json.RootElement.EnumerateArray().ToList();
        items.Should().ContainSingle(e => e.GetProperty("employeeCode").GetString() == "EMP-L4-1");

        var forbiddenKeys = new[]
        {
            "nationalId", "national_id", "baseSalary", "base_salary", "bankName", "bank_name",
            "bankAccountNo", "bank_account_no", "bankAccountName", "bank_account_name",
            "ssoNumber", "sso_number", "taxId", "tax_id", "hireDate", "hire_date",
        };
        foreach (var e in items)
        {
            var keys = e.EnumerateObject().Select(p => p.Name).ToList();
            keys.Should().BeEquivalentTo(["employeeId", "employeeCode", "fullNameTh"],
                "the lookup DTO must carry ONLY employeeId/employeeCode/fullNameTh — nothing payroll-sensitive");
            foreach (var forbidden in forbiddenKeys)
                keys.Should().NotContain(forbidden, $"'{forbidden}' must never appear in the lookup response (L4-1 DTO leak guard)");
        }
    }

    [SkippableFact]
    public async Task Every_template_role_holding_expense_claim_create_also_holds_employee_lookup()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var claimCreateHolders = await db.Database.SqlQueryRaw<string>(
                "SELECT role_code AS \"Value\" FROM sys.role_permission_templates " +
                "WHERE permission_code = 'expense.claim.create'")
            .ToListAsync();
        claimCreateHolders.Should().Contain(["ACCOUNTANT", "CHIEF_ACCOUNTANT", "COMPANY_ADMIN"],
            "sanity — these are the 3 roles 617's template grants expense.claim.create to");

        var lookupHolders = (await db.Database.SqlQueryRaw<string>(
                "SELECT role_code AS \"Value\" FROM sys.role_permission_templates " +
                "WHERE permission_code = 'master.employee.lookup'")
            .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        claimCreateHolders.Should().BeSubsetOf(lookupHolders,
            "every role holding expense.claim.create in the template must also hold master.employee.lookup (640's dynamic derivation)");
    }

    /// <summary>
    /// fix-c1-backend-cleanup item 3 (r2 Tier-2 N2) — 640's step 4 (the direct-grant regression
    /// guard, mirroring 629's step 5b / Opus Tier-2 F1) has NEVER fired in this suite: every
    /// existing employee-lookup test uses a TEMPLATE-cloned system role (ACCOUNTANT etc.), which
    /// steps 2/3 already cover. A role holding expense.claim.create ONLY via a direct
    /// sys.role_permissions grant (RbacAdminService.SetRolePermissionsAsync on a
    /// CreateRoleAsync-minted custom role — no sys.role_permission_templates row at all, since
    /// custom role codes never match a template row) is the one shape steps 2/3 cannot see.
    /// Mirrors ReadManageSplitGrantTests.Custom_role_with_a_direct_manage_grant_and_no_template_row_resolves_read_after_629_replay
    /// exactly (same real-service setup, same per-company-loop scoping rationale — teas_test
    /// has grown to 9,000+ companies from repeated test runs; an unscoped full-file replay of
    /// 640's `FOR c IN SELECT company_id FROM master.companies LOOP` was avoided here for the
    /// identical reason that test documents, not re-measured separately). Also proves the
    /// SECOND replay is idempotent (no 23505) — the direct-grant INSERT is itself guarded by a
    /// NOT EXISTS, so a double-run must be silent, exactly like steps 1-3.
    /// </summary>
    [SkippableFact]
    public async Task Custom_role_with_a_direct_expense_claim_create_grant_resolves_employee_lookup_after_640_replay()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
        }).Build();
        await using var sp = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = co.CompanyId, BranchId = 0, UserId = 1, IsSuperAdmin = false,
            })
            .BuildServiceProvider();
        var rbac = sp.GetRequiredService<IRbacAdminService>();

        // 1. CreateRoleAsync's custom-role path — a brand-new role_code that has NEVER existed
        //    in sys.role_permission_templates.
        var roleCode = "C1_U9_" + TestIds.Suffix().ToUpperInvariant();
        int roleId;
        try
        {
            roleId = await rbac.CreateRoleAsync(
                new CreateRoleRequest(roleCode, "C1 item 3 direct-grant regression test", null, null), default);

            // 2. SetRolePermissionsAsync's direct-grant path — writes straight to
            //    sys.role_permissions, never touching sys.role_permission_templates.
            await rbac.SetRolePermissionsAsync(roleId,
                new SetRolePermissionsRequest([Permissions.Expense.ClaimCreate]), default);

            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

            // Sanity — confirm the setup actually reproduces step 4's gap before asserting the fix.
            var templateRows = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permission_templates WHERE role_code = {0}",
                    roleCode)
                .FirstAsync();
            templateRows.Should().Be(0, "sanity: this is a brand-new custom role, never in the template");

            var lookupBefore = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permissions rp " +
                    "JOIN sys.permissions p ON p.permission_id = rp.permission_id " +
                    "WHERE rp.role_id = {0} AND p.permission_code = 'master.employee.lookup'", roleId)
                .FirstAsync();
            lookupBefore.Should().Be(0, "sanity: the custom role must start without the lookup grant");

            // 3. Read the ACTUAL 640 SQL from disk and scope its per-company loop to just this
            //    test's company (mirrors ReadManageSplitGrantTests — teas_test's company count
            //    makes the unscoped full-file loop needlessly slow for a unit test).
            var sqlPath = Path.Combine(RbacTestPaths.RepoRoot(),
                "backend", "src", "Accounting.Infrastructure", "Migrations", "SqlScripts",
                "640_seed_employee_lookup_perm.sql");
            File.Exists(sqlPath).Should().BeTrue($"640 seed script must exist at {sqlPath}");
            var fileSql = await File.ReadAllTextAsync(sqlPath);

            const string loopAnchor = "FOR c IN SELECT company_id FROM master.companies LOOP";
            fileSql.Should().Contain(loopAnchor,
                "640's per-company loop anchor text changed — update this test's scoping "
                + "substitution to match, or it would silently fall back to the full, very slow loop");
            var scopedSql = fileSql.Replace(loopAnchor,
                $"FOR c IN SELECT company_id FROM master.companies WHERE company_id = {co.CompanyId} LOOP");

            db.Database.SetCommandTimeout(30);
            await db.Database.ExecuteSqlRawAsync(scopedSql);

            // 4. The custom role must now resolve master.employee.lookup via step 4's direct-grant guard.
            var lookupAfter = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permissions rp " +
                    "JOIN sys.permissions p ON p.permission_id = rp.permission_id " +
                    "WHERE rp.role_id = {0} AND p.permission_code = 'master.employee.lookup'", roleId)
                .FirstAsync();
            lookupAfter.Should().BeGreaterThan(0,
                "640 step 4: a role holding expense.claim.create ONLY via a direct sys.role_permissions "
                + "grant (no template row) must still resolve master.employee.lookup after the replay");

            // 5. Idempotency — a second replay must not raise 23505 (the direct-grant INSERT's own
            //    NOT EXISTS guard, exactly mirroring steps 1-3's ON CONFLICT/NOT EXISTS guards).
            var secondRun = async () => await db.Database.ExecuteSqlRawAsync(scopedSql);
            await secondRun.Should().NotThrowAsync("640's step 4 INSERT is NOT EXISTS-guarded; a second "
                + "replay over the same company/role must be a silent no-op, not a 23505 unique violation");
        }
        finally
        {
            // Cleanup — a synthetic role must not linger in the shared teas_test.
            await using var cleanupScope = sp.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await cleanupDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM sys.role_permissions WHERE role_id IN " +
                "(SELECT role_id FROM sys.roles WHERE role_code = {0})", roleCode);
            await cleanupDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM sys.roles WHERE role_code = {0}", roleCode);
        }
    }
}
