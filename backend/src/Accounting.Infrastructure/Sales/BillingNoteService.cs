using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล).
// Numbering MM-YYYY-BL-{BU}-NNNN on Issue. Draft-only edit + hard-delete (no doc_no
// yet). Issued/Settled/Cancelled are read-only header. Settled is the terminal
// "fully paid" state set by ReceiptService (Sprint 13i) — manual MarkSettled
// endpoint shipped here for ckpt3.

public sealed class BillingNoteService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    ICompanyTaxConfigService taxCfg, IFileStorageService storage) : IBillingNoteService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateBillingNoteRequest req, CancellationToken ct)
    {
        Auth();

        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        req = req with { BusinessUnitId = effBu };

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId
                && c.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        var bn = new BillingNote
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            // §10 — DocDate pinned to Asia/Bangkok today; DueDate stays the caller's future date.
            Status = BillingNoteStatus.Draft, DocDate = clock.TodayInBangkok(), DueDate = req.DueDate,
            CustomerId = cust.CustomerId, CustomerName = cust.NameTh,
            CustomerAddress = cust.BillingAddress, CustomerTaxId = cust.TaxId,
            CustomerType = cust.CustomerType, BusinessUnitId = req.BusinessUnitId,
            QuotationId = req.QuotationId,
            CurrencyCode = req.CurrencyCode, ExchangeRate = req.ExchangeRate,
            Notes = req.Notes, InternalNotes = req.InternalNotes,
        };
        await ApplyLinesAsync(bn, req.Lines, ct);
        foreach (var link in await BuildTaxInvoiceLinksAsync(req.TaxInvoiceIds, ct))
            bn.TaxInvoiceLinks.Add(link);
        db.BillingNotes.Add(bn);
        await db.SaveChangesAsync(ct);    // assigns BillingNoteId — activity.Record needs it
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);    // ponytail: second save needed (activity row staged after ID assigned)
        return bn.BillingNoteId;
    }

    // cont.69 Phase 1 — DO → Invoice (ใบแจ้งหนี้), manual. Copies the DO's customer
    // snapshot + lines into a new Draft BillingNote with DeliveryOrderId set. The DO need
    // only exist in the tenant (any status); the new Invoice is Draft (number on Issue).
    public async Task<long> CreateFromDeliveryOrderAsync(long deliveryOrderId, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == deliveryOrderId, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {deliveryOrderId} not found.");

        // cont.69 (Ham) — one Invoice per Delivery Order: block a duplicate create.
        if (await db.BillingNotes.AnyAsync(
                b => b.CompanyId == tenant.CompanyId && b.DeliveryOrderId == deliveryOrderId, ct))
            throw new DomainException("do.invoice_exists",
                $"Delivery Order {deliveryOrderId} already has an Invoice.");

        var bn = new BillingNote
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            // §10 — chain-copy re-derives DocDate deterministically to Asia/Bangkok today.
            Status = BillingNoteStatus.Draft, DocDate = clock.TodayInBangkok(), DueDate = clock.TodayInBangkok(),
            CustomerId = dord.CustomerId, CustomerName = dord.CustomerName,
            CustomerAddress = dord.CustomerAddress, CustomerTaxId = dord.CustomerTaxId,
            CustomerType = dord.CustomerType, BusinessUnitId = dord.BusinessUnitId,
            DeliveryOrderId = dord.DeliveryOrderId,
            CurrencyCode = dord.CurrencyCode, ExchangeRate = dord.ExchangeRate,
            Notes = dord.Notes,
        };
        int n = 1;
        foreach (var l in dord.Lines.OrderBy(l => l.LineNo))
        {
            bn.Lines.Add(new BillingNoteLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductCode = l.ProductCode,
                ProductType = l.ProductType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, DiscountAmount = l.DiscountAmount,
                LineAmount = l.LineAmount,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = l.TaxAmount, TotalAmount = l.TotalAmount,
            });
            bn.SubtotalAmount += l.LineAmount; bn.VatAmount += l.TaxAmount; bn.TotalAmount += l.TotalAmount;
        }
        db.BillingNotes.Add(bn);
        await db.SaveChangesAsync(ct);    // assigns BillingNoteId — activity.Record needs it
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Created",
            toStatus: "Draft", note: $"จากใบส่งของ {dord.DocNo ?? dord.DeliveryOrderId.ToString()}");
        await db.SaveChangesAsync(ct);    // ponytail: second save needed (activity row staged after ID assigned)
        return bn.BillingNoteId;
    }

    private async Task<BillingNote> LoadAsync(long id, CancellationToken ct) =>
        await db.BillingNotes.Include(x => x.Lines).Include(x => x.TaxInvoiceLinks)
            .Where(x => x.CompanyId == tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.BillingNoteId == id, ct)
            ?? throw new DomainException("billing_note.not_found", $"Billing note {id} not found.");

    // Sprint 13i C7 — resolve requested TI ids into join rows. applied_amount defaults
    // to the TaxInvoice total at link time. TIs outside the tenant are silently skipped.
    private async Task<List<BillingNoteTaxInvoice>> BuildTaxInvoiceLinksAsync(
        long[]? tiIds, CancellationToken ct)
    {
        if (tiIds is null || tiIds.Length == 0)
            return new List<BillingNoteTaxInvoice>();
        var distinct = tiIds.Distinct().ToArray();
        var totals = await db.TaxInvoices.AsNoTracking()
            .Where(t => t.CompanyId == tenant.CompanyId && distinct.Contains(t.TaxInvoiceId))
            .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.TotalAmount, ct);
        return distinct
            .Where(id => totals.ContainsKey(id))
            .Select(id => new BillingNoteTaxInvoice
            {
                TaxInvoiceId = id, CompanyId = tenant.CompanyId, AppliedAmount = totals[id],
            })
            .ToList();
    }

    public async Task UpdateDraftAsync(long id, CreateBillingNoteRequest req, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status != BillingNoteStatus.Draft)
            throw new DomainException("billing_note.cannot_edit_after_issue",
                "Billing note can only be edited while in Draft.");

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId
                && c.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        bn.DocDate = clock.TodayInBangkok(); bn.DueDate = req.DueDate;   // §10 — re-pin on edit
        bn.CustomerId = cust.CustomerId; bn.CustomerName = cust.NameTh;
        bn.CustomerAddress = cust.BillingAddress; bn.CustomerTaxId = cust.TaxId;
        bn.CustomerType = cust.CustomerType; bn.BusinessUnitId = req.BusinessUnitId;
        bn.QuotationId = req.QuotationId;
        bn.CurrencyCode = req.CurrencyCode; bn.ExchangeRate = req.ExchangeRate;
        bn.Notes = req.Notes; bn.InternalNotes = req.InternalNotes;

        db.RemoveRange(bn.Lines);
        bn.Lines.Clear();
        bn.SubtotalAmount = bn.VatAmount = bn.TotalAmount = 0m;
        await ApplyLinesAsync(bn, req.Lines, ct);

        db.BillingNoteTaxInvoices.RemoveRange(bn.TaxInvoiceLinks);
        bn.TaxInvoiceLinks.Clear();
        foreach (var link in await BuildTaxInvoiceLinksAsync(req.TaxInvoiceIds, ct))
            bn.TaxInvoiceLinks.Add(link);

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteDraftAsync(long id, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status != BillingNoteStatus.Draft)
            throw new DomainException("billing_note.cannot_delete_after_issue",
                "Billing note can only be deleted while in Draft.");
        db.RemoveRange(bn.Lines);
        db.BillingNotes.Remove(bn);
        await db.SaveChangesAsync(ct);
    }

    public async Task IssueAsync(long id, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status != BillingNoteStatus.Draft)
            throw new DomainException("billing_note.bad_status", "Only a Draft billing note can be issued.");
        // cont.69 (Ham) — Invoice number prefix BL → IV. Existing BL-numbered invoices
        // keep their numbers; new ones start a fresh IV monthly sequence per BU.
        bn.DocNo = await SubPrefixNumberAsync("IV", bn.BusinessUnitId, bn.DocDate, ct);
        bn.Status = BillingNoteStatus.Issued;
        bn.IssuedAt = clock.UtcNow;
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Issued", "Draft", "Issued");
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status is BillingNoteStatus.Settled or BillingNoteStatus.Cancelled)
            throw new DomainException("billing_note.bad_status",
                "Cannot cancel a settled or already cancelled billing note.");
        var fromCancel = bn.Status.ToString();
        bn.Status = BillingNoteStatus.Cancelled; bn.CancelledReason = reason;
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Cancelled", fromCancel, "Cancelled", note: reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkSettledAsync(long id, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status != BillingNoteStatus.Issued)
            throw new DomainException("billing_note.bad_status",
                "Only an Issued billing note can be marked Settled.");
        bn.Status = BillingNoteStatus.Settled;
        bn.SettledAt = clock.UtcNow;
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Settled", "Issued", "Settled");
        await db.SaveChangesAsync(ct);
    }

    // Sprint 13j-PDF — shared PaperDocument mirror. Seller = company; customer
    // enriched from the master (BN detail carries only name+id); vatRate derived
    // from VatAmount/Subtotal (BN lines don't expose a per-line rate).
    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var d = await GetAsync(id, ct)
            ?? throw new DomainException("billing_note.not_found", $"Billing note {id} not found.");
        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == d.CustomerId && c.CompanyId == tenant.CompanyId, ct);
        // Non-VAT companies (ม.86): suppress VAT total rows on the billing-note PDF.
        var showVat = (await taxCfg.GetAsync(ct)).VatMode;
        // Mirror FE billing-notes/[id] mapping: line amount = net lineAmount, no
        // descriptionSub, summary carries no vatRate (PaperFoot defaults 7%),
        // customer enriched from the master with formatTaxId.
        var cfg = Pdf.PaperDoc.Config[Pdf.PaperDocKind.BillingNote];
        var model = new Pdf.PaperDocModel(
            cfg.DocType, cfg.DocTypeEn, d.DocNo ?? string.Empty, d.DocDate,
            await Pdf.PaperSellerSource.FromCompanyProfileAsync(db, tenant.CompanyId, ct, storage),
            new Pdf.PaperCustomer(d.CustomerName, Pdf.PaperFormat.TaxId(cust?.TaxId), cust?.BranchCode, cust?.BillingAddress),
            d.Lines.Select(l => new Pdf.PaperLine(
                l.DescriptionTh, null, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList(),
            new Pdf.PaperSummary(d.SubtotalAmount, null, null, d.VatAmount, d.TotalAmount, null, showVat),
            new Pdf.PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            ValidUntil: d.DueDate, ValidUntilLabel: cfg.ValidUntilLabel,
            Notes: d.Notes,
            // cont.69 Phase 4 (D8) — copy=true → สำเนา watermark (universal print).
            Watermark: copy
                ? new Pdf.PaperWatermark("สำเนา", Pdf.PaperWatermarkVariant.Warning)
                : Pdf.PaperDoc.Watermark(Pdf.PaperDocKind.BillingNote, d.Status));
        return Pdf.PaperDocumentPdf.Render(model);
    }

    public async Task<IReadOnlyList<BillingNoteListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.BillingNotes.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId);
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<BillingNoteStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.BillingNoteId)
            .Select(x => new BillingNoteListItem(
                x.BillingNoteId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.DueDate, x.CustomerName, x.TotalAmount, x.QuotationId,
                x.CustomerId, x.BusinessUnitId))
            .ToListAsync(ct);
    }

    public async Task<BillingNoteDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var bn = await db.BillingNotes.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.BillingNoteId == id, ct);
        if (bn is null) return null;

        // Sprint 13i C7 — grouped TIs resolved from the join table, with doc_no for chips.
        var tiRefs = await (
            from j in db.BillingNoteTaxInvoices.AsNoTracking()
            join t in db.TaxInvoices.AsNoTracking() on j.TaxInvoiceId equals t.TaxInvoiceId
            where j.BillingNoteId == id && j.CompanyId == tenant.CompanyId
            orderby j.TaxInvoiceId
            select new BillingNoteTaxInvoiceRef(j.TaxInvoiceId, t.DocNo, j.AppliedAmount)
        ).ToListAsync(ct);

        return new BillingNoteDetail(
            bn.BillingNoteId, bn.DocNo, bn.Status.ToString(), bn.DocDate, bn.DueDate,
            bn.CustomerId, bn.CustomerName, bn.BusinessUnitId,
            bn.QuotationId, tiRefs,
            bn.CurrencyCode, bn.SubtotalAmount, bn.VatAmount, bn.TotalAmount,
            bn.Notes,
            bn.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList());
    }

    // Compliance backstops (SalesLineBackstop): snapshot ProductType from the product
    // master and zero all VAT for a non-VAT company, whatever the request carried.
    private async Task ApplyLinesAsync(BillingNote bn, IReadOnlyList<BillingLineInput> lines, CancellationToken ct)
    {
        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, lines.Select(l => l.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, lines.Select(l => l.TaxCode), ct);
        int n = 1;
        foreach (var l in lines)
        {
            var (prodType, taxRate, taxCode) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productTypes, taxCodeFlags);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            bn.Lines.Add(new BillingNoteLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType,
                TaxInvoiceId = l.TaxInvoiceId, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            bn.SubtotalAmount += net; bn.VatAmount += vat; bn.TotalAmount += total;
        }
    }

    private async Task<string> SubPrefixNumberAsync(
        string prefix, int? buId, DateOnly docDate, CancellationToken ct)
    {
        string? buCode = buId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        return await numbers.NextAsync(tenant.CompanyId, tenant.BranchId, prefix, buCode, docDate, ct);
    }
}
