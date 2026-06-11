using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Entities.Audit;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// Per-company-vat-mode spec (§4.6 amendment, 2026-06-11) — VAT mode/rate/ภ.พ.30
/// mode are company master data on master.companies:
///  • CompanyTaxConfigService serves the CURRENT tenant's row (different companies
///    see different configs). /system/info and /api/v1/system/info return exactly
///    these values via the same service (both RequireAuthorization).
///  • Every tax-field change through ICompanyService.UpdateAsync (PUT /companies/{id})
///    writes an audit.activity_log row (action "tax_config_change"); a non-tax change
///    does not.
///  • Bad values are rejected twice: FluentValidation (PUT → 400 ValidationProblem,
///    see MasterEndpoints) and the DB ck constraint ck_companies_pnd30_submission_mode.
/// Real Postgres.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyTaxConfigTests
{
    private readonly PostgresFixture _fx;
    public CompanyTaxConfigTests(PostgresFixture fx) => _fx = fx;

    private static async Task<CompanyTaxConfig> ConfigOf(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<ICompanyTaxConfigService>()
            .GetAsync(default);
    }

    // ── 1. Service reads the tenant's company row (§4.6 per-company-vat-mode) ──
    [SkippableFact]
    public async Task TaxConfig_is_per_company_row()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var vat = await TestCompanyFactory.CreateAsync(_fx.ConnectionString,
            vatRegistered: true, vatRate: 0.07m, pnd30SubmissionMode: "manual");
        // NOTE: vat_rate 0 cannot be persisted on INSERT — EF HasDefaultValue(0.07m)
        // treats the decimal CLR default 0 as "unset" and lets the DB default win
        // (flagged to main agent). A distinct non-zero rate still proves per-row reads.
        var nonVat = await TestCompanyFactory.CreateAsync(_fx.ConnectionString,
            vatRegistered: false, vatRate: 0.05m, pnd30SubmissionMode: "auto");

        await using var spVat = TestCompanyFactory.BuildProvider(_fx.ConnectionString, vat.CompanyId, vat.BranchId);
        await using var spNon = TestCompanyFactory.BuildProvider(_fx.ConnectionString, nonVat.CompanyId, nonVat.BranchId);

        var cfgVat = await ConfigOf(spVat);
        cfgVat.VatMode.Should().BeTrue();
        cfgVat.VatRate.Should().Be(0.07m);
        cfgVat.Pnd30SubmissionMode.Should().Be("manual");

        var cfgNon = await ConfigOf(spNon);
        cfgNon.VatMode.Should().BeFalse();
        cfgNon.VatRate.Should().Be(0.05m);
        cfgNon.Pnd30SubmissionMode.Should().Be("auto");
    }

    // ── 2. Company 1 (seeded) → the values /system/info serves a company-1 token ──
    // Program.cs /system/info and ApiV1Endpoints /api/v1/system/info read vat_mode /
    // vat_rate / pnd30_submission_mode from ICompanyTaxConfigService for the caller's
    // company; this pins the source values for the seeded tenant.
    [SkippableFact]
    public async Task Company1_tax_config_matches_seed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1);

        var cfg = await ConfigOf(sp);
        cfg.VatMode.Should().BeTrue("company 1 is seeded VAT-registered (seed 120)");
        cfg.VatRate.Should().Be(0.07m, "CompanyTaxConfig migration default");
        cfg.Pnd30SubmissionMode.Should().Be("manual", "CompanyTaxConfig migration default");
    }

    // ── 3. Tax-field change is audited; a non-tax change is not (§4.6) ──────
    [SkippableFact]
    public async Task Update_audits_tax_config_change_only_for_tax_fields()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        async Task<int> AuditRows()
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            return await db.ActivityLogs.IgnoreQueryFilters().CountAsync(a =>
                a.EntityType == "company" && a.EntityId == c.CompanyId
                && a.ActivityType == "tax_config_change");
        }

        UpdateCompanyRequest Req(string? phone, decimal vatRate) => new(
            c.NameTh, null, true, new DateOnly(2020, 1, 1),
            "99 ถ.ทดสอบ กรุงเทพฯ 10110", null, null, null, null,
            phone, null, true, null, vatRate, "manual");

        // Phone-only change → NO tax_config_change row.
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ICompanyService>()
                .UpdateAsync(c.CompanyId, Req("02-111-2222", 0.07m), default);
        (await AuditRows()).Should().Be(0, "a non-tax change must not be logged as tax_config_change");

        // VatRate change → exactly one audited row with old → new values.
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ICompanyService>()
                .UpdateAsync(c.CompanyId, Req("02-111-2222", 0.05m), default);
        (await AuditRows()).Should().Be(1);

        await using (var s2 = sp.CreateAsyncScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var row = await db.ActivityLogs.IgnoreQueryFilters().SingleAsync(a =>
                a.EntityType == "company" && a.EntityId == c.CompanyId
                && a.ActivityType == "tax_config_change");
            row.Module.Should().Be("master");
            row.MetadataJson.Should().Contain("vat_rate").And.Contain("0.05");
        }
    }

    // ── 4. PUT /companies/{id} rejects bad values with 400 (ValidationProblem) ──
    // MasterEndpoints runs UpdateCompanyValidator before ICompanyService.UpdateAsync;
    // an invalid result returns Results.ValidationProblem → HTTP 400.
    [Fact]
    public void Validator_rejects_bogus_pnd30_mode_and_out_of_range_vat_rate()
    {
        var v = new UpdateCompanyValidator();
        UpdateCompanyRequest Req(decimal vatRate, string mode) => new(
            "บริษัททดสอบ", null, true, null, null, null, null, null, null,
            null, null, true, null, vatRate, mode);

        var bogusMode = v.Validate(Req(0.07m, "bogus"));
        bogusMode.IsValid.Should().BeFalse();
        bogusMode.Errors.Should().Contain(e => e.PropertyName == "Pnd30SubmissionMode");

        var badRate = v.Validate(Req(2m, "manual"));
        badRate.IsValid.Should().BeFalse();
        badRate.Errors.Should().Contain(e => e.PropertyName == "VatRate");

        v.Validate(Req(0.07m, "manual")).IsValid.Should().BeTrue();
        v.Validate(Req(0m, "auto")).IsValid.Should().BeTrue();
    }

    // ── 5. GET /companies/{id} detail carries the full updatable row (the FE
    //       super-admin edit form prefills from it — PUT is whole-row replace) ──
    [SkippableFact]
    public async Task Get_detail_returns_full_row_including_tax_fields()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString,
            vatRegistered: false, vatRate: 0.05m, pnd30SubmissionMode: "auto");
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        await using var s = sp.CreateAsyncScope();
        var dto = await s.ServiceProvider.GetRequiredService<ICompanyService>()
            .GetAsync(c.CompanyId, default);

        dto.CompanyId.Should().Be(c.CompanyId);
        dto.NameTh.Should().Be(c.NameTh);
        dto.VatRegistered.Should().BeFalse();
        dto.VatRate.Should().Be(0.05m);
        dto.Pnd30SubmissionMode.Should().Be("auto");
        dto.FiscalYearStartMonth.Should().Be(1);
    }

    // ── 6. DB backstop: ck_companies_pnd30_submission_mode rejects bad mode ──
    [SkippableFact]
    public async Task Db_check_constraint_rejects_bad_pnd30_mode()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var act = () => db.Database.ExecuteSqlRawAsync(
            "UPDATE master.companies SET pnd30_submission_mode = 'bogus' WHERE company_id = {0}",
            c.CompanyId);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation); // 23514
    }
}
