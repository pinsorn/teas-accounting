using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.ETax;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Sprint 13c — append-only e-Tax submission audit writer. Inserts only; the
/// <c>etax.submissions</c> table rejects UPDATE/DELETE at the DB trigger layer.
/// </summary>
public sealed class ETaxSubmissionAudit : IETaxSubmissionAudit
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public ETaxSubmissionAudit(AccountingDbContext db, ITenantContext tenant, IClock clock)
    {
        _db = db; _tenant = tenant; _clock = clock;
    }

    public async Task<int> NextAttemptNoAsync(long taxInvoiceId, CancellationToken ct)
    {
        var max = await _db.ETaxSubmissions
            .Where(s => s.TaxInvoiceId == taxInvoiceId)
            .Select(s => (int?)s.AttemptNo)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<IReadOnlyList<ETaxSubmissionRow>> ListByInvoiceAsync(
        long taxInvoiceId, CancellationToken ct) =>
        await _db.ETaxSubmissions.AsNoTracking()
            .Where(s => s.TaxInvoiceId == taxInvoiceId)
            .OrderByDescending(s => s.AttemptNo)
            .Select(s => new ETaxSubmissionRow(
                s.SubmissionId, s.TaxInvoiceId, s.AttemptNo, s.Outcome.ToString(),
                s.AttemptedAt, s.ToEmailSnapshot, s.RedirectApplied,
                s.DeadLetter, s.RdAckRef, s.Notes))
            .ToListAsync(ct);

    public async Task<long> RecordAsync(ETaxSubmissionRecord rec, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var now = _clock.UtcNow;
        var row = new ETaxSubmission
        {
            CompanyId       = _tenant.CompanyId,
            TaxInvoiceId    = rec.TaxInvoiceId,
            AttemptedAt     = now,
            AttemptNo       = rec.AttemptNo,
            Outcome         = rec.Outcome,
            XmlSha256       = rec.XmlSha256,
            SignedXmlPath   = rec.SignedXmlPath,
            PdfPath         = rec.PdfPath,
            EmailMessageId  = rec.EmailMessageId,
            ToEmailSnapshot = rec.ToEmailSnapshot,
            CcEmailSnapshot = rec.CcEmailSnapshot,
            RedirectApplied = rec.RedirectApplied,
            IntendedToEmail = rec.IntendedToEmail,
            SmtpResponse    = rec.SmtpResponse,
            RdAckRef        = rec.RdAckRef,
            RdRejectionCode = rec.RdRejectionCode,
            RetryAfter      = rec.RetryAfter,
            DeadLetter      = rec.DeadLetter,
            Notes           = rec.Notes,
            CreatedAt       = now,
        };
        _db.ETaxSubmissions.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.SubmissionId;
    }
}
