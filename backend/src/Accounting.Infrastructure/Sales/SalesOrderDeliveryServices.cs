using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

public sealed class SalesOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    ICompanyTaxConfigService taxCfg) : ISalesOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateSalesOrderRequest req, CancellationToken ct)
    {
        Auth();
        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        // S9 (2026-07-16 fix) — same company-level BU requirement as Quotation/TaxInvoice/
        // Receipt: a direct SO create (POST /sales-orders) must not slip a null BU through
        // when the company opted in.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

        var so = new SalesOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = SalesOrderStatus.Draft, DocDate = req.DocDate,
            ExpectedDeliveryDate = req.ExpectedDeliveryDate,
            CustomerId = cust.CustomerId, CustomerName = cust.NameTh,
            CustomerAddress = cust.BillingAddress, CustomerTaxId = cust.TaxId,
            CustomerType = cust.CustomerType, BusinessUnitId = req.BusinessUnitId,
            QuotationId = req.FromQuotationId, CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate, Notes = req.Notes,
        };
        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, req.Lines.Select(x => x.TaxCode), ct);
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productTypes, taxCodeFlags);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            so.Lines.Add(new SalesOrderLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            so.SubtotalAmount += net; so.VatAmount += vat; so.TotalAmount += total;
        }
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);
        return so.SalesOrderId;
    }

    // S15 (2026-07-16 fix) — Draft-only full edit, mirrors Quotation's UpdateDraftAsync:
    // replaces line items wholesale (drop+add), recomputes header aggregates. DocDate is
    // user-editable (§10 Option B — passed through from the request, never re-pinned).
    public async Task UpdateDraftAsync(long id, CreateSalesOrderRequest req, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {id} not found.");
        if (so.Status != SalesOrderStatus.Draft)
            throw new DomainException("so.cannot_edit_after_post",
                "Sales Order can only be edited while in Draft.");

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        // S9 (2026-07-16 fix) — same company-level BU requirement as Create; an edit can
        // otherwise re-null a previously-valid BU before Post.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

        so.DocDate = req.DocDate;
        so.ExpectedDeliveryDate = req.ExpectedDeliveryDate;
        so.CustomerId = cust.CustomerId;
        so.CustomerName = cust.NameTh;
        so.CustomerAddress = cust.BillingAddress;
        so.CustomerTaxId = cust.TaxId;
        so.CustomerType = cust.CustomerType;
        so.BusinessUnitId = req.BusinessUnitId;
        so.CurrencyCode = req.CurrencyCode;
        so.ExchangeRate = req.ExchangeRate;
        so.Notes = req.Notes;

        db.RemoveRange(so.Lines);
        so.Lines.Clear();
        so.SubtotalAmount = so.VatAmount = so.TotalAmount = 0m;

        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, req.Lines.Select(x => x.TaxCode), ct);
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productTypes, taxCodeFlags);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            so.Lines.Add(new SalesOrderLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            so.SubtotalAmount += net; so.VatAmount += vat; so.TotalAmount += total;
        }
        // S12-BE (2026-07-16 fix) — same activity-logging parity as Quotation's draft edit.
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Updated");
        await db.SaveChangesAsync(ct);
    }

    public async Task PostAsync(long id, CancellationToken ct)
    {
        Auth();
        // H8 (review 2026-07-04) — wrap alloc+save in one explicit tx, mirroring the safe
        // TaxInvoiceService.PostAsync pattern; else a failed save after allocation leaves a
        // consumed-but-unused SO number (a gap).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var so = await db.SalesOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {id} not found.");
        if (so.Status != SalesOrderStatus.Draft)
            throw new DomainException("so.bad_status", "Only a Draft SO can be posted.");
        var postedAt = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            db,
            c => SubNumAsync("SO", so.BusinessUnitId, so.DocDate, c),
            (v, _) => { so.DocNo = v.Value; so.Status = SalesOrderStatus.Posted; so.PostedAt = postedAt; so.PostedBy = tenant.UserId; },
            ct);
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Posted", "Draft", "Posted");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<long> CreateDeliveryOrderAsync(
        long salesOrderId, CreateDeliveryOrderRequest req, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == salesOrderId, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {salesOrderId} not found.");
        if (so.Status != SalesOrderStatus.Posted)
            throw new DomainException("so.not_posted",
                "Sales Order must be Posted before creating a Delivery Order.");
        // NOTE (mcp-document-chain) — a "no 2nd DO for this SO" guard does NOT belong here:
        // this shared method is ALSO the existing partial-delivery path (multiple DOs against
        // one SO, each covering different lines/quantities — see Sprint10ChainTests
        // Partial_delivery_keeps_so_open_until_fully_delivered / ImmutabilityAndGuardTests
        // DeliveryOrder_exceeding_so_line_qty_is_rejected). The MCP full-qty-only assumption
        // (§A3) is enforced at the MCP TOOL layer instead (create_delivery_order_draft), not
        // here, so the still-supported general partial-delivery flow keeps working unchanged.

        // §4.6 / ม.86 / ม.80 — DO creation is request-fed (the client picks the line tax rate),
        // so the company VAT mode is the authoritative backstop here, exactly as the SO/Quotation
        // origin builders do. A non-VAT company carries 0 VAT on every line (rate forced to 0,
        // code VAT0) and the DO can never be combined with a Tax Invoice (a VAT-only document).
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, req.Lines.Select(x => x.TaxCode), ct);

        var dord = new DeliveryOrder
        {
            CompanyId = so.CompanyId, BranchId = so.BranchId,
            Status = DeliveryOrderStatus.Draft, DocDate = req.DocDate,
            CustomerId = so.CustomerId, CustomerName = so.CustomerName,
            CustomerAddress = so.CustomerAddress, CustomerTaxId = so.CustomerTaxId,
            CustomerType = so.CustomerType,
            BusinessUnitId = so.BusinessUnitId,           // BU cascade SO→DO
            SalesOrderId = so.SalesOrderId,
            IsCombinedWithTi = req.IsCombinedWithTi && cfg.VatMode, Notes = req.Notes,
            CurrencyCode = so.CurrencyCode, ExchangeRate = so.ExchangeRate,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode) = SalesLineBackstop.Resolve(
                cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode,
                productTypes, taxCodeFlags);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            dord.Lines.Add(new DeliveryOrderLine
            {
                LineNo = n++, SalesOrderLineId = l.SalesOrderLineId,
                ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            dord.SubtotalAmount += net; dord.VatAmount += vat; dord.TotalAmount += total;

            if (l.SalesOrderLineId is { } solId)
            {
                var sol = so.Lines.FirstOrDefault(x => x.LineId == solId);
                if (sol is not null)
                {
                    if (sol.DeliveredQuantity + l.Quantity > sol.Quantity)
                        throw new DomainException("do.over_delivered",
                            $"Delivery quantity {l.Quantity} for SO line {solId} would exceed " +
                            $"the ordered quantity {sol.Quantity} (already delivered: {sol.DeliveredQuantity}).");
                    sol.DeliveredQuantity += l.Quantity;
                }
            }
        }
        db.DeliveryOrders.Add(dord);

        // Auto-close the SO when every line is fully delivered.
        var soClosed = false;
        if (so.Lines.All(x => x.DeliveredQuantity >= x.Quantity) && so.Lines.Count > 0)
        {
            so.Status = SalesOrderStatus.Closed;
            so.ClosedAt = clock.UtcNow;
            soClosed = true;
        }
        await db.SaveChangesAsync(ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Created",
            toStatus: "Draft", note: $"จากใบสั่งขาย {so.DocNo ?? so.SalesOrderId.ToString()}");
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "CreatedDeliveryOrder",
            note: $"→ ใบส่งของ {dord.DeliveryOrderId}");
        if (soClosed)
            activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Closed", "Posted", "Closed");
        await db.SaveChangesAsync(ct);
        return dord.DeliveryOrderId;
    }

    public async Task<IReadOnlyList<SalesOrderListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.SalesOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<SalesOrderStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.SalesOrderId)
            .Select(x => new SalesOrderListItem(
                x.SalesOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.CustomerName, x.TotalAmount, x.QuotationId, x.BusinessUnitId)).ToListAsync(ct);
    }

    public async Task<SalesOrderDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct);
        return so is null ? null : new SalesOrderDetail(
            so.SalesOrderId, so.DocNo, so.Status.ToString(), so.DocDate,
            so.CustomerId, so.CustomerName, so.BusinessUnitId,
            so.SubtotalAmount, so.VatAmount, so.TotalAmount, so.QuotationId,
            so.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList(),
            // mcp-document-chain (D4) — GOOD/EXEMPT_GOOD lines are physical; a Delivery Order
            // is mandatory before invoicing. EXEMPT_GOOD counts as a good (still a physical thing).
            DeliveryRequired: so.Lines.Any(l => l.ProductType is "GOOD" or "EXEMPT_GOOD"));
    }

    private async Task<DocumentNumber> SubNumAsync(
        string prefix, int? buId, DateOnly docDate, CancellationToken ct)
    {
        string? buCode = buId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        return await numbers.NextAsync(tenant.CompanyId, tenant.BranchId, prefix, buCode, docDate, ct);
    }
}

