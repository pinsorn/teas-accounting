using System.Text;
using System.Text.Json;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 — single source of truth for finalizing a tax filing into the
/// immutable <c>tax.tax_filings</c> history (C8). Shared by ภ.พ.30 (Part B) and
/// ภ.ง.ด.3/53/54 + ภ.พ.36 (Part C) so the immutability + already-finalized
/// guard + auto-mode RD stub is written once, not per form.
/// </summary>
internal static class TaxFilingStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Resolve the form status string for a finalize.</summary>
    public static string FinalStatus(string submissionMode) =>
        submissionMode == "auto" ? "Submitted" : "Finalized";

    /// <summary>
    /// Persist a finalized filing. Throws <c>tax_filing.already_finalized</c> if
    /// (company, form, period) already exists — finalized filings are immutable;
    /// amendment = Phase 2.
    /// </summary>
    public static async Task FinalizeAsync(
        AccountingDbContext db, ITenantContext tenant, IClock clock,
        string formType, int period, string submissionMode, object payload,
        CancellationToken ct, IRdEfilingClient? rd = null)
    {
        var exists = await db.TaxFilings.AnyAsync(
            f => f.FormType == formType && f.Period == period, ct);
        if (exists)
            throw new DomainException("tax_filing.already_finalized",
                $"{formType} for period {period} is already finalized (immutable). " +
                "Amendment filings are Phase 2.");

        var now = clock.UtcNow;
        var auto = submissionMode == "auto";
        var payloadJson = JsonSerializer.Serialize(payload, Json);

        string? rdAck = null;
        if (auto)
        {
            // Sprint 13c — real RD e-Filing client (Tier 1 = Mock canned ack;
            // Tier 2/3 = real HTTP). Falls back to a STUB ref only if no client
            // is wired (legacy/test path).
            if (rd is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(payloadJson);
                var res = await SubmitAsync(rd, formType, tenant.CompanyId, period, bytes, ct);
                rdAck = Trim50(res.AckReference ?? res.SubmissionId);
            }
            else
            {
                rdAck = $"STUB-{formType}-{period}-{Guid.NewGuid():N}"[..32];
            }
        }

        db.TaxFilings.Add(new TaxFiling
        {
            CompanyId      = tenant.CompanyId,
            FormType       = formType,
            Period         = period,
            Status         = FinalStatus(submissionMode),
            FinalizedAt    = now,
            FinalizedBy    = tenant.UserId,
            SubmittedAt    = auto ? now : null,
            SubmissionMode = submissionMode,
            RdAckRef       = rdAck,
            PayloadJson    = payloadJson,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Trim50(string s) => s.Length <= 50 ? s : s[..50];

    private static Task<RdSubmissionResult> SubmitAsync(
        IRdEfilingClient rd, string formType, int companyId, int period,
        byte[] payload, CancellationToken ct) => formType.ToUpperInvariant() switch
    {
        "PND30" => rd.SubmitPnd30Async(companyId, period, payload, ct),
        "PND3"  => rd.SubmitPnd3Async(companyId, period, payload, ct),
        "PND53" => rd.SubmitPnd53Async(companyId, period, payload, ct),
        "PND54" => rd.SubmitPnd54Async(companyId, period, payload, ct),
        "PND36" => rd.SubmitPnd36Async(companyId, period, payload, ct),
        // N1 (Tier-2 round 2, specs/pnd2-filing.md §10) — defense-in-depth: an unrecognised
        // form (e.g. "PND2", which has no SubmitPnd2Async arm) must never silently fake a
        // failed-but-recorded submission. The real guard against this for PND2 today is
        // WhtFilingService forcing submissionMode="manual" (this branch is never reached in
        // that path); this throw only protects a future/other caller that reaches auto mode
        // with an unwired form.
        _       => throw new DomainException("tax_filing.unknown_form",
                       $"No RD e-Filing submit method wired for form '{formType}' — cannot auto-submit."),
    };
}
