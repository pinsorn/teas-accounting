using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Sales;

public sealed partial class ReceiptService
{
    public async Task<CursorPage<ReceiptListItem>> ListAsync(long? cursor, int limit, CancellationToken ct,
        int? businessUnitId = null, bool includeUnspecified = false)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.Receipts.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(r => r.ReceiptId < c);
        if (businessUnitId is { } bu)
            q = includeUnspecified
                ? q.Where(r => r.BusinessUnitId == bu || r.BusinessUnitId == null)
                : q.Where(r => r.BusinessUnitId == bu);
        var rows = await q.OrderByDescending(r => r.ReceiptId).Take(lim + 1)
            .Select(r => new ReceiptListItem(
                r.ReceiptId, r.DocNo, r.DocDate, r.CustomerName, r.Amount,
                r.Status.ToString(), r.CurrencyCode, r.WhtAmount,
                r.CustomerId, r.BusinessUnitId, r.CreatedViaApiKeyName))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<ReceiptListItem>(rows, more ? rows[^1].ReceiptId : null, more);
    }

    public async Task<ReceiptDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var r = await _db.Receipts.AsNoTracking()
            .Include(x => x.Applications)
            .Include(x => x.WhtLines)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.ReceiptId == id, ct);
        if (r is null) return null;

        var tiIds = r.Applications.Where(a => a.TaxInvoiceId.HasValue)
            .Select(a => a.TaxInvoiceId!.Value).ToList();
        var ti = await _db.TaxInvoices.AsNoTracking()
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .Select(t => new { t.TaxInvoiceId, t.DocNo, t.BusinessUnitId })
            .ToListAsync(ct);
        var tiNo = ti.ToDictionary(x => x.TaxInvoiceId, x => x.DocNo);

        // Sprint (receipt itemize) — derive the goods/service line items from the
        // applied (immutable) Tax Invoices so the receipt shows what was paid for.
        var lineRows = await _db.TaxInvoiceLines.AsNoTracking()
            .Where(l => tiIds.Contains(l.TaxInvoiceId))
            .OrderBy(l => l.TaxInvoiceId).ThenBy(l => l.LineNo)
            .Select(l => new ReceiptLineView(
                l.DescriptionTh, l.ProductType, l.Quantity, l.UomText,
                l.UnitPrice, l.LineAmount, tiNo.GetValueOrDefault(l.TaxInvoiceId)))
            .ToListAsync(ct);

        // Non-VAT sources override the TI-derived lines:
        //  • standalone receipt → its own ReceiptLines
        //  • DO-applied receipt (non-VAT credit) → lines from the applied Delivery Order(s)
        if (r.Lines.Count > 0)
        {
            lineRows = r.Lines.OrderBy(l => l.LineNo)
                .Select(l => new ReceiptLineView(
                    l.DescriptionTh, l.ProductType, l.Quantity, l.UomText ?? string.Empty, l.UnitPrice, l.Amount, null))
                .ToList();
        }
        else if (lineRows.Count == 0)
        {
            // cont.69 Phase 1 — Invoice (BillingNote)-applied receipt derives its lines
            // from the applied Invoice. Preferred over DO for the new non-VAT flow.
            var bnIds = r.Applications.Where(a => a.BillingNoteId.HasValue)
                .Select(a => a.BillingNoteId!.Value).ToList();
            if (bnIds.Count > 0)
            {
                var bnData = await _db.BillingNotes.AsNoTracking()
                    .Where(b => bnIds.Contains(b.BillingNoteId))
                    .Select(b => new
                    {
                        b.DocNo,
                        Lines = b.Lines.OrderBy(l => l.LineNo).Select(l => new
                        {
                            l.DescriptionTh, l.ProductType, l.Quantity, l.UomText, l.UnitPrice, l.LineAmount,
                        }),
                    }).ToListAsync(ct);
                lineRows = bnData.SelectMany(b => b.Lines.Select(l => new ReceiptLineView(
                    l.DescriptionTh, l.ProductType, l.Quantity, l.UomText, l.UnitPrice, l.LineAmount, b.DocNo)))
                    .ToList();
            }
            if (lineRows.Count == 0)
            {
                var doIds = r.Applications.Where(a => a.DeliveryOrderId.HasValue)
                    .Select(a => a.DeliveryOrderId!.Value).ToList();
                if (doIds.Count > 0)
                {
                    var doData = await _db.DeliveryOrders.AsNoTracking()
                        .Where(d => doIds.Contains(d.DeliveryOrderId))
                        .Select(d => new
                        {
                            d.DocNo,
                            Lines = d.Lines.OrderBy(l => l.LineNo).Select(l => new
                            {
                                l.DescriptionTh, l.Quantity, l.UomText, l.UnitPrice, l.LineAmount,
                            }),
                        }).ToListAsync(ct);
                    lineRows = doData.SelectMany(d => d.Lines.Select(l => new ReceiptLineView(
                        l.DescriptionTh, "GOOD", l.Quantity, l.UomText, l.UnitPrice, l.LineAmount, d.DocNo)))
                        .ToList();
                }
            }
        }
        var tiBu = ti.Where(x => x.BusinessUnitId != null)
            .Select(x => x.BusinessUnitId!.Value).Distinct().ToList();
        var buCodes = await _db.BusinessUnits.AsNoTracking()
            .Where(b => tiBu.Contains(b.BusinessUnitId))
            .ToDictionaryAsync(b => b.BusinessUnitId, b => b.Code, ct);
        var tiBuCode = ti.ToDictionary(
            x => x.TaxInvoiceId,
            x => x.BusinessUnitId is { } bid ? buCodes.GetValueOrDefault(bid) : null);
        var headerBuCode = r.BusinessUnitId is { } hbid
            ? await _db.BusinessUnits.Where(b => b.BusinessUnitId == hbid)
                .Select(b => b.Code).FirstOrDefaultAsync(ct)
            : null;

        // Sprint (multi-category WHT) — the breakdown is the persisted WhtLines. The
        // aggregate WhtTypeCode/Rate carry the single-category value or null when the
        // bill spans multiple categories (consumers should read WhtLines).
        var whtLineViews = r.WhtLines
            .OrderBy(l => l.WhtTypeCode)
            .Select(l => new ReceiptWhtLineView(
                l.WhtTypeId, l.WhtTypeCode, l.IncomeTypeCode, l.WhtRate, l.BaseAmount, l.WhtAmount))
            .ToList();

        string? whtCode;
        decimal whtRate, whtBase;
        if (whtLineViews.Count == 1)
        {
            whtCode = whtLineViews[0].WhtTypeCode;
            whtRate = whtLineViews[0].WhtRate;
            whtBase = whtLineViews[0].BaseAmount;
        }
        else if (whtLineViews.Count > 1)
        {
            whtCode = null;          // multi-category — see WhtLines
            whtRate = 0m;
            whtBase = whtLineViews.Sum(l => l.BaseAmount);
        }
        else
        {
            // Legacy receipts posted before the multi-category change: derive the
            // single category from the header WhtTypeId (no WhtLines rows exist).
            whtCode = null; whtRate = 0m; whtBase = 0m;
            if (r.WhtAmount > 0m && r.WhtTypeId is { } wtId)
            {
                var wt = await _db.WhtTypes.AsNoTracking()
                    .Where(w => w.WhtTypeId == wtId)
                    .Select(w => new { w.Code, w.Rate }).FirstOrDefaultAsync(ct);
                whtCode = wt?.Code;
                whtRate = wt?.Rate ?? 0m;
                whtBase = whtRate > 0m
                    ? Math.Round(r.WhtAmount / whtRate, 2, MidpointRounding.AwayFromZero)
                    : 0m;
            }
        }

        // Customer billing address + branch (live from master) for the header block.
        var custParty = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == r.CustomerId)
            .Select(c => new { c.BillingAddress, c.BranchCode })
            .FirstOrDefaultAsync(ct);

        return new ReceiptDetail(
            r.ReceiptId, r.DocNo, r.Status.ToString(), r.DocDate, r.CustomerName,
            r.CustomerTaxId, r.PaymentMethod.ToString(), r.ChequeNo, r.Amount,
            r.CurrencyCode, r.Notes, r.PostedAt,
            // AppliedTo lists the TI settlements (VAT path). DO applications + standalone
            // lines are shown via the derived line items above, not here.
            r.Applications.Where(a => a.TaxInvoiceId.HasValue).Select(a => new ReceiptAppliedTo(
                a.TaxInvoiceId!.Value, tiNo.GetValueOrDefault(a.TaxInvoiceId!.Value), a.AppliedAmount,
                tiBuCode.GetValueOrDefault(a.TaxInvoiceId!.Value))).ToList(),
            headerBuCode,
            r.WhtAmount, whtCode, whtRate, whtBase, r.CashReceived,
            r.CustomerWhtCertNo, r.CustomerWhtCertDate,
            lineRows, whtLineViews,
            custParty?.BillingAddress, custParty?.BranchCode,
            r.CreatedViaApiKeyName);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {id} not found.");

        // Sprint (receipt itemize, 2026-05-22) — list the goods/service line items the
        // customer paid for (derived from the applied immutable Tax Invoices). The
        // applied TI number(s) go in the notes (Ham 2026-05-22). No VAT row (the
        // receipt settles already-VAT'd invoices). WHT is NOT printed on the receipt
        // (only recorded: WhtAmount + per-category WhtLines + the customer 50ทวิ).
        var cfg = Pdf.PaperDoc.Config[Pdf.PaperDocKind.Receipt];

        // Prefer the derived line items; fall back to one row per applied TI for
        // legacy receipts whose source TIs have no line rows.
        var lines = d.Lines is { Count: > 0 }
            ? d.Lines.Select(l => new Pdf.PaperLine(
                l.DescriptionTh, l.TiDocNo, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList()
            : d.AppliedTo.Select(a => new Pdf.PaperLine(
                $"ใบกำกับภาษี {a.TiDocNo ?? $"#{a.TaxInvoiceId}"}",
                a.BusinessUnitCode, null, null, null, null, a.AppliedAmount)).ToList();

        var tiRefs = string.Join(", ", d.AppliedTo
            .Select(a => a.TiDocNo ?? $"#{a.TaxInvoiceId}").Distinct());
        var notes = string.IsNullOrWhiteSpace(d.Notes)
            ? $"อ้างอิงใบกำกับภาษี: {tiRefs}"
            : $"{d.Notes}\nอ้างอิงใบกำกับภาษี: {tiRefs}";

        var model = new Pdf.PaperDocModel(
            cfg.DocType, cfg.DocTypeEn, d.DocNo ?? string.Empty, d.DocDate,
            await Pdf.PaperSellerSource.FromCompanyProfileAsync(_db, _tenant.CompanyId, ct, _storage),
            new Pdf.PaperCustomer(d.CustomerName, Pdf.PaperFormat.TaxId(d.CustomerTaxId),
                d.CustomerBranchCode, d.CustomerAddress),
            lines,
            new Pdf.PaperSummary(d.Amount, null, null, 0m, d.Amount, null,
                ShowVat: (await _taxCfg.GetAsync(ct)).VatMode),
            new Pdf.PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            Notes: notes,
            Watermark: copy
                ? new Pdf.PaperWatermark("สำเนา", Pdf.PaperWatermarkVariant.Warning)
                : Pdf.PaperDoc.Watermark(Pdf.PaperDocKind.Receipt, d.Status));
        return Pdf.PaperDocumentPdf.Render(model);
    }

    // Sprint (multi-category WHT, 2026-05-22) — replaces the single-rate suggestion.
    // A bill mixing rent 5% / service 3% / ads 2% withholds per income category, so
    // the suggestion is now a per-category breakdown: each applied TI's SERVICE lines
    // are grouped by the line's product DefaultWhtType (customer default as fallback),
    // pro-rated to the amount actually paid toward that TI (partial payments scale the
    // base). Goods/exempt-goods are excluded. The legacy aggregate fields are filled
    // (sum of categories) for any caller that still reads them.
    public async Task<WhtBaseSuggestion> SuggestWhtBaseAsync(
        IReadOnlyList<ReceiptApplicationInput> applications, long customerId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var tiIds = applications.Select(a => a.TaxInvoiceId).Distinct().ToList();

        var tiTotals = await _db.TaxInvoices.AsNoTracking()
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.TotalAmount, ct);

        var tiLines = await _db.TaxInvoiceLines.AsNoTracking()
            .Where(l => tiIds.Contains(l.TaxInvoiceId))
            .OrderBy(l => l.TaxInvoiceId).ThenBy(l => l.LineNo)
            .Select(l => new { l.TaxInvoiceId, l.ProductId, l.ProductType, l.LineAmount, l.DescriptionTh })
            .ToListAsync(ct);
        var tiDocNos = await _db.TaxInvoices.AsNoTracking()
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.DocNo, ct);

        // Non-VAT path (Phase 2a): a non-VAT company issues no Tax Invoice (ม.86/4),
        // so a receipt settles an Invoice (BillingNote) directly. Mirror the TI logic
        // off BillingNote lines — there is no VAT to strip, so paid = LineAmount × fraction.
        var bnIds = applications.Where(a => a.BillingNoteId.HasValue)
            .Select(a => a.BillingNoteId!.Value).Distinct().ToList();
        var bnTotals = await _db.BillingNotes.AsNoTracking()
            .Where(b => bnIds.Contains(b.BillingNoteId))
            .ToDictionaryAsync(b => b.BillingNoteId, b => b.TotalAmount, ct);
        var bnLines = await _db.BillingNoteLines.AsNoTracking()
            .Where(l => bnIds.Contains(l.BillingNoteId))
            .OrderBy(l => l.BillingNoteId).ThenBy(l => l.LineNo)
            .Select(l => new { l.BillingNoteId, l.ProductId, l.ProductType, l.LineAmount, l.DescriptionTh })
            .ToListAsync(ct);
        var bnDocNos = await _db.BillingNotes.AsNoTracking()
            .Where(b => bnIds.Contains(b.BillingNoteId))
            .ToDictionaryAsync(b => b.BillingNoteId, b => b.DocNo, ct);

        var productIds = tiLines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value)
            .Concat(bnLines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value))
            .Distinct().ToList();
        var productWht = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => p.DefaultWhtTypeId, ct);

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        // Fallback category for a service line with no product-level WHT type:
        // the customer default, else SVC-3% for a corporate customer (preserves the
        // pre-existing single-category behavior). B2C individual → no fallback.
        int? fallback = customer?.DefaultWhtTypeId;
        if (fallback is null && customer?.CustomerType == CustomerType.Corporate)
            fallback = await _db.WhtTypes.AsNoTracking()
                .Where(w => w.Code == "SVC" && w.IsActive && w.EffectiveTo == null)
                .Select(w => (int?)w.WhtTypeId).FirstOrDefaultAsync(ct);

        decimal serviceSub = 0m, goodsSub = 0m;
        var allocApps = new List<WhtAllocApplication>();
        // Per-line drafts: every applied line (goods + service), pro-rated, with the
        // resolved category (null for goods) — the FE renders the WHT table from these.
        var lineDrafts = new List<(string? TiDocNo, string Desc, string ProductType, decimal PaidExVat, int? WhtId)>();
        // WHT auto-suggest derives from applied Tax Invoice lines (VAT path). Non-VAT
        // DO/standalone applications have no TI lines → skipped (manual WHT entry).
        foreach (var a in applications.Where(x => x.TaxInvoiceId.HasValue))
        {
            var tiId = a.TaxInvoiceId!.Value;
            var tiTotal = tiTotals.GetValueOrDefault(tiId);
            var fraction = tiTotal > 0m ? a.AppliedAmount / tiTotal : 0m;
            var rows = new List<WhtAllocLine>();
            foreach (var l in tiLines.Where(l => l.TaxInvoiceId == tiId))
            {
                var paidExVat = Math.Round(l.LineAmount * fraction, 2, MidpointRounding.AwayFromZero);
                int? whtId = null;
                if (ReceiptWhtAllocator.IsService(l.ProductType))
                {
                    serviceSub += paidExVat;
                    whtId = (l.ProductId != null ? productWht.GetValueOrDefault(l.ProductId.Value) : null)
                            ?? fallback;
                    rows.Add(new WhtAllocLine(l.LineAmount, l.ProductType, whtId));
                }
                else
                {
                    goodsSub += paidExVat;
                    rows.Add(new WhtAllocLine(l.LineAmount, l.ProductType, null));
                }
                lineDrafts.Add((tiDocNos.GetValueOrDefault(tiId), l.DescriptionTh, l.ProductType, paidExVat, whtId));
            }
            allocApps.Add(new WhtAllocApplication(a.AppliedAmount, tiTotal, rows));
        }
        // Same per-line WHT auto-categorization for Invoice (BillingNote) applications
        // (non-VAT). LineAmount carries no VAT, so the paid base is LineAmount × fraction.
        foreach (var a in applications.Where(x => x.BillingNoteId.HasValue))
        {
            var bnId = a.BillingNoteId!.Value;
            var bnTotal = bnTotals.GetValueOrDefault(bnId);
            var fraction = bnTotal > 0m ? a.AppliedAmount / bnTotal : 0m;
            var rows = new List<WhtAllocLine>();
            foreach (var l in bnLines.Where(l => l.BillingNoteId == bnId))
            {
                var paid = Math.Round(l.LineAmount * fraction, 2, MidpointRounding.AwayFromZero);
                int? whtId = null;
                if (ReceiptWhtAllocator.IsService(l.ProductType))
                {
                    serviceSub += paid;
                    whtId = (l.ProductId != null ? productWht.GetValueOrDefault(l.ProductId.Value) : null)
                            ?? fallback;
                    rows.Add(new WhtAllocLine(l.LineAmount, l.ProductType, whtId));
                }
                else
                {
                    goodsSub += paid;
                    rows.Add(new WhtAllocLine(l.LineAmount, l.ProductType, null));
                }
                lineDrafts.Add((bnDocNos.GetValueOrDefault(bnId), l.DescriptionTh, l.ProductType, paid, whtId));
            }
            allocApps.Add(new WhtAllocApplication(a.AppliedAmount, bnTotal, rows));
        }
        serviceSub = Math.Round(serviceSub, 2, MidpointRounding.AwayFromZero);
        goodsSub = Math.Round(goodsSub, 2, MidpointRounding.AwayFromZero);
        var subtotal = serviceSub + goodsSub;

        var allocations = ReceiptWhtAllocator.Allocate(allocApps);

        // Load every WhtType referenced by a category OR a per-line suggestion.
        var allTypeIds = allocations.Select(a => a.WhtTypeId)
            .Concat(lineDrafts.Where(x => x.WhtId != null).Select(x => x.WhtId!.Value))
            .Distinct().ToList();
        var types = await _db.WhtTypes.AsNoTracking()
            .Where(w => allTypeIds.Contains(w.WhtTypeId))
            .ToDictionaryAsync(w => w.WhtTypeId, ct);

        var suggestLines = lineDrafts.Select(x =>
        {
            string? code = null; var rate = 0m;
            if (x.WhtId is { } id && types.TryGetValue(id, out var wt)) { code = wt.Code; rate = wt.Rate; }
            return new WhtSuggestLine(x.TiDocNo, x.Desc, x.ProductType, x.PaidExVat, x.WhtId, code, rate);
        }).ToList();

        var categories = new List<WhtCategorySuggestion>();
        foreach (var alloc in allocations)
        {
            if (!types.TryGetValue(alloc.WhtTypeId, out var wt) || !wt.IsActive) continue;
            var amount = Math.Round(alloc.BaseAmount * wt.Rate, 2, MidpointRounding.AwayFromZero);
            categories.Add(new WhtCategorySuggestion(
                wt.WhtTypeId, wt.Code, wt.NameTh, wt.Rate, alloc.BaseAmount, amount));
        }

        if (categories.Count == 0)
            return new WhtBaseSuggestion(subtotal, null, null, 0m, 0m, 0m,
                "ไม่มีรายการบริการที่ตั้งค่าหัก ณ ที่จ่าย — เลือกประเภทต่อรายการได้หากต้องการหัก",
                serviceSub, goodsSub, categories, suggestLines);

        var totalBase = categories.Sum(c => c.Base);
        var totalWht = categories.Sum(c => c.Amount);
        var single = categories.Count == 1 ? categories[0] : null;
        var explanation = categories.Count == 1
            ? $"WHT จากส่วนบริการ ({single!.Base:N2}) × {single.Rate:P2} = {single.Amount:N2} " +
              $"(สินค้า {goodsSub:N2} ไม่นำมาคำนวณ). ปรับต่อรายการได้"
            : $"WHT แยกตามประเภทบริการ {categories.Count} ประเภท (รวมหัก {totalWht:N2}; " +
              $"สินค้า {goodsSub:N2} ไม่นำมาคำนวณ). ปรับต่อรายการได้";

        return new WhtBaseSuggestion(
            subtotal, single?.WhtTypeId, single?.Code, single?.Rate ?? 0m,
            totalBase, totalWht, explanation, serviceSub, goodsSub, categories, suggestLines);
    }
}
