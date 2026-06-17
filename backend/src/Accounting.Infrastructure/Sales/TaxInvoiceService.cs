using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
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

    // ProductType is already snapshotted onto srcLines before rate derivation, so the
    // backstop's product-type map is intentionally empty there (it falls back to the line's type).
    private static readonly IReadOnlyDictionary<long, string> EmptyProductTypes =
        new Dictionary<long, string>();

    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly IGlPostingService       _gl;
    private readonly IPeriodCloseService     _period;
    private readonly IETaxXmlBuilder         _etaxXml;
    private readonly IETaxSubmissionPipeline _etaxPipeline;
    private readonly ETaxBehaviorOptions     _etaxOpts;
    private readonly ICompanyTaxConfigService _taxCfg;
    private readonly ILogger<TaxInvoiceService> _log;
    private readonly IActivityRecorder       _activity;

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
        ICompanyTaxConfigService taxCfg,
        ILogger<TaxInvoiceService> log,
        IActivityRecorder activity)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _gl = gl; _period = period;
        _etaxXml = etaxXml; _etaxPipeline = etaxPipeline;
        _etaxOpts = etaxOpts.Value; _taxCfg = taxCfg; _log = log;
        _activity = activity;
    }

    // Non-VAT companies (ม.86/4) cannot issue Tax Invoices. This is the single
    // chokepoint for ALL TI creation — manual, Pattern X (DO→TI combined), and
    // Pattern Y (separate DO→TI) all funnel through CreateDraftAsync.
    private async Task EnsureVatRegisteredAsync(CancellationToken ct)
    {
        if (!(await _taxCfg.GetAsync(ct)).VatMode)
            throw new DomainException("ti.non_vat_blocked",
                "VAT-not-registered companies cannot issue Tax Invoices (ม.86/4). " +
                "Use a delivery note / receipt instead.");
    }

    // cont.69 Phase 1 — Invoice (BillingNote) → Tax Invoice, manual, VAT only. The
    // EnsureVatRegistered() chokepoint in CreateDraftAsync blocks non-VAT (422
    // ti.non_vat_blocked) — called here up-front so a non-VAT tenant never loads the BN.
    public async Task<long> CreateFromBillingNoteAsync(long billingNoteId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        await EnsureVatRegisteredAsync(ct);

        var bn = await _db.BillingNotes.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == _tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.BillingNoteId == billingNoteId, ct)
            ?? throw new DomainException("billing_note.not_found", $"Invoice {billingNoteId} not found.");

        var lines = bn.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceLineInput(
            l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity, 1, l.UomText,
            l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate,
            l.ProductType)).ToList();

        // §4.6 / ม.80 — chain-copy: the BillingNote lines were already rate-derived at
        // their own origin builder; inherit those rates, do NOT re-derive (deriveLineTax:false).
        var tiId = await CreateDraftCoreAsync(new CreateTaxInvoiceRequest(
            bn.DocDate, bn.CustomerId, false, bn.CurrencyCode, bn.ExchangeRate,
            bn.Notes, null, null, lines, bn.BusinessUnitId), deriveLineTax: false, ct);

        // Stamp the source link (CreateTaxInvoiceRequest has no BillingNoteId field).
        var ti = await _db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == tiId, ct);
        ti.BillingNoteId = bn.BillingNoteId;
        await _db.SaveChangesAsync(ct);
        _activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId,
            "CreatedFromInvoice", note: $"จากใบแจ้งหนี้ {bn.DocNo ?? bn.BillingNoteId.ToString()}");
        await _db.SaveChangesAsync(ct);
        return tiId;
    }

    // Public/request-fed entry point — DERIVES the per-line VAT rate from company master
    // data (§4.6 / ม.80). The DO→TI and Invoice→TI chain-copy paths call CreateDraftCoreAsync
    // with deriveLineTax:false so an already-normalized source rate is inherited, not re-derived.
    public Task<long> CreateDraftAsync(CreateTaxInvoiceRequest req, CancellationToken ct)
        => CreateDraftCoreAsync(req, deriveLineTax: true, ct);

    private async Task<long> CreateDraftCoreAsync(
        CreateTaxInvoiceRequest req, bool deriveLineTax, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        await EnsureVatRegisteredAsync(ct);

        // Sprint 14 P7 — per-key BU lock (auto-fill / mismatch). Company-level
        // requires_business_unit still runs below on the resolved value.
        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, _tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {_tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        req = req with { BusinessUnitId = effBu };

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
        // be an active BU of this tenant. Company match is EXPLICIT (M13): the EF
        // tenant filter is bypassed for super admins and accepted a foreign BU.
        if (company.RequiresBusinessUnit && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await _db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId
                && x.CompanyId == _tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        // cont.69 — snapshot ProductType from the product when a line references a
        // productId but the caller didn't supply the type. The WHT service/goods split
        // (SuggestWhtBaseAsync) reads line.ProductType; without this a product-linked
        // service line silently defaulted to "GOOD" → ServiceSubtotal 0.
        var srcLines = req.Lines;
        var needType = req.Lines
            .Where(l => l.ProductId is not null && string.IsNullOrEmpty(l.ProductType))
            .Select(l => l.ProductId!.Value).Distinct().ToList();
        if (needType.Count > 0)
        {
            var prods = await _db.Products.AsNoTracking()
                .Where(p => needType.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductType })
                .ToListAsync(ct);
            // Line ProductType uses the UPPER_SNAKE convention IsService() expects;
            // the Product master stores a PascalCase enum → map it.
            var ptypes = prods.ToDictionary(p => p.ProductId, p => p.ProductType switch
            {
                Domain.Enums.ProductType.Service        => "SERVICE",
                Domain.Enums.ProductType.ExemptService  => "EXEMPT_SERVICE",
                Domain.Enums.ProductType.ExemptGood     => "EXEMPT_GOOD",
                _                                       => "GOOD",
            });
            srcLines = req.Lines.Select(l =>
                l.ProductId is { } pid && string.IsNullOrEmpty(l.ProductType)
                    && ptypes.TryGetValue(pid, out var pt)
                    ? l with { ProductType = pt }
                    : l).ToList();
        }

        // §4.6 / ม.80 — the per-line VAT RATE is company master data, NOT caller input. For a
        // request-fed Tax Invoice (POST /tax-invoices) this is the EXACT path that produced the
        // "VAT7 + taxRate:0 → 0-VAT tax invoice" hole: BuildLine trusted input.TaxRate. We are
        // VAT-registered here (EnsureVatRegisteredAsync above), so derive each line's rate/code
        // from its tax-code classification (exempt/zero-rated → 0; standard → companies.vat_rate).
        // Chain-copy paths (DO→TI, Invoice→TI) pass deriveLineTax:false and inherit the already-
        // normalized source rate — re-deriving there would double-process.
        if (deriveLineTax)
        {
            var vatRate = (await _taxCfg.GetAsync(ct)).VatRate;
            var taxCodeFlags = await SalesLineBackstop.LoadTaxCodeFlagsAsync(
                _db, srcLines.Select(l => l.TaxCode), ct);
            srcLines = srcLines.Select(l =>
            {
                var (_, rate, code) = SalesLineBackstop.Resolve(
                    vatMode: true, vatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode,
                    productTypes: EmptyProductTypes, taxCodeFlags);
                return l with { TaxRate = rate, TaxCode = code };
            }).ToList();
        }

        var lines = srcLines.Select((l, i) => BuildLine(l, i + 1, req.IsTaxInclusive)).ToList();

        var subtotal  = lines.Sum(l => l.LineAmount);
        var taxable   = lines.Where(l => l.TaxRate > 0).Sum(l => l.LineAmount);
        var nontaxable = subtotal - taxable;
        var vatAmount = lines.Sum(l => l.TaxAmount);
        var total     = lines.Sum(l => l.TotalAmount);

        // ม.86/4 #2 — the seller address on a Tax Invoice is the registered (DBD) address.
        // Prefer the CompanyProfile registered address; companies.AddressTh is the legacy
        // free-text fallback (it is empty on fresh seeds, which left the printed TI without
        // a seller address). Snapshot here = immutable after post (§4.2).
        var sellerProfile = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == _tenant.CompanyId, ct);
        var sellerAddress = Pdf.PaperSellerSource.ComposeRegisteredAddress(sellerProfile);
        if (string.IsNullOrWhiteSpace(sellerAddress))
            sellerAddress = company.AddressTh ?? string.Empty;

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
            SupplierAddress    = sellerAddress,
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
            QuotationId      = req.QuotationId,   // Sprint 13h P6.1
            Lines            = lines,
        };

        _db.TaxInvoices.Add(ti);
        await _db.SaveChangesAsync(ct);
        _activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "Created", toStatus: "Draft");
        await _db.SaveChangesAsync(ct);
        return ti.TaxInvoiceId;
    }

    public async Task<TaxInvoicePostedResult> PostAsync(long taxInvoiceId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        // Defense: a draft TI may survive a VAT→non-VAT config switch; it must not post.
        await EnsureVatRegisteredAsync(ct);

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
        _activity.Record("TaxInvoice", ti.TaxInvoiceId, docNo, ti.CompanyId, "Posted", "Draft", "Posted");

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
            ProductType     = input.ProductType ?? "GOOD",   // Sprint 13h P7 + 13i C5 default
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
