using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Vendor Invoice (AP accrual) pipeline. Snapshots vendor + the per-line
/// recoverable/capex/cogs flags at DRAFT (never re-resolved at POST — ม.82/5,
/// Answer-Sana-Question-Backend5-Followup §2). Input VAT lands in ภ.พ.30 by
/// <c>vat_claim_period</c> (ม.82/4 — TI month .. +6).
/// </summary>
public sealed partial class VendorInvoiceService : IVendorInvoiceService
{
    private const string ViPrefix = "VI";

    private readonly AccountingDbContext    _db;
    private readonly ITenantContext         _tenant;
    private readonly IClock                 _clock;
    private readonly INumberSequenceService _numbers;
    private readonly IGlPostingService      _gl;
    private readonly IPeriodCloseService    _period;
    private readonly IActivityRecorder      _activity;

    public VendorInvoiceService(
        AccountingDbContext db, ITenantContext tenant, IClock clock,
        INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period,
        IActivityRecorder activity)
    {
        _db = db; _tenant = tenant; _clock = clock;
        _numbers = numbers; _gl = gl; _period = period; _activity = activity;
    }

    private static int PeriodOf(DateOnly d) => d.Year * 100 + d.Month;

    /// <summary>ม.82/4 allowed claim periods: vendor-TI month through +6 months.</summary>
    private static IReadOnlyList<int> ClaimWindow(DateOnly vendorTiDate)
    {
        var anchor = new DateOnly(vendorTiDate.Year, vendorTiDate.Month, 1);
        return Enumerable.Range(0, 7).Select(m => PeriodOf(anchor.AddMonths(m))).ToList();
    }

    private static void EnsureClaimInWindow(int claim, DateOnly vendorTiDate)
    {
        var win = ClaimWindow(vendorTiDate);
        if (!win.Contains(claim))
            throw new DomainException("vi.claim_period_out_of_range",
                $"vat_claim_period {claim} is outside the ม.82/4 window " +
                $"[{win[0]}..{win[^1]}] for vendor TI dated {vendorTiDate:yyyy-MM-dd} " +
                "(claimable in the TI month or any of the following 6 months).");
    }

    public async Task<long> CreateDraftAsync(CreateVendorInvoiceRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // §10 / ม.86/4(7) — vi.DocDate (the AP-accrual / posting date) is ALWAYS today
        // in Asia/Bangkok, never trusted from the request. (VendorTaxInvoiceDate, which
        // drives the ม.82/4 claim window, stays the counterparty's date.)
        var docDate = _clock.TodayInBangkok();
        await _period.EnsureOpenAsync(docDate, ct);

        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.VendorId == req.VendorId, ct)
            ?? throw new DomainException("vi.vendor_missing", $"Vendor {req.VendorId} not found.");

        var claim = req.VatClaimPeriod ?? PeriodOf(req.VendorTaxInvoiceDate);
        EnsureClaimInWindow(claim, req.VendorTaxInvoiceDate);

