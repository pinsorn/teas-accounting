using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Pdf;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Sales;   // ChainMath
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Sprint 12 — internal PO. SoD approval mirrors PV B2 (entity guard +
/// ck_po_sod DB CHECK). doc_no PO-NNNN allocated on Approve (+BU sub-prefix).
/// Tenant-scoped via the global query filter.
/// </summary>
public sealed class PurchaseOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    IFileStorageService storage, ICompanyTaxConfigService taxCfg) : IPurchaseOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreatePurchaseOrderRequest req, CancellationToken ct)
    {
        Auth();
        var v = await db.Vendors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VendorId == req.VendorId, ct)
            ?? throw new DomainException("vendor.not_found", "Vendor not found.");

        // cont.79 — BU (GL dimension). Required when the company opted in; if supplied
        // it must be an active BU of this tenant (mirror TaxInvoiceService).
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstOrDefaultAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId
                && x.CompanyId == tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        // §10 — DocDate is ALWAYS today in Asia/Bangkok, never trusted from the request
        // (an agent must not back-date a PO). doc_no is allocated on Approve in this month's bucket.
        var docDate = clock.TodayInBangkok();
        var po = new PurchaseOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = PurchaseOrderStatus.Draft, DocDate = docDate,
            ExpectedDeliveryDate = req.ExpectedDeliveryDate,
            VendorId = v.VendorId, VendorName = v.NameTh, VendorTaxId = v.TaxId,
            // F-A (specs/fix-purchase-nonvat-ux.md) — was never snapshotted, so the printed PO
            // (BuildPaperAsync below) silently dropped the vendor address even though the column
            // exists (mirrors VendorInvoiceService.CreateDraftAsync's VendorAddress = vendor.Address).
            VendorAddress = v.Address,
            VendorType = v.VendorType, BusinessUnitId = req.BusinessUnitId,
            CurrencyCode = req.CurrencyCode, ExchangeRate = req.ExchangeRate,
            Notes = req.Notes, InternalNotes = req.InternalNotes,
            // M4 (MCP) — stamp the key name when created by an API-key principal (agent). Null for JWT.
            CreatedViaApiKeyName = tenant.ApiKeyName,
        };
        req = req with { Lines = await ResolveTaxCodesAsync(req.Lines, ct) };
        Fill(po, req);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Created", toStatus: "Draft", module: "purchase");
        await db.SaveChangesAsync(ct);
        return po.PurchaseOrderId;
    }

    /// <summary>specs/fix-c1-backend-cleanup.md item 1 (U9) — PurchaseOrderLine.TaxCodeId was
    /// written verbatim from the request with no validation at all (the last verbatim-id
    /// writer left in the codebase — see fix-r2-u2-billing-tax-integrity.md §8). Unlike the
    /// sales chain, a PO line is always REQUEST-fed at the point of origin (no immutable
    /// upstream to launder from), and PO lines have no ExpenseCategory to inherit a default
    /// from (unlike VendorInvoiceService.BuildLinesAsync) — so this is a small, PO-local
    /// resolver, not a reuse of SalesLineBackstop:
    ///   • an id is supplied            → must be an ACTIVE row of the caller's own company's
    ///                                     master (mirrors bu.invalid), else REJECT typed
    ///                                     "po.tax_code_invalid" — never store a foreign id.
    ///   • id null, TaxRate &gt; 0       → the line actually charges VAT (the FE already
    ///                                     encodes the vendor's VAT status into TaxRate:
    ///                                     `taxRate: vendorVat ? l.taxRate : 0`). Resolve the
    ///                                     TaxCode string against this company's own master
    ///                                     (case-insensitive); if it does not match, fall back
    ///                                     to the company's own standard PURCHASE (input) VAT
    ///                                     code ("VAT-IN7" preferred, else lowest id Input+
    ///                                     Active code). No input code at all → never throw,
    ///                                     leave the pair as sent (mirrors SalesLineBackstop's
    ///                                     "no master at all" invariant).
    ///   • id null, TaxRate == 0        → leave the pair as sent (null); nothing is charged.
    /// Money invariant: TaxRate/ChainMath.Line are untouched — this only ever resolves the
    /// reference pair, never the rate.</summary>
    private async Task<IReadOnlyList<PurchaseOrderLineInput>> ResolveTaxCodesAsync(
        IReadOnlyList<PurchaseOrderLineInput> lines, CancellationToken ct)
    {
        var master = await db.TaxCodes.AsNoTracking()
            .Select(t => new { t.TaxCodeId, t.Code, t.Direction, t.IsActive })
            .ToListAsync(ct);
        var byId = master.ToDictionary(t => t.TaxCodeId);
        var byCode = master
            .OrderBy(t => t.TaxCodeId)
            .GroupBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var standardInput = master
            .Where(t => t.IsActive && t.Direction == TaxDirection.Input)
            .OrderBy(t => t.Code == "VAT-IN7" ? 0 : 1).ThenBy(t => t.TaxCodeId)
            .FirstOrDefault();

        var result = new List<PurchaseOrderLineInput>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.TaxCodeId is { } id)
            {
                if (!byId.TryGetValue(id, out var row) || !row.IsActive)
                    throw new DomainException("po.tax_code_invalid",
                        $"Line {i + 1}: tax code {id} not found or inactive for this company.");
                result.Add(l);
                continue;
            }
            if (l.TaxRate > 0m)
            {
                if (!string.IsNullOrWhiteSpace(l.TaxCode) && byCode.TryGetValue(l.TaxCode, out var matched))
                    result.Add(l with { TaxCodeId = matched.TaxCodeId, TaxCode = matched.Code });
                else if (standardInput is not null)
                    result.Add(l with { TaxCodeId = standardInput.TaxCodeId, TaxCode = standardInput.Code });
                else
                    result.Add(l);
            }
            else
            {
                result.Add(l);
            }
        }
        return result;
    }

    private static void Fill(PurchaseOrder po, CreatePurchaseOrderRequest req)
    {
        po.Lines.Clear();
        po.SubtotalAmount = po.VatAmount = po.TotalAmount = 0m;
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            po.Lines.Add(new PurchaseOrderLine
            {
                LineNo = n++, ProductId = l.ProductId, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                LineAmount = net, TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode,
                TaxRate = l.TaxRate, TaxAmount = vat, TotalAmount = total, Notes = l.Notes,
            });
            po.SubtotalAmount += net; po.VatAmount += vat; po.TotalAmount += total;
        }
        po.TotalAmountThb = Math.Round(po.TotalAmount * po.ExchangeRate, 4, MidpointRounding.AwayFromZero);
    }

    private async Task<PurchaseOrder> LoadAsync(long id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct)
            ?? throw new DomainException("po.not_found", $"Purchase Order {id} not found.");

    public async Task UpdateDraftAsync(long id, CreatePurchaseOrderRequest req, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        if (po.Status != PurchaseOrderStatus.Draft)
            throw new DomainException("po.not_draft", "Only a Draft PO can be edited.");
        // §10 AMENDED (Ham decision, 2026-07-15 — R2 confirm-round) — DocDate is stamped
        // ONCE, at CreateDraftAsync, and PRESERVED across every subsequent edit; it is
        // never re-pinned here. `req.DocDate` is still ignored either way (client dates
        // are never trusted) — the difference is which server-set value wins: the
        // ORIGINAL create-time stamp, not "now" at save time. The earlier rule (re-pin on
        // every edit, matching create) silently reset a draft's date each time a user
        // tweaked an unrelated field (e.g. business unit), which is the bug this reverses.
        po.ExpectedDeliveryDate = req.ExpectedDeliveryDate;
        po.BusinessUnitId = req.BusinessUnitId; po.CurrencyCode = req.CurrencyCode;
        po.ExchangeRate = req.ExchangeRate; po.Notes = req.Notes;
        po.InternalNotes = req.InternalNotes;
        req = req with { Lines = await ResolveTaxCodesAsync(req.Lines, ct) };
        Fill(po, req);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PurchaseOrderApprovedResult> ApproveAsync(long id, CancellationToken ct)
    {
        Auth();
        // H8 (review 2026-07-04) — NumberSequenceService's UPSERT auto-commits immediately
        // when there is no ambient transaction, so a NextAsync followed by a failing SaveChanges
        // left a consumed-but-unused number (a gap). Wrap alloc+save in one explicit tx, mirroring
        // the safe TaxInvoiceService.PostAsync pattern.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var po = await LoadAsync(id, ct);
        // §4.3 — re-pin DocDate to today AT APPROVAL, when the sequential number is allocated, so the
        // doc-no period bucket (MM-YYYY) always matches DocDate even if the draft was created in a
        // prior month (agy review 2026-06-19: a June draft approved in July must not keep June's date).
        po.DocDate = clock.TodayInBangkok();
        string? buCode = po.BusinessUnitId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var approvedAt = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            db,
            c => numbers.NextAsync(po.CompanyId, "PO", buCode, po.DocDate, c),
            (v, first) => { if (first) po.MarkApproved(tenant.UserId ?? 0, v.Value, approvedAt); else po.DocNo = v.Value; },   // SoD in entity + ck_po_sod
            ct);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Approved", fromStatus: "Draft", toStatus: "Approved", module: "purchase");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new PurchaseOrderApprovedResult(
            po.PurchaseOrderId, po.DocNo!, po.ApprovedBy!.Value, po.ApprovedAt!.Value);
    }

    public async Task MarkSentAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        if (po.Status != PurchaseOrderStatus.Approved)
            throw new DomainException("po.not_approved", "Only an Approved PO can be marked sent.");
        po.SentToVendorAt = clock.UtcNow;
        // NB: there is no "Sent" PurchaseOrderStatus enum member — the status stays
        // Approved and only SentToVendorAt is set. "Sent" here is a semantic audit label.
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "MarkedSent", fromStatus: "Approved", toStatus: "Sent", module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        var fromClose = po.Status.ToString();
        po.MarkClosed(clock.UtcNow);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Closed", fromStatus: fromClose, toStatus: "Closed", module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    // WP3.4 (D3) — Closed → Approved, only when no Vendor Invoice linked to this PO has
    // POSTED (a Draft-linked VI doesn't block — nothing has been claimed/booked yet).
    public async Task ReopenAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        var hasPostedVi = await db.VendorInvoices.AsNoTracking()
            .AnyAsync(v => v.PurchaseOrderId == id && v.Status == DocumentStatus.Posted, ct);
        if (hasPostedVi)
            throw new DomainException("po.reopen_blocked",
                "Cannot reopen: a posted Vendor Invoice is already linked to this Purchase Order.");
        po.MarkReopened(clock.UtcNow);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Reopened", fromStatus: "Closed", toStatus: "Approved", module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        var fromCancel = po.Status.ToString();
        po.MarkCancelled(reason, clock.UtcNow);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Cancelled", fromStatus: fromCancel, toStatus: "Cancelled", note: reason, module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PurchaseOrderListItem>> ListAsync(
        string? status, long? vendorId, CancellationToken ct)
    {
        Auth();
        var q = db.PurchaseOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<PurchaseOrderStatus>(status, true, out var st))
            q = q.Where(x => x.Status == st);
        if (vendorId is { } vid) q = q.Where(x => x.VendorId == vid);
        return await q.OrderByDescending(x => x.PurchaseOrderId)
            .Select(x => new PurchaseOrderListItem(
                x.PurchaseOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.ExpectedDeliveryDate, x.VendorName, x.TotalAmount, x.BusinessUnitId))
            .ToListAsync(ct);
    }

    public async Task<PurchaseOrderDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct);
        if (po is null) return null;
        var vis = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.PurchaseOrderId == id)
            .Select(v => new LinkedViDto(v.VendorInvoiceId, v.DocNo, v.TotalAmount))
            .ToListAsync(ct);
        var linked = vis.Sum(v => v.TotalAmount);
        // cont.79 — resolve BU code/name for display (null when no BU on the PO).
        var bu = po.BusinessUnitId is { } buId
            ? await db.BusinessUnits.AsNoTracking()
                .Where(x => x.BusinessUnitId == buId)
                .Select(x => new { x.Code, x.NameTh }).FirstOrDefaultAsync(ct)
            : null;
        // cont.94d — resolve each line's product taxonomy so a PV/derived-VAT prefill
        // knows the right VAT treatment. Authoritative from the linked Product; for an
        // ad-hoc line (no product) infer from the stored TaxRate (0 → treat as exempt so
        // the prefill carries 0% VAT, else GOOD).
        var productIds = po.Lines.Where(l => l.ProductId is not null)
            .Select(l => l.ProductId!.Value).Distinct().ToList();
        var productTypes = productIds.Count == 0
            ? new Dictionary<long, ProductType>()
            : await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, p => p.ProductType, ct);
        string LineProductType(PurchaseOrderLine l) =>
            l.ProductId is { } pid && productTypes.TryGetValue(pid, out var pt)
                ? ProductTypeCodes.ToCode(pt)
                : (l.TaxRate > 0m ? "GOOD" : "EXEMPT_GOOD");
        return new PurchaseOrderDetail(
            po.PurchaseOrderId, po.DocNo, po.Status.ToString(), po.DocDate,
            po.ExpectedDeliveryDate, po.VendorId, po.VendorName, po.BusinessUnitId,
            po.CurrencyCode, po.SubtotalAmount, po.VatAmount, po.TotalAmount,
            po.Notes, po.InternalNotes, po.ApprovedAt, po.ApprovedBy,
            po.SentToVendorAt, po.ClosedAt, po.CancellationReason,
            linked, po.TotalAmount - linked,
            po.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseOrderLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount,
                LineProductType(l))).ToList(),
            vis, bu?.Code, bu?.NameTh,   // cont.79 — BU display
            po.CreatedViaApiKeyName);    // M4a — agent-drafted badge
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
        => Pdf.PaperDocumentPdf.Render(await BuildPaperAsync(id, ct, copy));

    // cont.121 canonical paper DTO — the exact mapping BuildPdfAsync used, exposed for GET /paper.
    public async Task<PaperDocModel> BuildPaperAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var po = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct)
            ?? throw new DomainException("po.not_found", $"Purchase Order {id} not found.");

        // Sprint 13j-PURCH Phase C — render via the shared PaperDocument mirror
        // (IDENTICAL shape to the Sales TI builder). Seller = the issuing company
        // (we are the buyer issuing the PO); Customer = the vendor. cont.119 — labels
        // aligned to the FE purchase-orders page (screen==print): title ALL-CAPS,
        // party box ผู้ขาย/Vendor, sign roles ผู้สั่งซื้อ/ผู้รับใบสั่งซื้อ, กำหนดส่งมอบ.
        // Watermark: explicit copy → "สำเนา", else "ต้นฉบับ".
        var seller = await Pdf.PaperSellerSource.FromCompanyProfileAsync(db, po.CompanyId, ct, storage);

        // cont.120 (Ham ruling; Codex A vs agy B adjudicated → A) — mirror the FE BP-04
        // reconstruction EXACTLY so print == screen on a discounted PO: gross = Σ(unitPrice ×
        // quantity) (the true pre-discount value; per-line discounts are baked into the stored
        // SubtotalAmount), discount = gross − stored subtotal rounded to 2dp, row shown only
        // when ≥ 0.01 (suppresses rounding residue). The printed equation always reconciles
        // because BeforeVat anchors to the authoritative stored SubtotalAmount. ShowVat follows
        // the company VAT mode like the screen (was hardcoded default-true — a non-VAT company's
        // PO printed VAT rows the screen never showed).
        var gross = po.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var discount = Math.Round(gross - po.SubtotalAmount, 2, MidpointRounding.AwayFromZero);
        var model = new PaperDocModel(
            DocType: "ใบสั่งซื้อ",
            DocTypeEn: "PURCHASE ORDER",
            DocNo: po.DocNo ?? "(ร่าง)",
            IssueDate: po.DocDate,
            Seller: seller,
            // F-A (specs/fix-purchase-nonvat-ux.md) — pass the vendor address snapshot through
            // to the printed/detail paper (was omitted; PV's BuildPaperAsync already did this).
            Customer: new PaperCustomer(po.VendorName, Pdf.PaperFormat.TaxId(po.VendorTaxId), Address: po.VendorAddress),
            Items: po.Lines.OrderBy(l => l.LineNo).Select(l => new PaperLine(
                l.DescriptionTh, null, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList(),
            Summary: new PaperSummary(
                Subtotal: discount >= 0.01m ? gross : po.SubtotalAmount,
                Discount: discount >= 0.01m ? discount : null,
                BeforeVat: po.SubtotalAmount,
                Vat: po.VatAmount, Total: po.TotalAmount, VatRate: null,
                ShowVat: (await taxCfg.GetAsync(ct)).VatMode),
            SignRoles: new PaperSignRoles("ผู้สั่งซื้อ", "ผู้รับใบสั่งซื้อ"),
            ValidUntil: po.ExpectedDeliveryDate,
            ValidUntilLabel: po.ExpectedDeliveryDate is null ? null : "กำหนดส่งมอบ",
            Notes: po.Notes,
            PartyLabel: new PaperPartyLabel("ผู้ขาย", "Vendor"),
            Watermark: new PaperWatermark(
                copy ? "สำเนา" : "ต้นฉบับ",
                copy ? PaperWatermarkVariant.Warning : PaperWatermarkVariant.Success),
            Signatures: await Pdf.PaperSignatureSource.ResolveAsync(
                db, storage, po.ApprovedBy, null, stampOnMiddle: false,
                isSigned: po.Status != PurchaseOrderStatus.Draft && po.ApprovedBy is not null, ct));
        return model;
    }

    public async Task<OutstandingPoReport> OutstandingAsync(
        DateOnly asOf, long? vendorId, bool overdueOnly, CancellationToken ct)
    {
        Auth();
        var q = db.PurchaseOrders.AsNoTracking()
            .Where(x => x.Status == PurchaseOrderStatus.Approved);
        if (vendorId is { } vid) q = q.Where(x => x.VendorId == vid);
        var pos = await q.Select(x => new
        {
            x.PurchaseOrderId, x.DocNo, x.VendorName, x.ExpectedDeliveryDate, x.TotalAmount
        }).ToListAsync(ct);

        var ids = pos.Select(p => p.PurchaseOrderId).ToList();
        var viAgg = (await db.VendorInvoices.AsNoTracking()
            .Where(v => v.PurchaseOrderId != null && ids.Contains(v.PurchaseOrderId!.Value))
            .Select(v => new { v.PurchaseOrderId, v.TotalAmount })
            .ToListAsync(ct))
            .GroupBy(v => v.PurchaseOrderId!.Value)
            .ToDictionary(g => g.Key, g => (cnt: g.Count(), sum: g.Sum(x => x.TotalAmount)));

        var rows = new List<OutstandingPoRow>();
        foreach (var p in pos)
        {
            var (cnt, sum) = viAgg.GetValueOrDefault(p.PurchaseOrderId, (0, 0m));
            var overdue = p.ExpectedDeliveryDate is { } ed && ed < asOf
                ? asOf.DayNumber - ed.DayNumber : 0;
            if (overdueOnly && overdue <= 0) continue;
            rows.Add(new OutstandingPoRow(
                p.PurchaseOrderId, p.DocNo, p.VendorName, p.ExpectedDeliveryDate,
                overdue, Bucket(overdue), p.TotalAmount, cnt, sum, p.TotalAmount - sum));
        }
        return new OutstandingPoReport(asOf,
            rows.OrderByDescending(r => r.DaysOverdue).ToList());
    }

    private static string Bucket(int daysOverdue) => daysOverdue switch
    {
        <= 0 => "Current",
        <= 7 => "1-7",
        <= 14 => "8-14",
        <= 30 => "15-30",
        _ => "30+",
    };
}
