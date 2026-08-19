using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
}
