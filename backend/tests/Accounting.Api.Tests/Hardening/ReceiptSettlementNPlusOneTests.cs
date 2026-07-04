using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// M9 (review 2026-07-04) — <see cref="ReceiptService.PostAsync"/>'s billing-note settlement
/// loop used to issue 2 extra DB round-trips PER affected Billing Note (a query for its linked
/// TI ids, then a query summing their AmountPaid) inside the post transaction. Fixed by batching
/// into one query keyed on all affected BNs' ids. This test posts a Receipt that settles TWO
/// billing notes at once and asserts (a) both still settle correctly (no functional regression)
/// and (b) exactly ONE command referencing <c>billing_note_tax_invoices</c> is executed during
/// the post — proving the query no longer scales with the number of affected BNs (pre-fix this
/// would have been 2, one per BN).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReceiptSettlementNPlusOneTests
{
    private readonly PostgresFixture _fx;
    public ReceiptSettlementNPlusOneTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Posting_a_receipt_that_settles_two_billing_notes_batches_the_lookup_into_one_query()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long ti1, ti2, bn1, bn2;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId))
        {
            await using var s = sp.CreateAsyncScope();
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();

            ti1 = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                DateOnly.FromDateTime(DateTime.Now), t.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "M9 ti1 line", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            await tiSvc.PostAsync(ti1, default);

            ti2 = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                DateOnly.FromDateTime(DateTime.Now), t.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "M9 ti2 line", 1m, 1, "ชิ้น", 2000m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            await tiSvc.PostAsync(ti2, default);

            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var today = DateOnly.FromDateTime(DateTime.Now);

            bn1 = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                today, today.AddDays(30), t.CustomerId, null, null, [ti1], "THB", 1m, null, null,
                [new BillingLineInput(null, ti1, "M9 bn1 line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await bnSvc.IssueAsync(bn1, default);

            bn2 = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                today, today.AddDays(30), t.CustomerId, null, null, [ti2], "THB", 1m, null, null,
                [new BillingLineInput(null, ti2, "M9 bn2 line", 1m, "ชิ้น", 2000m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await bnSvc.IssueAsync(bn2, default);
        }

        // Separate, freshly-built provider with a command counter — isolates the measurement to
        // ONLY the receipt-post call below, not the setup above (which also touches
        // billing_note_tax_invoices while building the links, and would inflate the count).
        var counter = new CommandCountingLoggerProvider("billing_note_tax_invoices");
        await using (var sp = BuildProviderWithCounter(_fx.ConnectionString, t.CompanyId, t.BranchId, counter))
        {
            await using var s = sp.CreateAsyncScope();
            var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                DateOnly.FromDateTime(DateTime.Now), t.CustomerId, Accounting.Domain.Enums.PaymentMethod.Transfer,
                null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(ti1, 1070m, null, null),
                 new ReceiptApplicationInput(ti2, 2140m, null, null)]),
                default);
            await rcSvc.PostAsync(rcId, default);
        }

        await using var check = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);
        await using var cs = check.CreateAsyncScope();
        var db = cs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn1Status = await db.BillingNotes.AsNoTracking()
            .Where(b => b.BillingNoteId == bn1).Select(b => b.Status).FirstAsync();
        var bn2Status = await db.BillingNotes.AsNoTracking()
            .Where(b => b.BillingNoteId == bn2).Select(b => b.Status).FirstAsync();

        bn1Status.Should().Be(Accounting.Domain.Enums.BillingNoteStatus.Settled,
            "BN1 was fully paid by the receipt — settlement must still work post-fix");
        bn2Status.Should().Be(Accounting.Domain.Enums.BillingNoteStatus.Settled,
            "BN2 was fully paid by the receipt — settlement must still work post-fix");

        // 1 (the affectedBns scan's own EXISTS-subquery against billing_note_tax_invoices,
        // unchanged by this fix) + 1 (the NEW batched settlement lookup, keyed on ALL affected
        // BNs' ids in one query) = 2, REGARDLESS of how many BNs are affected. Pre-fix this
        // would have been 1 + N (one extra query PER affected BN) = 3 for these 2 BNs — see the
        // regression proof (git-stash) in the report for the scaling difference.
        counter.Count.Should().Be(2,
            "the settlement lookup must not scale with the number of affected BNs — pre-fix, " +
            "2 affected BNs would add 2 MORE queries (one per BN) on top of the scan's own EXISTS");
    }

    private static ServiceProvider BuildProviderWithCounter(
        string connectionString, int companyId, int branchId, ILoggerProvider extraProvider)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging(b => b.AddProvider(extraProvider));
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    /// <summary>Counts EF Core command-log entries whose SQL text contains <paramref name="match"/>
    /// (table name) — a minimal, non-invasive way to prove a query count WITHOUT touching
    /// production DbContext wiring. Only Information-level logs are inspected: EF Core logs the
    /// executed-command message (with SQL text) at Information by default, once per round-trip.</summary>
    private sealed class CommandCountingLoggerProvider(string match) : ILoggerProvider
    {
        public int Count;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == "Microsoft.EntityFrameworkCore.Database.Command"
                ? new CountingLogger(this, match)
                : NullLogger.Instance;

        public void Dispose() { }

        private sealed class CountingLogger(CommandCountingLoggerProvider owner, string match) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var msg = formatter(state, exception);
                if (msg.Contains(match, StringComparison.OrdinalIgnoreCase))
                    Interlocked.Increment(ref owner.Count);
            }
        }
    }
}
