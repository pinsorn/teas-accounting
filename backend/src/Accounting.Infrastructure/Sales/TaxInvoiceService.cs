using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Infrastructure.ETax;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Sales;

/// <summary>
/// Tax Invoice (ม.86/4) issuance pipeline:
/// 1. Draft  — snapshot supplier (Company + Branch) + customer (Customer master) into the TI row.
/// 2. Post   — verify period open, allocate doc_no from sys.number_sequences(TI), freeze status,
///            auto-post GL, optionally trigger e-Tax XML+sign+email.
/// VAT calc uses HALF_UP rounding to 2 decimals — matches RD's 7/107 convention for inclusive prices.
/// </summary>
public sealed partial class TaxInvoiceService : ITaxInvoiceService
{
    private const string TiPrefix = "TI";

    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly IGlPostingService       _gl;
    private readonly IPeriodCloseService     _period;
    private readonly IETaxXmlBuilder         _etaxXml;
    private readonly IETaxSubmissionPipeline _etaxPipeline;
    private readonly ETaxBehaviorOptions     _etaxOpts;
    private readonly VatModeOptions          _vat;
    private readonly ILogger<TaxInvoiceService> _log;

    public TaxInvoiceService(
        AccountingDbContext db,
        ITenantContext tenant,
        IClock clock,
        INumberSequenceService numbers,
        IGlPostingService gl,
        IPeriodCloseService period,
        IETaxXmlBuilder etaxXml,
        IETaxSubmissionPipeline etaxPipeline,
        IOptions<ETaxBehaviorOptions> etaxOpts,
        IOptions<VatModeOptions> vat,
        ILogger<TaxInvoiceService> log)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _gl = gl; _period = period;
        _etaxXml = etaxXml; _etaxPipeline = etaxPipeline;
        _etaxOpts = etaxOpts.Value; _vat = vat.Value; _log = log;
    }

    public async Task<long> CreateDraftAsync(CreateTaxInvoiceRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await _period.EnsureOpenAsync(req.DocDate, ct);

        var company = await _db.Companies
                .Include(c => c.Branches)
                .FirstOrDefaultAsync(c => c.CompanyId == _tenant.CompanyId, ct)
            ?? throw new DomainException("ti.company_missing", "Company not found.");

        var branch = company.Branches.FirstOrDefault(b => b.BranchId == _tenant.BranchId)
                     ?? company.Branches.FirstOrDefault(b => b.IsHeadOffice)
                     ?? throw new DomainException("ti.branch_missing", "No branch / head office found.");

        var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("ti.customer_missing", $"Customer {req.CustomerId} not found.");

        // ม.86/4 #3 — VAT-registered customer must have Tax ID + branch.
        if (customer.VatRegistered && (string.IsNullOrEmpty(customer.TaxId) || string.IsNullOrEmpty(customer.BranchCode)))
            throw new DomainException("ti.customer_incomplete",
                "VAT-registered customer requires Tax ID + branch_code (ม.86/4 #3).");

        // Sprint 8 — BU. Required when the company opted in; if supplied it must
        // be an active BU of this tenant (query filter scopes to the company).
        if (company.RequiresBusinessUnit && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        var lines = req.Lines.Select((l, i) => BuildLine(l, i + 1, req.IsTaxInclusive)).ToList();

        var subtotal  = lines.Sum(l => l.LineAmount);
        var taxable   = lines.Where(l => l.TaxRate > 0).Sum(l => l.LineAmount);
        var nontaxable = subtotal - taxable;
        var vatAmount = lines.Sum(l => l.TaxAmount);
        var total     = lines.Sum(l => l.TotalAmount);

        var ti = new TaxInvoice
        {
            CompanyId = _tenant.CompanyId,
            BranchId  = _tenant.BranchId,
            DocDate       = req.DocDate,
            TaxPointDate  = req.DocDate,
            SupplierTaxId      = company.TaxId,
            SupplierBranchCode = branch.BranchCode,
            SupplierBranchName = branch.IsHeadOffice ? "สำนักงานใหญ่" : $"สาขาที่ {branch.BranchCode}",
            SupplierName       = company.NameTh,
            SupplierAddress    = company.AddressTh ?? string.Empty,
            CustomerId             = customer.CustomerId,
            CustomerTaxId          = customer.TaxId,
            CustomerBranchCode     = customer.BranchCode,
            CustomerBranchName     = customer.BranchName,
            CustomerName           = customer.NameTh,
            CustomerAddress        = customer.BillingAddress ?? string.Empty,
            CustomerVatRegistered  = customer.VatRegistered,
            CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate,
            IsTaxInclusive = req.IsTaxInclusive,
            SubtotalAmount   = subtotal,
            DiscountAmount   = 0m,
            TaxableAmount    = taxable,
            NonTaxableAmount = nontaxable,
            TaxAmount        = vatAmount,
            TotalAmount      = total,
            TotalAmountThb   = Math.Round(total * req.ExchangeRate, 4, MidpointRounding.AwayFromZero),
            DueDate          = req.DueDate,
            PaymentTerms     = req.PaymentTerms,
            Notes            = req.Notes,
            BusinessUnitId   = req.BusinessUnitId,
            Lines            = lines,
        };

        _db.TaxInvoices.Add(ti);
        await _db.SaveChangesAsync(ct);
        return ti.TaxInvoiceId;
    }

    public async Task<TaxInvoicePostedResult> PostAsync(long taxInvoiceId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var ti = await _db.TaxInvoices
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TaxInvoiceId == taxInvoiceId, ct)
            ?? throw new DomainException("ti.not_found", $"Tax Invoice {taxInvoiceId} not found.");

        await _period.EnsureOpenAsync(ti.DocDate, ct);

        var buCode = ti.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(x => x.BusinessUnitId == bid)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var docNo = await _numbers.NextAsync(
            ti.CompanyId, ti.BranchId, TiPrefix, subPrefix: buCode, ti.DocDate, ct);

        // Sprint 10 A3 — snapshot Product.ProductCode onto each linked line so a
        // POSTED TI stays immutable even if the Product master is later edited /
        // deactivated (mirrors the Sprint 5.5 Vendor snapshot pattern).
        var prodIds = ti.Lines.Where(l => l.ProductId is not null)
            .Select(l => l.ProductId!.Value).Distinct().ToList();
        if (prodIds.Count > 0)
        {
            var codes = await _db.Products
                .Where(p => prodIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductCode })
                .ToDictionaryAsync(p => p.ProductId, p => p.ProductCode, ct);
            foreach (var l in ti.Lines.Where(l => l.ProductId is not null))
                if (codes.TryGetValue(l.ProductId!.Value, out var pc))
                    l.ProductCode = pc;
        }

        var now = _clock.UtcNow;
        ti.MarkPosted(docNo, _tenant.UserId ?? 0, now);

        await _db.SaveChangesAsync(ct);

        // GL auto-post — fails fast if CoA mapping incomplete; rolled back with the TI.
        await _gl.PostTaxInvoiceAsync(ti.TaxInvoiceId, ct);

        await tx.CommitAsync(ct);

        // e-Tax submission — best-effort post-commit. Per RD spec the submission is
        // real-time and customer-facing; failures are logged so an operator can re-send.
        if (_etaxOpts.Enabled && _etaxOpts.AutoSendOnTaxInvoicePost)
            await TryAutoSendETaxAsync(ti, ct);

        return new TaxInvoicePostedResult(ti.TaxInvoiceId, docNo, now, ti.TotalAmount, ti.TaxAmount);
    }

    private async Task TryAutoSendETaxAsync(TaxInvoice ti, CancellationToken ct)
    {
        // Sprint 13c — the submission pipeline owns build→sign→validate→send and
        // writes the append-only etax.submissions audit row (incl. its own
        // failure handling + retry scheduling). Guarded so a pipeline fault
        // never rolls back the already-committed Tax Invoice post.
        try
        {
            await _etaxPipeline.EnqueueAsync(ti.TaxInvoiceId, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "e-Tax pipeline enqueue failed for TI {DocNo}; audit row not written.", ti.DocNo);
        }
    }

    private static TaxInvoiceLine BuildLine(TaxInvoiceLineInput input, int lineNo, bool inclusive)
    {
        var gross = Math.Round(input.Quantity * input.UnitPrice, 4, MidpointRounding.AwayFromZero);
        var afterDisc = input.DiscountPercent > 0
            ? Math.Round(gross * (1m - input.DiscountPercent / 100m), 4, MidpointRounding.AwayFromZero)
            : gross;

        decimal net, vat, total;
        if (inclusive && input.TaxRate > 0)
        {
            // Tax Inclusive: VAT = total × rate / (1+rate)
            total = afterDisc;
            vat   = Math.Round(total * input.TaxRate / (1m + input.TaxRate), 2, MidpointRounding.AwayFromZero);
            net   = total - vat;
        }
        else
        {
            net   = afterDisc;
            vat   = Math.Round(net * input.TaxRate, 2, MidpointRounding.AwayFromZero);
            total = net + vat;
        }

        return new TaxInvoiceLine
        {
            LineNo          = lineNo,
            ProductId       = input.ProductId,
            ProductCode     = input.ProductCode,
            DescriptionTh   = input.DescriptionTh,
            Quantity        = input.Quantity,
            UomId           = input.UomId,
            UomText         = input.UomText,
            UnitPrice       = input.UnitPrice,
            DiscountPercent = input.DiscountPercent,
            DiscountAmount  = gross - afterDisc,
            LineAmount      = net,
            TaxCodeId       = input.TaxCodeId,
            TaxCode         = input.TaxCode,
            TaxRate         = input.TaxRate,
            TaxAmount       = vat,
            TotalAmount     = total,
        };
    }
}
