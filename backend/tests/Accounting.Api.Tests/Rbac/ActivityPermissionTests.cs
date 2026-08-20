using System.Net;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Codex UI review 2026-08-20 R3 backend half (specs/fix-codex-review-2026-08-20.md) —
/// ActivityEndpoints.cs used to gate every document-activity route behind one shared
/// Permissions.Report.AuditRead, which no operator role holds — sales_staff got 403 on a
/// Quotation THEY created, ap_clerk got 403 on a Payment Voucher likewise. Fixed: each route now
/// requires that document's OWN read permission. Reproduces the exact two personas/doctypes named
/// in the UI review finding, in both directions (200 on the doc they can read, 403 on one they
/// can't) — mirrors EmployeeLookupGrantTests' real-role-permissions-from-DB pattern.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ActivityPermissionTests
{
    private readonly PostgresFixture _fx;
    public ActivityPermissionTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    /// <summary>Real grants for a role in the given company — read straight from
    /// sys.role_permissions (the EFFECTIVE grants), not hand-typed.</summary>
    private static async Task<List<string>> RolePermissionsAsync(AccountingDbContext db, int companyId, string roleCode) =>
        await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.Role!.CompanyId == companyId && rp.Role.RoleCode == roleCode)
            .Select(rp => rp.Permission!.PermissionCode)
            .ToListAsync();

    private static async Task<HttpResponseMessage> GetAsync(
        RbacApiFactory factory, string token, string path)
    {
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    [SkippableFact]
    public async Task Sales_staff_gets_200_on_quotation_activity_but_403_on_payment_voucher_activity()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var perms = await RolePermissionsAsync(db, co.CompanyId, "SALES_STAFF");
        perms.Should().Contain(Permissions.Sales.QuotationRead,
            "sanity — SALES_STAFF must hold its own quotation read permission");
        perms.Should().NotContain(Permissions.Purchase.PaymentVoucherRead,
            "sanity — SALES_STAFF must NOT hold the purchase-side payment voucher read permission (that's the whole point of this test)");

        var token = Issuer().Issue(new TokenClaims(
            UserId: 1, Username: "rbac-sales-staff-activity", CompanyId: co.CompanyId, BranchId: co.BranchId,
            IsSuperAdmin: false, Roles: ["SALES_STAFF"], Permissions: perms)).Token;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);

        using var quotationResp = await GetAsync(factory, token, "/quotations/1/activity");
        quotationResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "R3 — a role holding sales.quotation.read must see that quotation's activity, not a global-audit 403");

        using var pvResp = await GetAsync(factory, token, "/payment-vouchers/1/activity");
        pvResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a role without purchase.payment_voucher.read must stay 403 on that document's activity");
    }

    [SkippableFact]
    public async Task Ap_clerk_gets_200_on_payment_voucher_activity_but_403_on_quotation_activity()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var perms = await RolePermissionsAsync(db, co.CompanyId, "AP_CLERK");
        perms.Should().Contain(Permissions.Purchase.PaymentVoucherRead,
            "sanity — AP_CLERK must hold its own payment voucher read permission");
        perms.Should().NotContain(Permissions.Sales.QuotationRead,
            "sanity — AP_CLERK must NOT hold the sales-side quotation read permission (that's the whole point of this test)");

        var token = Issuer().Issue(new TokenClaims(
            UserId: 1, Username: "rbac-ap-clerk-activity", CompanyId: co.CompanyId, BranchId: co.BranchId,
            IsSuperAdmin: false, Roles: ["AP_CLERK"], Permissions: perms)).Token;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);

        using var pvResp = await GetAsync(factory, token, "/payment-vouchers/1/activity");
        pvResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "R3 — a role holding purchase.payment_voucher.read must see that payment voucher's activity, not a global-audit 403");

        using var quotationResp = await GetAsync(factory, token, "/quotations/1/activity");
        quotationResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a role without sales.quotation.read must stay 403 on that document's activity");
    }
}
