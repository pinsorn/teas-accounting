using System.Linq;
using Accounting.Api.Tests.Fixtures;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// specs/fix-c11-seed181-roles.md (cleanup unit C11) — troubles-wiki.md "Fresh reseed:
/// ap_clerk/sales_staff login 401s auth.no_company_assignment (RLS-seed footgun in
/// 181_seed_demo_pv_users.sql)". 181's INSERT INTO sys.user_roles ... SELECT ... FROM sys.roles
/// runs at boot with NO app.company_id/app.bypass_rls GUC set; sys.roles is a G3 FORCE RLS table
/// (600_superadmin_scoped_rls.sql), so the correlated SELECT matched zero rows and the INSERT
/// silently inserted nothing — no error, ON CONFLICT DO NOTHING on an empty source is a no-op.
///
/// This asserts directly on teas_test's own seeded state (company 1, the literal ids 181 seeds),
/// per the spec's own convention for a seed script that is NOT company-parameterized. Before this
/// unit's fix, teas_test itself reproduces the defect (confirmed live via psql,
/// 2026-08-19: sys.user_roles has 0 rows for user_id IN (3,4) AND company_id=1, even though
/// 181_seed_demo_pv_users.sql is recorded in sys.applied_sql_scripts and both the users and the
/// roles exist). 641_reconcile_demo_pv_user_roles.sql is a brand-new script name, so
/// PostgresFixture (which applies any not-yet-applied SqlScripts file at test-process startup,
/// mirroring DbInitializer) picks it up automatically on the next test run and repairs teas_test's
/// own long-lived state — no manual sys.applied_sql_scripts surgery needed.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DemoPvUserRoleSeedTests
{
    private readonly PostgresFixture _fx;
    public DemoPvUserRoleSeedTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Ap_clerk_and_sales_staff_hold_their_company1_role_assignment()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var assignments = await db.Database.SqlQueryRaw<string>("""
            SELECT u.username || ':' || r.role_code AS "Value"
            FROM sys.user_roles ur
            JOIN sys.users u ON u.user_id = ur.user_id
            JOIN sys.roles r ON r.role_id = ur.role_id
            WHERE ur.user_id IN (3, 4) AND ur.company_id = 1
            """).ToListAsync();

        assignments.Should().Contain("ap_clerk:AP_CLERK",
            "181's own INSERT intends this grant, but its correlated SELECT FROM sys.roles ran " +
            "under no RLS GUC/bypass and silently matched zero rows (troubles-wiki.md) — " +
            "641_reconcile_demo_pv_user_roles.sql must repair this on teas_test's already-applied state");
        assignments.Should().Contain("sales_staff:SALES_STAFF",
            "same RLS-seed footgun as ap_clerk, for the sales_staff/SALES_STAFF pairing");
    }
}
