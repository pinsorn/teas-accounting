using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.ETax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.ETax;

public sealed class ETaxSubmissionOptions
{
    public int RetryAttempts { get; init; } = 6;
    public string[] BackoffSchedule { get; init; } = ["1m", "5m", "15m", "1h", "4h", "24h"];
}

/// <summary>
/// Sprint 13c — build → sign → validate → send, with one append-only
/// <c>etax.submissions</c> row per attempt. Tenant-free at the core
/// (<see cref="RunAsync"/> takes an explicit companyId) so the retry worker —
/// which has no JWT context — can reuse it. In-process best-effort; failures
/// carry a backoff <c>retry_after</c>; the schedule exhausting → dead-letter.
/// </summary>
public sealed class ETaxSubmissionPipeline : IETaxSubmissionPipeline
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IETaxXmlBuilder _builder;
    private readonly IETaxSigner _signer;
    private readonly IETaxXmlValidator _validator;
    private readonly IETaxEmailSender _email;
    private readonly IFileStorageService _storage;
    private readonly ETaxValidationOptions _valOpts;
    private readonly ETaxSubmissionOptions _subOpts;
    private readonly ILogger<ETaxSubmissionPipeline> _log;

    public ETaxSubmissionPipeline(
        AccountingDbContext db, ITenantContext tenant, IClock clock,
        IETaxXmlBuilder builder, IETaxSigner signer, IETaxXmlValidator validator,
        IETaxEmailSender email, IFileStorageService storage,
        IOptions<ETaxValidationOptions> valOpts, IOptions<ETaxSubmissionOptions> subOpts,
        ILogger<ETaxSubmissionPipeline> log)
    {
        _db = db; _tenant = tenant; _clock = clock; _builder = builder;
        _signer = signer; _validator = validator; _email = email; _storage = storage;
        _valOpts = valOpts.Value; _subOpts = subOpts.Value; _log = log;
    }

    public async Task EnqueueAsync(long taxInvoiceId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return;          // best-effort, no tenant → skip
        await RunAsync(taxInvoiceId, _tenant.CompanyId, ct);
    }

    public async Task<string> RunAsync(long taxInvoiceId, int companyId, CancellationToken ct)
    {
        var attemptNo = await NextAttemptNoAsync(taxInvoiceId, companyId, ct);

        // Retry budget exhausted → terminal dead-letter row, no retry_after.
        // Checked first: never re-attempt a submission that has run out of road.
        if (attemptNo > _subOpts.RetryAttempts)
            return await RecordAsync(companyId, taxInvoiceId, attemptNo,
                ETaxSubmissionOutcome.SendFailed, "(retry-exhausted)",
                notes: $"Retry limit ({_subOpts.RetryAttempts}) exhausted — dead-letter.",
                deadLetter: true, ct: ct);

        var ti = await _db.TaxInvoices.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TaxInvoiceId == taxInvoiceId
                                   && t.CompanyId == companyId, ct);
        if (ti is null)
            return await RecordAsync(companyId, taxInvoiceId, attemptNo,
                ETaxSubmissionOutcome.NotApplicable, "(none)",
                notes: "Tax Invoice not found.", ct: ct);

        var email = await _db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CustomerId == ti.CustomerId && c.CompanyId == companyId)
            .Select(c => c.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email))
            return await RecordAsync(companyId, taxInvoiceId, attemptNo,
                ETaxSubmissionOutcome.NotApplicable, "(no-customer-email)",
                notes: $"Customer {ti.CustomerId} has no email — e-Tax skipped.", ct: ct);

        try
        {
            var rawXml = _builder.BuildTaxInvoiceXml(taxInvoiceId, ct);
            var signed = await _signer.SignAsync(rawXml, ct);

            var validation = await _validator.ValidateAsync(signed.Xml, ct);
            if (_valOpts.RequireSchemaPass && !validation.IsValid)
                return await RecordWithRetryAsync(companyId, taxInvoiceId, attemptNo,
                    email, xmlSha256: signed.Sha256,
                    notes: "XSD validation failed: " + string.Join(" | ", validation.Errors),
                    ct: ct);

            string? path = null;
            using (var ms = new MemoryStream(signed.Bytes))
                path = await _storage.SaveAsync(companyId, "ETAX_SUBMISSION",
                    taxInvoiceId, ms, $"{ti.DocNo}.xml", ct);

            var subject = $"ใบกำกับภาษีอิเล็กทรอนิกส์ / e-Tax Invoice {ti.DocNo}";
            var res = await _email.SendAsync(email, subject, signed, pdfA3: null, ct);

            if (res.Delivered)
                return await RecordAsync(companyId, taxInvoiceId, attemptNo,
                    ETaxSubmissionOutcome.SendOk, res.To,
                    xmlSha256: signed.Sha256, signedXmlPath: path,
                    emailMessageId: res.MessageId, ccEmailSnapshot: res.Cc,
                    redirectApplied: res.Redirected,
                    intendedToEmail: res.Redirected ? email : null,
                    smtpResponse: "OK", ct: ct);

            return await RecordWithRetryAsync(companyId, taxInvoiceId, attemptNo,
                res.To, xmlSha256: signed.Sha256, signedXmlPath: path,
                ccEmailSnapshot: res.Cc, redirectApplied: res.Redirected,
                intendedToEmail: res.Redirected ? email : null,
                notes: "SMTP send failed: " + (res.Error ?? "unknown"), ct: ct);
        }
        catch (DomainException dex)
        {
            _log.LogWarning(dex, "e-Tax pipeline DomainException TI={Id} attempt={N}", taxInvoiceId, attemptNo);
            return await RecordWithRetryAsync(companyId, taxInvoiceId, attemptNo,
                email, notes: $"{dex.Code}: {dex.Message}", ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "e-Tax pipeline error TI={Id} attempt={N}", taxInvoiceId, attemptNo);
            return await RecordWithRetryAsync(companyId, taxInvoiceId, attemptNo,
                email, notes: ex.Message, ct: ct);
        }
    }

    private async Task<int> NextAttemptNoAsync(long taxInvoiceId, int companyId, CancellationToken ct)
    {
        var max = await _db.ETaxSubmissions.IgnoreQueryFilters()
            .Where(s => s.TaxInvoiceId == taxInvoiceId && s.CompanyId == companyId)
            .Select(s => (int?)s.AttemptNo).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    /// <summary>SendFailed + a backoff retry_after (or dead-letter if the schedule is spent).</summary>
    private Task<string> RecordWithRetryAsync(
        int companyId, long taxInvoiceId, int attemptNo, string to,
        string? xmlSha256 = null, string? signedXmlPath = null,
        string? ccEmailSnapshot = null, bool redirectApplied = false,
        string? intendedToEmail = null, string? notes = null,
        CancellationToken ct = default)
    {
        var delay = ETaxBackoff.NextDelay(attemptNo, _subOpts.BackoffSchedule);
        var deadLetter = delay is null;
        var retryAfter = delay is { } d ? _clock.UtcNow + d : (DateTimeOffset?)null;
        return RecordAsync(companyId, taxInvoiceId, attemptNo,
            ETaxSubmissionOutcome.SendFailed, to,
            xmlSha256: xmlSha256, signedXmlPath: signedXmlPath,
            ccEmailSnapshot: ccEmailSnapshot, redirectApplied: redirectApplied,
            intendedToEmail: intendedToEmail, notes: notes,
            retryAfter: retryAfter, deadLetter: deadLetter, ct: ct);
    }

    private async Task<string> RecordAsync(
        int companyId, long taxInvoiceId, int attemptNo,
        ETaxSubmissionOutcome outcome, string to,
        string? xmlSha256 = null, string? signedXmlPath = null,
        string? emailMessageId = null, string? ccEmailSnapshot = null,
        bool redirectApplied = false, string? intendedToEmail = null,
        string? smtpResponse = null, string? notes = null,
        DateTimeOffset? retryAfter = null, bool deadLetter = false,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        _db.ETaxSubmissions.Add(new ETaxSubmission
        {
            CompanyId       = companyId,
            TaxInvoiceId    = taxInvoiceId,
            AttemptedAt     = now,
            AttemptNo       = attemptNo,
            Outcome         = outcome,
            XmlSha256       = xmlSha256,
            SignedXmlPath   = signedXmlPath,
            EmailMessageId  = emailMessageId,
            ToEmailSnapshot = to,
            CcEmailSnapshot = ccEmailSnapshot,
            RedirectApplied = redirectApplied,
            IntendedToEmail = intendedToEmail,
            SmtpResponse    = smtpResponse,
            RetryAfter      = retryAfter,
            DeadLetter      = deadLetter,
            Notes           = notes,
            CreatedAt       = now,
        });
        await _db.SaveChangesAsync(ct);
        return outcome.ToString();
    }
}
