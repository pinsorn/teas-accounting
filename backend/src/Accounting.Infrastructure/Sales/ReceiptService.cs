using Accounting.Application.Abstractions;
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

    public ReceiptService(AccountingDbContext db, ITenantContext tenant, IClock clock,
        INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _gl = gl; _period = period;
    }

    public async Task<long> CreateDraftAsync(CreateReceiptRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await _period.EnsureOpenAsync(req.DocDate, ct);

        var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("rc.customer_missing", $"Customer {req.CustomerId} not found.");

        // Validate each TI is posted, belongs to the same customer, and has outstanding balance.
        var tiIds = req.Applications.Select(a => a.TaxInvoiceId).ToList();
        var tis = await _db.TaxInvoices
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .ToListAsync(ct);

        foreach (var app in req.Applications)
        {
            var ti = tis.FirstOrDefault(t => t.TaxInvoiceId == app.TaxInvoiceId)
                ?? throw new DomainException("rc.ti_missing", $"Tax Invoice {app.TaxInvoiceId} not found.");
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

        // Sprint 8 — BU. Required when the company opted in. Header value here is
        // provisional; PostAsync recomputes it from the applied TIs (cross-BU → NULL).
        var requiresBu = await _db.Companies
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        var amount = req.Applications.Sum(a => a.AppliedAmount);

        // Sprint 8.6 — AR-side WHT. Type must be active; the customer can't be
        // withheld more than the total being settled (cash_received >= 0).
        if (req.WhtAmount > 0)
        {
            if (req.WhtTypeId is not { } wtId ||
                !await _db.WhtTypes.AnyAsync(w => w.WhtTypeId == wtId && w.IsActive, ct))
                throw new DomainException("rc.wht_type_invalid",
                    "A valid active WHT type is required when WHT is withheld.");
            if (req.WhtAmount > amount + 0.01m)
                throw new DomainException("rc.wht_exceeds_amount",
                    $"WHT {req.WhtAmount} exceeds the receipt amount {amount}.");
        }

        var rc = new Receipt
        {
            CompanyId       = _tenant.CompanyId,
            BranchId        = _tenant.BranchId,
            DocDate         = req.DocDate,
            CustomerId      = customer.CustomerId,
            CustomerName    = customer.NameTh,
            CustomerAddress = customer.BillingAddress ?? string.Empty,
            CustomerTaxId   = customer.TaxId,
            PaymentMethod   = req.PaymentMethod,
            ChequeNo        = req.ChequeNo,
            ChequeDate      = req.ChequeDate,
            BankAccountId   = req.BankAccountId,
            BusinessUnitId  = req.BusinessUnitId,
            WhtAmount           = req.WhtAmount,
            WhtTypeId           = req.WhtTypeId,
            CustomerWhtCertNo   = req.CustomerWhtCertNo,
            CustomerWhtCertDate = req.CustomerWhtCertDate,
            CurrencyCode    = req.CurrencyCode,
            ExchangeRate    = req.ExchangeRate,
            Amount          = amount,
            TotalAmount     = amount,
            TotalAmountThb  = Math.Round(amount * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            Notes           = req.Notes,
            Applications = req.Applications.Select(a => new ReceiptApplication
            {
                TaxInvoiceId  = a.TaxInvoiceId,
                AppliedAmount = a.AppliedAmount,
            }).ToList(),
        };

        _db.Receipts.Add(rc);
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
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {receiptId} not found.");

        await _period.EnsureOpenAsync(rc.DocDate, ct);

        // Sprint 8 cross-BU resolution: distinct BU across the applied TIs.
        // Exactly one non-null shared BU → that BU (number gets the sub-prefix).
        // Mixed (≥2 distinct, incl. a NULL/X mix) → header BU NULL + crosses flag.
        var appTiIds = rc.Applications.Select(a => a.TaxInvoiceId).ToList();
        var distinctBu = await _db.TaxInvoices
            .Where(t => appTiIds.Contains(t.TaxInvoiceId))
            .Select(t => t.BusinessUnitId)
            .Distinct().ToListAsync(ct);

        int? sharedBu = distinctBu is [var only] ? only : null;
        var crosses = distinctBu.Count > 1;
        rc.BusinessUnitId = sharedBu;

        var buCode = sharedBu is { } sbid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == sbid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var rcNo = await _numbers.NextAsync(rc.CompanyId, rc.BranchId, RcPrefix, subPrefix: buCode, rc.DocDate, ct);
        var now = _clock.UtcNow;

        rc.MarkPosted(rcNo, _tenant.UserId ?? 0, now);

        // Sprint 8.6 — net cash after the customer's withholding.
        rc.CashReceived = rc.Amount - rc.WhtAmount;

        // Apply payments → update each TI's AmountPaid / PaymentStatus.
        foreach (var app in rc.Applications)
        {
            var ti = await _db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == app.TaxInvoiceId, ct);
            ti.AmountPaid += app.AppliedAmount;
            ti.PaymentStatus = ti.AmountPaid >= ti.TotalAmount ? "PAID" : "PARTIAL";
        }

        // Sprint 8.6 — record the customer-issued 50ทวิ (Direction='R'). cert_no
        // is the customer's number (not from our WT sequence); no PDF generated.
        if (rc.WhtAmount > 0 && rc.WhtTypeId is { } wtId)
        {
            var company = await _db.Companies
                .FirstAsync(c => c.CompanyId == rc.CompanyId, ct);
            var wt = await _db.WhtTypes.FirstAsync(w => w.WhtTypeId == wtId, ct);
            var incomeBase = wt.Rate > 0m
                ? Math.Round(rc.WhtAmount / wt.Rate, 2, MidpointRounding.AwayFromZero)
                : rc.Amount;

            _db.WhtCertificates.Add(new Accounting.Domain.Entities.Tax.WhtCertificate
            {
                CompanyId       = rc.CompanyId,
                BranchId        = rc.BranchId,
                DocNo           = rc.CustomerWhtCertNo!,            // customer's cert no
                CertDate        = rc.CustomerWhtCertDate ?? rc.DocDate,
                Direction       = "R",
                PaymentVoucherId = null,
                ReceiptId       = rc.ReceiptId,
                // Payer = the customer who withheld from us.
                PayerTaxId      = rc.CustomerTaxId ?? string.Empty,
                PayerBranchCode = "00000",
                PayerName       = rc.CustomerName,
                PayerAddress    = rc.CustomerAddress,
                // Payee = us (the company).
                PayeeTaxId      = company.TaxId,
                PayeeName       = company.NameTh,
                PayeeAddress    = company.AddressTh ?? string.Empty,
                PayeeType       = Accounting.Domain.Enums.CustomerType.Corporate,
                FormType        = wt.FormType,
                IncomeTypeCode  = wt.IncomeTypeCode,
                IncomeDescription = wt.NameTh,
                IncomeAmount    = incomeBase,
                WhtRate         = wt.Rate,
                WhtAmount       = rc.WhtAmount,
                Status          = Accounting.Domain.Enums.DocumentStatus.Posted,
                IssuedAt        = now,
                IssuedBy        = _tenant.UserId,
            });
        }

        await _db.SaveChangesAsync(ct);

        await _gl.PostReceiptAsync(rc.ReceiptId, ct);

        await tx.CommitAsync(ct);

        return new ReceiptPostedResult(
            rc.ReceiptId, rcNo, now, rc.Amount, crosses, rc.CashReceived, rc.WhtAmount);
    }
}
