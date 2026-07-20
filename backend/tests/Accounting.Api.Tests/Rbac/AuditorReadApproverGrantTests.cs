using System.Net.Http;
using System.Net.Http.Headers;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// specs/fix-swarm-findings-all.md WP2 + WP4 —
/// 628_seed_auditor_read_approver_grant.sql grants AUDITOR the read-only permissions for the
/// modules/reports that previously 403'd-as-empty-state (swarm-findings/audit01.md HIGH #3 + MED
/// cit), and grants APPROVER sales.tax_invoice.read so the dashboard "ต้องทำ" widget
/// (GET /reports/pending-agent-approvals) stops 403ing (swarm-findings/appr01.md HIGH).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AuditorReadApproverGrantTests
{
    private readonly PostgresFixture _fx;
    public AuditorReadApproverGrantTests(PostgresFixture fx) => _fx = fx;

    private static readonly string[] ExpectedAuditorReads =
    [
        "purchase.purchase_order.read",
        "purchase.vendor_invoice.read",
        "purchase.payment_voucher.read",
        "expense.claim.read",
        "bank.account.read",
        "fixedasset.read",
        "bank.report.read",
        "tax.filing.preview",
    ];

    private static readonly string[] AuditorWriteCodesNeverGranted =
    [
        "purchase.purchase_order.create", "purchase.purchase_order.approve", "purchase.purchase_order.cancel",
        "purchase.vendor_invoice.create", "purchase.vendor_invoice.post",
        "purchase.payment_voucher.create", "purchase.payment_voucher.approve", "purchase.payment_voucher.post",
        "expense.claim.create", "expense.claim.approve", "expense.claim.pay",
        "bank.account.manage", "bank.reconcile",
        "fixedasset.manage", "fixedasset.dispose", "fixedasset.depreciation.run",
        "tax.filing.finalize",
        "sales.quotation.manage", "sales.sales_order.manage", "sales.delivery_order.manage",
        "master.vendor.manage", "master.business_unit.manage",
    ];

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    [SkippableFact]
    public async Task Auditor_resolves_the_new_read_perms_and_holds_zero_write_perms()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var auditor = roles.Should().ContainSingle(r => r.RoleCode == "AUDITOR").Which;

        foreach (var code in ExpectedAuditorReads)
            auditor.Permissions.Should().Contain(code,
                $"AUDITOR must resolve the WP2 read grant '{code}' (audit01.md HIGH #3)");

        foreach (var code in AuditorWriteCodesNeverGranted)
            auditor.Permissions.Should().NotContain(code,
                $"AUDITOR is READ-ONLY by design — '{code}' must never be granted (WP2 hard rule)");
    }

    [SkippableFact]
    public async Task Auditor_hits_purchase_order_list_and_ap_aging_without_a_403_after_the_grant()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var fx = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(fx);
        var auditor = roles.Should().ContainSingle(r => r.RoleCode == "AUDITOR").Which;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Issuer().Issue(new TokenClaims(
            UserId: 991_235, Username: "rbac-auditor-read",
            CompanyId: RbacMatrixData.ReferenceCompanyId, BranchId: 1,
            IsSuperAdmin: false, Roles: ["AUDITOR"],
            Permissions: auditor.Permissions.ToList())).Token;

        foreach (var path in new[] { "/purchase-orders", "/reports/ap-aging", "/reports/outstanding-po" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await client.SendAsync(req);

            ((int)resp.StatusCode).Should().NotBe(403,
                $"audit01.md HIGH #3: {path} 403'd for AUDITOR before the grant, rendering an " +
                "ambiguous empty state instead of real data");
        }
    }

    [SkippableFact]
    public async Task Approver_resolves_tax_invoice_read_and_hits_pending_agent_approvals_without_a_403()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var approver = roles.Should().ContainSingle(r => r.RoleCode == "APPROVER").Which;

        approver.Permissions.Should().Contain("sales.tax_invoice.read",
            "APPROVER needs sales.tax_invoice.read to load the pending-agent-approvals dashboard " +
            "widget (appr01.md HIGH — the endpoint's actual RequirePermission gate)");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Issuer().Issue(new TokenClaims(
            UserId: 991_236, Username: "rbac-approver-widget",
            CompanyId: RbacMatrixData.ReferenceCompanyId, BranchId: 1,
            IsSuperAdmin: false, Roles: ["APPROVER"],
            Permissions: approver.Permissions.ToList())).Token;

        using var req = new HttpRequestMessage(HttpMethod.Get, "/reports/pending-agent-approvals");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);

        ((int)resp.StatusCode).Should().NotBe(403,
            "appr01.md HIGH: this 403'd for every APPROVER before the grant, so the dashboard " +
            "widget silently showed a false 'all clear' while real drafts were pending");
    }
}