        // cont.79 — BU (GL dimension). Required when the company opted in; if supplied
        // it must be an active BU of this tenant (mirror TaxInvoiceService).
        // WP1.2 (F27, D1) — also read VatRegistered here: a non-VAT company must NEVER book
        // recoverable input VAT, regardless of the vendor's own VAT status or a client/MCP
        // HasInputVat=true override (D1 — VAT may be entered but always books as cost).
        var companyFlags = await _db.Companies
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => new { c.RequiresBusinessUnit, c.VatRegistered })
            .FirstOrDefaultAsync(ct);
        var requiresBu = companyFlags?.RequiresBusinessUnit ?? false;
        // F-3 (Opus Tier-2 review) — fail safe in the SAME direction as PostAsync's equivalent
        // query (missing company row -> non-recoverable, never the reverse). Unreachable in
        // practice (an authenticated tenant always has a company row) but the two guards must
        // not disagree on which way is "safe."
        var companyVatRegistered = companyFlags?.VatRegistered ?? false;
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId
                && x.CompanyId == _tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        // WP3.4 (D3, F29) — a Closed PO accepts no further VI/PV linking. This is the
        // single seam every create path funnels through (REST, MCP, and the from-PO
        // helper CreateFromPurchaseOrderAsync, which already checks Approved before
        // calling here — this re-check is cheap and closes the gap for a raw
        // req.PurchaseOrderId passed directly). Already-linked/posted VIs are untouched
        // — this only blocks a NEW link at create time, never PostAsync.
        if (req.PurchaseOrderId is { } linkedPoId)
        {
            var poStatus = await _db.PurchaseOrders.AsNoTracking()
                .Where(p => p.PurchaseOrderId == linkedPoId)
                .Select(p => (PurchaseOrderStatus?)p.Status)
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("po.not_found", $"Purchase Order {linkedPoId} not found.");
            if (poStatus != PurchaseOrderStatus.Approved)
                throw new DomainException("po.not_approved",
                    $"Purchase Order {linkedPoId} must be Approved to link a Vendor Invoice (status {poStatus}).");
        }

        var vi = new VendorInvoice
        {
            CompanyId = _tenant.CompanyId,
            BranchId  = _tenant.BranchId,
            BusinessUnitId = req.BusinessUnitId,   // cont.79 — GL dimension snapshot
            DocDate              = docDate,   // §10 — pinned to Asia/Bangkok today
            VendorTaxInvoiceNo   = req.VendorTaxInvoiceNo,
            VendorTaxInvoiceDate = req.VendorTaxInvoiceDate,
            VatClaimPeriod       = claim,
            VendorId         = vendor.VendorId,
            VendorTaxId      = vendor.TaxId,
            VendorBranchCode = vendor.BranchCode,
            VendorName       = vendor.NameTh,
            VendorAddress    = vendor.Address,
            VendorType       = vendor.VendorType,
            CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate,
            Notes        = req.Notes,
            // Sprint 8.7 — explicit wins; else auto-false for a non-VAT vendor
            // or a foreign vendor without Thai VAT-D (VAT can't be claimed).
            HasInputVat  = req.HasInputVat
                ?? !(!vendor.VatRegistered || (vendor.IsForeign && !vendor.HasThaiVatDReg)),
            RequiresPnd36ReverseCharge = vendor.IsForeign && !vendor.HasThaiVatDReg,
            PurchaseOrderId  = req.PurchaseOrderId,   // Sprint 12 — validated at Post
            Lines        = await BuildLinesAsync(req.Lines, ct),
            // M4 (MCP) — stamp the key name when created by an API-key principal (agent). Null for JWT.
            // Also covers the PV→VI guided path (CreateVendorInvoiceFromPvAsync routes through here).
            CreatedViaApiKeyName = _tenant.ApiKeyName,
        };
        // WP1.2 (F27, D1) — non-VAT company: force non-recoverable regardless of vendor/category
        // snapshot or an explicit req.HasInputVat=true. Every line's VAT lands in
        // NonRecoverableVatAmount (RollUp below), never VatAmount, so header and GL agree.
        if (!companyVatRegistered)
        {
            vi.HasInputVat = false;
            foreach (var l in vi.Lines) l.IsRecoverableVat = false;
        }
        RollUp(vi);

        _db.VendorInvoices.Add(vi);
        await _db.SaveChangesAsync(ct);
        _activity.Record("VendorInvoice", vi.VendorInvoiceId, vi.DocNo, vi.CompanyId,
            "Created", toStatus: "Draft", module: "purchase");
        await _db.SaveChangesAsync(ct);
        return vi.VendorInvoiceId;
    }

    // mcp-document-chain (D1) — PO → Vendor Invoice, inheriting vendor + lines. Footgun:
    // PurchaseOrderLine has NO ExpenseCategoryId/ProductType (unlike a VendorInvoiceLineInput,
    // which requires one) — so the caller's header ExpenseCategoryId is applied to EVERY
    // inherited line (mirrors how CreateVendorInvoiceFromPvAsync uses a single
    // pv.ExpenseCategoryId for all lines). Guards: PO must be Approved at CREATE time (the
    // standalone path only checks PO status at POST); one-VI-per-PO (today the standalone path
    // allows multiple VIs per PO by design — this is the NEW §B guard for the from-PO path only).
    public async Task<long> CreateFromPurchaseOrderAsync(
        long purchaseOrderId, CreateViFromPoRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var po = await _db.PurchaseOrders.AsNoTracking().Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.PurchaseOrderId == purchaseOrderId, ct)
            ?? throw new DomainException("po.not_found", $"Purchase Order {purchaseOrderId} not found.");
        if (po.Status != PurchaseOrderStatus.Approved)
            throw new DomainException("po.not_approved",
                $"Purchase Order {purchaseOrderId} must be Approved before creating a Vendor Invoice.");
        if (await _db.VendorInvoices.AnyAsync(
                v => v.PurchaseOrderId == purchaseOrderId && v.Status != DocumentStatus.Voided, ct))
            throw new DomainException("po.vi_exists",
                $"Purchase Order {purchaseOrderId} already has a Vendor Invoice.");

        var viReq = new CreateVendorInvoiceRequest(
            DocDate:              _clock.TodayInBangkok(),
            VendorId:             po.VendorId,
            VendorTaxInvoiceNo:   req.VendorTaxInvoiceNo,
            VendorTaxInvoiceDate: req.VendorTaxInvoiceDate,
            VatClaimPeriod:       req.VatClaimPeriod,
            CurrencyCode:         po.CurrencyCode,
            ExchangeRate:         po.ExchangeRate,
            Notes:                po.Notes,
            Lines: po.Lines.OrderBy(l => l.LineNo).Select(l => new VendorInvoiceLineInput(
                ExpenseCategoryId: req.ExpenseCategoryId,
                ExpenseAccountId:  null,
                Description:       l.DescriptionTh,
                Amount:            l.LineAmount,
                VatRate:           l.TaxRate)).ToList(),
            HasInputVat:     req.HasInputVat,
            PurchaseOrderId: po.PurchaseOrderId,
            BusinessUnitId:  req.BusinessUnitId ?? po.BusinessUnitId);

        var viId = await CreateDraftAsync(viReq, ct);
        _activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "CreatedVendorInvoice", note: $"→ ใบกำกับภาษีซื้อ {viId}", module: "purchase");
        await _db.SaveChangesAsync(ct);
        return viId;
    }

    private async Task<List<VendorInvoiceLine>> BuildLinesAsync(
        IReadOnlyList<VendorInvoiceLineInput> inputs, CancellationToken ct)
    {
        var lines = new List<VendorInvoiceLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var cat = await _db.ExpenseCategories
                    .FirstOrDefaultAsync(c => c.CategoryId == input.ExpenseCategoryId, ct)
                ?? throw new DomainException("vi.expense_category_missing",
                    $"Expense category {input.ExpenseCategoryId} not found.");

            var expenseAccountId = input.ExpenseAccountId ?? cat.DefaultExpenseAccountId
                ?? throw new DomainException("vi.expense_account_missing",
                    $"Line {i + 1}: no expense account (category '{cat.CategoryCode}' has no default).");

            // F-5 (Opus Tier-2 review) — the FluentValidation InclusiveBetween(0,1) bound on
            // VatRate only runs on the REST DTO; internal callers that build
            // CreateVendorInvoiceRequest in-code (CreateFromPurchaseOrderAsync, PV→VI) call
            // CreateDraftAsync/UpdateDraftAsync directly and skip it. BuildLinesAsync is the one
            // seam every create/update path funnels through, so enforce the same bound here
            // before computing VAT — a rate already in [0,1] is unaffected.
            if (input.VatRate < 0m || input.VatRate > 1m)
                throw new DomainException("vi.vat_rate_out_of_range",
                    $"Line {i + 1}: VAT rate {input.VatRate} is out of range; must be a fraction between 0 and 1.");

            // R1/C1 (WP-3) — THB is a 2-decimal currency; 4dp reached the immutable ledger.
            var net = Math.Round(input.Amount, 2, MidpointRounding.AwayFromZero);
            var vat = Math.Round(net * input.VatRate, 2, MidpointRounding.AwayFromZero);

            // cont.76 — สินค้า/บริการ snapshot. Default-GOOD on missing; reject invalid non-null.
            var productType = ProductTypeCodes.Normalize(input.ProductType, code =>
                throw new DomainException("vi.product_type_invalid",
                    $"Line {i + 1}: product_type '{code}' must be one of " +
                    "GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE."));

            lines.Add(new VendorInvoiceLine
            {
                LineNo            = i + 1,
                ExpenseCategoryId = cat.CategoryId,
                ExpenseAccountId  = expenseAccountId,
                Description       = input.Description,
                ProductType       = productType,
                Amount            = net,
                TaxCodeId         = cat.DefaultTaxCodeId,
                VatRate           = input.VatRate,
                VatAmount         = vat,
                // §2 — snapshot now, never re-resolve from category at POST.
                IsRecoverableVat  = cat.DefaultIsRecoverableVat,
                IsCapex           = cat.IsCapex,
                IsCogs            = cat.IsCogs,
            });
        }
        return lines;
    }

    private static void RollUp(VendorInvoice vi)
    {
        vi.SubtotalAmount          = vi.Lines.Sum(l => l.Amount);
        vi.VatAmount               = vi.Lines.Where(l => l.IsRecoverableVat).Sum(l => l.VatAmount);
        vi.NonRecoverableVatAmount = vi.Lines.Where(l => !l.IsRecoverableVat).Sum(l => l.VatAmount);
        vi.TotalAmount             = vi.SubtotalAmount + vi.VatAmount + vi.NonRecoverableVatAmount;
        vi.TotalAmountThb          = Math.Round(vi.TotalAmount * vi.ExchangeRate, 4, MidpointRounding.AwayFromZero);
    }

    public async Task UpdateDraftAsync(long id, CreateVendorInvoiceRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var vi = await _db.VendorInvoices.Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.VendorInvoiceId == id, ct)
            ?? throw new DomainException("vi.not_found", $"Vendor Invoice {id} not found.");
        if (vi.Status != Domain.Enums.DocumentStatus.Draft)
            throw new DomainException("vi.not_draft", "Only a Draft Vendor Invoice can be edited.");

        var claim = req.VatClaimPeriod ?? PeriodOf(req.VendorTaxInvoiceDate);
        EnsureClaimInWindow(claim, req.VendorTaxInvoiceDate);

        vi.DocDate              = _clock.TodayInBangkok();   // §10 — re-pin on edit
        vi.VendorTaxInvoiceNo   = req.VendorTaxInvoiceNo;
        vi.VendorTaxInvoiceDate = req.VendorTaxInvoiceDate;
        vi.VatClaimPeriod       = claim;
        vi.CurrencyCode         = req.CurrencyCode;
        vi.ExchangeRate         = req.ExchangeRate;
        vi.Notes                = req.Notes;
        vi.BusinessUnitId       = req.BusinessUnitId;   // cont.79 — parity with PO UpdateDraft

        _db.VendorInvoiceLines.RemoveRange(vi.Lines);
        vi.Lines = await BuildLinesAsync(req.Lines, ct);

        // F-2 (Opus Tier-2 review) — BuildLinesAsync re-snapshots IsRecoverableVat from the
        // category (true), so an edit to a non-VAT company's draft must re-apply the WP1.2
        // guard here too, or the header goes inconsistent (VatAmount gets the VAT back) until
        // PostAsync self-heals it.
        var updateCompanyVatRegistered = await _db.Companies
            .Where(c => c.CompanyId == vi.CompanyId)
            .Select(c => c.VatRegistered).FirstOrDefaultAsync(ct);
        if (!updateCompanyVatRegistered)
        {
            vi.HasInputVat = false;
            foreach (var l in vi.Lines) l.IsRecoverableVat = false;
        }
        RollUp(vi);

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetClaimPeriodAsync(long id, int vatClaimPeriod, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var vi = await _db.VendorInvoices.FirstOrDefaultAsync(v => v.VendorInvoiceId == id, ct)
            ?? throw new DomainException("vi.not_found", $"Vendor Invoice {id} not found.");
        if (vi.Status != Domain.Enums.DocumentStatus.Draft)
            throw new DomainException("vi.not_draft",
                "vat_claim_period is frozen once posted (ม.82/4 legal ref).");

        EnsureClaimInWindow(vatClaimPeriod, vi.VendorTaxInvoiceDate);
        vi.VatClaimPeriod = vatClaimPeriod;
        _activity.Record("VendorInvoice", vi.VendorInvoiceId, vi.DocNo, vi.CompanyId,
            "ClaimedPeriod", fromStatus: "Draft", toStatus: "Draft",
            note: $"period:{vatClaimPeriod}", module: "purchase");
        await _db.SaveChangesAsync(ct);
    }

    public async Task<VendorInvoicePostedResult> PostAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var vi = await _db.VendorInvoices.Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.VendorInvoiceId == id, ct)
            ?? throw new DomainException("vi.not_found", $"Vendor Invoice {id} not found.");

        // WP1.2 (F27, D1) — re-assert at Post: a draft created before this guard existed (or
        // via another path) must never post recoverable input VAT on a non-VAT company.
        var postCompanyVatRegistered = await _db.Companies
            .Where(c => c.CompanyId == vi.CompanyId)
            .Select(c => c.VatRegistered).FirstOrDefaultAsync(ct);
        if (!postCompanyVatRegistered && (vi.HasInputVat || vi.Lines.Any(l => l.IsRecoverableVat)))
        {
            vi.HasInputVat = false;
            foreach (var l in vi.Lines) l.IsRecoverableVat = false;
            RollUp(vi);
        }

        await _period.EnsureOpenAsync(vi.DocDate, ct);
        EnsureClaimInWindow(vi.VatClaimPeriod, vi.VendorTaxInvoiceDate);
        await EnsureClaimPeriodOpenAsync(vi, ct);   // §5

        // cont.77 (Ham 2026-05-30) — the vendor's tax-invoice file is the documentary
        // evidence for the ม.82/4 input-VAT claim, but in practice you often pay (and must
        // record the AP + claim period) BEFORE the vendor returns the document. So Post no
        // longer HARD-BLOCKS on a missing attachment; the document posts in an **incomplete**
        // state instead — surfaced by the advisory completeness flag MISSING_TAX_INVOICE_FILE
        // (computed on read) until the file is attached. The legal evidence must still exist
        // by the ภ.พ.30 filing; the completeness warning is the tracking mechanism.

        // cont.79 — embed the BU code into the doc-number sub-prefix at POST:
        // MM-YYYY-VI-{BU}-NNNN. No BU → unchanged (…-VI-NNNN).
        var buCode = vi.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == bid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var now = _clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        var docNo = await NumberedDocumentWriter.AllocateAndSaveAsync(
            _db,
            c => _numbers.NextAsync(vi.CompanyId, vi.BranchId, ViPrefix, subPrefix: buCode, vi.DocDate, c),
            (v, first) => { if (first) vi.MarkPosted(v.Value, _tenant.UserId ?? 0, now); else vi.DocNo = v.Value; },
            ct);
        _activity.Record("VendorInvoice", vi.VendorInvoiceId, vi.DocNo, vi.CompanyId,
            "Posted", fromStatus: "Draft", toStatus: "Posted", module: "purchase");
        await _db.SaveChangesAsync(ct);

        await _gl.PostVendorInvoiceAsync(vi.VendorInvoiceId, ct);

        // Sprint 12 — internal PO settlement / auto-close. Loose matching:
        // ≥95% of PO total → auto-close; >105% → over-receipt WARNING (HTTP 200
        // chip, not an error). Draft/Cancelled PO cannot be linked.
        string? poWarn = null;
        if (vi.PurchaseOrderId is { } poId)
        {
            var po = await _db.PurchaseOrders
                .FirstOrDefaultAsync(p => p.PurchaseOrderId == poId, ct)
                ?? throw new DomainException("vi.po_not_found",
                    $"Linked Purchase Order {poId} not found.");
            if (po.Status is Domain.Enums.PurchaseOrderStatus.Draft
                          or Domain.Enums.PurchaseOrderStatus.Cancelled)
                throw new DomainException("vi.po_link_invalid",
                    $"Cannot link a Vendor Invoice to a {po.Status} Purchase Order.");

            var linked = await _db.VendorInvoices
                .Where(x => x.PurchaseOrderId == poId
                         && x.Status == Domain.Enums.DocumentStatus.Posted)
                .SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0m;
            var (shouldClose, overReceipt) =
                Domain.Entities.Purchase.PoSettlement.Evaluate(linked, po.TotalAmount);
            if (po.Status == Domain.Enums.PurchaseOrderStatus.Approved && shouldClose)
                po.MarkClosed(now);
            if (overReceipt)
                poWarn = $"รับเกินใบสั่งซื้อ: รวม VI {linked:N2} > PO {po.TotalAmount:N2} " +
                         "(เกิน 105%) — โปรดตรวจสอบ";
            await _db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new VendorInvoicePostedResult(
            vi.VendorInvoiceId, docNo.Value, now, vi.TotalAmount, vi.VatAmount,
            vi.VatClaimPeriod, poWarn);
    }

    /// <summary>
    /// §5 — if the vat_claim_period falls in a CLOSED accounting period, reject with an
    /// actionable hint naming the next OPEN period still inside the ม.82/4 window.
    /// </summary>
    private async Task EnsureClaimPeriodOpenAsync(VendorInvoice vi, CancellationToken ct)
    {
        var (cy, cm) = (vi.VatClaimPeriod / 100, vi.VatClaimPeriod % 100);
        if (await _period.IsOpenAsync(cy, cm, ct)) return;

        foreach (var p in ClaimWindow(vi.VendorTaxInvoiceDate))
        {
            if (p <= vi.VatClaimPeriod) continue;
            if (await _period.IsOpenAsync(p / 100, p % 100, ct))
                throw new DomainException("vi.claim_period_closed",
                    $"vat_claim_period {vi.VatClaimPeriod} is in a CLOSED accounting period. " +
                    $"Set it to {p} (next OPEN period within the ม.82/4 window) and re-post.");
        }
        throw new DomainException("vi.claim_period_closed",
            $"vat_claim_period {vi.VatClaimPeriod} is CLOSED and no later period within the " +
            "ม.82/4 window is open. Re-open a period or correct the document.");
    }
}
