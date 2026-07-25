using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// specs/fix-army-findings-2026-07-22.md WP-E1/E2 — full HTTP-pipeline coverage for the
/// super-admin /settings/companies create + edit flows (real routing, model binding,
/// FluentValidation, JSON casing — none of which a direct <see cref="Accounting.Infrastructure.Master.CompanyService"/>
/// call exercises).
///
/// E1 (create ignores จด VAT off): investigated end-to-end (FE payload builder, DTO, validator,
/// <c>CompanyService.CreateAsync</c>) and found NO defect — <c>vatRegistered: false</c> threads
/// through untouched at every layer; this test proves it persists via the real endpoint.
///
/// E2 (edit 500) note: this test runs against <see cref="RbacApiFactory"/>'s teas_test
/// connection, which — like every other test in this suite — connects as a Postgres SUPERUSER
/// and therefore BYPASSES RLS (see troubles-wiki.md). It exercises the full request pipeline but
/// canNOT by itself prove the RLS fix; <see cref="Accounting.Api.Tests.Persistence.CompanyUpdateRlsTests"/>
/// is the authoritative regression test (uses the portable non-bypass `pg_database_owner` role
/// trick to actually enforce RLS, matching prod's NOBYPASSRLS app role).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyVatFlagHttpTests
{
    private readonly PostgresFixture _fx;
    public CompanyVatFlagHttpTests(PostgresFixture fx) => _fx = fx;

    private static string SuperAdminToken() => new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
    })).Issue(new TokenClaims(
        UserId: 990_998, Username: "wpe-super", CompanyId: 1, BranchId: 1,
        IsSuperAdmin: true, Roles: ["SUPER_ADMIN"], Permissions: [])).Token;

    private static async Task<(int Status, string Body)> SendAsync(
        HttpClient client, HttpMethod method, string route, string? json = null)
    {
        using var req = new HttpRequestMessage(method, route);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SuperAdminToken());
        using var resp = await client.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    /// <summary>Unlike <c>Accounting.TestKit.TestIds.TaxId()</c> (a bare random 13-digit string,
    /// fine for tests that call <c>ICompanyService.CreateAsync</c> directly — the FluentValidation
    /// checksum rule never runs there), this test goes through the REAL POST /companies endpoint,
    /// which DOES run <c>CreateCompanyValidator</c>'s checksum check. Computes a real
    /// mod-11-checksummed id (<see cref="Accounting.Domain.ValueObjects.ThaiTaxId"/>'s own
    /// algorithm) so the test is deterministic, not ~1-in-11 flaky.</summary>
    private static string ValidTaxId()
    {
        var twelve = $"0000{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelve[i] - '0') * (13 - i);
        var check = (11 - (sum % 11)) % 10;
        return twelve + check;
    }

    [SkippableFact]
    public async Task Create_with_vat_registered_false_persists_false()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var taxId = ValidTaxId();
        var body = JsonSerializer.Serialize(new
        {
            taxId, nameTh = "บริษัท repro non-vat", nameEn = (string?)null,
            legalEntityType = "LimitedCompany", registrationDate = (string?)null,
            vatRegistered = false, vatRegisterDate = (string?)null, fiscalYearStartMonth = 1,
            addressTh = (string?)null, subDistrict = (string?)null, district = (string?)null,
            province = "กรุงเทพมหานคร", postalCode = "10110", phone = (string?)null, email = (string?)null,
            paidUpCapital = (decimal?)null, vatRate = 0.07m, pnd30SubmissionMode = "manual",
        });

        var (status, respBody) = await SendAsync(client, HttpMethod.Post, "/companies", body);
        status.Should().Be(201, $"create should succeed: {respBody}");

        var (listStatus, listBody) = await SendAsync(client, HttpMethod.Get, "/companies");
        listStatus.Should().Be(200);
        using var doc = JsonDocument.Parse(listBody);
        var created = doc.RootElement.EnumerateArray()
            .Single(c => c.GetProperty("taxId").GetString() == taxId);
        created.GetProperty("vatRegistered").GetBoolean().Should().BeFalse(
            "the จด VAT toggle was off at create — must not silently persist true");
    }

    [SkippableFact]
    public async Task Update_flips_vat_registered_true_to_false()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            nameTh = co.NameTh, nameEn = (string?)null, vatRegistered = false, vatRegisterDate = (string?)null,
            addressTh = "99 ถ.ทดสอบ กรุงเทพฯ 10110", subDistrict = "ทุ่งมหาเมฆ", district = "เขตสาทร",
            province = "กรุงเทพมหานคร", postalCode = "10110", phone = (string?)null, email = (string?)null,
            isActive = true, paidUpCapital = (decimal?)null, vatRate = 0.07m, pnd30SubmissionMode = "manual",
        });

        var (status, respBody) = await SendAsync(client, HttpMethod.Put, $"/companies/{co.CompanyId}", body);
        status.Should().Be(204, $"flipping จด VAT off through the real edit form must not 500: {respBody}");

        var (getStatus, getBody) = await SendAsync(client, HttpMethod.Get, $"/companies/{co.CompanyId}");
        getStatus.Should().Be(200);
        using var doc = JsonDocument.Parse(getBody);
        doc.RootElement.GetProperty("vatRegistered").GetBoolean().Should().BeFalse();
    }
}
