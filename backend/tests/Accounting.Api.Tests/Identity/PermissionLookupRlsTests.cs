using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// Regression guard for the cont.112 empty-token-at-login bug. PermissionLookup runs during the
/// ANONYMOUS login request (before TenantMiddleware pins app.company_id); under a real NOBYPASSRLS
/// role the RLS on sys.roles / sys.role_permissions (script 510) hides the company's roles+grants,
/// so the JWT is minted with NO roles/permissions for every non-super user. The rest of the suite
/// connects as a SUPERUSER (RLS bypassed) and CANNOT see this class of bug — so this test SET ROLEs
/// to a provisioned NOBYPASSRLS role and asserts the lookup still resolves the grants (the fix pins
/// app.company_id for its own transaction). Without the fix this returns 0 → the test fails.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PermissionLookupRlsTests
{
    private readonly PostgresFixture _fx;
    public PermissionLookupRlsTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task LoadAsync_resolves_grants_under_NOBYPASSRLS_role_with_company_unset()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        Skip.If(_fx.RlsRoleSkip is not null, _fx.RlsRoleSkip);

        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        // Create a non-super user with the seeded ACCOUNTANT role (which carries grants).
        long userId;
        int expectedGrants;
        await using (var sp0 = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1))
        await using (var s0 = sp0.CreateAsyncScope())
        {
            var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var roleId = await db0.Roles
                .Where(r => r.CompanyId == co.CompanyId && r.RoleCode == "ACCOUNTANT")
                .Select(r => r.RoleId).FirstAsync();
            expectedGrants = await db0.RolePermissions.CountAsync(rp => rp.RoleId == roleId);

            var svc = s0.ServiceProvider.GetRequiredService<IRbacAdminService>();
            userId = await svc.CreateUserAsync(new CreateUserRequest(
                "rls" + TestIds.Suffix(), "Str0ng#Password!", "RLS probe user", null,
                IsActive: true, new[] { roleId }), default);
        }
        expectedGrants.Should().BeGreaterThan(0, "the ACCOUNTANT role is seeded with grants");

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1, userId: userId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        await db.Database.OpenConnectionAsync();
        try
        {
            // Reproduce the exact login condition: RLS ENFORCED (NOBYPASSRLS role) + app.company_id UNSET.
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', '', false)");
            await db.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresFixture.RlsTestRole}");

            var (roles, perms) = await new PermissionLookup(db).LoadAsync(userId, co.CompanyId, default);

            // With the fix, PermissionLookup pins app.company_id for its own transaction so RLS
            // exposes the company's roles+grants. Without it, RLS hides them → 0 (red).
            roles.Should().Contain("ACCOUNTANT");
            perms.Should().HaveCount(expectedGrants).And.NotBeEmpty();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }
}
