using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.ETax;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.ETax;

/// <summary>
/// M3 (review 2026-07-04) — <see cref="ETaxRetryWorker.RunDueAsync"/> runs inside an
/// API-hosted <c>BackgroundService</c> tick (<c>ETaxRetryHostedService</c>) with no
/// per-request tenant pin, so its candidate scan + per-candidate freshness check are
/// legitimate cross-tenant admin reads over <c>etax.submissions</c>. 581 added FORCE RLS to
/// that table (H1), so under prod NOBYPASSRLS the fail-closed <c>company_isolation</c> policy
/// hid every row from both reads — a regression 581 introduced (before it there was no RLS to
/// block them), silently making the retry worker find nothing. <c>SET ROLE pg_database_owner</c>
/// (rolbypassrls=false — the repo's non-bypass-role trick, see
/// <see cref="Persistence.SalesChainRlsTests"/>) with <c>app.company_id</c> UNSET reproduces the
/// exact prod pre-pin condition. The per-item pipeline call is replaced with a
/// <see cref="FakePipeline"/> test double — it is explicitly OUT of this fix's scope (already
/// re-scopes by its own explicit companyId, per the design doc) and this test must not depend
/// on its own, separately-tracked RLS behaviour.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ETaxRetryWorkerRlsTests
{
    private readonly PostgresFixture _fx;
    public ETaxRetryWorkerRlsTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task RunDueAsync_finds_and_reattempts_a_pending_submission_under_NOBYPASSRLS()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        const long taxInvoiceId = 8_000_001; // etax.submissions.tax_invoice_id carries no FK

        await using (var seed = new NpgsqlConnection(_fx.ConnectionString))
        {
            await seed.OpenAsync();

            // A due, non-dead-letter SendFailed attempt — exactly what the scan targets.
            await using var insert = new NpgsqlCommand($@"
                INSERT INTO etax.submissions
                    (company_id, tax_invoice_id, attempted_at, attempt_no, outcome,
                     to_email_snapshot, redirect_applied, retry_after, dead_letter, created_at)
                VALUES
                    ({co.CompanyId}, {taxInvoiceId}, now(), 1, 'SendFailed',
                     'test@example.com', false, now() - interval '1 minute', false, now())", seed);
            await insert.ExecuteNonQueryAsync();

            await using var grant = new NpgsqlCommand(
                "GRANT USAGE ON SCHEMA etax TO pg_database_owner; " +
                "GRANT SELECT ON etax.submissions TO pg_database_owner;", seed);
            await grant.ExecuteNonQueryAsync();
        }

        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var fakePipeline = new FakePipeline();

        // Keep ONE physical connection open for the whole block so SET ROLE (session-scoped)
        // sticks across RunDueAsync's own internal transactions — same technique as
        // ApiKeyResolverRlsTests/VatRegisterSnapshotJobRlsTests.
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', '', false)");
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.is_super_admin', 'false', false)");
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var done = await ETaxRetryWorker.RunDueAsync(db, fakePipeline, new SystemClock(), default);

            // The scan is a legitimate CROSS-TENANT admin read, so on the suite's shared,
            // long-lived teas_test DB it may also pick up other due rows other tests left
            // behind — assert on OUR specific candidate rather than an exact total count.
            done.Should().BeGreaterThanOrEqualTo(1, "at least the seeded pending submission must be visible to the scan under the pin");
            fakePipeline.Calls.Should().Contain((taxInvoiceId, co.CompanyId),
                "the seeded due submission for this company/TI must have been re-attempted");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Records calls instead of running the real e-Tax pipeline — the per-item
    /// execution path is explicitly out of scope for this fix (design doc: "already re-scopes
    /// by its own explicit company_id") and must not entangle this test with its own,
    /// separately-tracked RLS behaviour.</summary>
    private sealed class FakePipeline : IETaxSubmissionPipeline
    {
        public readonly List<(long TaxInvoiceId, int CompanyId)> Calls = new();

        public Task EnqueueAsync(long taxInvoiceId, CancellationToken ct) =>
            throw new NotSupportedException("not exercised by RunDueAsync");

        public Task<string> RunAsync(long taxInvoiceId, int companyId, CancellationToken ct)
        {
            Calls.Add((taxInvoiceId, companyId));
            return Task.FromResult("ok");
        }
    }
}
