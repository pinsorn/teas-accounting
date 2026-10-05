using System.Globalization;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// specs/cancel-reissue-sales-docs.md 3.4.2 / 3.4.7 / 3.4.6 - receipt cancel (full unwind), cancel-and-reissue,
/// reissue, discard, the replacement amount lock and the O11 retarget of applications from a Voided TI to its
/// newest Posted replacement. Lock order (3.3.5): the receipt, then affected TIs ascending, then BNs ascending.
/// TRIGGER TRAP: the reversal JE is posted before the receipt row is touched, then Status + cancel columns +
/// JournalEntryId land in ONE SaveChanges (643 freezes the cancel columns once VOIDED).
/// </summary>
public sealed partial class ReceiptService
{
    private async Task<T> InCancelTxAsync<T>(Func<Task<T>> body, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var result = await body();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsPostedRaceViolation(ex)
            || ex is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } pg }
               && pg.ConstraintName == "ux_receipts_replaces")
        {
            throw new DomainException("rc.locked_mismatch",
                "This receipt was changed by someone else. Reload and try again.");
        }
    }

    private static string BuddhistDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";

    public Task<ReceiptCancelResult> CancelAsync(long id, string reasonCode, string reason, CancellationToken ct) =>
        InCancelTxAsync(() => CancelCoreAsync(id, reasonCode, reason, reissue: false, ct), ct);

    public Task<ReceiptCancelResult> CancelAndReissueAsync(long id, string reasonCode, string reason, CancellationToken ct) =>
        InCancelTxAsync(async () =>
        {
            var res = await CancelCoreAsync(id, reasonCode, reason, reissue: true, ct);
            var orig = await _db.Receipts.FirstAsync(r => r.ReceiptId == id, ct);
            var replId = await CreateReplacementDraftCoreAsync(orig, ct);
            return res with { ReplacementReceiptId = replId };
        }, ct);

    public Task<long> ReissueAsync(long id, CancellationToken ct) =>
        InCancelTxAsync(async () =>
        {
            await DocumentCancellation.LockReceiptsAsync(_db, [id], ct);
            var orig = await _db.Receipts.FirstOrDefaultAsync(r => r.ReceiptId == id, ct)
                ?? throw new DomainException("rc.not_found", $"Receipt {id} not found.");
            if (orig.Status != DocumentStatus.Voided)
                throw new DomainException("rc.not_cancelled", "Only a cancelled (Voided) receipt can be reissued.");
            return await CreateReplacementDraftCoreAsync(orig, ct);
        }, ct);

    public Task DiscardReplacementAsync(long id, CancellationToken ct) =>
        InCancelTxAsync<bool>(async () =>
        {
            await DocumentCancellation.LockReceiptsAsync(_db, [id], ct);
            var rc = await _db.Receipts.Include(r => r.Applications).Include(r => r.Lines).Include(r => r.WhtLines)
                    .FirstOrDefaultAsync(r => r.ReceiptId == id, ct)
                ?? throw new DomainException("rc.not_found", $"Receipt {id} not found.");
            if (rc.Status != DocumentStatus.Draft || rc.ReplacesReceiptId is not { } origId)
                throw new DomainException("rc.delete_not_allowed", "Only a replacement draft can be discarded.");
            var origNo = await _db.Receipts.Where(r => r.ReceiptId == origId).Select(r => r.DocNo).FirstOrDefaultAsync(ct);
            _activity.Record("Receipt", rc.ReceiptId, rc.DocNo, rc.CompanyId, "ReplacementDiscarded",
                note: $"ยกเลิกใบแทนของ {origNo}");
            _activity.Record("Receipt", origId, origNo, rc.CompanyId, "ReplacementDiscarded",
                note: $"ลบฉบับร่างใบแทน #{rc.ReceiptId}");
            _db.RemoveRange(rc.Applications);
            _db.RemoveRange(rc.Lines);
            _db.RemoveRange(rc.WhtLines);
            _db.Receipts.Remove(rc);
            await _db.SaveChangesAsync(ct);
            return true;
        }, ct);

    private async Task<ReceiptCancelResult> CancelCoreAsync(
        long id, string reasonCode, string reason, bool reissue, CancellationToken ct)
    {
        await DocumentCancellation.LockReceiptsAsync(_db, [id], ct);
        var rc = await _db.Receipts
                .Include(r => r.Applications).Include(r => r.WhtLines).Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.ReceiptId == id, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {id} not found.");
        if (rc.Status != DocumentStatus.Posted)
            throw new DomainException("rc.cannot_cancel_status", rc.Status == DocumentStatus.Voided
                ? "Receipt is already cancelled."
                : "Only a Posted receipt can be cancelled.");
        CancelReasonCodes.Require(reasonCode, reissue ? CancelReasonCodes.ReceiptReissue : CancelReasonCodes.ReceiptCancel);
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("validation.reason_required", "A reason is required.");
        if (reason.Length > 500)
            throw new DomainException("validation.reason_too_long", "Reason must be 500 characters or fewer.");

        if (await _db.StatementLines.AnyAsync(s => s.MatchedReceiptId == id, ct))
            throw new DomainException("rc.bank_reconciled",
                "This receipt is matched to a bank statement line; unmatch it first.");

        var je = await DocumentCancellation.ResolveOriginalJournalAsync(
            _db, rc.CompanyId, rc.JournalEntryId, rc.DocNo, "RC ", ct);
        var glDate = await DocumentCancellation.ResolveGlDateAsync(_period, _clock, rc.DocDate, ct);

        // (a) unwind every applied TI (ascending id lock first).
        var tiApps = rc.Applications.Where(a => a.TaxInvoiceId.HasValue)
            .GroupBy(a => a.TaxInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.AppliedAmount));
        await DocumentCancellation.LockTaxInvoicesAsync(_db, tiApps.Keys, ct);
        var tiKeys = tiApps.Keys.ToList();
        var tis = await _db.TaxInvoices.Where(t => tiKeys.Contains(t.TaxInvoiceId)).ToListAsync(ct);
        foreach (var ti in tis.OrderBy(t => t.TaxInvoiceId))
        {
            var left = ti.AmountPaid - tiApps[ti.TaxInvoiceId];
            if (left < 0m)
                throw new DomainException("rc.unwind_negative",
                    $"Cancelling would make Tax Invoice {ti.DocNo} AmountPaid negative ({left}).");
            ti.AmountPaid = left;
            ti.PaymentStatus = left == 0m ? "UNPAID" : left >= ti.TotalAmount ? "PAID" : "PARTIAL";
            ti.Version++;
        }
        await _db.SaveChangesAsync(ct);   // flush so the BN re-derive below reads the new AmountPaid

        // (b) Settled billing notes this receipt helped cover: recompute paid from scratch, receipt excluded.
        var directBnIds = rc.Applications.Where(a => a.BillingNoteId.HasValue)
            .Select(a => a.BillingNoteId!.Value).Distinct().ToList();
        var linkBnIds = tiKeys.Count == 0
            ? new List<long>()
            : await _db.BillingNotes.AsNoTracking()
                .Where(b => b.CompanyId == rc.CompanyId && b.Status == BillingNoteStatus.Settled
                         && b.TaxInvoiceLinks.Any(j => tiKeys.Contains(j.TaxInvoiceId)))
                .Select(b => b.BillingNoteId).ToListAsync(ct);
        var bnIds = linkBnIds.Union(directBnIds).ToList();
        if (bnIds.Count > 0)
        {
            await DocumentCancellation.LockBillingNotesAsync(_db, bnIds, ct);
            var bns = await _db.BillingNotes
                .Where(b => bnIds.Contains(b.BillingNoteId) && b.Status == BillingNoteStatus.Settled)
                .ToListAsync(ct);
            var linkPaid = await _db.BillingNoteTaxInvoices
                .Where(j => bnIds.Contains(j.BillingNoteId))
                .Join(_db.TaxInvoices, j => j.TaxInvoiceId, t => t.TaxInvoiceId, (j, t) => new { j.BillingNoteId, t.AmountPaid })
                .GroupBy(x => x.BillingNoteId)
                .Select(g => new { Id = g.Key, Paid = g.Sum(x => x.AmountPaid) })
                .ToDictionaryAsync(x => x.Id, x => x.Paid, ct);
            var directPaid = await _db.ReceiptApplications
                .Where(a => a.BillingNoteId.HasValue && bnIds.Contains(a.BillingNoteId!.Value) && a.ReceiptId != id)
                .Join(_db.Receipts.Where(r => r.Status == DocumentStatus.Posted),
                    a => a.ReceiptId, r => r.ReceiptId, (a, r) => a)
                .GroupBy(a => a.BillingNoteId!.Value)
                .Select(g => new { Id = g.Key, Paid = g.Sum(x => x.AppliedAmount) })
                .ToDictionaryAsync(x => x.Id, x => x.Paid, ct);
            foreach (var bn in bns.OrderBy(b => b.BillingNoteId))
            {
                var paid = linkBnIds.Contains(bn.BillingNoteId)
                    ? linkPaid.GetValueOrDefault(bn.BillingNoteId)
                    : directPaid.GetValueOrDefault(bn.BillingNoteId);
                if (paid >= bn.TotalAmount) continue;
                bn.Status = BillingNoteStatus.Issued;
                bn.SettledAt = null;
                bn.Version++;
                _activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId,
                    "Unsettled", "Settled", "Issued", note: $"ยกเลิกใบเสร็จ {rc.DocNo}");
            }
        }

        // (c) the customer's 50 tawi rows booked by this receipt.
        var certs = await _db.WhtCertificates
            .Where(w => w.ReceiptId == id && w.Direction == "R").ToListAsync(ct);
        foreach (var c in certs) c.Status = DocumentStatus.Voided;

        // (d) reversal FIRST (the receipt row is still clean), then the receipt in one UPDATE.
        var revId = await _gl.PostReversalAsync(je.JournalId, glDate, "ยกเลิก " + je.Description, ct);
        var revDocNo = await _db.JournalEntries.AsNoTracking().Where(j => j.JournalId == revId)
            .Select(j => j.DocNo).FirstAsync(ct) ?? string.Empty;

        rc.Status = DocumentStatus.Voided;
        rc.CancelReasonCode = reasonCode;
        rc.CancelReason = reason;
        rc.CancelledAt = _clock.UtcNow;
        rc.CancelledBy = _tenant.UserId;
        rc.ReversalJournalEntryId = revId;
        rc.JournalEntryId ??= je.JournalId;
        rc.Version++;
        _activity.Record("Receipt", rc.ReceiptId, rc.DocNo, rc.CompanyId, "Cancelled", "Posted", "Voided",
            note: $"{DocumentCancellation.Note(reasonCode, reason)}; JV {revDocNo} @ {glDate:yyyy-MM-dd}");
        await _db.SaveChangesAsync(ct);
        return new ReceiptCancelResult(rc.ReceiptId, "Voided", revId, revDocNo, glDate, null);
    }

    // ── O11 retarget ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A Voided TI maps to its newest POSTED replacement (following Voided replacements down the chain,
    /// looked up through ux_tax_invoices_replaces); anything else maps to itself. A still-draft replacement does
    /// not count, so the Voided id is kept and the post-time TI status guard refuses it.</summary>
    private async Task<long> RetargetTiAsync(long tiId, CancellationToken ct)
    {
        var st = await _db.TaxInvoices.AsNoTracking().Where(t => t.TaxInvoiceId == tiId)
            .Select(t => (DocumentStatus?)t.Status).FirstOrDefaultAsync(ct);
        if (st != DocumentStatus.Voided) return tiId;
        var cur = tiId;
        for (var guard = 0; guard < 20; guard++)
        {
            var child = await _db.TaxInvoices.AsNoTracking().Where(t => t.ReplacesTaxInvoiceId == cur)
                .Select(t => new { t.TaxInvoiceId, t.Status }).FirstOrDefaultAsync(ct);
            if (child is null || child.Status == DocumentStatus.Draft) return tiId;
            if (child.Status == DocumentStatus.Posted) return child.TaxInvoiceId;
            cur = child.TaxInvoiceId;
        }
        return tiId;
    }

    private async Task<Dictionary<long, long>> BuildRetargetMapAsync(IEnumerable<long> tiIds, CancellationToken ct)
    {
        var map = new Dictionary<long, long>();
        foreach (var id in tiIds.Distinct()) map[id] = await RetargetTiAsync(id, ct);
        return map;
    }

    // ── replacement draft ────────────────────────────────────────────────────────────────────────

    private async Task<long> CreateReplacementDraftCoreAsync(Receipt o, CancellationToken ct)
    {
        if (await _db.Receipts.AnyAsync(r => r.ReplacesReceiptId == o.ReceiptId, ct))
            throw new DomainException("rc.replacement_exists", "A replacement already exists for this receipt.");
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == o.CustomerId, ct)
            ?? throw new DomainException("rc.customer_missing", $"Customer {o.CustomerId} not found.");
        var src = await _db.Receipts.AsNoTracking()
            .Include(r => r.Applications).Include(r => r.Lines).Include(r => r.WhtLines)
            .FirstAsync(r => r.ReceiptId == o.ReceiptId, ct);

        var map = await BuildRetargetMapAsync(
            src.Applications.Where(a => a.TaxInvoiceId.HasValue).Select(a => a.TaxInvoiceId!.Value), ct);

        var repl = new Receipt
        {
            CompanyId = src.CompanyId, BranchId = src.BranchId, DocDate = src.DocDate,
            BusinessUnitId = src.BusinessUnitId,
            CustomerId = customer.CustomerId, CustomerName = customer.NameTh,
            CustomerAddress = customer.BillingAddress ?? string.Empty, CustomerTaxId = customer.TaxId,
            PaymentMethod = src.PaymentMethod, ChequeNo = src.ChequeNo, ChequeDate = src.ChequeDate,
            BankAccountId = src.BankAccountId,
            CurrencyCode = src.CurrencyCode, ExchangeRate = src.ExchangeRate,
            Amount = src.Amount, TotalAmount = src.TotalAmount, TotalAmountThb = src.TotalAmountThb,
            WhtAmount = src.WhtAmount, WhtTypeId = src.WhtTypeId,
            CustomerWhtCertNo = src.CustomerWhtCertNo, CustomerWhtCertDate = src.CustomerWhtCertDate,
            CashReceived = 0m,   // computed at post
            Notes = src.Notes,
            ReplacesReceiptId = src.ReceiptId,
            Applications = src.Applications.OrderBy(a => a.ApplicationId).Select(a => new ReceiptApplication
            {
                TaxInvoiceId = a.TaxInvoiceId is { } t ? map[t] : null,
                DeliveryOrderId = a.DeliveryOrderId, BillingNoteId = a.BillingNoteId, AppliedAmount = a.AppliedAmount,
            }).ToList(),
            Lines = src.Lines.OrderBy(l => l.LineNo).Select(l => new ReceiptLine
            {
                LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = l.ProductCode, ProductType = l.ProductType,
                DescriptionTh = l.DescriptionTh, Quantity = l.Quantity, UomText = l.UomText,
                UnitPrice = l.UnitPrice, Amount = l.Amount,
            }).ToList(),
            WhtLines = src.WhtLines.OrderBy(w => w.ReceiptWhtLineId).Select(w => new ReceiptWhtLine
            {
                WhtTypeId = w.WhtTypeId, IncomeTypeCode = w.IncomeTypeCode, WhtTypeCode = w.WhtTypeCode,
                WhtRate = w.WhtRate, BaseAmount = w.BaseAmount, WhtAmount = w.WhtAmount,
            }).ToList(),
        };
        _db.Receipts.Add(repl);
        await _db.SaveChangesAsync(ct);
        _activity.Record("Receipt", repl.ReceiptId, null, repl.CompanyId, "ReplacementCreated", toStatus: "Draft",
            note: $"แทน {o.DocNo}");
        _activity.Record("Receipt", o.ReceiptId, o.DocNo, o.CompanyId, "Reissued", note: $"ใบแทน #{repl.ReceiptId}");
        await _db.SaveChangesAsync(ct);
        return repl.ReceiptId;
    }

    // ── 3.4.6 amount lock ────────────────────────────────────────────────────────────────────────

    private static string D(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Locked-field names where <paramref name="repl"/> differs from the Voided <paramref name="orig"/>.
    /// Application TaxInvoiceIds on BOTH sides are mapped through <paramref name="map"/> (O11) before comparing, so
    /// a retarget to the replacement TI never trips the lock. Both receipts must carry their child collections.</summary>
    private static List<string> LockedReceiptDiffs(Receipt orig, Receipt repl, IReadOnlyDictionary<long, long> map)
    {
        var d = new List<string>();
        void C<T>(string n, T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) d.Add(n); }
        C("Amount", orig.Amount, repl.Amount);
        C("TotalAmount", orig.TotalAmount, repl.TotalAmount);
        C("TotalAmountThb", orig.TotalAmountThb, repl.TotalAmountThb);
        C("WhtAmount", orig.WhtAmount, repl.WhtAmount);
        C("CurrencyCode", orig.CurrencyCode, repl.CurrencyCode);
        C("ExchangeRate", orig.ExchangeRate, repl.ExchangeRate);
        C("BusinessUnitId", orig.BusinessUnitId, repl.BusinessUnitId);
        long? M(long? t) => t is { } v ? map.GetValueOrDefault(v, v) : null;
        string AppKey(ReceiptApplication a) => $"{M(a.TaxInvoiceId)}|{a.DeliveryOrderId}|{a.BillingNoteId}|{D(a.AppliedAmount)}";
        if (!orig.Applications.Select(AppKey).Order().SequenceEqual(repl.Applications.Select(AppKey).Order()))
            d.Add("Applications");
        string WhtKey(ReceiptWhtLine w) => $"{w.WhtTypeId}|{D(w.BaseAmount)}|{D(w.WhtRate)}|{D(w.WhtAmount)}";
        if (!orig.WhtLines.Select(WhtKey).Order().SequenceEqual(repl.WhtLines.Select(WhtKey).Order()))
            d.Add("WhtLines");
        string LineKey(ReceiptLine l) => $"{l.LineNo}|{l.ProductId}|{l.ProductType}|{D(l.Quantity)}|{D(l.UnitPrice)}|{D(l.Amount)}";
        if (!orig.Lines.Select(LineKey).Order().SequenceEqual(repl.Lines.Select(LineKey).Order()))
            d.Add("Lines");
        return d;
    }

    private static DomainException LockedFieldError(List<string> diffs) =>
        new("replacement.locked_field",
            "A replacement keeps the original's date and amounts; these fields cannot change: " + string.Join(", ", diffs));

    private async Task<Receipt> LoadVoidedOriginalAsync(long origId, CancellationToken ct)
    {
        var orig = await _db.Receipts.AsNoTracking()
                .Include(r => r.Applications).Include(r => r.Lines).Include(r => r.WhtLines)
                .FirstOrDefaultAsync(r => r.ReceiptId == origId, ct)
            ?? throw new DomainException("replacement.original_not_cancelled", "Original receipt not found.");
        if (orig.Status != DocumentStatus.Voided)
            throw new DomainException("replacement.original_not_cancelled",
                "The original receipt is not cancelled; a replacement cannot be posted.");
        return orig;
    }

    /// <summary>UpdateDraft lock for a replacement receipt: maps the request's TI ids through the retarget, rebuilds
    /// it with the normal validation, and refuses any difference from the Voided original in a locked field.
    /// Returns the (possibly retargeted) request to continue with.</summary>
    private async Task<(CreateReceiptRequest Req, ReceiptComputedFields Computed)> LockReplacementUpdateAsync(
        Receipt rc, long origId, Accounting.Domain.Entities.Master.Customer customer, CreateReceiptRequest req, CancellationToken ct)
    {
        var orig = await LoadVoidedOriginalAsync(origId, ct);
        var tiIds = req.Applications.Where(a => a.TaxInvoiceId.HasValue).Select(a => a.TaxInvoiceId!.Value)
            .Concat(orig.Applications.Where(a => a.TaxInvoiceId.HasValue).Select(a => a.TaxInvoiceId!.Value));
        var map = await BuildRetargetMapAsync(tiIds, ct);
        req = req with
        {
            Applications = req.Applications
                .Select(a => a.TaxInvoiceId is { } t ? a with { TaxInvoiceId = map.GetValueOrDefault(t, t) } : a).ToList(),
        };
        var computed = await RebuildLinesAndTotalsAsync(customer, req, ct);
        var probe = new Receipt
        {
            CustomerName = string.Empty, CustomerAddress = string.Empty,
            Amount = computed.Amount, TotalAmount = computed.Amount,
            TotalAmountThb = Math.Round(computed.Amount * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            WhtAmount = computed.WhtTotal, CurrencyCode = req.CurrencyCode, ExchangeRate = req.ExchangeRate,
            BusinessUnitId = req.BusinessUnitId,
            Applications = computed.Applications, WhtLines = computed.WhtLines, Lines = computed.Lines,
        };
        var diffs = LockedReceiptDiffs(orig, probe, map);
        if (diffs.Count > 0) throw LockedFieldError(diffs);
        return (req, computed);
    }

    /// <summary>Post-time replacement handling (3.4.7 + O11b): retarget the draft's applications to the newest Posted
    /// replacement TI (persisted on the draft, which has no immutability trigger), then re-run the amount lock.
    /// Must run BEFORE the applied-TI set is read.</summary>
    private async Task CheckAndRetargetReplacementAsync(Receipt rc, long origId, CancellationToken ct)
    {
        foreach (var a in rc.Applications.Where(a => a.TaxInvoiceId.HasValue))
        {
            var m = await RetargetTiAsync(a.TaxInvoiceId!.Value, ct);
            if (m != a.TaxInvoiceId) a.TaxInvoiceId = m;
        }
        var orig = await LoadVoidedOriginalAsync(origId, ct);
        var map = await BuildRetargetMapAsync(
            orig.Applications.Where(a => a.TaxInvoiceId.HasValue).Select(a => a.TaxInvoiceId!.Value), ct);
        var diffs = LockedReceiptDiffs(orig, rc, map);
        if (diffs.Count > 0) throw LockedFieldError(diffs);
        await _db.SaveChangesAsync(ct);   // persist the retarget while the receipt is still a draft
    }
}
