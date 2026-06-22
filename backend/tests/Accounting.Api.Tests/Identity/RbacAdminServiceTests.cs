using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// Sprint 13k — per-company RBAC admin service (Phases A–C). Runs on the shared
/// teas_test DB; every unique value uses TestIds.* and every company is created
/// fresh via TestCompanyFactory (NEVER mutates company 1). §4.7 multi-tenant,
/// §4.8 audit trail.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RbacAdminServiceTests
{
    private readonly PostgresFixture _fx;
    public RbacAdminServiceTests(PostgresFixture fx) => _fx = fx;

    // ---- harness -----------------------------------------------------------

    private ServiceProvider BuildProvider(int companyId, bool isSuperAdmin, long userId = 1)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = companyId, BranchId = 0, UserId = userId, IsSuperAdmin = isSuperAdmin,
                Username = $"actor-{userId}",
            })
            .BuildServiceProvider();
    }

    /// <summary>Insert a fresh user and (optionally) assign roles in a company.
    /// Returns the new user id. Uses a separate scope/provider so the SUT sees
    /// committed rows.</summary>
    private async Task<long> CreateUserWithRolesAsync(int companyId, params int[] roleIds)
    {
        await using var sp = BuildProvider(companyId, isSuperAdmin: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Seed 100/400 insert users with EXPLICIT ids without advancing the identity
        // sequence (mirrors the companies/branches gotcha in TestCompanyFactory) — on a
        // fresh teas_test the first EF-generated user_id would collide (23505). Align it.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('sys.users','user_id'), " +
            "(SELECT COALESCE(MAX(user_id),0)+1 FROM sys.users), false);");

        var user = new User
        {
            Username = "u-" + TestIds.Suffix(),
            Email = TestIds.Email(),
            PasswordHash = "x",
            FullName = TestIds.Name(),
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var rid in roleIds)
            db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId, RoleId = rid, CompanyId = companyId,
                BranchId = 0, ValidFrom = today, ValidTo = null,
            });
        if (roleIds.Length > 0) await db.SaveChangesAsync();
        return user.UserId;
    }

    private async Task<int> SystemRoleIdAsync(int companyId, string roleCode)
    {
        await using var sp = BuildProvider(companyId, isSuperAdmin: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Roles.Where(r => r.CompanyId == companyId && r.RoleCode == roleCode)
            .Select(r => r.RoleId).FirstAsync();
    }

    /// <summary>The first N permission codes actually present in sys.permissions
    /// (the seed the grant validator checks against).</summary>
    private async Task<string[]> SeededPermissionCodesAsync(int n)
    {
        await using var sp = BuildProvider(1, isSuperAdmin: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Permissions.AsNoTracking()
            .OrderBy(p => p.PermissionCode).Take(n)
            .Select(p => p.PermissionCode).ToArrayAsync();
    }

    private async Task<int> SuperAdminRoleIdAsync()
    {
        await using var sp = BuildProvider(1, isSuperAdmin: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Roles.Where(r => r.RoleCode == Role.SystemRoles.SuperAdmin)
            .Select(r => r.RoleId).FirstAsync();
    }

    // ---- Phase A: catalog --------------------------------------------------

    [Fact]
    public void Catalog_count_matches_all_permissions()
    {
        PermissionCatalog.Items.Should().HaveCount(Permissions.All.Count);
        PermissionCatalog.Items.Select(i => i.Code).Should()
            .BeEquivalentTo(Permissions.All);
        PermissionCatalog.Items.Should().OnlyContain(i =>
            !string.IsNullOrWhiteSpace(i.LabelTh) && !string.IsNullOrWhiteSpace(i.LabelEn)
            && !string.IsNullOrWhiteSpace(i.Module));
    }

    // ---- Phase C: anti-lockout — a per-company role edit must NOT drop a user's
    //      system-global SUPER_ADMIN assignment (it supplies their company context). ----

    [Fact]
    public async Task Setting_company_roles_preserves_a_users_global_super_admin_assignment()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var superId = await SuperAdminRoleIdAsync();
        var accountantId = await SystemRoleIdAsync(co.CompanyId, "ACCOUNTANT");
        var chiefId = await SystemRoleIdAsync(co.CompanyId, "CHIEF_ACCOUNTANT");

        // A super-admin user: a global SUPER_ADMIN user_role AND a per-company role,
        // both recorded under company `co` (mirrors seed 130's admin → SUPER_ADMIN row).
        var userId = await CreateUserWithRolesAsync(co.CompanyId, superId, accountantId);

        // Another super-admin (the real seeded admin id=1) edits this user's company roles.
        await using (var sp = BuildProvider(co.CompanyId, isSuperAdmin: true, userId: 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
            await svc.SetUserRolesAsync(userId,
                new SetUserRolesRequest(new[] { chiefId }, co.CompanyId), default);
        }

        await using var vsp = BuildProvider(co.CompanyId, isSuperAdmin: true);
        await using var vscope = vsp.CreateAsyncScope();
        var db = vscope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.CompanyId == co.CompanyId)
            .Select(ur => ur.RoleId).ToListAsync();

        roleIds.Should().Contain(superId,
            "the system-global SUPER_ADMIN assignment must survive a per-company role edit (anti-lockout)");
        roleIds.Should().Contain(chiefId);
        roleIds.Should().NotContain(accountantId, "the per-company set was replaced");
    }

    // ---- Phase A: list/get -------------------------------------------------

    [Fact]
    public async Task CompanyAdmin_sees_only_own_company_roles()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var roles = await svc.ListRolesAsync(null, default);

        roles.Should().HaveCount(11);   // the 11 per-company system roles
        roles.Should().NotContain(r => r.RoleCode == Role.SystemRoles.SuperAdmin);
        roles.Should().OnlyContain(r => r.IsSystem);
    }

    [Fact]
    public async Task SuperAdmin_can_target_any_company()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        // Super-admin is "in" a DIFFERENT company context but targets co explicitly.
        await using var sp = BuildProvider(1, isSuperAdmin: true);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var roles = await svc.ListRolesAsync(co.CompanyId, default);

        roles.Should().HaveCount(11);
        roles.Should().NotContain(r => r.RoleCode == Role.SystemRoles.SuperAdmin);
    }

    [Fact]
    public async Task GetRole_cross_company_returns_not_found()
    {
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleInB = await SystemRoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.GetRoleAsync(roleInB, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role.not_found");
    }

    [Fact]
    public async Task GetRole_returns_permission_codes()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.CompanyAdmin);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var detail = await svc.GetRoleAsync(roleId, default);
        detail.RoleId.Should().Be(roleId);
        detail.CompanyId.Should().Be(co.CompanyId);
        detail.RoleCode.Should().Be(Role.SystemRoles.CompanyAdmin);
    }

    [Fact]
    public async Task SuperAdmin_can_GetRole_in_any_company()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);

        // Super-admin sits in company 1's context but opens co's role by id.
        await using var sp = BuildProvider(1, isSuperAdmin: true);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var detail = await svc.GetRoleAsync(roleId, default);
        detail.CompanyId.Should().Be(co.CompanyId);
        detail.RoleCode.Should().Be(Role.SystemRoles.Accountant);
    }

    // ---- Phase B: set permissions -----------------------------------------

    [Fact]
    public async Task SetRolePermissions_whole_set_replace_writes_audit_and_company_id()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Auditor);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        // Pick two codes that ACTUALLY exist in sys.permissions (the seed is the source
        // of truth the service validates against; hardcoding risks an unseeded code).
        var codes = await SeededPermissionCodesAsync(2);
        await svc.SetRolePermissionsAsync(roleId, new SetRolePermissionsRequest(codes), default);

        var detail = await svc.GetRoleAsync(roleId, default);
        detail.PermissionCodes.Should().BeEquivalentTo(codes);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // company_id denormalized on every grant.
        var grants = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        grants.Should().OnlyContain(g => g.CompanyId == co.CompanyId);
        // one audit row for the change.
        var audit = await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM audit.activity_log " +
                "WHERE entity_type='role' AND entity_id={0} AND activity_type='rbac_grant_change'", (long)roleId)
            .FirstAsync();
        audit.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SetRolePermissions_unknown_code_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetRolePermissionsAsync(roleId,
            new SetRolePermissionsRequest(new[] { "no.such.permission" }), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.unknown_permission");
    }

    [Fact]
    public async Task SetRolePermissions_on_super_admin_refused()
    {
        var saRoleId = await SuperAdminRoleIdAsync();
        await using var sp = BuildProvider(1, isSuperAdmin: true);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetRolePermissionsAsync(saRoleId,
            new SetRolePermissionsRequest(new[] { Permissions.Sales.TaxInvoiceRead }), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.super_admin_locked");
    }

    [Fact]
    public async Task SetRolePermissions_cross_company_scope_required()
    {
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleInB = await SystemRoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetRolePermissionsAsync(roleInB,
            new SetRolePermissionsRequest(new[] { Permissions.Sales.TaxInvoiceRead }), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("rbac.cross_company.scope_required");
    }

    // ---- Phase B: create / rename / delete --------------------------------

    [Fact]
    public async Task CreateRole_creates_custom_role()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var code = "CUSTOM_" + TestIds.Suffix().ToUpperInvariant();
        var id = await svc.CreateRoleAsync(new CreateRoleRequest(code, "บทบาททดสอบ", "desc", null), default);
        id.Should().BeGreaterThan(0);

        var detail = await svc.GetRoleAsync(id, default);
        detail.IsSystem.Should().BeFalse();
        detail.RoleCode.Should().Be(code);
        detail.CompanyId.Should().Be(co.CompanyId);
    }

    [Fact]
    public async Task CreateRole_duplicate_code_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateRoleAsync(
            new CreateRoleRequest(Role.SystemRoles.Accountant, "dup", null, null), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role_code_duplicate");
    }

    [Fact]
    public async Task CreateRole_super_admin_code_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateRoleAsync(
            new CreateRoleRequest(Role.SystemRoles.SuperAdmin, "x", null, null), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.super_admin_locked");
    }

    [Fact]
    public async Task UpdateRole_renames()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.SalesStaff);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        await svc.UpdateRoleAsync(roleId, new UpdateRoleRequest("ชื่อใหม่", "new desc"), default);
        var detail = await svc.GetRoleAsync(roleId, default);
        detail.NameTh.Should().Be("ชื่อใหม่");
        detail.RoleCode.Should().Be(Role.SystemRoles.SalesStaff);   // code unchanged
    }

    [Fact]
    public async Task DeleteRole_system_role_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleId = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.SalesStaff);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.DeleteRoleAsync(roleId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role_is_system");
    }

    [Fact]
    public async Task DeleteRole_in_use_rejected_then_succeeds_when_unassigned()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var code = "DEL_" + TestIds.Suffix().ToUpperInvariant();
        var roleId = await svc.CreateRoleAsync(new CreateRoleRequest(code, "ลบได้", null, null), default);
        await CreateUserWithRolesAsync(co.CompanyId, roleId);   // now in use

        var act = () => svc.DeleteRoleAsync(roleId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role_in_use");
    }

    [Fact]
    public async Task DeleteRole_custom_unassigned_succeeds()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var code = "DELOK_" + TestIds.Suffix().ToUpperInvariant();
        var roleId = await svc.CreateRoleAsync(new CreateRoleRequest(code, "ลบ", null, null), default);
        await svc.DeleteRoleAsync(roleId, default);

        var act = () => svc.GetRoleAsync(roleId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role.not_found");
    }

    // ---- Phase C: users ----------------------------------------------------

    [Fact]
    public async Task ListUsers_scopes_roles_to_target_company()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleAcct = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var uid = await CreateUserWithRolesAsync(co.CompanyId, roleAcct);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var users = await svc.ListUsersAsync(null, default);
        var u = users.Should().ContainSingle(x => x.UserId == uid).Subject;
        u.Roles.Should().ContainSingle(r => r.RoleId == roleAcct);
    }

    [Fact]
    public async Task SetUserRoles_whole_set_replace()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleA = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var roleB = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.ArClerk);
        var uid = await CreateUserWithRolesAsync(co.CompanyId, roleA);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        await svc.SetUserRolesAsync(uid, new SetUserRolesRequest(new[] { roleB }), default);

        var users = await svc.ListUsersAsync(null, default);
        var u = users.Single(x => x.UserId == uid);
        u.Roles.Select(r => r.RoleId).Should().BeEquivalentTo(new[] { roleB });
    }

    [Fact]
    public async Task SetUserRoles_role_company_mismatch_rejected()
    {
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleInB = await SystemRoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);
        var uid = await CreateUserWithRolesAsync(coA.CompanyId,
            await SystemRoleIdAsync(coA.CompanyId, Role.SystemRoles.ArClerk));

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetUserRolesAsync(uid, new SetUserRolesRequest(new[] { roleInB }), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role_company_mismatch");
    }

    [Fact]
    public async Task SetUserRoles_self_lockout_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleA = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.CompanyAdmin);
        var uid = await CreateUserWithRolesAsync(co.CompanyId, roleA);

        // tenant.UserId == uid AND tenant.CompanyId == target  → empty set blocked.
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false, userId: uid);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetUserRolesAsync(uid, new SetUserRolesRequest(Array.Empty<int>()), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.self_lockout");
    }

    [Fact]
    public async Task SuperAdmin_SetUserRoles_cross_company_lands_in_target_company()
    {
        // Super-admin sits in company 1's context but manages a user in a DIFFERENT company,
        // assigning a role that belongs to THAT target company. §4.7 cross-company admin.
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        co.CompanyId.Should().NotBe(1);   // guard: the target really differs from the actor's home
        var roleInTarget = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var uid = await CreateUserWithRolesAsync(co.CompanyId);   // user with no roles yet

        await using var sp = BuildProvider(1, isSuperAdmin: true);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        await svc.SetUserRolesAsync(uid,
            new SetUserRolesRequest(new[] { roleInTarget }, CompanyId: co.CompanyId), default);

        // Read the landed user_role directly in a fresh scope: it must carry the TARGET
        // company_id (co), not the super-admin's home company (1).
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rows = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == uid)
            .Select(ur => new { ur.RoleId, ur.CompanyId })
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].RoleId.Should().Be(roleInTarget);
        rows[0].CompanyId.Should().Be(co.CompanyId);
    }

    [Fact]
    public async Task CompanyAdmin_SetUserRoles_foreign_company_scope_required()
    {
        // A company-admin (non-super) passing a CompanyId other than their own is a scope
        // violation — resolved in ResolveTargetCompany before any user/role lookup.
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var uid = await CreateUserWithRolesAsync(coA.CompanyId);

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetUserRolesAsync(uid,
            new SetUserRolesRequest(Array.Empty<int>(), CompanyId: coB.CompanyId), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("rbac.cross_company.scope_required");
    }

    // ---- Phase D: user lifecycle (create / active / reset) -----------------

    private const string GoodPw = "Str0ng#Password!";   // ≥ 12 chars

    [Fact]
    public async Task CreateUser_creates_active_user_with_roles_and_usable_password()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleAcct = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var username = "u" + TestIds.Suffix();
        var uid = await svc.CreateUserAsync(new CreateUserRequest(
            username, GoodPw, "ผู้ใช้ใหม่", null, IsActive: true, new[] { roleAcct }), default);
        uid.Should().BeGreaterThan(0);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == uid);
        user.Username.Should().Be(username);
        user.IsActive.Should().BeTrue();
        user.IsSuperAdmin.Should().BeFalse("a super-admin is only minted by the first-run bootstrap");
        user.Email.Should().Be($"{username}@teas.local");   // defaulted when omitted

        // Password is hashed + verifiable → a real login would succeed.
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        hasher.Verify(GoodPw, user.PasswordHash).Should().BeTrue();
        user.PasswordHash.Should().NotBe(GoodPw);

        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == uid && ur.CompanyId == co.CompanyId).Select(ur => ur.RoleId).ToListAsync();
        roleIds.Should().Equal(roleAcct);

        var listed = (await svc.ListUsersAsync(null, default)).Single(x => x.UserId == uid);
        listed.Roles.Select(r => r.RoleId).Should().Contain(roleAcct);
    }

    [Fact]
    public async Task CreateUser_duplicate_username_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var username = "dup" + TestIds.Suffix();
        await svc.CreateUserAsync(new CreateUserRequest(username, GoodPw, "n", null, true, Array.Empty<int>()), default);

        var act = () => svc.CreateUserAsync(
            new CreateUserRequest(username, GoodPw, "n", null, true, Array.Empty<int>()), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.username_duplicate");
    }

    [Fact]
    public async Task CreateUser_short_password_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateUserAsync(
            new CreateUserRequest("u" + TestIds.Suffix(), "short", "n", null, true, Array.Empty<int>()), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.password_too_short");
    }

    [Fact]
    public async Task CreateUser_invalid_username_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateUserAsync(
            new CreateUserRequest("bad name!", GoodPw, "n", null, true, Array.Empty<int>()), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.username_invalid");
    }

    [Fact]
    public async Task CreateUser_role_from_other_company_rejected()
    {
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleInB = await SystemRoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateUserAsync(
            new CreateUserRequest("u" + TestIds.Suffix(), GoodPw, "n", null, true, new[] { roleInB }), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.role_company_mismatch");
    }

    [Fact]
    public async Task CreateUser_company_admin_cross_company_scope_required()
    {
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = BuildProvider(coA.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.CreateUserAsync(new CreateUserRequest(
            "u" + TestIds.Suffix(), GoodPw, "n", null, true, Array.Empty<int>(), CompanyId: coB.CompanyId), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("rbac.cross_company.scope_required");
    }

    [Fact]
    public async Task SetUserActive_deactivate_then_reactivate()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        // A company-admin manages users who belong to their company (have a role there).
        var uid = await svc.CreateUserAsync(
            new CreateUserRequest("u" + TestIds.Suffix(), GoodPw, "n", null, true, new[] { role }), default);

        await svc.SetUserActiveAsync(uid, false, default);
        (await IsActiveAsync(sp, uid)).Should().BeFalse();

        await svc.SetUserActiveAsync(uid, true, default);
        (await IsActiveAsync(sp, uid)).Should().BeTrue();
    }

    [Fact]
    public async Task SetUserActive_self_deactivate_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.CompanyAdmin);
        var uid = await CreateUserWithRolesAsync(co.CompanyId, role);

        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false, userId: uid);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetUserActiveAsync(uid, false, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rbac.self_lockout");
    }

    [Fact]
    public async Task ResetUserPassword_changes_and_verifies()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await SystemRoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();

        var uid = await svc.CreateUserAsync(
            new CreateUserRequest("u" + TestIds.Suffix(), GoodPw, "n", null, true, new[] { role }), default);

        const string newPw = "Rotated#Password9";
        await svc.ResetUserPasswordAsync(uid, newPw, default);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var hash = await db.Users.AsNoTracking().Where(u => u.UserId == uid).Select(u => u.PasswordHash).FirstAsync();
        hasher.Verify(newPw, hash).Should().BeTrue();
        hasher.Verify(GoodPw, hash).Should().BeFalse("the old password no longer works");
    }

    [Fact]
    public async Task ResetUserPassword_short_rejected()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = BuildProvider(co.CompanyId, isSuperAdmin: false);
        var svc = sp.GetRequiredService<IRbacAdminService>();

        var uid = await svc.CreateUserAsync(
            new CreateUserRequest("u" + TestIds.Suffix(), GoodPw, "n", null, true, Array.Empty<int>()), default);

        var act = () => svc.ResetUserPasswordAsync(uid, "short", default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.password_too_short");
    }

    private static async Task<bool> IsActiveAsync(ServiceProvider sp, long uid)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.UserId == uid).Select(u => u.IsActive).FirstAsync();
    }
}
