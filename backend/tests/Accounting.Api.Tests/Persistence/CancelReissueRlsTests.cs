using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Sales;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// cancel-reissue T20. teas_test connects as a BYPASSRLS role, so a plain service call proves nothing about RLS
/// (memory: rls-masked-by-superuser-tests). This drives the REAL services (real ActivityRecorder, real GL poster)
/// on ONE pinned connection after <c>SET ROLE pg_database_owner</c> (a NOBYPASSRLS built-in role) with the tenant
/// GUC set the way TenantMiddleware sets it, then proves cross-tenant ids are invisible and the 645 permission seed
/// is visible per company.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CancelReissueRlsTests
{
    private readonly PostgresFixture _fx;
    public CancelReissueRlsTests(PostgresFixture fx) => _fx = fx;

    private sealed record Probe(string Code, int Own, int Foreign, int Global);

    private static async Task GrantAllAsync(AccountingDbContext db)
    {
        var schemas = await db.Database
            .SqlQuery<string>($"SELECT nspname AS \"Value\" FROM pg_namespace WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema'")
            .ToListAsync();
#pragma warning disable EF1003 // schema names come from pg_namespace, not user input
        foreach (var sch in schemas)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"GRANT USAGE ON SCHEMA \"{sch}\" TO pg_database_owner; " +
                $"GRANT ALL ON ALL TABLES IN SCHEMA \"{sch}\" TO pg_database_owner; " +
                $"GRANT ALL ON ALL SEQUENCES IN SCHEMA \"{sch}\" TO pg_database_owner;");
#pragma warning restore EF1003
        }
    }

    /// <summary>Opens ONE connection for the scope, grants, switches to the policy-bound role and pins the tenant.</summary>
    private static async Task PinAsync(AccountingDbContext db, int companyId)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("RESET ROLE");
        await GrantAllAsync(db);
        await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.company_id', {companyId.ToString()}, false)");
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'false', false)");
    }

    [SkippableFact]
    public async Task Cancel_and_reissue_run_under_real_rls_and_are_company_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await CancelKit.VatCoAsync(_fx);
        var coB = await CancelKit.VatCoAsync(_fx);

        // Seed company A's documents through the normal (bypass) connection.
        long tiPaid, tiOther;
        long rcId;
        await using (var spSeed = CancelKit.Sp(_fx, coA))
        {
            tiPaid = await CancelKit.PostTiAsync(spSeed, coA.CustomerId);
            tiOther = await CancelKit.PostTiAsync(spSeed, coA.CustomerId);
            (rcId, _) = await CancelKit.PostReceiptAsync(spSeed, coA.CustomerId, tiPaid, 1070m);
        }

        long replTi;
        // ── Company A, GUC = A: the whole chain succeeds under RLS ──
        await using (var spA = CancelKit.Sp(_fx, coA))
        await using (var sA = spA.CreateAsyncScope())
        {
            var db = sA.ServiceProvider.GetRequiredService<AccountingDbContext>();
            try
            {
                await PinAsync(db, coA.CompanyId);
                (await db.Database.SqlQuery<bool>($"SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync())
                    .Should().BeFalse("the test must really run as a NOBYPASSRLS role");

                await sA.ServiceProvider.GetRequiredService<IReceiptService>().CancelAsync(rcId, "ISSUED_IN_ERROR", "rls", default);
                var res = await sA.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                    .CancelAndReissueAsync(tiPaid, "BUYER_DETAILS_ERROR", "rls", default);
                replTi = res.ReplacementTaxInvoiceId!.Value;
                res.ReversalJournalId.Should().BeGreaterThan(0);
                var posted = await sA.ServiceProvider.GetRequiredService<ITaxInvoiceService>().PostAsync(replTi, default);
                posted.DocNo.Should().NotBeNullOrEmpty();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("RESET ROLE");
                await db.Database.CloseConnectionAsync();
            }
        }

        // ── Company B, GUC = B: company A's ids do not exist ──
        await using (var spB = CancelKit.Sp(_fx, coB))
        await using (var sB = spB.CreateAsyncScope())
        {
            var db = sB.ServiceProvider.GetRequiredService<AccountingDbContext>();
            try
            {
                await PinAsync(db, coB.CompanyId);
                var ti = sB.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
                var rc = sB.ServiceProvider.GetRequiredService<IReceiptService>();
                (await CancelKit.CodeOfAsync(() => ti.CancelAsync(tiOther, "ISSUED_IN_ERROR", "x", default))).Should().Be("ti.not_found");
                (await CancelKit.CodeOfAsync(() => ti.CancelAndReissueAsync(tiOther, "BUYER_DETAILS_ERROR", "x", default))).Should().Be("ti.not_found");
                (await CancelKit.CodeOfAsync(() => ti.ReissueAsync(tiPaid, default))).Should().Be("ti.not_found");
                (await CancelKit.CodeOfAsync(() => ti.DiscardReplacementAsync(replTi, default))).Should().Be("ti.not_found");
                (await CancelKit.CodeOfAsync(() => rc.CancelAsync(rcId, "ISSUED_IN_ERROR", "x", default))).Should().Be("rc.not_found");
                (await CancelKit.CodeOfAsync(() => rc.ReissueAsync(rcId, default))).Should().Be("rc.not_found");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("RESET ROLE");
                await db.Database.CloseConnectionAsync();
            }
        }

        // Nothing leaked: company A's untouched TI is still Posted, the replacement is Posted.
        await using var spCheck = CancelKit.Sp(_fx, coA);
        (await CancelKit.WithDbAsync(spCheck, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiOther)))
            .Status.Should().Be(DocumentStatus.Posted);
        (await CancelKit.WithDbAsync(spCheck, db => db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == replTi)))
            .Status.Should().Be(DocumentStatus.Posted);
    }

    [SkippableFact]
    public async Task Cancel_permission_seed_is_visible_per_company_under_rls()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await CancelKit.VatCoAsync(_fx);
        var coB = await CancelKit.VatCoAsync(_fx);

        foreach (var co in new[] { coA, coB })
        {
            await using var sp = CancelKit.Sp(_fx, co);
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            try
            {
                await PinAsync(db, co.CompanyId);
                var cid = co.CompanyId;
                var rows = await db.Database.SqlQuery<Probe>($@"
                    SELECT p.permission_code AS ""Code"",
                           count(*) FILTER (WHERE rp.company_id = {cid})::int AS ""Own"",
                           count(*) FILTER (WHERE rp.company_id IS NOT NULL AND rp.company_id <> {cid})::int AS ""Foreign"",
                           count(*) FILTER (WHERE rp.company_id IS NULL)::int AS ""Global""
                    FROM sys.role_permissions rp
                    JOIN sys.permissions p ON p.permission_id = rp.permission_id
                    WHERE p.permission_code IN ('sales.tax_invoice.cancel', 'sales.receipt.cancel', 'sales.billing_note.cancel')
                    GROUP BY p.permission_code").ToListAsync();
                rows.Select(r => r.Code).Should().BeEquivalentTo(
                    ["sales.tax_invoice.cancel", "sales.receipt.cancel", "sales.billing_note.cancel"]);
                rows.Should().OnlyContain(r => r.Own >= 2, "CHIEF_ACCOUNTANT and COMPANY_ADMIN hold every cancel code in each company");
                rows.Should().OnlyContain(r => r.Foreign == 0, "RLS shows no other company grants (system-global SUPER_ADMIN rows are visible by design)");
                rows.Should().OnlyContain(r => r.Global >= 1, "SUPER_ADMIN holds every cancel code (645 step 4/4b)");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("RESET ROLE");
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
