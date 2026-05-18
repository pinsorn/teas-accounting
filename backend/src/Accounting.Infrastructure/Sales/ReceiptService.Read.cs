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
                r.Status.ToString(), r.CurrencyCode, r.WhtAmount))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<ReceiptListItem>(rows, more ? rows[^1].ReceiptId : null, more);
    }

    public async Task<ReceiptDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var r = await _db.Receipts.AsNoTracking().Include(x => x.Applications)
            .FirstOrDefaultAsync(x => x.ReceiptId == id, ct);
        if (r is null) return null;

        var tiIds = r.Applications.Select(a => a.TaxInvoiceId).ToList();
        var ti = await _db.TaxInvoices.AsNoTracking()
            .Where(t => tiIds.Contains(t.TaxInvoiceId))
            .Select(t => new { t.TaxInvoiceId, t.DocNo, t.BusinessUnitId })
            .ToListAsync(ct);
        var tiNo = ti.ToDictionary(x => x.TaxInvoiceId, x => x.DocNo);
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

        // Sprint 8.6 — WHT detail (rate/code from the snapshot type; base derived).
        string? whtCode = null;
        var whtRate = 0m;
        if (r.WhtAmount > 0m && r.WhtTypeId is { } wtId)
        {
            var wt = await _db.WhtTypes.AsNoTracking()
                .Where(w => w.WhtTypeId == wtId)
                .Select(w => new { w.Code, w.Rate }).FirstOrDefaultAsync(ct);
            whtCode = wt?.Code;
            whtRate = wt?.Rate ?? 0m;
        }
        var whtBase = whtRate > 0m
            ? Math.Round(r.WhtAmount / whtRate, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ReceiptDetail(
            r.ReceiptId, r.DocNo, r.Status.ToString(), r.DocDate, r.CustomerName,
            r.CustomerTaxId, r.PaymentMethod.ToString(), r.ChequeNo, r.Amount,
            r.CurrencyCode, r.Notes, r.PostedAt,
            r.Applications.Select(a => new ReceiptAppliedTo(
                a.TaxInvoiceId, tiNo.GetValueOrDefault(a.TaxInvoiceId), a.AppliedAmount,
                tiBuCode.GetValueOrDefault(a.TaxInvoiceId))).ToList(),
            headerBuCode,
            r.WhtAmount, whtCode, whtRate, whtBase, r.CashReceived,
            r.CustomerWhtCertNo, r.CustomerWhtCertDate);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("rc.not_found", $"Receipt {id} not found.");
        return Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(28); p.DefaultTextStyle(s => s.FontSize(10));
            p.Header().AlignCenter().Text("ใบเสร็จรับเงิน / RECEIPT").Bold().FontSize(15);
            p.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text($"เลขที่ / No.: {d.DocNo ?? "(ร่าง)"}");
                col.Item().Text($"วันที่ / Date: {d.DocDate:dd/MM/yyyy}");
                col.Item().Text($"ลูกค้า / Customer: {d.CustomerName}");
                col.Item().Text($"วิธีชำระ / Method: {d.PaymentMethod}");
                col.Item().Text($"จำนวนเงิน / Amount: {d.Amount:N2} {d.CurrencyCode}").Bold().FontSize(12);
                col.Item().PaddingTop(6).Text("ชำระสำหรับใบกำกับภาษี / Applied to:").Bold();
                foreach (var a in d.AppliedTo)
                    col.Item().Text($"  {a.TiDocNo ?? a.TaxInvoiceId.ToString()} : {a.AppliedAmount:N2}");

                // Sprint 8.6 — AR-side WHT section (conditional). Receipt header
                // is VAT-status independent (8.5 §2.1) — only this block is added.
                if (d.WhtAmount > 0m)
                {
                    col.Item().PaddingTop(8).LineHorizontal(0.5f);
                    col.Item().Text("หัก ภาษี ณ ที่จ่าย / Withholding tax").Bold();
                    col.Item().Text(
                        $"  ประเภท: {d.WhtTypeCode}   อัตรา: {d.WhtRate:P2}   " +
                        $"ฐาน: {d.WhtBase:N2}");
                    col.Item().Text($"  WHT: ({d.WhtAmount:N2}) {d.CurrencyCode}").Bold();
                    col.Item().Text(
                        $"  ยอดสุทธิที่ได้รับ / Net received: {d.CashReceived:N2} {d.CurrencyCode}")
                        .Bold().FontSize(12);
                    col.Item().Text(
                        $"  เลขที่ใบ 50ทวิ ที่ลูกค้าออก: {d.CustomerWhtCertNo} " +
                        $"ลงวันที่ {(d.CustomerWhtCertDate is { } cd ? cd.ToString("dd/MM/yyyy") : "-")}")
                        .FontSize(8);
                }
            });
            p.Footer().AlignCenter().Text("ออกโดยระบบ TEAS").FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }

    // Sprint 10 A4 — Product master now exists. Split applied TI lines by
    // Product.ProductType: SERVICE/EXEMPT_SERVICE → service, GOOD/EXEMPT_GOOD →
    // goods, NULL product (legacy/unlinked) → service (conservative for WHT).
    // SuggestedWhtBase defaults to the service portion (was full ex-VAT in
    // 8.6 R-B1a). User still override-able. Rate/type from customer default,
    // else CORPORATE→SVC heuristic; B2C individuals get no suggestion.
    public async Task<WhtBaseSuggestion> SuggestWhtBaseAsync(
        IReadOnlyList<long> taxInvoiceIds, long customerId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var lines = await _db.TaxInvoiceLines.AsNoTracking()
            .Where(l => taxInvoiceIds.Contains(l.TaxInvoiceId))
            .Select(l => new { l.ProductId, l.LineAmount })
            .ToListAsync(ct);
        var svcProductIds = (await _db.Products.AsNoTracking()
            .Where(p => p.ProductType == ProductType.Service
                     || p.ProductType == ProductType.ExemptService)
            .Select(p => p.ProductId).ToListAsync(ct)).ToHashSet();

        decimal serviceSub = 0m, goodsSub = 0m;
        foreach (var l in lines)
        {
            // NULL product → service bucket (conservative for WHT, per spec A4).
            if (l.ProductId is null || svcProductIds.Contains(l.ProductId.Value))
                serviceSub += l.LineAmount;
            else
                goodsSub += l.LineAmount;
        }
        var subtotal = serviceSub + goodsSub;

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        var wt = customer?.DefaultWhtTypeId is { } dw
            ? await _db.WhtTypes.AsNoTracking()
                .FirstOrDefaultAsync(w => w.WhtTypeId == dw && w.IsActive, ct)
            : customer?.CustomerType == CustomerType.Corporate
                ? await _db.WhtTypes.AsNoTracking().FirstOrDefaultAsync(
                    w => w.Code == "SVC" && w.IsActive && w.EffectiveTo == null, ct)
                : null;

        if (wt is null)
            return new WhtBaseSuggestion(subtotal, null, null, 0m, 0m, 0m,
                "ลูกค้าบุคคลธรรมดา / ไม่มีค่าตั้งต้น — โดยทั่วไปไม่มีหัก ณ ที่จ่าย ปรับเองหากจำเป็น",
                serviceSub, goodsSub);

        var wht = Math.Round(serviceSub * wt.Rate, 2, MidpointRounding.AwayFromZero);
        return new WhtBaseSuggestion(
            subtotal, wt.WhtTypeId, wt.Code, wt.Rate, serviceSub, wht,
            $"WHT คำนวณจากส่วนบริการ ({serviceSub:N2}) เท่านั้น × {wt.Rate:P2} = {wht:N2} " +
            $"(สินค้า {goodsSub:N2} ไม่นำมาคำนวณ). ปรับฐานเองได้หากจำเป็น",
            serviceSub, goodsSub);
    }
}
