using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.ETax;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.ETax;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 13c — e-Tax submission pipeline + retry/dead-letter + append-only
/// audit trigger (integration, real Postgres). End-to-end Tier-1 (real
/// MailHog) is covered by the e2e etax-pipeline-mock.spec.ts.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint13cEtaxPipelineTests
{
    private readonly PostgresFixture _fx;
    public Sprint13cEtaxPipelineTests(PostgresFixture fx) => _fx = fx;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public DateOnly TodayInBangkok() => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
    private sealed class FakeBuilder(string xml) : IETaxXmlBuilder
    { public string BuildTaxInvoiceXml(long id, CancellationToken ct) => xml; }
    private sealed class FakeSigner : IETaxSigner
    {
        public Task<ETaxSignedDocument> SignAsync(string xml, CancellationToken ct)
            => Task.FromResult(new ETaxSignedDocument(xml, "deadbeef", System.Text.Encoding.UTF8.GetBytes(xml)));
    }
    private sealed class ThrowingSigner : IETaxSigner
    {
        public Task<ETaxSignedDocument> SignAsync(string xml, CancellationToken ct)
            => throw new DomainException("etax.pfx_missing", "Signing certificate not found.");
    }
    private sealed class OkValidator : IETaxXmlValidator
    {
        public Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct)
            => Task.FromResult(new XmlValidationResult(true, []));
    }
    private sealed class BadValidator : IETaxXmlValidator
    {
        public Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct)
            => Task.FromResult(new XmlValidationResult(false, ["missing required element X"]));
    }
    private sealed class FakeStorage : IFileStorageService
    {
        public Task<string> SaveAsync(int c, string pt, long pid, Stream s, string fn, CancellationToken ct)
            => Task.FromResult($"{c}/{pt}/{pid}/{fn}");
        public Task<Stream> OpenReadAsync(string p, CancellationToken ct) => Task.FromResult(Stream.Null);
        public Task DeleteAsync(string p, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string p, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class FakeEmail(ETaxDeliveryResult res) : IETaxEmailSender
    {
        public Task<ETaxDeliveryResult> SendAsync(string to, string subj,
            ETaxSignedDocument xml, byte[]? pdf, CancellationToken ct) => Task.FromResult(res);
    }

    private AccountingDbContext Db(out ServiceProvider sp, int companyId = 1, long userId = 1)
    {
        sp = _fx.BuildServiceProvider(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false });
        return sp.GetRequiredService<AccountingDbContext>();
    }

    private static ETaxSubmissionPipeline Pipeline(
        AccountingDbContext db, IClock clock,
        IETaxXmlBuilder builder, IETaxSigner signer, IETaxXmlValidator validator,
        IETaxEmailSender email, bool requireSchema = false, int retryAttempts = 6)
        => new(db, new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1 }, clock,
            builder, signer, validator, email, new FakeStorage(),
            Options.Create(new ETaxValidationOptions { RequireSchemaPass = requireSchema }),
            Options.Create(new ETaxSubmissionOptions
                { RetryAttempts = retryAttempts, BackoffSchedule = ["1m", "5m", "15m", "1h", "4h", "24h"] }),
            NullLogger<ETaxSubmissionPipeline>.Instance);

    private static async Task<long> SeedTiAsync(AccountingDbContext db, string? email)
    {
        var sfx = Guid.NewGuid().ToString("N")[..8];
        var cust = new Customer
        {
            CompanyId = 1, CustomerCode = "C-" + sfx, CustomerType = CustomerType.Corporate,
            NameTh = "ลูกค้า " + sfx, Email = email,
        };
        db.Customers.Add(cust);
        await db.SaveChangesAsync(default);

        var ti = new TaxInvoice
        {
            CompanyId = 1, BranchId = 1, CustomerId = cust.CustomerId,
            DocNo = "TI-" + sfx, DocDate = new(2026, 5, 1), TaxPointDate = new(2026, 5, 1),
            SupplierTaxId = "0000000000000", SupplierBranchCode = "00000",
            SupplierBranchName = "HQ", SupplierName = "TEAS", SupplierAddress = "BKK",
            CustomerName = "ลูกค้า " + sfx, CustomerAddress = "BKK",
            CurrencyCode = "THB", Status = DocumentStatus.Posted,
        };
        db.TaxInvoices.Add(ti);
        await db.SaveChangesAsync(default);
        return ti.TaxInvoiceId;
    }

    private static Task<ETaxSubmission?> LatestAsync(AccountingDbContext db, long tiId) =>
        db.ETaxSubmissions.IgnoreQueryFilters()
            .Where(s => s.TaxInvoiceId == tiId)
            .OrderByDescending(s => s.AttemptNo).FirstOrDefaultAsync();

    [SkippableFact]
    public async Task Tier1_send_ok_records_audit_row_with_redirect()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var tiId = await SeedTiAsync(db, "real-customer@acme.co");

        var p = Pipeline(db, new TestClock(), new FakeBuilder("<Inv/>"), new FakeSigner(),
            new OkValidator(),
            new FakeEmail(new ETaxDeliveryResult(true, DateTimeOffset.UtcNow, "MSG-1", null,
                To: "dev@localhost", Cc: "dev@localhost", Redirected: true)));
        var outcome = await p.RunAsync(tiId, 1, default);

        outcome.Should().Be("SendOk");
        var row = await LatestAsync(db, tiId);
        row!.Outcome.Should().Be(ETaxSubmissionOutcome.SendOk);
        row.RedirectApplied.Should().BeTrue();
        row.IntendedToEmail.Should().Be("real-customer@acme.co");
        row.ToEmailSnapshot.Should().Be("dev@localhost");
        row.XmlSha256.Should().Be("deadbeef");
        row.SignedXmlPath.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Signer_missing_pfx_records_send_failed_not_crash()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var tiId = await SeedTiAsync(db, "c@acme.co");

        var p = Pipeline(db, new TestClock(), new FakeBuilder("<Inv/>"), new ThrowingSigner(),
            new OkValidator(), new FakeEmail(new ETaxDeliveryResult(true, default, "", null)));
        var outcome = await p.RunAsync(tiId, 1, default);

        outcome.Should().Be("SendFailed");
        var row = await LatestAsync(db, tiId);
        row!.Outcome.Should().Be(ETaxSubmissionOutcome.SendFailed);
        row.Notes.Should().Contain("etax.pfx_missing");
        row.RetryAfter.Should().NotBeNull();        // scheduled for retry
        row.DeadLetter.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Xsd_validation_failure_aborts_and_records()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var tiId = await SeedTiAsync(db, "c@acme.co");

        var p = Pipeline(db, new TestClock(), new FakeBuilder("<Bad/>"), new FakeSigner(),
            new BadValidator(),
            new FakeEmail(new ETaxDeliveryResult(true, default, "", null)),
            requireSchema: true);
        var outcome = await p.RunAsync(tiId, 1, default);

        outcome.Should().Be("SendFailed");
        (await LatestAsync(db, tiId))!.Notes.Should().Contain("XSD validation failed");
    }

    [SkippableFact]
    public async Task Whitelist_violation_is_recorded_as_send_failed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var tiId = await SeedTiAsync(db, "x@blocked.com");

        var realSender = new ETaxEmailSender(Options.Create(new ETaxEmailOptions
        {
            SmtpHost = "localhost", FromEmail = "noreply@teas.local",
            WhitelistDomains = ["allowed.com"],   // blocked.com → violation before SMTP
        }));
        var p = Pipeline(db, new TestClock(), new FakeBuilder("<Inv/>"), new FakeSigner(),
            new OkValidator(), realSender);
        var outcome = await p.RunAsync(tiId, 1, default);

        outcome.Should().Be("SendFailed");
        (await LatestAsync(db, tiId))!.Notes.Should().Contain("whitelist_violation");
    }

    [SkippableFact]
    public async Task Retry_worker_picks_up_a_due_failed_submission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var clock = new TestClock();
        long tiId = Random.Shared.Next(100_000_000, 200_000_000);   // no TI → re-attempt yields NotApplicable

        db.ETaxSubmissions.Add(new ETaxSubmission
        {
            CompanyId = 1, TaxInvoiceId = tiId, AttemptNo = 1,
            Outcome = ETaxSubmissionOutcome.SendFailed, ToEmailSnapshot = "c@x.co",
            AttemptedAt = clock.UtcNow.AddMinutes(-10),
            RetryAfter = clock.UtcNow.AddMinutes(-1), CreatedAt = clock.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync(default);

        var p = Pipeline(db, clock, new FakeBuilder("<Inv/>"), new FakeSigner(),
            new OkValidator(), new FakeEmail(new ETaxDeliveryResult(true, default, "", null)));
        var n = await ETaxRetryWorker.RunDueAsync(db, p, clock, default);

        n.Should().BeGreaterThanOrEqualTo(1);
        var row = await LatestAsync(db, tiId);
        row!.AttemptNo.Should().Be(2);          // worker re-invoked the pipeline
    }

    [SkippableFact]
    public async Task Seventh_attempt_dead_letters()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var clock = new TestClock();
        long tiId = Random.Shared.Next(200_000_000, 300_000_000);

        for (var i = 1; i <= 6; i++)
            db.ETaxSubmissions.Add(new ETaxSubmission
            {
                CompanyId = 1, TaxInvoiceId = tiId, AttemptNo = i,
                Outcome = ETaxSubmissionOutcome.SendFailed, ToEmailSnapshot = "c@x.co",
                AttemptedAt = clock.UtcNow.AddMinutes(-10),
                RetryAfter = clock.UtcNow.AddMinutes(-1), CreatedAt = clock.UtcNow.AddMinutes(-10),
            });
        await db.SaveChangesAsync(default);

        var p = Pipeline(db, clock, new FakeBuilder("<Inv/>"), new FakeSigner(),
            new OkValidator(), new FakeEmail(new ETaxDeliveryResult(true, default, "", null)));
        await ETaxRetryWorker.RunDueAsync(db, p, clock, default);   // attempt 7 → dead-letter

        var row = await LatestAsync(db, tiId);
        row!.AttemptNo.Should().Be(7);
        row.DeadLetter.Should().BeTrue();
        row.RetryAfter.Should().BeNull();
    }

    [SkippableFact]
    public async Task Etax_submissions_is_append_only()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var db = Db(out var sp); await using var _ = sp;
        var row = new ETaxSubmission
        {
            CompanyId = 1, TaxInvoiceId = Random.Shared.Next(300_000_000, 400_000_000),
            AttemptNo = 1, Outcome = ETaxSubmissionOutcome.SendOk, ToEmailSnapshot = "a@b.co",
            AttemptedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ETaxSubmissions.Add(row);
        await db.SaveChangesAsync(default);

        row.Notes = "tampered";
        var update = () => db.SaveChangesAsync(default);
        (await update.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException!.Message.Should().Contain("immutable");
    }
}
