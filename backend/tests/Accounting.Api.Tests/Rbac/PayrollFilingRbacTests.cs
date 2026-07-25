using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// WP-H (specs/fix-army-findings-2026-07-22.md, B2-pr F3) — the RD/SSO payroll filing PDFs
/// (ภ.ง.ด.1, ภ.ง.ด.1ก) were gated on <c>payroll.run.manage</c> only, so a TAX_OFFICER — who
/// holds <c>tax.filing.preview</c> (627_seed_tax_officer_filing_grant.sql) but not payroll
/// administration — got 403 on the filings themselves (live repro: nvtax01 on co6).
/// PayrollEndpoints.CanFile now gates those two endpoints on
/// <c>payroll.run.manage OR tax.filing.preview</c> (mirrors TaxAdjustmentNoteEndpoints' inline
/// RequireAssertion OR-set pattern). Payroll list/detail/create/approve/post/pay/payslip
/// endpoints are UNCHANGED (still RunManage-only) — not exercised here.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PayrollFilingRbacTests
{
    private readonly PostgresFixture _fx;
    public PayrollFilingRbacTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static async Task<HttpResponseMessage> SendAsAsync(
        RbacApiFactory factory, RoleGrants role, HttpMethod method, string url)
    {
        using var client = factory.CreateClient();
        var token = Issuer().Issue(new TokenClaims(
            UserId: 992_000 + role.RoleId, Username: $"rbac-payroll-{role.RoleCode}",
            CompanyId: RbacMatrixData.ReferenceCompanyId, BranchId: 1,
            IsSuperAdmin: role.IsSuperAdmin, Roles: [role.RoleCode],
            Permissions: role.Permissions.ToList())).Token;

        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    [SkippableFact]
    public async Task TaxOfficer_gets_past_the_gate_on_pnd1_and_pnd1a_pdf()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var taxOfficer = roles.Should().ContainSingle(r => r.RoleCode == "TAX_OFFICER").Which;
        taxOfficer.Permissions.Should().NotContain(Permissions.Payroll.RunManage,
            "the point of this test is TAX_OFFICER reaching the filing PDFs WITHOUT payroll admin");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);

        // No live payroll run exists for this probe — a non-existent id/year still passes the
        // RBAC gate (401/403 are decided before the handler ever touches the DB), same convention
        // as TaxOfficerFilingGrantTests' pnd30/pdf probe.
        using var pnd1 = await SendAsAsync(factory, taxOfficer, HttpMethod.Get,
            "/payroll/runs/999999/pnd1/pdf");
        ((int)pnd1.StatusCode).Should().NotBe(401).And.NotBe(403,
            "WP-H: TAX_OFFICER holds tax.filing.preview, which now satisfies PayrollEndpoints.CanFile");

        using var pnd1a = await SendAsAsync(factory, taxOfficer, HttpMethod.Get,
            "/payroll/pnd1a/pdf?year=2026");
        ((int)pnd1a.StatusCode).Should().NotBe(401).And.NotBe(403,
            "WP-H: same CanFile gate as pnd1/pdf");
    }

    [SkippableFact]
    public async Task Role_with_neither_payroll_nor_filing_grant_still_403s()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var outsider = roles.FirstOrDefault(r => !r.IsSuperAdmin
            && !r.Permissions.Contains(Permissions.Payroll.RunManage)
            && !r.Permissions.Contains(Permissions.Tax.FilingPreview));
        outsider.Should().NotBeNull(
            "at least one non-super role must hold neither grant, else this test proves nothing");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);

        using var resp = await SendAsAsync(factory, outsider!, HttpMethod.Get,
            "/payroll/runs/999999/pnd1/pdf");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            $"role {outsider!.RoleCode} holds neither payroll.run.manage nor tax.filing.preview");
    }

    [SkippableTheory]
    [InlineData("COMPANY_ADMIN")]
    [InlineData("CHIEF_ACCOUNTANT")]
    public async Task Existing_payroll_admin_roles_are_unchanged(string roleCode)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var role = roles.Should().ContainSingle(r => r.RoleCode == roleCode).Which;
        role.Permissions.Should().Contain(Permissions.Payroll.RunManage,
            $"{roleCode} must still hold payroll.run.manage directly (regression guard)");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);

        using var resp = await SendAsAsync(factory, role, HttpMethod.Get,
            "/payroll/runs/999999/pnd1/pdf");
        ((int)resp.StatusCode).Should().NotBe(401).And.NotBe(403,
            $"{roleCode} held payroll.run.manage before WP-H and must still pass the gate");
    }
}
