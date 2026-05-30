using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Payment Voucher pipeline. Snapshots vendor + expense category at Draft; allocates
/// PV-CATEGORY-NNNN + (when WHT>0) WT-NNNN at Post. Certificate is issued in the same TX
/// so the auditor never sees a PV without its accompanying 50 ทวิ.
/// </summary>
public sealed partial class PaymentVoucherService : IPaymentVoucherService
{
    private const string PvPrefix = "PV";
    private const string WtPrefix = "WT";

    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly IGlPostingService       _gl;
    private readonly IPeriodCloseService     _period;
    private readonly IActivityRecorder       _activity;
    private readonly IVendorInvoiceService   _viService;   // cont.76 — PV→VI guided create

    public PaymentVoucherService(
        AccountingDbContext db,
        ITenantContext tenant,
        IClock clock,
        INumberSequenceService numbers,
        IGlPostingService gl,
        IPeriodCloseService period,
        IActivityRecorder activity,
        IVendorInvoiceService viService)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _gl = gl; _period = period; _activity = activity; _viService = viService;
    }

    /// <summary>
    /// cont.76 — create a VendorInvoice pre-filled from this PV and link it back. Reuses the
    /// VI draft pipeline (which owns the ม.82/4 VatClaimPeriod default + recoverable-VAT split),
    /// so this method only maps PV → VI request and sets the link. doc_date = today Asia/Bangkok
    /// (UTC+7, no DST), never user input (§10). Standalone VI create is intentionally left intact.
    /// </summary>
    public async Task<long> CreateVendorInvoiceFromPvAsync(
        long paymentVoucherId, CreateViFromPvRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var pv = await _db.PaymentVouchers.Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.PaymentVoucherId == paymentVoucherId, ct)
            ?? throw new DomainException("pv.not_found", $"Payment Voucher {paymentVoucherId} not found.");

        if (pv.VendorInvoiceId is not null)
            throw new DomainException("pv.vi_exists",
                $"Payment Voucher {paymentVoucherId} already has a linked Vendor Invoice " +
                $"({pv.VendorInvoiceId}).");

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddHours(7));   // Asia/Bangkok = UTC+7
        var viReq = new CreateVendorInvoiceRequest(
            DocDate:              today,
            VendorId:             pv.VendorId,
            VendorTaxInvoiceNo:   req.VendorTaxInvoiceNo,
            VendorTaxInvoiceDate: req.VendorTaxInvoiceDate,
            VatClaimPeriod:       req.VatClaimPeriod,
            CurrencyCode:         pv.CurrencyCode,
            ExchangeRate:         pv.ExchangeRate,
            Notes:                pv.Description,
            Lines: pv.Lines.OrderBy(l => l.LineNo).Select(l => new VendorInvoiceLineInput(
                ExpenseCategoryId: pv.ExpenseCategoryId,
                ExpenseAccountId:  l.ExpenseAccountId,
                Description:       l.Description,
                Amount:            l.Amount,
                VatRate:           l.VatRate,
                ProductType:       l.ProductType)).ToList(),
            HasInputVat:     req.HasInputVat,
            PurchaseOrderId: null,
            BusinessUnitId:  pv.BusinessUnitId);   // cont.79 — carry the PV's BU to the VI

        var viId = await _viService.CreateDraftAsync(viReq, ct);

        pv.VendorInvoiceId = viId;
        pv.UpdatedAt = _clock.UtcNow;
        _activity.Record("PaymentVoucher", pv.PaymentVoucherId, pv.DocNo, pv.CompanyId,
            "LinkedVendorInvoice", note: $"vi:{viId}", module: "purchase");
        await _db.SaveChangesAsync(ct);
        return viId;
    }

    public async Task<long> CreateDraftAsync(CreatePaymentVoucherRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await _period.EnsureOpenAsync(req.DocDate, ct);

        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.VendorId == req.VendorId, ct)
            ?? throw new DomainException("pv.vendor_missing", $"Vendor {req.VendorId} not found.");

        var category = await _db.ExpenseCategories
                .FirstOrDefaultAsync(c => c.CategoryId == req.ExpenseCategoryId, ct)
            ?? throw new DomainException("pv.expense_category_missing",
                $"Expense category {req.ExpenseCategoryId} not found.");

        // cont.79 — BU (GL dimension). Required when the company opted in; if supplied
        // it must be an active BU of this tenant (mirror TaxInvoiceService).
        var requiresBu = await _db.Companies
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstOrDefaultAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        var lines = new List<PaymentVoucherLine>();
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var input = req.Lines[i];
            var net   = Math.Round(input.Amount, 4, MidpointRounding.AwayFromZero);
            var vat   = Math.Round(net * input.VatRate, 2, MidpointRounding.AwayFromZero);
            var wht   = Math.Round(net * input.WhtRate, 2, MidpointRounding.AwayFromZero);

            var expenseAccountId = input.ExpenseAccountId ?? category.DefaultExpenseAccountId
                ?? throw new DomainException("pv.expense_account_missing",
                    $"Line {i + 1}: no expense account (category '{category.CategoryCode}' has no default).");

            // cont.76 — สินค้า/บริการ snapshot. Default-GOOD on a missing value (existing
            // call-sites omit it); reject only an explicitly-invalid non-null code.
            var productType = ProductTypeCodes.Normalize(input.ProductType, code =>
                throw new DomainException("pv.product_type_invalid",
                    $"Line {i + 1}: product_type '{code}' must be one of " +
                    "GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE."));

            lines.Add(new PaymentVoucherLine
            {
                LineNo            = i + 1,
                ExpenseAccountId  = expenseAccountId,
                Description       = input.Description,
                ProductType       = productType,
                Amount            = net,
                TaxCodeId         = input.TaxCodeId,
                VatRate           = input.VatRate,
                VatAmount         = vat,
                IsRecoverableVat  = input.IsRecoverableVat,
                // Category auto-fills the default WHT type (CLAUDE.md §12.1) when the
                // request omits it — required so a WHT line can issue its 50 ทวิ.
                WhtTypeId         = input.WhtTypeId ?? category.DefaultWhtTypeId,
                WhtRate           = input.WhtRate,
                WhtAmount         = wht,
            });
        }

        var subtotal = lines.Sum(l => l.Amount);
        var vatTotal = lines.Sum(l => l.VatAmount);
        var whtTotal = lines.Sum(l => l.WhtAmount);

        // Sprint 8.7 — self-withhold: explicit value wins; else auto-true for a
        // foreign vendor without Thai VAT-D (Scenario B). VI-linked is blocked
        // by the validator, so this only ever applies to standalone PV.
        var selfWithhold = req.SelfWithholdMode
            ?? (vendor.IsForeign && !vendor.HasThaiVatDReg);
        var requiresPnd36 = vendor.IsForeign && !vendor.HasThaiVatDReg;
        // Self-withhold: we pay the vendor the full amount (no WHT deducted);
        // WHT is owed to RD separately. Otherwise the WHT is netted off payment.
        var totalPaid = selfWithhold
            ? subtotal + vatTotal
            : subtotal + vatTotal - whtTotal;

        var pv = new PaymentVoucher
        {
            CompanyId          = _tenant.CompanyId,
            BranchId           = _tenant.BranchId,
            BusinessUnitId     = req.BusinessUnitId,   // cont.79 — GL dimension snapshot
            SubPrefix          = category.CategoryCode,
            DocDate            = req.DocDate,
            PostingDate        = req.DocDate,
            VendorId           = vendor.VendorId,
            ExpenseCategoryId  = category.CategoryId,
            VendorInvoiceId    = req.VendorInvoiceId,
            SelfWithholdMode          = selfWithhold,
            RequiresPnd36ReverseCharge = requiresPnd36,
            VendorTaxId        = vendor.TaxId,
            VendorBranchCode   = vendor.BranchCode,
            VendorName         = vendor.NameTh,
            VendorAddress      = vendor.Address,
            VendorType         = vendor.VendorType,
            PaymentMethod      = req.PaymentMethod,
            ChequeNo           = req.ChequeNo,
            ChequeDate         = req.ChequeDate,
            BankAccountId      = req.BankAccountId,
            CurrencyCode       = req.CurrencyCode,
            ExchangeRate       = req.ExchangeRate,
            SubtotalAmount     = subtotal,
            VatAmount          = vatTotal,
            WhtAmount          = whtTotal,
            TotalPaid          = totalPaid,
            TotalAmountThb     = Math.Round(totalPaid * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            Description        = req.Description,
            Notes              = req.Notes,
            Lines              = lines,
        };

        _db.PaymentVouchers.Add(pv);
        await _db.SaveChangesAsync(ct);
        _activity.Record("PaymentVoucher", pv.PaymentVoucherId, pv.DocNo, pv.CompanyId,
            "Created", toStatus: "Draft", module: "purchase");
        await _db.SaveChangesAsync(ct);
        return pv.PaymentVoucherId;
    }

    public async Task<PaymentVoucherApprovedResult> ApproveAsync(long paymentVoucherId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var pv = await _db.PaymentVouchers
                .FirstOrDefaultAsync(p => p.PaymentVoucherId == paymentVoucherId, ct)
            ?? throw new DomainException("pv.not_found", $"PV {paymentVoucherId} not found.");

        var approver = _tenant.UserId ?? 0;
        // SoD enforced in the entity (and belt-and-braces by DB CHECK ck_pv_sod).
        pv.MarkApproved(approver, _clock.UtcNow);
        _activity.Record("PaymentVoucher", pv.PaymentVoucherId, pv.DocNo, pv.CompanyId,
            "Approved", fromStatus: "Draft", toStatus: "Approved", module: "purchase");
        await _db.SaveChangesAsync(ct);

        return new PaymentVoucherApprovedResult(
            pv.PaymentVoucherId, pv.ApprovedBy!.Value, pv.ApprovedAt!.Value);
    }

    public async Task<PaymentVoucherPostedResult> PostAsync(long paymentVoucherId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var pv = await _db.PaymentVouchers
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.PaymentVoucherId == paymentVoucherId, ct)
            ?? throw new DomainException("pv.not_found", $"PV {paymentVoucherId} not found.");

        await _period.EnsureOpenAsync(pv.DocDate, ct);

        // cont.79 — embed the BU code into the doc-number sub-prefix at POST:
        // MM-YYYY-PV-{BU}-{CATEGORY}-NNNN. No BU → unchanged (…-PV-{CATEGORY}-…).
        var buCode = pv.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == bid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var subPrefix = buCode is null ? pv.SubPrefix : $"{buCode}-{pv.SubPrefix}";
        var pvNo = await _numbers.NextAsync(
            pv.CompanyId, pv.BranchId, PvPrefix, subPrefix, pv.DocDate, ct);

        var now = _clock.UtcNow;
        pv.MarkPosted(pvNo, _tenant.UserId ?? 0, now);
        _activity.Record("PaymentVoucher", pv.PaymentVoucherId, pv.DocNo, pv.CompanyId,
            "Posted", fromStatus: "Approved", toStatus: "Posted", module: "purchase");

        long? certId = null;
        string? certNo = null;
        if (pv.WhtAmount > 0)
        {
            var company = await _db.Companies
                    .Include(c => c.Branches)
                    .FirstAsync(c => c.CompanyId == pv.CompanyId, ct);
            var branch = company.Branches.FirstOrDefault(b => b.BranchId == pv.BranchId)
                         ?? company.Branches.First(b => b.IsHeadOffice);
            var formType = pv.VendorType == CustomerType.Individual
                ? WhtFormType.Pnd3 : WhtFormType.Pnd53;

            // One 50 ทวิ per income type (ม.50 ทวิ): a PV mixing e.g. rent (5%) and
            // service (3%) must report each income type on its own certificate line/doc.
            var whtGroups = pv.Lines
                .Where(l => l.WhtAmount > 0)
                .GroupBy(l => l.WhtTypeId)
                .OrderBy(g => g.Key);

            foreach (var grp in whtGroups)
            {
                var whtType = await _db.WhtTypes.FirstOrDefaultAsync(w => w.WhtTypeId == grp.Key, ct)
                    ?? throw new DomainException("pv.wht_type_missing",
                        $"WHT line references missing WhtType {grp.Key}.");

                var groupIncome = grp.Sum(l => l.Amount);
                var groupWht    = grp.Sum(l => l.WhtAmount);
                var grpNo = (await _numbers.NextAsync(
                    pv.CompanyId, pv.BranchId, WtPrefix, subPrefix: null, pv.DocDate, ct)).Value;

                var cert = new WhtCertificate
                {
                    CompanyId        = pv.CompanyId,
                    BranchId         = pv.BranchId,
                    DocNo            = grpNo,
                    CertDate         = pv.DocDate,
                    PaymentVoucherId = pv.PaymentVoucherId,
                    PayerTaxId       = company.TaxId,
                    PayerBranchCode  = branch.BranchCode,
                    PayerName        = company.NameTh,
                    PayerAddress     = company.AddressTh ?? string.Empty,
                    PayeeTaxId       = pv.VendorTaxId,
                    PayeeName        = pv.VendorName,
                    PayeeAddress     = pv.VendorAddress ?? string.Empty,
                    PayeeType        = pv.VendorType,
                    FormType         = formType,
                    IncomeTypeCode    = whtType.IncomeTypeCode,
                    IncomeDescription = whtType.NameTh,
                    IncomeAmount      = groupIncome,
                    // Effective rate for the group (handles per-line rate variance within a type).
                    WhtRate           = groupIncome == 0m ? 0m
                                        : Math.Round(groupWht / groupIncome, 6, MidpointRounding.AwayFromZero),
                    WhtAmount         = groupWht,
                    Status            = DocumentStatus.Posted,
                    IssuedAt          = now,
                    IssuedBy          = _tenant.UserId,
                };
                _db.WhtCertificates.Add(cert);
                await _db.SaveChangesAsync(ct);

                // D3 — the 50 ทวิ is auto-generated here (WHT > 0); the audit hook
                // lives inside PaymentVoucherService.PostAsync, not WhtCertificateService
                // (which is read-only). One "Generated" row per income-type certificate.
                _activity.Record("WhtCertificate", cert.WhtCertificateId, cert.DocNo, cert.CompanyId,
                    "Generated", toStatus: "Issued", note: $"pv:{pv.DocNo}", module: "purchase");

                // Surface the first (lowest WhtTypeId) certificate on the result for back-compat.
                certId ??= cert.WhtCertificateId;
                certNo ??= grpNo;
            }
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        // 6A §3 — settle a Vendor Invoice. Amount cleared against AP = the gross the VI
        // accrued (subtotal + all VAT); WHT is withheld (remitted to RD), not "unpaid".
        if (pv.VendorInvoiceId is { } viId)
        {
            var vi = await _db.VendorInvoices
                    .FirstOrDefaultAsync(v => v.VendorInvoiceId == viId, ct)
                ?? throw new DomainException("pv.vi_not_found",
                    $"Vendor Invoice {viId} not found (or other tenant).");
            if (vi.CompanyId != pv.CompanyId)
                throw new DomainException("pv.vi_cross_tenant",
                    "Payment Voucher and Vendor Invoice belong to different companies.");
            if (vi.Status != DocumentStatus.Posted)
                throw new DomainException("pv.vi_not_posted",
                    $"Vendor Invoice {viId} must be Posted before it can be settled (status {vi.Status}).");

            var applied = pv.SubtotalAmount + pv.VatAmount;
            var outstanding = vi.TotalAmount - vi.SettledAmount;
            if (applied > outstanding + 0.01m)
                throw new DomainException("pv.vi_over_settle",
                    $"Settlement {applied} exceeds Vendor Invoice {viId} outstanding {outstanding} " +
                    "(0.01 baht tolerance).");

            _db.PaymentVoucherApplications.Add(new PaymentVoucherApplication
            {
                PaymentVoucherId = pv.PaymentVoucherId,
                VendorInvoiceId  = vi.VendorInvoiceId,
                AppliedAmount    = applied,
            });
            vi.SettledAmount += applied;                       // stored, never SUM-computed
            vi.SettlementStatus = vi.SettledAmount >= vi.TotalAmount - 0.01m ? "PAID"
                                : vi.SettledAmount > 0m ? "PARTIAL" : "UNPAID";
            // VI is IConcurrencyVersioned → concurrent settles collide on Version (no double-settle).
            await _db.SaveChangesAsync(ct);
        }

        // Flush any audit rows still tracked (the "Posted" PV row + any per-cert
        // "Generated" rows recorded after the last cert save) before the GL post,
        // so they always land in this same transaction regardless of branch taken.
        await _db.SaveChangesAsync(ct);

        await _gl.PostPaymentVoucherAsync(pv.PaymentVoucherId, ct);

        await tx.CommitAsync(ct);

        return new PaymentVoucherPostedResult(
            pv.PaymentVoucherId, pvNo.Value, now,
            pv.TotalPaid, pv.VatAmount, pv.WhtAmount, certId, certNo);
    }
}
