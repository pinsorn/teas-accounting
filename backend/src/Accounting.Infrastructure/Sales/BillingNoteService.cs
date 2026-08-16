using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Pdf;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล).
// Numbering MM-YYYY-BL-{BU}-NNNN on Issue. Draft-only edit + hard-delete (no doc_no
// yet). Issued/Settled/Cancelled are read-only header. Settled is the terminal
// "fully paid" state, set ONLY by ReceiptService (Sprint 13i) when a posted
// receipt covers the total — R2/WP-7 (2026-08-12) deleted the manual MarkSettled
// endpoint that used to flip Settled with no receipt and no ledger entry.
// R1/C6 (WP-1) — a non-VAT company's Issue now accrues revenue+AR (its Invoice is the
// only sales document, ม.86/4 blocks the Tax Invoice); IGlPostingService/IPeriodCloseService
// added for that. VAT companies are unaffected — their BN groups already-accrued TIs.

public sealed class BillingNoteService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    ICompanyTaxConfigService taxCfg, IFileStorageService storage,
    IGlPostingService gl, IPeriodCloseService period) : IBillingNoteService
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

        // S9 (2026-07-16 fix) — same company-level BU requirement as Quotation/TaxInvoice/
        // Receipt: a direct Invoice create must not slip a null BU through when opted in.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

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
        var taxInvoiceLinks = await BuildTaxInvoiceLinksAsync(req.TaxInvoiceIds, ct);
        foreach (var link in taxInvoiceLinks)
            bn.TaxInvoiceLinks.Add(link);
        // Linked TIs generate document-granularity lines only when the caller supplied no
        // manual lines; once manual lines exist they are an intentional, untouched override.
        if (req.TaxInvoiceIds is { Length: > 0 } && req.Lines.Count == 0)
            await ApplyTaxInvoiceLinesAsync(bn, taxInvoiceLinks, ct);
        else
            await ApplyLinesAsync(bn, req.Lines, ct);
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

        // S14 (2026-07-16 fix) — DueDate previously always equalled DocDate, ignoring the
        // customer's credit term. Apply PaymentTermDays when set; keep prior behavior
        // (DueDate = DocDate) when 0/unset.
        var custTermDays = await db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == dord.CustomerId).Select(c => c.PaymentTermDays).FirstOrDefaultAsync(ct);
        var docDate = clock.TodayInBangkok();
        var dueDate = custTermDays > 0 ? docDate.AddDays(custTermDays) : docDate;

        var bn = new BillingNote
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            // §10 — chain-copy re-derives DocDate deterministically to Asia/Bangkok today.
            Status = BillingNoteStatus.Draft, DocDate = docDate, DueDate = dueDate,
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

    // mcp-document-chain (D1) — SO → Invoice, direct (service-only skip-DO path, §A2). Mirrors
    // CreateFromDeliveryOrderAsync, sourcing lines from SalesOrder.Lines instead. Guards: SO must
    // be Posted; SO must be service-only (a goods line means a Delivery Order is mandatory — the
    // MCP layer recomputes and enforces the same rule up-front so the error text stays actionable);
    // one Invoice per SO (no BillingNote already carrying this SalesOrderId).
    public async Task<long> CreateFromSalesOrderAsync(long salesOrderId, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.SalesOrderId == salesOrderId, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {salesOrderId} not found.");
        if (so.Status != SalesOrderStatus.Posted)
            throw new DomainException("so.not_posted",
                "Sales Order must be Posted before creating an Invoice.");
        if (so.Lines.Any(l => l.ProductType is "GOOD" or "EXEMPT_GOOD"))
            throw new DomainException("so.delivery_required",
                $"Sales Order {salesOrderId} has goods lines — create a Delivery Order first.");
        if (await db.BillingNotes.AnyAsync(
                b => b.CompanyId == tenant.CompanyId && b.SalesOrderId == salesOrderId, ct))
            throw new DomainException("so.invoice_exists",
                $"Sales Order {salesOrderId} already has an Invoice.");

        // S14 (2026-07-16 fix) — DueDate previously always equalled DocDate, ignoring the
        // customer's credit term. Apply PaymentTermDays when set; keep prior behavior
        // (DueDate = DocDate) when 0/unset.
        var custTermDays = await db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == so.CustomerId).Select(c => c.PaymentTermDays).FirstOrDefaultAsync(ct);
        var docDate = clock.TodayInBangkok();
        var dueDate = custTermDays > 0 ? docDate.AddDays(custTermDays) : docDate;

        var bn = new BillingNote
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = BillingNoteStatus.Draft, DocDate = docDate, DueDate = dueDate,
            CustomerId = so.CustomerId, CustomerName = so.CustomerName,
            CustomerAddress = so.CustomerAddress, CustomerTaxId = so.CustomerTaxId,
            CustomerType = so.CustomerType, BusinessUnitId = so.BusinessUnitId,
            SalesOrderId = so.SalesOrderId,
            CurrencyCode = so.CurrencyCode, ExchangeRate = so.ExchangeRate,
        };
        int n = 1;
        foreach (var l in so.Lines.OrderBy(l => l.LineNo))
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
            toStatus: "Draft", note: $"จากใบสั่งขาย {so.DocNo ?? so.SalesOrderId.ToString()}");
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

        // S9 (2026-07-16 fix) — same company-level BU requirement as Create; an edit can
        // otherwise re-null a previously-valid BU before Issue.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

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
        db.BillingNoteTaxInvoices.RemoveRange(bn.TaxInvoiceLinks);
        bn.TaxInvoiceLinks.Clear();
        var taxInvoiceLinks = await BuildTaxInvoiceLinksAsync(req.TaxInvoiceIds, ct);
        foreach (var link in taxInvoiceLinks)
            bn.TaxInvoiceLinks.Add(link);
        // Linked TIs generate document-granularity lines only when the caller supplied no
        // manual lines; once manual lines exist they are an intentional, untouched override.
        if (req.TaxInvoiceIds is { Length: > 0 } && req.Lines.Count == 0)
            await ApplyTaxInvoiceLinesAsync(bn, taxInvoiceLinks, ct);
        else
            await ApplyLinesAsync(bn, req.Lines, ct);

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
        // H8 (review 2026-07-04) — this is the highest-priority of the 5 unsafe numbering
        // paths (it allocates a real invoice number). Wrap alloc+save in one explicit tx,
        // mirroring the safe TaxInvoiceService.PostAsync pattern — else a failed save after
        // allocation leaves a consumed-but-unused IV number (a gap).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bn = await LoadAsync(id, ct);
        if (bn.Status != BillingNoteStatus.Draft)
            throw new DomainException("billing_note.bad_status", "Only a Draft billing note can be issued.");

        // R1/C6 (WP-1) — non-VAT only: issuing now moves money, so the period must be open
        // BEFORE the number allocation below (no invoice number consumed on a closed-period
        // issue). VAT companies: no JE, unchanged behaviour, zero risk (their BN groups
        // already-accrued Tax Invoices).
        var tax = await taxCfg.GetAsync(ct);
        if (!tax.VatMode)
            await period.EnsureOpenAsync(bn.DocDate, ct);

        // cont.69 (Ham) — Invoice number prefix BL → IV. Existing BL-numbered invoices
        // keep their numbers; new ones start a fresh IV monthly sequence per BU.
        var issuedAt = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            db,
            c => SubPrefixNumberAsync("IV", bn.BusinessUnitId, bn.DocDate, c),
            (v, _) => { bn.DocNo = v.Value; bn.Status = BillingNoteStatus.Issued; bn.IssuedAt = issuedAt; bn.IssuedBy = tenant.UserId; },
            ct);
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Issued", "Draft", "Issued");

        // R1/C6 (WP-1) — post AFTER number allocation: PostBillingNoteAsync re-reads the BN
        // on this same DbContext and needs DocNo populated (identity-map note, mirrors
        // ExpenseClaimService.PayAsync:280-284).
        if (!tax.VatMode)
            bn.JournalEntryId = await gl.PostBillingNoteAsync(bn.BillingNoteId, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var bn = await LoadAsync(id, ct);
        if (bn.Status is BillingNoteStatus.Settled or BillingNoteStatus.Cancelled)
            throw new DomainException("billing_note.bad_status",
                "Cannot cancel a settled or already cancelled billing note.");
        // R1/C6 (WP-1) — an accrued Invoice (JournalEntryId set) is ledger-backed and
        // immutable like every other posted document: cancelling it would strand its AR
        // debit with nothing left to clear it.
        if (bn.JournalEntryId is not null)
            throw new DomainException("billing_note.cannot_cancel_posted",
                $"Invoice {bn.DocNo} has already posted to the GL and cannot be cancelled.");
        var fromCancel = bn.Status.ToString();
        bn.Status = BillingNoteStatus.Cancelled; bn.CancelledReason = reason;
        activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Cancelled", fromCancel, "Cancelled", note: reason);
        await db.SaveChangesAsync(ct);
    }

    // Sprint 13j-PDF — shared PaperDocument mirror. Seller = company; customer
    // enriched from the master (BN detail carries only name+id); vatRate derived
    // from VatAmount/Subtotal (BN lines don't expose a per-line rate).
    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
        => Pdf.PaperDocumentPdf.Render(await BuildPaperAsync(id, ct, copy));

    // cont.121 canonical paper DTO — the exact mapping BuildPdfAsync used, exposed for GET /paper.
    public async Task<PaperDocModel> BuildPaperAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var d = await GetAsync(id, ct)
            ?? throw new DomainException("billing_note.not_found", $"Billing note {id} not found.");

        // doc-signature spec (§D5) — BillingNoteDetail carries no IssuedBy; a one-column
        // scalar read avoids widening the read DTO for a single signature-resolution need.
        var issuedBy = await db.BillingNotes.AsNoTracking()
            .Where(x => x.BillingNoteId == id).Select(x => x.IssuedBy).FirstOrDefaultAsync(ct);

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == d.CustomerId && c.CompanyId == tenant.CompanyId, ct);
        // Non-VAT companies (ม.86): suppress VAT total rows on the billing-note PDF.
        var showVat = (await taxCfg.GetAsync(ct)).VatMode;
        // Mirror FE billing-notes/[id] mapping: line amount = net lineAmount, no
        // descriptionSub, summary carries no vatRate (PaperFoot defaults 7%),
        // customer enriched from the master with formatTaxId.
        var cfg = Pdf.PaperDoc.Config[Pdf.PaperDocKind.BillingNote];
        var model = new PaperDocModel(
            cfg.DocType, cfg.DocTypeEn, d.DocNo ?? string.Empty, d.DocDate,
            await Pdf.PaperSellerSource.FromCompanyProfileAsync(db, tenant.CompanyId, ct, storage),
            new PaperCustomer(d.CustomerName, Pdf.PaperFormat.TaxId(cust?.TaxId), cust?.BranchCode,
                cust?.BillingAddress, cust?.ContactPerson, cust?.Phone),
            d.Lines.Select(l => new PaperLine(
                l.DescriptionTh, null, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList(),
            new PaperSummary(d.SubtotalAmount, null, null, d.VatAmount, d.TotalAmount, null, showVat),
            new PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            ValidUntil: d.DueDate, ValidUntilLabel: cfg.ValidUntilLabel,
            Notes: d.Notes,
            // cont.69 Phase 4 (D8) — copy=true → สำเนา watermark (universal print).
            Watermark: copy
                ? new PaperWatermark("สำเนา", PaperWatermarkVariant.Warning)
                : Pdf.PaperDoc.Watermark(Pdf.PaperDocKind.BillingNote, d.Status),
            Signatures: await Pdf.PaperSignatureSource.ResolveAsync(
                db, storage, issuedBy, null, stampOnMiddle: false,
                isSigned: d.Status != "Draft" && issuedBy is not null, ct));
        return model;
    }

    public async Task<IReadOnlyList<BillingNoteListItem>> ListAsync(string? status, CancellationToken ct,
        DateOnly? dateFrom = null, DateOnly? dateTo = null, long? customerId = null, long? productId = null)
    {
        Auth();
        var qy = db.BillingNotes.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId);
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<BillingNoteStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        // E1 — date-range/customer/product filters.
        if (dateFrom is { } df) qy = qy.Where(x => x.DocDate >= df);
        if (dateTo   is { } dt) qy = qy.Where(x => x.DocDate <= dt);
        if (customerId is { } cid) qy = qy.Where(x => x.CustomerId == cid);
        if (productId is { } pid) qy = qy.Where(x => x.Lines.Any(l => l.ProductId == pid));
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
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount,
                l.DiscountPercent, l.TaxCode, l.TaxCodeId)).ToList(),
            bn.JournalEntryId);
    }

    // Compliance backstops (SalesLineBackstop): snapshot ProductType from the product
    // master and zero all VAT for a non-VAT company, whatever the request carried.
    private async Task ApplyLinesAsync(BillingNote bn, IReadOnlyList<BillingLineInput> lines, CancellationToken ct)
    {
        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, lines.Select(l => l.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, lines.Select(l => l.TaxCode), ct);
        var standardOutput = cfg.VatMode ? await SalesLineBackstop.LoadStandardOutputTaxCodeAsync(db, ct) : null;
        int n = 1;
        foreach (var l in lines)
        {
            var (prodType, taxRate, taxCode, taxCodeId) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productTypes, taxCodeFlags, standardOutput);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            bn.Lines.Add(new BillingNoteLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType,
                TaxInvoiceId = l.TaxInvoiceId, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = taxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            bn.SubtotalAmount += net; bn.VatAmount += vat; bn.TotalAmount += total;
        }
    }

    private async Task ApplyTaxInvoiceLinesAsync(
        BillingNote bn, IReadOnlyList<BillingNoteTaxInvoice> links, CancellationToken ct)
    {
        var linkedIds = links.Select(l => l.TaxInvoiceId).ToArray();
        var invoices = await db.TaxInvoices.AsNoTracking()
            .Include(t => t.Lines)
            .Where(t => t.CompanyId == tenant.CompanyId && linkedIds.Contains(t.TaxInvoiceId))
            .ToDictionaryAsync(t => t.TaxInvoiceId, ct);

        int n = 1;
        foreach (var link in links)
        {
            var ti = invoices[link.TaxInvoiceId];
            var orderedSourceLines = ti.Lines.OrderBy(l => l.LineNo).ToArray();
            var sourceLine = orderedSourceLines.Select(l => l.TaxCodeId).Distinct().Count() == 1
                ? orderedSourceLines[0]
                : orderedSourceLines.OrderByDescending(l => l.LineAmount).First();
            var reference = ti.DocNo ?? "(ยังไม่ออกเลขที่)";
            var line = new BillingNoteLine
            {
                LineNo = n++,
                ProductType = "SERVICE",
                TaxInvoiceId = ti.TaxInvoiceId,
                DescriptionTh = $"ใบกำกับภาษี {reference} ลงวันที่ {ti.DocDate:dd/MM/yyyy}",
                Quantity = 1m,
                UomText = "ฉบับ",
                UnitPrice = ti.SubtotalAmount,
                LineAmount = ti.SubtotalAmount,
                TaxCodeId = sourceLine.TaxCodeId,
                TaxCode = sourceLine.TaxCode,
                TaxRate = sourceLine.TaxRate,
                TaxAmount = ti.TaxAmount,
                TotalAmount = ti.TotalAmount,
            };
            bn.Lines.Add(line);
            bn.SubtotalAmount += line.LineAmount;
            bn.VatAmount += line.TaxAmount;
            bn.TotalAmount += line.TotalAmount;
        }
    }
    private async Task<DocumentNumber> SubPrefixNumberAsync(
        string prefix, int? buId, DateOnly docDate, CancellationToken ct)
    {
        string? buCode = buId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        return await numbers.NextAsync(tenant.CompanyId, prefix, buCode, docDate, ct);
    }
}
