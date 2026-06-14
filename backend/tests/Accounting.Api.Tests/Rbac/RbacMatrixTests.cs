using System.IO;
using System.Linq;
using System.Text;
using Accounting.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Phase B — emit the human-reviewable role×permission matrix (source of truth Ham signs off)
/// and assert the invariants that protect it: no orphan grants, SoD separation, and that the
/// 14 "default-unassigned" permissions (Ham 2026-06-14) really are SUPER_ADMIN-only in the seed.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RbacMatrixTests
{
    private readonly PostgresFixture _fx;
    public RbacMatrixTests(PostgresFixture fx) => _fx = fx;

    /// <summary>
    /// Permissions deliberately granted to SUPER_ADMIN only. After the Plan 2 grant reconcile
    /// (530) the previously "default-unassigned 14" were found to be a seed-ordering bug and were
    /// restored to their operational roles; the only remaining super-admin-only permission is
    /// `master.company.manage` (§4.6 — company tax config is super-admin-only; it also gates
    /// /company-profile/*, a shared-permission finding flagged to Ham).
    /// </summary>
    private static readonly string[] IntentionallySuperOnly =
    [
        "master.company.manage",
    ];

    /// <summary>Segregation-of-duties pairs — one role must never hold BOTH (§12.1 SoD).</summary>
    private static readonly (string A, string B, string Rule)[] SoDPairs =
    [
        ("purchase.payment_voucher.create", "purchase.payment_voucher.approve",
            "PV maker-checker (§12.1)"),
        ("purchase.purchase_order.create", "purchase.purchase_order.approve",
            "PO maker-checker (§12.1, advisory)"),
    ];

    /// <summary>
    /// SoD overlaps that exist in the shipped seed and are surfaced for Ham's review (plan §2 =
    /// "flag", not auto-remove). Admin-tier roles legitimately hold both sides (SoD is enforced by
    /// assigning maker vs checker roles to DIFFERENT people, not by crippling the admin role);
    /// CHIEF_ACCOUNTANT is flagged PENDING HAM CONFIRMATION. Anything NOT in this set fails the
    /// test as a new regression. Format: "ROLE|PERM_A|PERM_B".
    /// </summary>
    private static readonly string[] AcceptedSoDExceptions =
    [
        "COMPANY_ADMIN|purchase.payment_voucher.create|purchase.payment_voucher.approve",
        "COMPANY_ADMIN|purchase.purchase_order.create|purchase.purchase_order.approve",
        "CHIEF_ACCOUNTANT|purchase.payment_voucher.create|purchase.payment_voucher.approve",
    ];

    [SkippableFact]
    public async Task Generate_role_permission_matrix_and_assert_invariants()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, allPerms) = await RbacMatrixData.LoadAsync(sp);

        roles.Should().HaveCountGreaterThan(1);
        allPerms.Should().NotBeEmpty();

        var nonSuper = roles.Where(r => !r.IsSuperAdmin).ToList();

        // SoD overlaps (role holds BOTH sides of a maker-checker pair). Plan §2 = "flag", so these
        // are written into the matrix doc for review; the assertion only guards against NEW ones.
        var sodFindings = nonSuper
            .SelectMany(r => SoDPairs
                .Where(p => r.Permissions.Contains(p.A) && r.Permissions.Contains(p.B))
                .Select(p => (Role: r.RoleCode, p.A, p.B, p.Rule)))
            .OrderBy(x => x.Role, StringComparer.Ordinal)
            .ToList();

        WriteMatrix(roles, allPerms, sodFindings);

        // Invariant 1 — no orphan grants (every granted code exists in the catalog).
        var orphans = nonSuper
            .SelectMany(r => r.Permissions.Select(p => (r.RoleCode, p)))
            .Where(x => !allPerms.Contains(x.p))
            .Select(x => $"{x.RoleCode}:{x.p}")
            .ToList();
        orphans.Should().BeEmpty("a role grants a permission code not in sys.permissions: "
            + string.Join(", ", orphans));

        // Invariant 2 — SoD: only the reviewed/accepted overlaps may exist; a NEW one fails.
        var newSod = sodFindings
            .Select(x => $"{x.Role}|{x.A}|{x.B}")
            .Except(AcceptedSoDExceptions)
            .ToList();
        newSod.Should().BeEmpty("a NEW segregation-of-duties overlap appeared (review + either fix "
            + "the seed or add to AcceptedSoDExceptions): " + string.Join("; ", newSod));

        // Invariant 3 — the set of permissions granted to NO non-super role must be exactly the
        // intentional super-admin-only allowlist. Catches BOTH an under-grant regression (a perm
        // silently reachable by super only) AND an unexpected policy change (a super-only perm
        // getting granted). After 530 the only entry is master.company.manage (§4.6).
        var superOnly = allPerms
            .Where(p => !nonSuper.Any(r => r.Permissions.Contains(p)))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        superOnly.Should().BeEquivalentTo(IntentionallySuperOnly,
            "the only permission expected to be SUPER_ADMIN-only is master.company.manage (§4.6); "
            + "anything else super-only is an under-grant to review. Actual super-only set: "
            + string.Join(", ", superOnly));
    }

    private void WriteMatrix(IReadOnlyList<RoleGrants> roles, IReadOnlyList<string> allPerms,
        IReadOnlyList<(string Role, string A, string B, string Rule)> sodFindings)
    {
        // Columns = roles (super last); rows = permissions. † marks default-unassigned.
        var ordered = roles.Where(r => !r.IsSuperAdmin)
            .Concat(roles.Where(r => r.IsSuperAdmin)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Role × Permission Matrix (source of truth — review me)");
        sb.AppendLine();
        sb.AppendLine("> Generated by `RbacMatrixTests` from `sys.role_permissions` (reference company 1 = template).");
        sb.AppendLine("> Canonical by construction: every company (incl. company 1) is cloned from the SAME");
        sb.AppendLine("> `sys.role_permission_templates` via `sys.seed_company_roles`, so company 1's grants ARE the template.");
        sb.AppendLine("> Sprint 13k Plan 2 (RBAC Cartesian audit). ✓ = granted · blank = not granted.");
        sb.AppendLine("> `SUPER_ADMIN` bypasses per-permission checks (CLAUDE.md §4.1) — shown as ✓ everywhere.");
        sb.AppendLine("> † = **SUPER_ADMIN-only by design** (`master.company.manage` — §4.6 company tax config).");
        sb.AppendLine("> Grants reconciled by `530_seed_rbac_grant_reconcile.sql` (Plan 2 Phase D). See that file's header for the per-role rationale.");
        sb.AppendLine();

        sb.Append("| Permission |");
        foreach (var r in ordered) sb.Append($" {r.RoleCode} |");
        sb.AppendLine();
        sb.Append("|---|");
        foreach (var _ in ordered) sb.Append("---|");
        sb.AppendLine();

        foreach (var perm in allPerms)
        {
            var marker = IntentionallySuperOnly.Contains(perm) ? " †" : "";
            sb.Append($"| `{perm}`{marker} |");
            foreach (var r in ordered)
                sb.Append(r.Permissions.Contains(perm) ? " ✓ |" : "  |");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## Per-role permission counts");
        sb.AppendLine();
        sb.AppendLine("| Role | Granted |");
        sb.AppendLine("|---|---:|");
        foreach (var r in ordered)
            sb.AppendLine($"| {r.RoleCode} | {(r.IsSuperAdmin ? $"{allPerms.Count} (bypass)" : r.Permissions.Count.ToString())} |");

        sb.AppendLine();
        sb.AppendLine("## ⚠️ Segregation-of-duties review (Ham to confirm)");
        sb.AppendLine();
        if (sodFindings.Count == 0)
        {
            sb.AppendLine("_No role holds both sides of a maker-checker pair._");
        }
        else
        {
            sb.AppendLine("Roles below hold BOTH sides of a maker-checker pair. SoD is normally enforced by");
            sb.AppendLine("assigning maker vs checker roles to **different people** — an admin/chief role holding");
            sb.AppendLine("both is the conventional \"senior can do everything\" pattern, but Ham should confirm");
            sb.AppendLine("whether each is acceptable or the grant should be split.");
            sb.AppendLine();
            sb.AppendLine("| Role | Holds | + | Rule |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var f in sodFindings)
                sb.AppendLine($"| {f.Role} | `{f.A}` | `{f.B}` | {f.Rule} |");
        }

        var path = Path.Combine(RbacTestPaths.RbacDocsDir(), "role-permission-matrix.md");
        File.WriteAllText(path, sb.ToString());
    }
}
