using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// specs/fix-swarm-findings-all.md WP6 — 629_seed_read_manage_split_grant.sql splits
/// quotations/sales-orders/delivery-orders/vendors/business-units' combined `.manage` code into a
/// separate `.read` (list/get/PDF) so AUDITOR (and other read-only roles) get true read-only
/// visibility without write/lifecycle access. Mirrors AuditorReadApproverGrantTests /
/// TaxOfficerFilingGrantTests. See 628_seed_auditor_read_approver_grant.sql's header for why this
/// split was deferred out of WP2 into its own follow-up.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReadManageSplitGrantTests
{
    private readonly PostgresFixture _fx;
    public ReadManageSplitGrantTests(PostgresFixture fx) => _fx = fx;

    private static readonly (string Read, string Manage)[] SplitPairs =
    [
        ("sales.quotation.read", "sales.quotation.manage"),
        ("sales.sales_order.read", "sales.sales_order.manage"),
        ("sales.delivery_order.read", "sales.delivery_order.manage"),
        ("master.vendor.read", "master.vendor.manage"),
        ("master.business_unit.read", "master.business_unit.manage"),
    ];

    private static readonly string[] NewReadCodes = SplitPairs.Select(p => p.Read).ToArray();
    private static readonly string[] PairedManageCodes = SplitPairs.Select(p => p.Manage).ToArray();

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    [SkippableFact]
    public async Task Auditor_resolves_all_5_new_reads_and_holds_none_of_the_paired_manage_codes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var auditor = roles.Should().ContainSingle(r => r.RoleCode == "AUDITOR").Which;

        foreach (var code in NewReadCodes)
            auditor.Permissions.Should().Contain(code,
                $"AUDITOR must resolve the WP6 read-split grant '{code}'");

        foreach (var code in PairedManageCodes)
            auditor.Permissions.Should().NotContain(code,
                $"AUDITOR is READ-ONLY by design — '{code}' must never be granted (WP6 hard rule)");
    }

    /// <summary>
    /// Regression guard — the risky part of the split. Opus Tier-2 review F1 (2026-07-21): a
    /// template-only enumeration (the original version of this test) can never fail on the exact
    /// case 629's step 5b guards against — a role holding `.manage` ONLY via a direct
    /// sys.role_permissions grant (RbacAdminService.SetRolePermissionsAsync, or a company-local
    /// custom role from CreateRoleAsync) has NO row in sys.role_permission_templates at all.
    /// Hardened to enumerate the EFFECTIVE per-company grants straight from sys.role_permissions
    /// instead, so it actually exercises what's really granted, template or not.
    ///
    /// One set-based anti-join query per pair (NOT a per-role round trip): the shared teas_test has
    /// grown to ~30,000 companies from repeated test runs across sessions, and every company clones
    /// every system role, so an N+1 "list holders, then re-query each one" pattern here does tens of
    /// thousands of round trips and takes minutes (confirmed the hard way — the first version of
    /// this test took 4m47s). This single indexed anti-join finds every violation server-side.
    ///
    /// Excludes the system-global SUPER_ADMIN row (company_id IS NULL): PermissionHandler bypasses
    /// SUPER_ADMIN on the is_super_admin CLAIM, never consulting its granted codes at all (510's own
    /// header + RbacMatrixData.LoadAsync's doc comment), and it sits outside the per-company model
    /// entirely (627/628/629's resync loops all iterate master.companies, never touching it). A
    /// missing .read on that legacy company_id-NULL row is a functional no-op, not a real
    /// regression — 629's step 5b deliberately scopes to `rp.company_id = c.company_id` and must NOT
    /// be widened to chase it (coordinator's prod de-risk note: SUPER_ADMIN is the ONLY manage
    /// holder outside the template today, so this exclusion is the only carve-out needed).
    /// </summary>
    [SkippableFact]
    public async Task Every_effective_manage_holder_across_every_company_still_resolves_the_paired_read()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.Database.SetCommandTimeout(60);

        var missing = new List<string>();
        foreach (var (read, manage) in SplitPairs)
        {
            // One query per pair: role_ids holding `manage` (company-scoped, i.e. NOT the global
            // SUPER_ADMIN row) that do NOT ALSO hold the paired `read` — computed entirely
            // server-side via an indexed anti-join, capped so a genuine wide regression doesn't
            // blow up the assertion message.
            var violations = await db.Database.SqlQueryRaw<int>(
                    "SELECT DISTINCT rp.role_id AS \"Value\" " +
                    "FROM sys.role_permissions rp " +
                    "JOIN sys.permissions pm ON pm.permission_id = rp.permission_id AND pm.permission_code = {0} " +
                    "WHERE rp.company_id IS NOT NULL " +
                    "  AND NOT EXISTS ( " +
                    "    SELECT 1 FROM sys.role_permissions rp2 " +
                    "    JOIN sys.permissions pr ON pr.permission_id = rp2.permission_id AND pr.permission_code = {1} " +
                    "    WHERE rp2.role_id = rp.role_id) " +
                    "LIMIT 10", manage, read)
                .ToListAsync();

            foreach (var roleId in violations)
                missing.Add($"role_id={roleId} holds '{manage}' but NOT '{read}'");
        }

        missing.Should().BeEmpty("every EFFECTIVE .manage holder (template-backed OR a direct/custom "
            + "grant) must keep read access after the WP6 split (regression guard): "
            + string.Join("; ", missing));
    }

    /// <summary>
    /// Opus Tier-2 review F1 (2026-07-21) — the exact regression the template-only guard missed:
    /// a role holding `.manage` ONLY via a direct sys.role_permissions grant, with NO backing
    /// sys.role_permission_templates row, mirroring RbacAdminService.CreateRoleAsync (a fresh
    /// company-local custom role code never in the template) + SetRolePermissionsAsync (writes the
    /// grant straight to sys.role_permissions). Creates that exact scenario via the REAL service,
    /// then replays the ACTUAL 629 SQL text read from disk — genuinely RED before 629's step 5b fix
    /// existed (a template-only top-up never sees this role) and GREEN with it, not a hand-copied
    /// duplicate that can drift out of sync with the shipped script.
    ///
    /// Scoped to ONE company via a single, asserted textual substitution of 629's per-company loop
    /// driver (`FOR c IN SELECT company_id FROM master.companies LOOP` → the same, WHERE-filtered to
    /// this test's company_id) rather than executing the real loop across every company: the shared
    /// teas_test has grown to ~30,000 companies from repeated test runs across sessions, and a
    /// full-file replay over all of them was TIMED at over 10 minutes against this exact DB
    /// (confirmed directly, not guessed) — impractical for a unit test and only getting slower as
    /// the DB grows further. Every statement's SQL text is otherwise untouched (steps 1-4 run
    /// globally and are idempotent no-ops on replay; only WHICH companies the DO block's loop visits
    /// is narrowed). If the loop's anchor text ever changes, the substitution assertion below fails
    /// loudly instead of silently executing the unscoped (very slow) original.
    /// </summary>
    [SkippableFact]
    public async Task Custom_role_with_a_direct_manage_grant_and_no_template_row_resolves_read_after_629_replay()
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

        // 1. CreateRoleAsync's custom-role path — a brand-new role_code that has NEVER existed in
        //    sys.role_permission_templates (unlike a system role, which 510 seeded there).
        var roleCode = "WP6_F1_" + TestIds.Suffix().ToUpperInvariant();
        int roleId;
        try
        {
            roleId = await rbac.CreateRoleAsync(
                new CreateRoleRequest(roleCode, "F1 direct-grant regression test", null, null), default);

            // 2. SetRolePermissionsAsync's direct-grant path — writes straight to
            //    sys.role_permissions, never touching sys.role_permission_templates.
            await rbac.SetRolePermissionsAsync(roleId,
                new SetRolePermissionsRequest([Permissions.Sales.QuotationManage]), default);

            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

            // Sanity — confirm the setup actually reproduces F1's gap before asserting the fix.
            var templateRows = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permission_templates WHERE role_code = {0}",
                    roleCode)
                .FirstAsync();
            templateRows.Should().Be(0, "sanity: this is a brand-new custom role, never in the template");

            var readBefore = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permissions rp " +
                    "JOIN sys.permissions p ON p.permission_id = rp.permission_id " +
                    "WHERE rp.role_id = {0} AND p.permission_code = 'sales.quotation.read'", roleId)
                .FirstAsync();
            readBefore.Should().Be(0, "sanity: the custom role must start without the read grant");

            // 3. Read the ACTUAL 629 SQL from disk and scope its per-company loop to just this
            //    test's company (see class doc for why: the shared teas_test has ~30,000 companies,
            //    and the unscoped full-file replay was timed at >10 minutes there).
            var sqlPath = Path.Combine(RbacTestPaths.RepoRoot(),
                "backend", "src", "Accounting.Infrastructure", "Migrations", "SqlScripts",
                "629_seed_read_manage_split_grant.sql");
            File.Exists(sqlPath).Should().BeTrue($"629 seed script must exist at {sqlPath}");
            var fileSql = await File.ReadAllTextAsync(sqlPath);

            const string loopAnchor = "FOR c IN SELECT company_id FROM master.companies LOOP";
            fileSql.Should().Contain(loopAnchor,
                "629's per-company loop anchor text changed — update this test's scoping "
                + "substitution to match, or it would silently fall back to the full, very slow loop");
            var scopedSql = fileSql.Replace(loopAnchor,
                $"FOR c IN SELECT company_id FROM master.companies WHERE company_id = {co.CompanyId} LOOP");

            db.Database.SetCommandTimeout(30);
            await db.Database.ExecuteSqlRawAsync(scopedSql);

            // 4. The custom role must now resolve the paired read.
            var readAfter = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM sys.role_permissions rp " +
                    "JOIN sys.permissions p ON p.permission_id = rp.permission_id " +
                    "WHERE rp.role_id = {0} AND p.permission_code = 'sales.quotation.read'", roleId)
                .FirstAsync();
            readAfter.Should().BeGreaterThan(0,
                "Opus F1: a role holding .manage ONLY via a direct sys.role_permissions grant (no "
                + "template row) must still resolve the paired .read after 629's step 5b logic runs");
        }
        finally
        {
            // Cleanup — this is a synthetic role that must not linger in the shared teas_test and
            // confuse Every_effective_manage_holder_across_every_company_still_resolves_the_paired_read
            // above (a leftover custom grant from a crashed prior run of THIS test did exactly that).
            await using var cleanupScope = sp.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await cleanupDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM sys.role_permissions WHERE role_id IN " +
                "(SELECT role_id FROM sys.roles WHERE role_code = {0})", roleCode);
            await cleanupDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM sys.roles WHERE role_code = {0}", roleCode);
        }
    }

    [SkippableFact]
    public async Task No_role_gained_write_access_it_did_not_already_have()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);

        foreach (var manage in PairedManageCodes)
        {
            var expectedHolders = (await db.Database.SqlQueryRaw<string>(
                    "SELECT role_code AS \"Value\" FROM sys.role_permission_templates WHERE permission_code = {0}",
                    manage)
                .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

            var actualHolders = roles.Where(r => !r.IsSuperAdmin && r.Permissions.Contains(manage))
                .Select(r => r.RoleCode).ToHashSet(StringComparer.Ordinal);

            actualHolders.Should().BeEquivalentTo(expectedHolders,
                $"WP6 must never grant '{manage}' to any role beyond the pre-existing template holders");
        }
    }

    [SkippableFact]
    public async Task Auditor_hits_the_5_split_resources_without_a_403_but_a_post_is_still_403()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var auditor = roles.Should().ContainSingle(r => r.RoleCode == "AUDITOR").Which;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Issuer().Issue(new TokenClaims(
            UserId: 991_237, Username: "rbac-auditor-readsplit",
            CompanyId: RbacMatrixData.ReferenceCompanyId, BranchId: 1,
            IsSuperAdmin: false, Roles: ["AUDITOR"],
            Permissions: auditor.Permissions.ToList())).Token;

        foreach (var path in new[]
                 { "/quotations", "/sales-orders", "/delivery-orders", "/vendors", "/business-units" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await client.SendAsync(req);

            ((int)resp.StatusCode).Should().NotBe(403,
                $"WP6: AUDITOR should now have read access to {path}");
        }

        // Defense-in-depth: AUDITOR holds sales.quotation.read but NOT sales.quotation.manage —
        // the write route must stay 403 (this is the whole point of the split).
        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/quotations")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var postResp = await client.SendAsync(postReq);

        ((int)postResp.StatusCode).Should().Be(403,
            "AUDITOR must never be able to create a quotation — read/manage split must not leak write");

        // Same for business-units (the highest-value split — kills the BU console-403 spam).
        using var buPostReq = new HttpRequestMessage(HttpMethod.Post, "/business-units")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        buPostReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var buPostResp = await client.SendAsync(buPostReq);

        ((int)buPostResp.StatusCode).Should().Be(403,
            "AUDITOR must never be able to create a business unit — read/manage split must not leak write");
    }
}