public sealed class DeliveryOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, ITaxInvoiceService taxInvoices,
    IActivityRecorder activity, ICompanyTaxConfigService taxCfg)
    : IDeliveryOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateDeliveryOrderRequest req, CancellationToken ct)
    {
        Auth();
        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");
        // §4.6 / ม.86 / ม.80 — same request-fed backstop as the SO→DO path: derive the line
        // VAT from company master data, never the caller's rate. Non-VAT → 0 VAT, no combined TI.
        var cfg = await taxCfg.GetAsync(ct);
        var productTypes = await SalesLineBackstop.LoadProductTypesAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(db, req.Lines.Select(x => x.TaxCode), ct);

        var dord = new DeliveryOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = DeliveryOrderStatus.Draft, DocDate = req.DocDate,
            CustomerId = cust.CustomerId, CustomerName = cust.NameTh,
            CustomerAddress = cust.BillingAddress, CustomerTaxId = cust.TaxId,
            CustomerType = cust.CustomerType, BusinessUnitId = req.BusinessUnitId,
            SalesOrderId = req.FromSalesOrderId, IsCombinedWithTi = req.IsCombinedWithTi && cfg.VatMode,
            Notes = req.Notes,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode) = SalesLineBackstop.Resolve(
                cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode,
                productTypes, taxCodeFlags);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            dord.Lines.Add(new DeliveryOrderLine
            {
                LineNo = n++, SalesOrderLineId = l.SalesOrderLineId,
                ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            dord.SubtotalAmount += net; dord.VatAmount += vat; dord.TotalAmount += total;
        }
        db.DeliveryOrders.Add(dord);
        await db.SaveChangesAsync(ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);
        return dord.DeliveryOrderId;
    }

    // Sprint 13h P9 — Draft → Issued. Doc number allocated; TI NOT yet created.
    public async Task IssueAsync(long id, CancellationToken ct)
    {
        Auth();
        // H8 (review 2026-07-04) — wrap alloc+save in one explicit tx, mirroring the safe
        // TaxInvoiceService.PostAsync pattern; else a failed save after allocation leaves a
        // consumed-but-unused DO number (a gap).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        if (dord.Status != DeliveryOrderStatus.Draft)
            throw new DomainException("do.bad_status", "Only a Draft DO can be issued.");

        string? buCode = dord.BusinessUnitId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var issuedAt = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            db,
            c => numbers.NextAsync(tenant.CompanyId, tenant.BranchId, "DO", buCode, dord.DocDate, c),
            (v, _) => { dord.DocNo = v.Value; dord.Status = DeliveryOrderStatus.Issued; dord.PostedAt = issuedAt; dord.PostedBy = tenant.UserId; },
            ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Issued", "Draft", "Issued");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // cont.69 Phase 1 — Issued → Delivered is a STATUS CHANGE ONLY. The old
    // IsCombinedWithTi auto-TI path is removed: a non-VAT DO marking delivered no longer
    // funnels through EnsureVatRegistered → 422. The new linear flow issues the
    // Tax Invoice manually from the Invoice (BillingNote) step, never from delivery.
    // Legacy DOs already linked to a TI (dord.TaxInvoiceId set) keep that history.
    public async Task MarkDeliveredAsync(long id, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        if (dord.Status != DeliveryOrderStatus.Issued)
            throw new DomainException("do.bad_status", "Only an Issued DO can be marked Delivered.");

        dord.Status = DeliveryOrderStatus.Delivered;
        dord.DeliveredAt = clock.UtcNow;
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Delivered", "Issued", "Delivered");
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> CreateTaxInvoiceAsync(long deliveryOrderId, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == deliveryOrderId, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {deliveryOrderId} not found.");
        if (dord.Status != DeliveryOrderStatus.Delivered)
            throw new DomainException("do.not_delivered", "DO must be Delivered to create a Tax Invoice.");
        if (dord.TaxInvoiceId is not null)
            throw new DomainException("do.ti_exists", "This DO already has a linked Tax Invoice.");
        // Pattern Y — separate manual step (non-combined DO, TI issued later).
        dord.TaxInvoiceId = await GenerateTiAsync(dord, ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId,
            "CreatedTaxInvoice", note: $"→ ใบกำกับภาษี {dord.TaxInvoiceId}");
        await db.SaveChangesAsync(ct);
        return dord.TaxInvoiceId.Value;
    }

    private async Task<long> GenerateTiAsync(DeliveryOrder dord, CancellationToken ct)
    {
        var lines = dord.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceLineInput(
            l.ProductId, null, l.DescriptionTh, l.Quantity, 1, l.UomText,
            l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate,
            l.ProductType)).ToList();   // Sprint 13h P7 — DO→TI cascade
        // §4.6 / ม.80 — DERIVE the line VAT rate on the TI. A Delivery Order's lines come straight
        // from the client request (DO builders store raw l.TaxRate, NOT a normalized source rate),
        // so DO→TI is effectively request-fed: a "VAT7 + taxRate:0" DO would otherwise mint a posted
        // TI coded VAT7 carrying 0 VAT. Derivation is idempotent for a correctly-coded line.
        var tiId = await taxInvoices.CreateDraftAsync(new CreateTaxInvoiceRequest(
            dord.DocDate, dord.CustomerId, false, dord.CurrencyCode, dord.ExchangeRate,
            dord.Notes, null, null, lines, dord.BusinessUnitId), ct);   // BU cascade DO→TI
        await taxInvoices.PostAsync(tiId, ct);
        return tiId;
    }

    public async Task<IReadOnlyList<DeliveryOrderListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.DeliveryOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<DeliveryOrderStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.DeliveryOrderId)
            .Select(x => new DeliveryOrderListItem(
                x.DeliveryOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.CustomerName, x.IsCombinedWithTi, x.TaxInvoiceId, x.SalesOrderId,
                x.CustomerId, x.TotalAmount, x.BusinessUnitId))
            .ToListAsync(ct);
    }

    public async Task<DeliveryOrderDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var d = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct);
        if (d is null) return null;
        // cont.69 — the Invoice created from this DO (one-per-DO), so the FE can hide
        // the "create Invoice" action once it exists.
        var billingNoteId = await db.BillingNotes.AsNoTracking()
            .Where(b => b.DeliveryOrderId == id)
            .Select(b => (long?)b.BillingNoteId).FirstOrDefaultAsync(ct);
        return new DeliveryOrderDetail(
            d.DeliveryOrderId, d.DocNo, d.Status.ToString(), d.DocDate,
            d.CustomerId, d.CustomerName, d.BusinessUnitId, d.IsCombinedWithTi,
            d.TaxInvoiceId, d.SalesOrderId, d.SubtotalAmount, d.VatAmount, d.TotalAmount,
            d.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList(),
            billingNoteId);
    }
}
