using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
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

        await _period.EnsureOpenAsync(req.DocDate, ct);

        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.VendorId == req.VendorId, ct)
            ?? throw new DomainException("vi.vendor_missing", $"Vendor {req.VendorId} not found.");

        var claim = req.VatClaimPeriod ?? PeriodOf(req.VendorTaxInvoiceDate);
        EnsureClaimInWindow(claim, req.VendorTaxInvoiceDate);

        var vi = new VendorInvoice
        {
            CompanyId = _tenant.CompanyId,
            BranchId  = _tenant.BranchId,
            DocDate              = req.DocDate,
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
        };
        RollUp(vi);

        _db.VendorInvoices.Add(vi);
        await _db.SaveChangesAsync(ct);
        _activity.Record("VendorInvoice", vi.VendorInvoiceId, vi.DocNo, vi.CompanyId,
            "Created", toStatus: "Draft", module: "purchase");
        await _db.SaveChangesAsync(ct);
        return vi.VendorInvoiceId;
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

            var net = Math.Round(input.Amount, 4, MidpointRounding.AwayFromZero);
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

        vi.DocDate              = req.DocDate;
        vi.VendorTaxInvoiceNo   = req.VendorTaxInvoiceNo;
        vi.VendorTaxInvoiceDate = req.VendorTaxInvoiceDate;
        vi.VatClaimPeriod       = claim;
        vi.CurrencyCode         = req.CurrencyCode;
        vi.ExchangeRate         = req.ExchangeRate;
        vi.Notes                = req.Notes;

        _db.VendorInvoiceLines.RemoveRange(vi.Lines);
        vi.Lines = await BuildLinesAsync(req.Lines, ct);
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

        await _period.EnsureOpenAsync(vi.DocDate, ct);
        EnsureClaimInWindow(vi.VatClaimPeriod, vi.VendorTaxInvoiceDate);
        await EnsureClaimPeriodOpenAsync(vi, ct);   // §5

        // The vendor's tax invoice (ใบกำกับภาษีซื้อ) is the primary documentary
        // evidence supporting the input-VAT claim under ม.82/4 + ม.86/4; the Revenue
        // Department audit (สรรพากร) requires the source document to be retrievable
        // for every input-VAT claim. We do NOT issue our own tax invoice (no /pdf
        // route) — we merely RECORD the vendor's, so requiring an attachment at Post
        // is the gate that "Post" actually establishes audit evidence. Same shape as
        // ReceiptService rejecting wht_amount > 0 without a wht_type — block the
        // state transition, keep the doc in Draft.
        var attachCount = await _db.Attachments.CountAsync(
            a => a.ParentType == Domain.Enums.AttachmentParentType.VendorInvoice
              && a.ParentId == id
              && a.DeletedAt == null, ct);
        if (attachCount == 0)
            throw new DomainException("vi.attachment_required",
                "ต้องแนบไฟล์ใบกำกับภาษีจากผู้ขายก่อนจึงจะโพสต์ใบกำกับภาษีซื้อได้ " +
                "(Attach the vendor's tax-invoice file before posting.)");

        var docNo = await _numbers.NextAsync(
            vi.CompanyId, vi.BranchId, ViPrefix, subPrefix: null, vi.DocDate, ct);

        var now = _clock.UtcNow;
        vi.MarkPosted(docNo.Value, _tenant.UserId ?? 0, now);
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
