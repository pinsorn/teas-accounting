using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.Infrastructure.Audit;
using Accounting.Infrastructure.Master;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// specs/fix-army-findings-2026-07-22.md WP-E2 — root cause of the live prod 500 on
/// PUT /companies/{id} (co6, vatRegistered flip): <see cref="CompanyService.UpdateAsync"/>
/// calls <see cref="IActivityRecorder.Record"/> (queues an <c>audit.activity_log</c> insert on
/// the change tracker) whenever a tax field changes, then a single <c>SaveChangesAsync</c>
/// commits BOTH the companies-row UPDATE and the activity_log INSERT in one implicit
/// transaction — but unlike <see cref="CompanyService.CreateAsync"/> (fixed by
/// specs/fix-company-create-rls-atomic.md, commit 4b92efd), UpdateAsync never re-pins
/// <c>app.company_id</c> to the TARGET company first. <c>audit.activity_log</c> carries RLS
/// (600_superadmin_scoped_rls.sql, G3: <c>company_id = current_setting('app.company_id') OR
/// company_id IS NULL OR app.bypass_rls</c>) — a super-admin's session is pinned to THEIR OWN
/// company (TenantMiddleware), not the row they're editing. Editing any OTHER company's tax
/// fields (VatRegistered/VatRate/Pnd30SubmissionMode) queues an activity_log row whose
/// company_id (the target) mismatches the session's app.company_id GUC, so the INSERT's
/// implicit RLS WITH CHECK fails, SaveChangesAsync throws an unhandled PostgresException,
/// the WHOLE transaction rolls back (the vat flag flip never lands either), and the
/// unmapped exception surfaces as a generic 500 (matches the live repro: PUT vatRegistered
/// on co6 500'd twice, no partial write).
///
/// Reproduces the exact RLS-enforced shape <see cref="CompanyCreateRlsTests"/> uses (portable
/// NON-bypass role trick — teas_test's normal connection is a Postgres SUPERUSER and would
/// silently bypass RLS, masking this exact class of prod-only bug).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyUpdateRlsTests
{
    private readonly PostgresFixture _fx;
    public CompanyUpdateRlsTests(PostgresFixture fx) => _fx = fx;

    private static async Task ExecAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task UpdateAsync_flipping_vat_registered_on_another_company_does_not_500_under_rls()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Two companies, both seeded on the bypass (superuser) connection like every other
        // fixture company — "own" is the super-admin caller's OWN tenant (session pinned
        // here by TenantMiddleware in real life); "target" is a DIFFERENT company being
        // edited from the super-admin /settings/companies screen (co6's exact shape).
        var own = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var target = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        await ExecAsync(c,
            "GRANT USAGE ON SCHEMA master, audit TO pg_database_owner; " +
            "GRANT ALL ON ALL TABLES IN SCHEMA master, audit TO pg_database_owner;");

        Exception? thrown = null;
        try
        {
            await ExecAsync(c, "SET ROLE pg_database_owner");
            // Exactly what TenantMiddleware pins for the super-admin caller: their OWN
            // company, SESSION-scoped — NOT the target company's id.
            await ExecAsync(c, $"SELECT set_config('app.company_id', '{own.CompanyId}', false)");

            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseNpgsql(c).UseSnakeCaseNamingConvention().Options;
            // tenant pinned to the CALLER's own company — the real ActivityRecorder only reads
            // UserId/Username off it (audit-row content), matching what a real super-admin
            // request's DI-resolved ITenantContext would carry.
            var callerTenant = new StubTenant
            { CompanyId = own.CompanyId, BranchId = own.BranchId, UserId = 990_997, Username = "wpe-super", IsSuperAdmin = true };
            await using var db = new AccountingDbContext(options, tenant: callerTenant);
            var svc = new CompanyService(db, new ActivityRecorder(db, callerTenant));

            // Full-form PUT flipping VatRegistered true->false — same tax-field-changed
            // condition that queues the audit.activity_log insert.
            await svc.UpdateAsync(target.CompanyId, new UpdateCompanyRequest(
                target.NameTh, null, false, new DateOnly(2020, 1, 1),
                "99 ถ.ทดสอบ กรุงเทพฯ 10110", "ทุ่งมหาเมฆ", "เขตสาทร", "กรุงเทพมหานคร", "10110",
                null, null, true, null, 0.07m, "manual"), default);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        finally
        {
            await ExecAsync(c, "RESET ROLE");
        }

        Console.WriteLine($"[CompanyUpdateRlsTests] thrown={thrown}");
        thrown.Should().BeNull(
            "flipping a tax field on another company from a super-admin session must not 42501 " +
            $"on the audit.activity_log RLS check (was: {thrown})");

        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var rdb = new AccountingDbContext(opts, tenant: null);
        var row = await rdb.Companies.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == target.CompanyId);
        row.VatRegistered.Should().BeFalse("the update must actually persist, not roll back");
    }
}
