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

/// <summary>
/// Receipt issuance. On POST: allocates RC-NNNN, marks posted, updates the linked Tax Invoices'
/// payment status (PAID / PARTIAL) atomically. Posted TIs are not edited beyond payment_status —
/// the immutability trigger only blocks the legal fields. Auto-posts the cash/AR settlement to GL.
/// </summary>
public sealed partial class ReceiptService : IReceiptService
{
    private const string RcPrefix = "RC";

    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly IGlPostingService       _gl;
    private readonly IPeriodCloseService     _period;
    private readonly IActivityRecorder       _activity;
    private readonly ICompanyTaxConfigService _taxCfg;
    private readonly IFileStorageService     _storage;   // Sprint 13k — logo on PDF

    public ReceiptService(AccountingDbContext db, ITenantContext tenant, IClock clock,
        INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period,
        IActivityRecorder activity, ICompanyTaxConfigService taxCfg, IFileStorageService storage)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _gl = gl; _period = period; _activity = activity; _taxCfg = taxCfg;
        _storage = storage;
    }

    public async Task<long> CreateDraftAsync(CreateReceiptRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // Sprint 14 P7 — per-key BU lock.
        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, _tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {_tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        // Standalone non-VAT receipts omit Applications entirely → normalize null to empty
        // so the apply/standalone branches below can treat it uniformly.
        req = req with { BusinessUnitId = effBu, Applications = req.Applications ?? Array.Empty<ReceiptApplicationInput>() };

        // §10 — the receipt date is ALWAYS today in Asia/Bangkok, never from the request.
        var docDate = _clock.TodayInBangkok();
        await _period.EnsureOpenAsync(docDate, ct);

        var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("rc.customer_missing", $"Customer {req.CustomerId} not found.");

        // Validate applications. VAT path → posted TI, same customer, within outstanding.
        // Non-VAT credit path → issued/delivered DO, same customer. Standalone non-VAT
        // (no applications) carries its own lines (validated by the request validator).
        var tiIds = req.Applications.Where(a => a.TaxInvoiceId.HasValue)
            .Select(a => a.TaxInvoiceId!.Value).ToList();
        var tis = await _db.TaxInvoices
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .ToListAsync(ct);
        var doIds = req.Applications.Where(a => a.DeliveryOrderId.HasValue)
            .Select(a => a.DeliveryOrderId!.Value).ToList();
        var dos = doIds.Count > 0
            ? await _db.DeliveryOrders.Where(d => doIds.Contains(d.DeliveryOrderId)).ToListAsync(ct)
            : new List<DeliveryOrder>();
        // cont.69 Phase 1 — non-VAT apply-to-Invoice (BillingNote).
        var bnIds = req.Applications.Where(a => a.BillingNoteId.HasValue)
            .Select(a => a.BillingNoteId!.Value).ToList();
        var bns = bnIds.Count > 0
            ? await _db.BillingNotes.Where(b => bnIds.Contains(b.BillingNoteId)).ToListAsync(ct)
            : new List<BillingNote>();

        // A non-VAT company issues no Tax Invoice (ม.86/4) → it has none to apply to.
        var tax = await _taxCfg.GetAsync(ct);
        if (!tax.VatMode && tiIds.Count > 0)
            throw new DomainException("rc.non_vat_no_ti",
                "A non-VAT company has no Tax Invoices; apply to a Delivery Order or issue a standalone receipt.");

        foreach (var app in req.Applications)
        {
            if (app.TaxInvoiceId is { } tiId)
            {
                var ti = tis.FirstOrDefault(t => t.TaxInvoiceId == tiId)
                    ?? throw new DomainException("rc.ti_missing", $"Tax Invoice {tiId} not found.");
                if (ti.Status != DocumentStatus.Posted)
                    throw new DomainException("rc.ti_not_posted",
                        $"Tax Invoice {ti.TaxInvoiceId} must be POSTED to apply a receipt.");
                if (ti.CustomerId != customer.CustomerId)
                    throw new DomainException("rc.ti_customer_mismatch",
                        $"Tax Invoice {ti.TaxInvoiceId} is for a different customer.");
                var outstanding = ti.TotalAmount - ti.AmountPaid;
                if (app.AppliedAmount > outstanding)
                    throw new DomainException("rc.overpaid",
                        $"Applied amount {app.AppliedAmount} exceeds outstanding {outstanding} on TI {ti.DocNo}.");
            }
            else if (app.DeliveryOrderId is { } doId)
            {
                var dord = dos.FirstOrDefault(d => d.DeliveryOrderId == doId)
                    ?? throw new DomainException("rc.do_missing", $"Delivery Order {doId} not found.");
                if (dord.Status == DeliveryOrderStatus.Draft)
                    throw new DomainException("rc.do_not_issued",
                        $"Delivery Order {dord.DeliveryOrderId} must be issued before a receipt applies to it.");
                if (dord.CustomerId != customer.CustomerId)
                    throw new DomainException("rc.do_customer_mismatch",
                        $"Delivery Order {dord.DeliveryOrderId} is for a different customer.");
            }
            else if (app.BillingNoteId is { } bnId)
            {
                // cont.69 Phase 1 — non-VAT apply-to-Invoice: the Invoice must be Issued
                // (doc number allocated) and belong to the same customer.
                var bn = bns.FirstOrDefault(b => b.BillingNoteId == bnId)
                    ?? throw new DomainException("rc.invoice_missing", $"Invoice {bnId} not found.");
                if (bn.Status == BillingNoteStatus.Draft)
                    throw new DomainException("rc.invoice_not_issued",
                        $"Invoice {bn.BillingNoteId} must be issued before a receipt applies to it.");
                if (bn.CustomerId != customer.CustomerId)
                    throw new DomainException("rc.invoice_customer_mismatch",
                        $"Invoice {bn.BillingNoteId} is for a different customer.");
            }
        }

        // Sprint 14 P7 — an API-key caller may NOT settle a cross-BU receipt
        // (a key for Lab must not receive Reptify payments). BFF/JWT users keep
        // the Sprint-8.6 cross-BU flexibility.
        if (_tenant.ApiKeyId is not null)
        {
            var distinctBus = tis.Where(t => t.BusinessUnitId is not null)
                .Select(t => t.BusinessUnitId!.Value).Distinct().ToList();
            if (distinctBus.Count > 1)
                throw new DomainException("business_unit.cross_bu_not_allowed_for_this_key",
                    "An API key cannot settle a receipt spanning multiple Business Units.");
        }

        // Sprint 8 — BU. Required when the company opted in. Header value here is
        // provisional; PostAsync recomputes it from the applied TIs (cross-BU → NULL).
        var requiresBu = await _db.Companies
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId
                && x.CompanyId == _tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        // Amount = sum of applications (apply path) OR sum of own lines (standalone non-VAT).
        var amount = req.Applications.Count > 0
            ? req.Applications.Sum(a => a.AppliedAmount)
            : (req.Lines?.Sum(l => l.Amount) ?? 0m);

        // Sprint (multi-category WHT, 2026-05-22) — build the per-income-type WHT
        // breakdown. Prefer req.WhtLines; fall back to a single legacy line when only
        // the scalar WhtAmount/WhtTypeId are supplied (older API callers / tests).
        // Legacy scalar WHT with no type is a caller error — silently dropping the
        // amount would lose withholding (and the customer's 50ทวิ). Reject it.
        if (req.WhtAmount > 0m && (req.WhtLines is null or { Count: 0 }) && req.WhtTypeId is null)
            throw new DomainException("rc.wht_type_invalid",
                "WhtTypeId is required when WhtAmount > 0.");
        var whtInputs = req.WhtLines is { Count: > 0 }
            ? req.WhtLines.ToList()
            : req.WhtAmount > 0m && req.WhtTypeId is { } legacyId
                ? new List<ReceiptWhtLineInput> { new(legacyId, 0m) }
                : new List<ReceiptWhtLineInput>();

        var whtLines = new List<ReceiptWhtLine>();
        if (whtInputs.Count > 0)
        {
            var typeIds = whtInputs.Select(w => w.WhtTypeId).Distinct().ToList();
            var types = await _db.WhtTypes
                .Where(w => typeIds.Contains(w.WhtTypeId) && w.IsActive)
                .ToDictionaryAsync(w => w.WhtTypeId, ct);
            foreach (var wi in whtInputs)
            {
                if (!types.TryGetValue(wi.WhtTypeId, out var wt))
                    throw new DomainException("rc.wht_type_invalid",
                        $"WHT type {wi.WhtTypeId} is not a valid active type.");
                // Supplied base, else derive from the legacy scalar amount/rate.
                var baseAmt = wi.BaseAmount > 0m
                    ? wi.BaseAmount
                    : wt.Rate > 0m
                        ? Math.Round(req.WhtAmount / wt.Rate, 2, MidpointRounding.AwayFromZero)
                        : 0m;
                var lineWht = Math.Round(baseAmt * wt.Rate, 2, MidpointRounding.AwayFromZero);
                whtLines.Add(new ReceiptWhtLine
                {
                    WhtTypeId      = wt.WhtTypeId,
                    IncomeTypeCode = wt.IncomeTypeCode,
                    WhtTypeCode    = wt.Code,
                    WhtRate        = wt.Rate,
                    BaseAmount     = baseAmt,
                    WhtAmount      = lineWht,
                });
            }
        }
        var whtTotal = whtLines.Sum(l => l.WhtAmount);
        if (whtTotal > amount + 0.01m)
            throw new DomainException("rc.wht_exceeds_amount",
                $"WHT {whtTotal} exceeds the receipt amount {amount}.");
        // Legacy single-category pointer: the one type, else NULL when multi-category.
        int? headerWhtType = whtLines.Count == 1 ? whtLines[0].WhtTypeId : null;

        var rc = new Receipt
        {
            CompanyId       = _tenant.CompanyId,
            BranchId        = _tenant.BranchId,
            DocDate         = docDate,   // §10 — pinned to Asia/Bangkok today
            CustomerId      = customer.CustomerId,
            CustomerName    = customer.NameTh,
            CustomerAddress = customer.BillingAddress ?? string.Empty,
            CustomerTaxId   = customer.TaxId,
            PaymentMethod   = req.PaymentMethod,
            ChequeNo        = req.ChequeNo,
            ChequeDate      = req.ChequeDate,
            BankAccountId   = req.BankAccountId,
            BusinessUnitId  = req.BusinessUnitId,
            WhtAmount           = whtTotal,
            WhtTypeId           = headerWhtType,
            WhtLines            = whtLines,
            CustomerWhtCertNo   = req.CustomerWhtCertNo,
            CustomerWhtCertDate = req.CustomerWhtCertDate,
            CurrencyCode    = req.CurrencyCode,
            ExchangeRate    = req.ExchangeRate,
            Amount          = amount,
            TotalAmount     = amount,
            TotalAmountThb  = Math.Round(amount * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            Notes           = req.Notes,
            // M4a — stamp the key name when created by an API-key principal (MCP agent).
            CreatedViaApiKeyName = _tenant.ApiKeyName,
            Applications = req.Applications.Select(a => new ReceiptApplication
            {
                TaxInvoiceId    = a.TaxInvoiceId,
                DeliveryOrderId = a.DeliveryOrderId,
                BillingNoteId   = a.BillingNoteId,
                AppliedAmount   = a.AppliedAmount,
            }).ToList(),
            // Standalone non-VAT receipt — own line items (cash bill). Empty for the
            // apply path (lines are derived from the applied TI/DO on read).
            Lines = (req.Lines ?? Array.Empty<ReceiptLineInput>())
                .Select((l, i) => new ReceiptLine
                {
                    LineNo        = i + 1,
                    ProductId     = l.ProductId,
                    ProductCode   = l.ProductCode,
                    ProductType   = l.ProductType,
                    DescriptionTh = l.DescriptionTh,
                    Quantity      = l.Quantity,
                    UomText       = l.UomText,
                    UnitPrice     = l.UnitPrice,
                    Amount        = l.Amount,
                }).ToList(),
        };

        _db.Receipts.Add(rc);
        await _db.SaveChangesAsync(ct);
        _activity.Record("Receipt", rc.ReceiptId, rc.DocNo, rc.CompanyId, "Created", toStatus: "Draft");
        await _db.SaveChangesAsync(ct);
        return rc.ReceiptId;
    }

    public async Task<ReceiptPostedResult> PostAsync(long receiptId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var rc = await _db.Receipts
                .Include(r => r.Applications)
                .Include(r => r.WhtLines)
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {receiptId} not found.");

        await _period.EnsureOpenAsync(rc.DocDate, ct);

        // Sprint 8 cross-BU resolution: distinct BU across the applied TIs.
        // Exactly one non-null shared BU → that BU (number gets the sub-prefix).
        // Mixed (≥2 distinct, incl. a NULL/X mix) → header BU NULL + crosses flag.
        // Non-VAT (DO-applied / standalone) receipts have no TI → BU stays as set on draft.
        var appTiIds = rc.Applications.Where(a => a.TaxInvoiceId.HasValue)
            .Select(a => a.TaxInvoiceId!.Value).ToList();
        var crosses = false;
        int? sharedBu = rc.BusinessUnitId;   // non-VAT (no TI) keeps the draft BU
        if (appTiIds.Count > 0)
        {
            var distinctBu = await _db.TaxInvoices
                .Where(t => appTiIds.Contains(t.TaxInvoiceId))
                .Select(t => t.BusinessUnitId)
                .Distinct().ToListAsync(ct);

            sharedBu = distinctBu is [var only] ? only : null;
            crosses = distinctBu.Count > 1;
            rc.BusinessUnitId = sharedBu;
        }

        var buCode = sharedBu is { } sbid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == sbid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var rcNo = await _numbers.NextAsync(rc.CompanyId, rc.BranchId, RcPrefix, subPrefix: buCode, rc.DocDate, ct);
        var now = _clock.UtcNow;

        rc.MarkPosted(rcNo, _tenant.UserId ?? 0, now);
        _activity.Record("Receipt", rc.ReceiptId, rcNo, rc.CompanyId, "Posted", "Draft", "Posted");

        // Sprint 8.6 — net cash after the customer's withholding.
        rc.CashReceived = rc.Amount - rc.WhtAmount;

        // Apply payments → update each TI's AmountPaid / PaymentStatus. Only TI
        // applications settle AR; DO applications + standalone lines recognize revenue
        // at receipt (no prior AR to settle — see GlPostingService.PostReceiptAsync).
        // ponytail: app-level check; add a row lock / unique constraint if concurrent
        // over-apply is seen in practice (mirrors PaymentVoucherService.PostAsync 409-414).
        var tiApps = rc.Applications
            .Where(a => a.TaxInvoiceId.HasValue)
            .GroupBy(a => a.TaxInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AppliedAmount));

        foreach (var (tiId, applied) in tiApps)
        {
            var ti = await _db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == tiId, ct);
            if (ti.AmountPaid + applied > ti.TotalAmount + 0.01m)
                throw new DomainException("receipt.over_applied",
                    $"Applying {applied} to Tax Invoice {ti.DocNo} would exceed its total " +
                    $"{ti.TotalAmount} (already paid: {ti.AmountPaid}).");
            ti.AmountPaid += applied;
            ti.PaymentStatus = ti.AmountPaid >= ti.TotalAmount ? "PAID" : "PARTIAL";
        }

        // Sprint (multi-category WHT) — record the customer-issued 50ทวิ (Direction='R').
        // One certificate ROW per income type, all sharing the customer's single cert
        // number (cert_no is the customer's, not from our WT sequence; no PDF). The
        // cert no may be supplied later: if it's missing the receipt still posts
        // ("ขาดใบทวิ 50") and SetWhtCertAsync records the rows later.
        if (rc.WhtLines.Count > 0 && !string.IsNullOrWhiteSpace(rc.CustomerWhtCertNo))
        {
            await AddReceivableCertsAsync(rc, rc.CustomerWhtCertNo!, rc.CustomerWhtCertDate, now, ct);
        }

        await _db.SaveChangesAsync(ct);

        // Sprint 13i C6 — auto-derive Billing Note settlement. Any Issued BN that
        // references one of the just-paid TIs flips to Settled once the sum paid
        // across all its referenced TIs covers the BN total. Manual MarkSettled
        // stays for admin override. Grouping is via the sales.billing_note_tax_invoices
        // join table (Sprint 13i C7 — replaced the bigint[] column).
        var affectedBns = await _db.BillingNotes
            .Where(b => b.CompanyId == rc.CompanyId
                && b.Status == BillingNoteStatus.Issued
                && b.TaxInvoiceLinks.Any(j => appTiIds.Contains(j.TaxInvoiceId)))
            .ToListAsync(ct);
        if (affectedBns.Count > 0)
        {
            foreach (var bn in affectedBns)
            {
                var bnTiIds = await _db.BillingNoteTaxInvoices
                    .Where(j => j.BillingNoteId == bn.BillingNoteId)
                    .Select(j => j.TaxInvoiceId).ToListAsync(ct);
                var paid = await _db.TaxInvoices
                    .Where(t => t.CompanyId == rc.CompanyId && bnTiIds.Contains(t.TaxInvoiceId))
                    .SumAsync(t => t.AmountPaid, ct);
                if (paid >= bn.TotalAmount)
                {
                    bn.Status = BillingNoteStatus.Settled;
                    bn.SettledAt = now;
                    _activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId,
                        "Settled", "Issued", "Settled", note: $"ชำระครบจากใบเสร็จ {rcNo}");
                }
            }
            await _db.SaveChangesAsync(ct);
        }

        await _gl.PostReceiptAsync(rc.ReceiptId, ct);

        await tx.CommitAsync(ct);

        return new ReceiptPostedResult(
            rc.ReceiptId, rcNo, now, rc.Amount, crosses, rc.CashReceived, rc.WhtAmount);
    }

    // Sprint 13j-FE / multi-category WHT — supply the customer's 50ทวิ number/date
    // after the receipt was posted without it ("ขาดใบทวิ 50"). Creates one
    // WhtCertificate row per income type on first set (idempotent), all sharing the
    // single customer cert no. Attachments handled separately via AttachmentsSection.
    public async Task SetWhtCertAsync(long receiptId, string certNo, DateOnly? certDate, CancellationToken ct)
    {
        var rc = await _db.Receipts
                .Include(r => r.WhtLines)
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {receiptId} not found.");
        if (rc.WhtAmount <= 0 || rc.WhtLines.Count == 0)
            throw new DomainException("rc.no_wht", "Receipt has no withholding tax.");
        if (string.IsNullOrWhiteSpace(certNo))
            throw new DomainException("rc.cert_required", "ระบุเลขที่ใบ 50ทวิ");

        rc.CustomerWhtCertNo = certNo.Trim();
        rc.CustomerWhtCertDate = certDate;

        var exists = await _db.WhtCertificates.AnyAsync(w => w.ReceiptId == receiptId && w.Direction == "R", ct);
        if (!exists)
            await AddReceivableCertsAsync(rc, rc.CustomerWhtCertNo!, certDate, DateTimeOffset.UtcNow, ct);

        await _db.SaveChangesAsync(ct);
    }

    // Adds one Direction='R' WhtCertificate per receipt WHT line, all sharing the
    // customer's single 50ทวิ number. Snapshots payer=customer / payee=company.
    // Does NOT SaveChanges — the caller commits (same transaction on POST).
    private async Task AddReceivableCertsAsync(
        Receipt rc, string certNo, DateOnly? certDate, DateTimeOffset now, CancellationToken ct)
    {
        var company = await _db.Companies.FirstAsync(c => c.CompanyId == rc.CompanyId, ct);
        var typeIds = rc.WhtLines.Select(l => l.WhtTypeId).Distinct().ToList();
        var wtMap = await _db.WhtTypes.Where(w => typeIds.Contains(w.WhtTypeId))
            .ToDictionaryAsync(w => w.WhtTypeId, ct);

        foreach (var line in rc.WhtLines)
        {
            var wt = wtMap[line.WhtTypeId];
            _db.WhtCertificates.Add(new Accounting.Domain.Entities.Tax.WhtCertificate
            {
                CompanyId        = rc.CompanyId,
                BranchId         = rc.BranchId,
                DocNo            = certNo,                       // customer's cert no (shared)
                CertDate         = certDate ?? rc.DocDate,
                Direction        = "R",
                PaymentVoucherId = null,
                ReceiptId        = rc.ReceiptId,
                // Payer = the customer who withheld from us.
                PayerTaxId       = rc.CustomerTaxId ?? string.Empty,
                PayerBranchCode  = "00000",
                PayerName        = rc.CustomerName,
                PayerAddress     = rc.CustomerAddress,
                // Payee = us (the company).
                PayeeTaxId       = company.TaxId,
                PayeeName        = company.NameTh,
                PayeeAddress     = company.AddressTh ?? string.Empty,
                PayeeType        = Accounting.Domain.Enums.CustomerType.Corporate,
                FormType         = wt.FormType,
                IncomeTypeCode   = line.IncomeTypeCode,
                IncomeDescription = wt.NameTh,
                IncomeAmount     = line.BaseAmount,
                WhtRate          = line.WhtRate,
                WhtAmount        = line.WhtAmount,
                Status           = Accounting.Domain.Enums.DocumentStatus.Posted,
                IssuedAt         = now,
                IssuedBy         = _tenant.UserId,
            });
        }
    }
}
