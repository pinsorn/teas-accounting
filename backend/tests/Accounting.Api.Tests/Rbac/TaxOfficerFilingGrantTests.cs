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
/// CRIT-2 (specs/fix-swarm-crit-numbering-rbac.md) — 627_seed_tax_officer_filing_grant.sql grants
/// TAX_OFFICER tax.filing.preview + tax.filing.read (preview/export/read scope only; NOT finalize).
/// Root cause: TaxFilingEndpoints.cs gates ภ.พ.30/ภ.ง.ด.3/53/54/36/51/50 preview+PDF+.txt export on
/// tax.filing.preview, which 530_seed_rbac_grant_reconcile.sql never granted TAX_OFFICER — a role
/// holding tax.pnd30.read etc. still got 403 on every actual filing page.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxOfficerFilingGrantTests
{
    private readonly PostgresFixture _fx;
    public TaxOfficerFilingGrantTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    [SkippableFact]
    public async Task TaxOfficer_resolves_filing_preview_and_read_but_not_finalize()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(sp);
        var taxOfficer = roles.Should().ContainSingle(r => r.RoleCode == "TAX_OFFICER").Which;

        taxOfficer.Permissions.Should().Contain("tax.filing.preview",
            "TAX_OFFICER must be able to preview a tax filing (the CRIT-2 root cause)");
        taxOfficer.Permissions.Should().Contain("tax.filing.read",
            "TAX_OFFICER must be able to view filing history (/tax-filings)");
        taxOfficer.Permissions.Should().NotContain("tax.filing.finalize",
            "finalize/close-period stays a Chief/Admin-only action (separate SoD line) — " +
            "preview/export scope ONLY");
    }

    [SkippableFact]
    public async Task TaxOfficer_hits_the_pnd30_pdf_endpoint_without_a_403_after_the_grant()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var fx = _fx.BuildServiceProvider();
        var (roles, _) = await RbacMatrixData.LoadAsync(fx);
        var taxOfficer = roles.Should().ContainSingle(r => r.RoleCode == "TAX_OFFICER").Which;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Issuer().Issue(new TokenClaims(
            UserId: 991_234, Username: "rbac-taxofficer-filing",
            CompanyId: RbacMatrixData.ReferenceCompanyId, BranchId: 1,
            IsSuperAdmin: false, Roles: ["TAX_OFFICER"],
            Permissions: taxOfficer.Permissions.ToList())).Token;

        var period = DateTime.UtcNow.Year * 100 + DateTime.UtcNow.Month;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/tax-filings/pnd30/pdf?period={period}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);

        var status = (int)resp.StatusCode;
        status.Should().NotBe(401, "the minted JWT must authenticate");
        status.Should().NotBe(403,
            "CRIT-2: before the grant this 403'd for every TAX_OFFICER despite the role holding " +
            "tax.pnd30.read — the actual gate is tax.filing.preview, now granted");
    }
}
