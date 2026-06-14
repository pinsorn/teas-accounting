using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

public sealed partial class TaxAdjustmentNoteService : ITaxAdjustmentNoteService
{
    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly IGlPostingService       _gl;
    private readonly IPeriodCloseService     _period;
    private readonly ICompanyTaxConfigService _taxCfg;
    private readonly IActivityRecorder       _activity;
    private readonly IFileStorageService     _storage;   // Sprint 13k — logo on PDF

    public TaxAdjustmentNoteService(AccountingDbContext db, ITenantContext tenant, IClock clock,
        INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period,
        ICompanyTaxConfigService taxCfg, IActivityRecorder activity, IFileStorageService storage)
    { _db = db; _tenant = tenant; _clock = clock; _numbers = numbers; _gl = gl; _period = period; _taxCfg = taxCfg; _activity = activity; _storage = storage; }

    // CN vs DN audit EntityType, matching ActivityEndpoints route mapping.
    private static string EntityTypeOf(TaxAdjustmentNote n) =>
        n.NoteType == TaxAdjustmentNoteType.Credit ? "CreditNote" : "DebitNote";

    public async Task<long> CreateDraftAsync(CreateTaxAdjustmentNoteRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // Sprint 14 P7 — per-key BU lock (applied before the TI-inherit default).
        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, _tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {_tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        req = req with { BusinessUnitId = effBu };

        await _period.EnsureOpenAsync(req.DocDate, ct);

        var ti = await _db.TaxInvoices
                .FirstOrDefaultAsync(t => t.TaxInvoiceId == req.OriginalTaxInvoiceId, ct)
            ?? throw new DomainException("note.original_missing",
                $"Original Tax Invoice {req.OriginalTaxInvoiceId} not found.");

        if (ti.Status != DocumentStatus.Posted)
            throw new DomainException("note.original_not_posted",
                "Original Tax Invoice must be POSTED to issue an adjustment note.");

        // Sprint 8 — BU defaults to the original TI's BU unless overridden.
        var buId = req.BusinessUnitId ?? ti.BusinessUnitId;
        if (req.BusinessUnitId is { } reqBu &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == reqBu
                && x.CompanyId == _tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {reqBu} not found or inactive.");
        var requiresBu = await _db.Companies
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && buId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

        var tax = Math.Round(req.AdjustmentSubtotal * req.TaxRate, 2, MidpointRounding.AwayFromZero);
        var total = req.AdjustmentSubtotal + tax;

        var note = new TaxAdjustmentNote
        {
            CompanyId  = _tenant.CompanyId,
            BranchId   = _tenant.BranchId,
            PrefixCode = req.NoteType == TaxAdjustmentNoteType.Credit ? "CN" : "DN",
            NoteType   = req.NoteType,
            DocDate       = req.DocDate,
            TaxPointDate  = req.DocDate,
            OriginalTaxInvoiceId = ti.TaxInvoiceId,
            ReasonCode = req.ReasonCode,
            Reason     = req.Reason,
            CustomerId            = ti.CustomerId,
            CustomerTaxId         = ti.CustomerTaxId,
            CustomerBranchCode    = ti.CustomerBranchCode,
            CustomerName          = ti.CustomerName,
            CustomerAddress       = ti.CustomerAddress,
            CustomerVatRegistered = ti.CustomerVatRegistered,
            CurrencyCode   = req.CurrencyCode,
            ExchangeRate   = req.ExchangeRate,
            SubtotalAmount = req.AdjustmentSubtotal,
            TaxAmount      = tax,
            TotalAmount    = total,
            TotalAmountThb = Math.Round(total * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            TaxRate        = req.TaxRate,
            Notes          = req.Notes,
            BusinessUnitId = buId,
        };

        _db.TaxAdjustmentNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        _activity.Record(EntityTypeOf(note), note.NoteId, note.DocNo, note.CompanyId, "Created",
            toStatus: "Draft", note: $"อ้างอิงใบกำกับภาษี {ti.DocNo ?? ti.TaxInvoiceId.ToString()}");
        await _db.SaveChangesAsync(ct);
        return note.NoteId;
    }

    public async Task<TaxAdjustmentNotePostedResult> PostAsync(long noteId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var note = await _db.TaxAdjustmentNotes.FirstOrDefaultAsync(n => n.NoteId == noteId, ct)
            ?? throw new DomainException("note.not_found", $"Note {noteId} not found.");

        await _period.EnsureOpenAsync(note.DocDate, ct);

        var buCode = note.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == bid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var docNo = await _numbers.NextAsync(
            note.CompanyId, note.BranchId, note.PrefixCode, subPrefix: buCode, note.DocDate, ct);

        var now = _clock.UtcNow;
        note.MarkPosted(docNo, _tenant.UserId ?? 0, now);
        _activity.Record(EntityTypeOf(note), note.NoteId, docNo, note.CompanyId, "Posted", "Draft", "Posted");

        await _db.SaveChangesAsync(ct);

        await _gl.PostTaxAdjustmentNoteAsync(note.NoteId, ct);

        await tx.CommitAsync(ct);

        return new TaxAdjustmentNotePostedResult(
            note.NoteId, docNo, now, note.NoteType, note.TotalAmount, note.TaxAmount);
    }
}
