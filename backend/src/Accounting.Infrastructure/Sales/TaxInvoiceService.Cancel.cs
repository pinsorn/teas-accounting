using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// specs/cancel-reissue-sales-docs.md 3.4.1 / 3.4.4 / 3.4.6 - cancel, cancel-and-reissue, reissue and discard
/// of a posted Tax Invoice, plus the replacement amount lock. Lock order (3.3.5): the TI row first, then
/// nothing else is locked (a TI cancel touches no other mutable row).
/// TRIGGER TRAP: 643 freezes the cancel columns once OLD.status = VOIDED, so the reversal JE is posted FIRST
/// (with the TI tracked and clean) and Status + the five cancel columns + JournalEntryId land in ONE SaveChanges.
/// </summary>
public sealed partial class TaxInvoiceService
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
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsPostedRaceViolation(ex) || IsReplacesUniqueViolation(ex))
        {
            throw new DomainException("ti.locked_mismatch",
                "This tax invoice was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>Buddhist dd/MM/yyyy (local copy of the paper format, PaperDocumentPdf.BuddhistDate is private).</summary>
    private static string BuddhistDate(DateOnly d) => $"{d.Day:00}/{d.Month:00}/{d.Year + 543}";

    private static bool IsReplacesUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } pg }
        && pg.ConstraintName == "ux_tax_invoices_replaces";

    public Task<TaxInvoiceCancelResult> CancelAsync(long id, string reasonCode, string reason, CancellationToken ct) =>
        InCancelTxAsync(() => CancelCoreAsync(id, reasonCode, reason, reissue: false, ct), ct);

    public Task<TaxInvoiceCancelResult> CancelAndReissueAsync(long id, string reasonCode, string reason, CancellationToken ct) =>
        InCancelTxAsync(async () =>
        {
            await EnsureVatRegisteredAsync(ct);
            var res = await CancelCoreAsync(id, reasonCode, reason, reissue: true, ct);
            var orig = await _db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == id, ct);
            var replId = await CreateReplacementDraftCoreAsync(orig, ct);
            return res with { ReplacementTaxInvoiceId = replId };
        }, ct);

    public Task<long> ReissueAsync(long id, CancellationToken ct) =>
        InCancelTxAsync(async () =>
        {
            await EnsureVatRegisteredAsync(ct);
            await DocumentCancellation.LockTaxInvoicesAsync(_db, [id], ct);
            var orig = await _db.TaxInvoices.FirstOrDefaultAsync(t => t.TaxInvoiceId == id, ct)
                ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");
            if (orig.Status != DocumentStatus.Voided)
                throw new DomainException("ti.not_cancelled", "Only a cancelled (Voided) Tax Invoice can be reissued.");
            return await CreateReplacementDraftCoreAsync(orig, ct);
        }, ct);

    public Task DiscardReplacementAsync(long id, CancellationToken ct) =>
        InCancelTxAsync<bool>(async () =>
        {
            await DocumentCancellation.LockTaxInvoicesAsync(_db, [id], ct);
            var ti = await _db.TaxInvoices.Include(t => t.Lines).FirstOrDefaultAsync(t => t.TaxInvoiceId == id, ct)
                ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");
            if (ti.Status != DocumentStatus.Draft || ti.ReplacesTaxInvoiceId is not { } origId)
                throw new DomainException("ti.delete_not_allowed",
                    "Only a replacement draft can be discarded.");
            var origNo = await _db.TaxInvoices.Where(t => t.TaxInvoiceId == origId)
                .Select(t => t.DocNo).FirstOrDefaultAsync(ct);
            _activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "ReplacementDiscarded",
                note: $"ยกเลิกใบแทนของ {origNo}");
            _activity.Record("TaxInvoice", origId, origNo, ti.CompanyId, "ReplacementDiscarded",
                note: $"ลบฉบับร่างใบแทน #{ti.TaxInvoiceId}");
            _db.RemoveRange(ti.Lines);
            _db.TaxInvoices.Remove(ti);
            await _db.SaveChangesAsync(ct);
            return true;
        }, ct);

    private async Task<TaxInvoiceCancelResult> CancelCoreAsync(
        long id, string reasonCode, string reason, bool reissue, CancellationToken ct)
    {
        await DocumentCancellation.LockTaxInvoicesAsync(_db, [id], ct);
        var ti = await _db.TaxInvoices.FirstOrDefaultAsync(t => t.TaxInvoiceId == id, ct)
            ?? throw new DomainException("ti.not_found", $"Tax Invoice {id} not found.");

        if (ti.Status != DocumentStatus.Posted)
            throw new DomainException("ti.cannot_cancel_status", ti.Status == DocumentStatus.Voided
                ? "Tax Invoice is already cancelled."
                : "Only a Posted Tax Invoice can be cancelled.");
        CancelReasonCodes.Require(reasonCode, reissue ? CancelReasonCodes.TaxInvoiceReissue : CancelReasonCodes.TaxInvoiceCancel);
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("validation.reason_required", "A reason is required.");
        if (reason.Length > 500)
            throw new DomainException("validation.reason_too_long", "Reason must be 500 characters or fewer.");

        if (ti.ETaxSubmittedAt != null)
            throw new DomainException("ti.etax_submitted",
                "This Tax Invoice was submitted to the Revenue Department; issue a credit/debit note instead.");
        if (ti.AmountPaid > 0m || await _db.ReceiptApplications
                .Where(a => a.TaxInvoiceId == id)
                .Join(_db.Receipts.Where(r => r.Status == DocumentStatus.Posted),
                    a => a.ReceiptId, r => r.ReceiptId, (a, r) => a.ReceiptId)
                .AnyAsync(ct))
            throw new DomainException("ti.has_posted_receipts",
                "A posted receipt is applied to this Tax Invoice; cancel the receipt first.");
        if (await _db.TaxAdjustmentNotes.AnyAsync(
                n => n.OriginalTaxInvoiceId == id && n.Status == DocumentStatus.Posted, ct))
            throw new DomainException("ti.has_adjustment_notes",
                "A credit/debit note is posted against this Tax Invoice; it cannot be cancelled.");
        var bnRef = await _db.BillingNotes.AsNoTracking()
            .Where(b => b.Status != BillingNoteStatus.Cancelled && b.TaxInvoiceLinks.Any(j => j.TaxInvoiceId == id))
            .Select(b => new { b.BillingNoteId, b.DocNo })
            .FirstOrDefaultAsync(ct);
        if (bnRef is not null)
            throw new DomainException("ti.linked_to_billing_note",
                $"ใบกำกับภาษีนี้ถูกอ้างอิงในใบแจ้งหนี้ {bnRef.DocNo ?? "#" + bnRef.BillingNoteId} — ยกเลิกใบแจ้งหนี้ก่อน " +
                $"(Tax Invoice {id} is linked from Invoice {bnRef.DocNo ?? bnRef.BillingNoteId.ToString()}; cancel it first.)");

        var je = await DocumentCancellation.ResolveOriginalJournalAsync(
            _db, ti.CompanyId, ti.JournalEntryId, ti.DocNo, "TI ", ct);
        var glDate = await DocumentCancellation.ResolveGlDateAsync(_period, _clock, ti.DocDate, ct);

        // The TI is tracked and CLEAN here: PostReversalAsync saves internally, and a flush of a half-set
        // VOIDED row would make the later cancel-column UPDATE hit the 643 freeze.
        var revId = await _gl.PostReversalAsync(je.JournalId, glDate, "ยกเลิก " + je.Description, ct);
        var revDocNo = await _db.JournalEntries.AsNoTracking().Where(j => j.JournalId == revId)
            .Select(j => j.DocNo).FirstAsync(ct) ?? string.Empty;

        ti.Status = DocumentStatus.Voided;
        ti.CancelReasonCode = reasonCode;
        ti.CancelReason = reason;
        ti.CancelledAt = _clock.UtcNow;
        ti.CancelledBy = _tenant.UserId;
        ti.ReversalJournalEntryId = revId;
        ti.JournalEntryId ??= je.JournalId;
        ti.Version++;
        _activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "Cancelled", "Posted", "Voided",
            note: $"{DocumentCancellation.Note(reasonCode, reason)}; JV {revDocNo} @ {glDate:yyyy-MM-dd}");
        await _db.SaveChangesAsync(ct);
        return new TaxInvoiceCancelResult(ti.TaxInvoiceId, "Voided", revId, revDocNo, glDate, null);
    }

    /// <summary>3.4.4 - builds the replacement DRAFT of a Voided original. NEVER goes through CreateDraftCoreAsync
    /// (that pins DocDate to today and requires an open period). Lines are copied verbatim, never recomputed.</summary>
    private async Task<long> CreateReplacementDraftCoreAsync(TaxInvoice o, CancellationToken ct)
    {
        if (await _db.TaxInvoices.AnyAsync(t => t.ReplacesTaxInvoiceId == o.TaxInvoiceId, ct))
            throw new DomainException("ti.replacement_exists", "A replacement already exists for this Tax Invoice.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == o.CustomerId, ct)
            ?? throw new DomainException("ti.customer_missing", $"Customer {o.CustomerId} not found.");
        if (customer.VatRegistered && (string.IsNullOrEmpty(customer.TaxId) || string.IsNullOrEmpty(customer.BranchCode)))
            throw new DomainException("ti.customer_incomplete",
                "VAT-registered customer requires Tax ID + branch_code (ม.86/4 #3).");

        var src = await _db.TaxInvoices.AsNoTracking().Include(t => t.Lines)
            .FirstAsync(t => t.TaxInvoiceId == o.TaxInvoiceId, ct);

        var repl = new TaxInvoice
        {
            CompanyId = src.CompanyId, BranchId = src.BranchId,
            DocDate = src.DocDate, TaxPointDate = src.DocDate, TaxPointReason = src.TaxPointReason,
            BookNo = src.BookNo, BusinessUnitId = src.BusinessUnitId,
            QuotationId = src.QuotationId, BillingNoteId = src.BillingNoteId,
            SalesOrderId = src.SalesOrderId, DeliveryOrderId = src.DeliveryOrderId,
            SupplierTaxId = src.SupplierTaxId, SupplierBranchCode = src.SupplierBranchCode,
            SupplierBranchName = src.SupplierBranchName, SupplierName = src.SupplierName,
            SupplierAddress = src.SupplierAddress,
            // Buyer snapshot is re-pulled from the CURRENT master: correcting it is the point of a reissue.
            CustomerId = customer.CustomerId, CustomerTaxId = customer.TaxId,
            CustomerBranchCode = customer.BranchCode, CustomerBranchName = customer.BranchName,
            CustomerName = customer.NameTh, CustomerAddress = customer.BillingAddress ?? string.Empty,
            CustomerVatRegistered = customer.VatRegistered,
            CurrencyCode = src.CurrencyCode, ExchangeRate = src.ExchangeRate, IsTaxInclusive = src.IsTaxInclusive,
            SubtotalAmount = src.SubtotalAmount, DiscountAmount = src.DiscountAmount, TaxableAmount = src.TaxableAmount,
            NonTaxableAmount = src.NonTaxableAmount, TaxAmount = src.TaxAmount, TotalAmount = src.TotalAmount,
            TotalAmountThb = src.TotalAmountThb,
            DueDate = src.DueDate, PaymentTerms = src.PaymentTerms, Notes = src.Notes,
            ReplacesTaxInvoiceId = src.TaxInvoiceId,
            Lines = src.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceLine
            {
                LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = l.ProductCode, ProductType = l.ProductType,
                DescriptionTh = l.DescriptionTh, Quantity = l.Quantity, UomId = l.UomId, UomText = l.UomText,
                UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, DiscountAmount = l.DiscountAmount,
                LineAmount = l.LineAmount, TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = l.TaxAmount, TotalAmount = l.TotalAmount,
            }).ToList(),
        };
        _db.TaxInvoices.Add(repl);
        await _db.SaveChangesAsync(ct);
        _activity.Record("TaxInvoice", repl.TaxInvoiceId, null, repl.CompanyId, "ReplacementCreated", toStatus: "Draft",
            note: $"แทน {o.DocNo}");
        _activity.Record("TaxInvoice", o.TaxInvoiceId, o.DocNo, o.CompanyId, "Reissued",
            note: $"ใบแทน #{repl.TaxInvoiceId}");
        await _db.SaveChangesAsync(ct);
        return repl.TaxInvoiceId;
    }

    // ── 3.4.6 amount lock ────────────────────────────────────────────────────────────────────────

    /// <summary>Names of the locked fields where <paramref name="repl"/> differs from the Voided original.
    /// Both must carry Lines. Editable: descriptions, UoM text, customer, notes, payment terms, due date.</summary>
    private static List<string> LockedTiDiffs(TaxInvoice o, TaxInvoice repl)
    {
        var d = new List<string>();
        void C<T>(string n, T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) d.Add(n); }
        C("SubtotalAmount", o.SubtotalAmount, repl.SubtotalAmount);
        C("DiscountAmount", o.DiscountAmount, repl.DiscountAmount);
        C("TaxableAmount", o.TaxableAmount, repl.TaxableAmount);
        C("NonTaxableAmount", o.NonTaxableAmount, repl.NonTaxableAmount);
        C("TaxAmount", o.TaxAmount, repl.TaxAmount);
        C("TotalAmount", o.TotalAmount, repl.TotalAmount);
        C("TotalAmountThb", o.TotalAmountThb, repl.TotalAmountThb);
        C("CurrencyCode", o.CurrencyCode, repl.CurrencyCode);
        C("ExchangeRate", o.ExchangeRate, repl.ExchangeRate);
        C("IsTaxInclusive", o.IsTaxInclusive, repl.IsTaxInclusive);
        C("BusinessUnitId", o.BusinessUnitId, repl.BusinessUnitId);
        C("QuotationId", o.QuotationId, repl.QuotationId);
        C("BillingNoteId", o.BillingNoteId, repl.BillingNoteId);
        C("SalesOrderId", o.SalesOrderId, repl.SalesOrderId);
        C("DeliveryOrderId", o.DeliveryOrderId, repl.DeliveryOrderId);
        var ol = o.Lines.OrderBy(l => l.LineNo).ToList();
        var rl = repl.Lines.OrderBy(l => l.LineNo).ToList();
        C("Lines.Count", ol.Count, rl.Count);
        for (var i = 0; i < Math.Min(ol.Count, rl.Count); i++)
        {
            var n = $"Lines[{i + 1}].";
            C(n + "ProductId", ol[i].ProductId, rl[i].ProductId);
            C(n + "ProductType", ol[i].ProductType, rl[i].ProductType);
            C(n + "Quantity", ol[i].Quantity, rl[i].Quantity);
            C(n + "UnitPrice", ol[i].UnitPrice, rl[i].UnitPrice);
            C(n + "DiscountPercent", ol[i].DiscountPercent, rl[i].DiscountPercent);
            C(n + "DiscountAmount", ol[i].DiscountAmount, rl[i].DiscountAmount);
            C(n + "TaxCodeId", ol[i].TaxCodeId, rl[i].TaxCodeId);
            C(n + "TaxRate", ol[i].TaxRate, rl[i].TaxRate);
            C(n + "LineAmount", ol[i].LineAmount, rl[i].LineAmount);
            C(n + "TaxAmount", ol[i].TaxAmount, rl[i].TaxAmount);
            C(n + "TotalAmount", ol[i].TotalAmount, rl[i].TotalAmount);
        }
        return d;
    }

    private static DomainException LockedFieldError(List<string> diffs) =>
        new("replacement.locked_field",
            "A replacement keeps the original's date and amounts; these fields cannot change: " + string.Join(", ", diffs));

    /// <summary>UpdateDraft for a replacement draft: customer re-snapshot + descriptive fields only. The request's
    /// numeric fields must equal what is stored (else replacement.locked_field); tax is NEVER recomputed.</summary>
    private async Task UpdateReplacementDraftAsync(TaxInvoice ti, CreateTaxInvoiceRequest req, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("ti.customer_missing", $"Customer {req.CustomerId} not found.");
        if (customer.VatRegistered && (string.IsNullOrEmpty(customer.TaxId) || string.IsNullOrEmpty(customer.BranchCode)))
            throw new DomainException("ti.customer_incomplete",
                "VAT-registered customer requires Tax ID + branch_code (ม.86/4 #3).");

        var diffs = new List<string>();
        if (req.IsTaxInclusive != ti.IsTaxInclusive) diffs.Add("IsTaxInclusive");
        if (req.CurrencyCode != ti.CurrencyCode) diffs.Add("CurrencyCode");
        if (req.ExchangeRate != ti.ExchangeRate) diffs.Add("ExchangeRate");
        if (req.BusinessUnitId != ti.BusinessUnitId) diffs.Add("BusinessUnitId");
        if (req.QuotationId != ti.QuotationId) diffs.Add("QuotationId");
        var stored = ti.Lines.OrderBy(l => l.LineNo).ToList();
        if (req.Lines.Count != stored.Count) diffs.Add("Lines.Count");
        else
            for (var i = 0; i < stored.Count; i++)
            {
                var s = stored[i]; var r = req.Lines[i]; var n = $"Lines[{i + 1}].";
                if (r.ProductId != s.ProductId) diffs.Add(n + "ProductId");
                if (r.Quantity != s.Quantity) diffs.Add(n + "Quantity");
                if (r.UnitPrice != s.UnitPrice) diffs.Add(n + "UnitPrice");
                if (r.DiscountPercent != s.DiscountPercent) diffs.Add(n + "DiscountPercent");
                if (r.TaxRate != s.TaxRate) diffs.Add(n + "TaxRate");
                if (r.TaxCodeId is { } tc && tc != s.TaxCodeId) diffs.Add(n + "TaxCodeId");
                if (r.ProductType is { } pt && pt != s.ProductType) diffs.Add(n + "ProductType");
            }
        if (diffs.Count > 0) throw LockedFieldError(diffs);

        ti.CustomerId = customer.CustomerId;
        ti.CustomerTaxId = customer.TaxId;
        ti.CustomerBranchCode = customer.BranchCode;
        ti.CustomerBranchName = customer.BranchName;
        ti.CustomerName = customer.NameTh;
        ti.CustomerAddress = customer.BillingAddress ?? string.Empty;
        ti.CustomerVatRegistered = customer.VatRegistered;
        ti.DueDate = req.DueDate;
        ti.PaymentTerms = req.PaymentTerms;
        ti.Notes = req.Notes;

        // Delete-and-recreate the lines (D3.2) with the stored numbers and the requested descriptive text.
        var merged = stored.Select((s, i) => new TaxInvoiceLine
        {
            LineNo = s.LineNo, ProductId = s.ProductId, ProductCode = s.ProductCode, ProductType = s.ProductType,
            DescriptionTh = req.Lines[i].DescriptionTh, Quantity = s.Quantity, UomId = s.UomId,
            UomText = req.Lines[i].UomText, UnitPrice = s.UnitPrice, DiscountPercent = s.DiscountPercent,
            DiscountAmount = s.DiscountAmount, LineAmount = s.LineAmount, TaxCodeId = s.TaxCodeId, TaxCode = s.TaxCode,
            TaxRate = s.TaxRate, TaxAmount = s.TaxAmount, TotalAmount = s.TotalAmount,
        }).ToList();
        _db.RemoveRange(ti.Lines);
        ti.Lines.Clear();
        foreach (var l in merged) ti.Lines.Add(l);
    }

    /// <summary>Post-time re-check (3.4.5/3.4.6): the draft may have been edited out of band; refuses a replacement
    /// whose original is not Voided, whose DocDate moved, or whose locked fields differ.</summary>
    private async Task CheckReplacementAsync(TaxInvoice ti, long origId, CancellationToken ct)
    {
        var orig = await _db.TaxInvoices.AsNoTracking().Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.TaxInvoiceId == origId, ct)
            ?? throw new DomainException("replacement.original_not_cancelled", "Original Tax Invoice not found.");
        if (orig.Status != DocumentStatus.Voided)
            throw new DomainException("replacement.original_not_cancelled",
                "The original Tax Invoice is not cancelled; a replacement cannot be posted.");
        var diffs = LockedTiDiffs(orig, ti);
        if (ti.DocDate != orig.DocDate) diffs.Add("DocDate");
        if (diffs.Count > 0) throw LockedFieldError(diffs);
    }
}
