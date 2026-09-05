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
using Npgsql;
using NpgsqlTypes;

namespace Accounting.Infrastructure.Sales;

// Sprint 10 Part B — Q → SO → DO chain. Numbering on the POST-equivalent
// (Quotation = Send, SO = Post, DO = Post) via INumberSequenceService with the
// BU code as sub-prefix. BU cascades Q→SO→DO→TI.

internal static class ChainMath
{
    public static (decimal net, decimal vat, decimal total) Line(
        decimal qty, decimal price, decimal discPct, decimal rate)
    {
        // R1/C1 (WP-3, deferral reversed) — THB is a 2-decimal currency; this net/total
        // cascades Q→SO→DO→TI with no rounding step in between and can reach a posted JE.
        var gross = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero);
        var net = discPct > 0
            ? Math.Round(gross * (1m - discPct / 100m), 2, MidpointRounding.AwayFromZero)
            : gross;
        var vat = Math.Round(net * rate, 2, MidpointRounding.AwayFromZero);
        return (net, vat, net + vat);
    }
}

public sealed class QuotationService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    ICompanyTaxConfigService taxCfg, IIdempotencyContext idem) : IQuotationService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    // WP-J (specs/fix-idempotency-document-fence.md §3.3) — AsNoTracking lookup shared by the
    // initial check and the post-23505 re-lookup; same predicate both times.
    private Task<Quotation?> FindFencedAsync(long apiKeyId, string key, CancellationToken ct) =>
        db.Quotations.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId          // M13 explicit, belt over the
                     && x.CreatedViaApiKeyId == apiKeyId         //   global query filter + RLS
                     && x.IdempotencyKey == key)
            .FirstOrDefaultAsync(ct);

    /// WP-J: this method OWNS its transaction and its change tracker (BeginTransaction + Commit;
    /// the 23505 net calls ChangeTracker.Clear()). Never call it from inside a caller's
    /// transaction or with caller-tracked entities pending — see
    /// SalesOrderDeliveryServices.GenerateTiAsync, which calls it BEFORE touching its own tracked
    /// DeliveryOrder.
    public async Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct)
    {
        Auth();

        // WP-J document idempotency fence (§3.3) — lookup at the TOP (after auth), lock BEFORE
        // lookup, single tx on BOTH the keyed and the unkeyed path.
        var key = idem.Key;
        var hash = idem.RequestHash;
        var apiKeyId = tenant.ApiKeyId;
        var fenced = key is not null && apiKeyId is not null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (fenced)
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(@company, @lock)",
                new NpgsqlParameter[]
                {
                    new("company", NpgsqlDbType.Integer) { Value = tenant.CompanyId },
                    new("lock", NpgsqlDbType.Integer) { Value = IdempotencyFenceLock.LockKey(apiKeyId!.Value, key!) },
                }, ct);

            var existing = await FindFencedAsync(apiKeyId.Value, key!, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.IdempotencyRequestHash, hash, StringComparison.Ordinal))
                    throw new DomainException("idempotency.body_mismatch",
                        "This Idempotency-Key was already used with a different request body.");
                await tx.CommitAsync(ct);
                return existing.QuotationId;
            }
        }

        // Sprint 14 P7 — per-key BU lock (SO/DO inherit this locked Q BU; v1
        // exposes no direct SO/DO create, so the lock at Q entry is sufficient).
        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        req = req with { BusinessUnitId = effBu };

        // S9 (2026-07-16 fix) — company-level BU requirement (same rule already enforced
        // on TaxInvoice/Receipt/TaxAdjustmentNote): MCP/API drafts must not slip through
        // with a null BU when the company opted in.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        var q = new Quotation
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = QuotationStatus.Draft, DocDate = req.DocDate,
            ValidUntilDate = req.ValidUntilDate, CustomerId = cust.CustomerId,
            CustomerName = cust.NameTh, CustomerAddress = cust.BillingAddress,
            CustomerTaxId = cust.TaxId, CustomerType = cust.CustomerType,
            BusinessUnitId = req.BusinessUnitId, CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate, Notes = req.Notes,
            InternalNotes = req.InternalNotes,
            ShowWhtNote = cust.CustomerType == CustomerType.Corporate,
            // M4a — stamp the key name when created by an API-key principal (MCP agent).
            CreatedViaApiKeyName = tenant.ApiKeyName,
            // WP-J — audit id ALWAYS stamped; key/hash only when fenced (partial index ignores
            // unkeyed rows).
            CreatedViaApiKeyId = tenant.ApiKeyId,
            IdempotencyKey = fenced ? key : null,
            IdempotencyRequestHash = fenced ? hash : null,
        };
        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productDefaults = await SalesLineBackstop.LoadProductDefaultsAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodes = await SalesLineBackstop.LoadTaxCodeMasterAsync(db, ct);
        var standardOutput = cfg.VatMode ? await SalesLineBackstop.LoadStandardOutputTaxCodeAsync(db, ct) : null;
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode, taxCodeId) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productDefaults, taxCodes, standardOutput);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            q.Lines.Add(new QuotationLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = taxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            q.SubtotalAmount += net; q.VatAmount += vat; q.TotalAmount += total;
        }
        db.Quotations.Add(q);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IdempotencyFenceLock.IsFenceCollision(ex))
        {
            // 23505 safety net (§3.3) — the lock's braces. Explicit rollback + ChangeTracker
            // clear: the failed insert is still Added, and `await using` disposal alone would
            // not let us re-lookup on this same, still-open connection before the method returns.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var found = await FindFencedAsync(apiKeyId!.Value, key!, ct);
            if (found is not null)
            {
                if (!string.Equals(found.IdempotencyRequestHash, hash, StringComparison.Ordinal))
                    throw new DomainException("idempotency.body_mismatch",
                        "This Idempotency-Key was already used with a different request body.");
                return found.QuotationId;
            }
            throw;   // unexplained collision — never swallow into a wrong id
        }
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return q.QuotationId;
    }

    private async Task<Quotation> LoadAsync(long id, CancellationToken ct) =>
        await db.Quotations.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct)
            ?? throw new DomainException("quotation.not_found", $"Quotation {id} not found.");

    // Sprint 13h P4 — Draft-only full edit. Replaces line items wholesale (drop+add)
    // and recomputes header aggregates. Customer + BU may change while Draft.
    public async Task UpdateDraftAsync(long id, CreateQuotationRequest req, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Draft)
            throw new DomainException("quotation.cannot_edit_after_send",
                "Quotation can only be edited while in Draft.");

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        // S9 (2026-07-16 fix) — same company-level BU requirement as Create; an edit can
        // otherwise re-null a previously-valid BU before Send.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");

        q.DocDate = req.DocDate;
        q.ValidUntilDate = req.ValidUntilDate;
        q.CustomerId = cust.CustomerId;
        q.CustomerName = cust.NameTh;
        q.CustomerAddress = cust.BillingAddress;
        q.CustomerTaxId = cust.TaxId;
        q.CustomerType = cust.CustomerType;
        q.BusinessUnitId = req.BusinessUnitId;
        q.CurrencyCode = req.CurrencyCode;
        q.ExchangeRate = req.ExchangeRate;
        q.Notes = req.Notes;
        q.InternalNotes = req.InternalNotes;
        q.ShowWhtNote = cust.CustomerType == CustomerType.Corporate;

        db.RemoveRange(q.Lines);
        q.Lines.Clear();
        q.SubtotalAmount = q.VatAmount = q.TotalAmount = 0m;

        // §4.6 / ม.80 — VAT rate + tax-code classification come from company master data.
        var cfg = await taxCfg.GetAsync(ct);
        var productDefaults = await SalesLineBackstop.LoadProductDefaultsAsync(db, req.Lines.Select(x => x.ProductId), ct);
        var taxCodes = await SalesLineBackstop.LoadTaxCodeMasterAsync(db, ct);
        var standardOutput = cfg.VatMode ? await SalesLineBackstop.LoadStandardOutputTaxCodeAsync(db, ct) : null;
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (prodType, taxRate, taxCode, taxCodeId) =
                SalesLineBackstop.Resolve(cfg.VatMode, cfg.VatRate, l.ProductId, l.ProductType, l.TaxRate, l.TaxCode, productDefaults, taxCodes, standardOutput);
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, taxRate);
            q.Lines.Add(new QuotationLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = prodType, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = taxCodeId, TaxCode = taxCode, TaxRate = taxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            q.SubtotalAmount += net; q.VatAmount += vat; q.TotalAmount += total;
        }
        // S12-BE (2026-07-16 fix) — draft edits previously wrote no activity entry at all.
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Updated");
        await db.SaveChangesAsync(ct);
    }

    // Sprint 13h P4 — hard-delete a Draft. Allowed because no doc_no allocated yet,
    // so the gap-rule (Plan §17.6) is not violated.
    // WP-J J9 — the fence lives on the document, so deleting the draft must release the
    // operation key too: otherwise a retry inside the 24h claim window replays a 201 for a now-
    // dead id, and after 24h the key is silently free anyway (the deletion just makes it free
    // immediately, deterministically). The (company, api_key, key) tuple comes from the DOCUMENT
    // being deleted, never the deleting principal's own tenant/key. Raw SQL mirrors the advisory-
    // lock call in CreateDraftAsync — no IIdempotencyStore interface change, which would force
    // every test decorator to grow a passthrough. This method now OWNS a transaction (J8 — same
    // discipline as CreateDraftAsync): never call it from inside a caller's unit of work.
    public async Task DeleteDraftAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Draft)
            throw new DomainException("quotation.cannot_delete_after_send",
                "Quotation can only be deleted while in Draft.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.RemoveRange(q.Lines);
        db.Quotations.Remove(q);
        await db.SaveChangesAsync(ct);
        if (q.IdempotencyKey is not null && q.CreatedViaApiKeyId is not null)
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM sys.idempotency_keys WHERE company_id = @c AND api_key_id = @a AND \"key\" = @k",
                new NpgsqlParameter[]
                {
                    new("c", NpgsqlDbType.Integer) { Value = q.CompanyId },
                    new("a", NpgsqlDbType.Bigint) { Value = q.CreatedViaApiKeyId.Value },
                    new("k", NpgsqlDbType.Text) { Value = q.IdempotencyKey },
                }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SendAsync(long id, CancellationToken ct)
    {
        Auth();
        // H8 (review 2026-07-04) — wrap alloc+save in one explicit tx, mirroring the safe
        // TaxInvoiceService.PostAsync pattern; else a failed save after allocation leaves a
        // consumed-but-unused QT number (a gap).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Draft)
            throw new DomainException("quotation.bad_status", "Only a Draft quotation can be sent.");
        // S9 (2026-07-16 fix) — defense at the numbering gate: catches drafts created BEFORE
        // this fix (e.g. MCP agent drafts) that already carry a null BU on a BU-required company.
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);
        if (requiresBu && q.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        var now = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            db,
            c => SubPrefixNumberAsync("QT", q.BusinessUnitId, q.DocDate, c),
            (v, _) => { q.DocNo = v.Value; q.Status = QuotationStatus.Sent; q.SentAt = now; q.SentBy = tenant.UserId; },
            ct);
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Sent", "Draft", "Sent");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task AcceptAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Sent)
            throw new DomainException("quotation.bad_status", "Only a Sent quotation can be accepted.");
        q.Status = QuotationStatus.Accepted;
        q.AcceptedAt = clock.UtcNow;
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Accepted", "Sent", "Accepted");
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Draft))
            throw new DomainException("quotation.bad_status", "Cannot reject in this status.");
        var fromReject = q.Status.ToString();
        q.Status = QuotationStatus.Rejected; q.RejectedReason = reason;
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Rejected", fromReject, "Rejected", note: reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status is QuotationStatus.Accepted && q.ConvertedToSoId is not null)
            throw new DomainException("quotation.converted",
                "Cannot cancel — already converted to a Sales Order.");
        var fromCancel = q.Status.ToString();
        q.Status = QuotationStatus.Cancelled; q.CancelledReason = reason;
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Cancelled", fromCancel, "Cancelled", note: reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> ConvertToSalesOrderAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Accepted)
            throw new DomainException("quotation.not_accepted",
                "Quotation must be Accepted before converting to a Sales Order.");
        if (q.ConvertedToSoId is not null)
            throw new DomainException("quotation.converted",
                "Quotation already converted.");

        var so = new SalesOrder
        {
            CompanyId = q.CompanyId, BranchId = q.BranchId,
            // §5/§10 — use Asia/Bangkok "today", not raw UTC (UTC could be the prior day near midnight).
            Status = SalesOrderStatus.Draft, DocDate = clock.TodayInBangkok(),
            CustomerId = q.CustomerId, CustomerName = q.CustomerName,
            CustomerAddress = q.CustomerAddress, CustomerTaxId = q.CustomerTaxId,
            CustomerType = q.CustomerType, BusinessUnitId = q.BusinessUnitId,
            QuotationId = q.QuotationId, CurrencyCode = q.CurrencyCode,
            ExchangeRate = q.ExchangeRate, SubtotalAmount = q.SubtotalAmount,
            VatAmount = q.VatAmount, TotalAmount = q.TotalAmount,
        };
        foreach (var l in q.Lines.OrderBy(x => x.LineNo))
            so.Lines.Add(new SalesOrderLine
            {
                LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = l.ProductCode,
                ProductType = l.ProductType,  // Sprint 13h P7 — Q→SO cascade
                DescriptionTh = l.DescriptionTh, Quantity = l.Quantity,
                UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = l.LineAmount,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = l.TaxAmount, TotalAmount = l.TotalAmount,
            });
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Created",
            toStatus: "Draft", note: $"จากใบเสนอราคา {q.DocNo ?? q.QuotationId.ToString()}");

        q.ConvertedToSoId = so.SalesOrderId;
        activity.Record("Quotation", q.QuotationId, q.DocNo, q.CompanyId, "Converted", "Accepted", "Accepted",
            note: $"→ ใบสั่งขาย {so.SalesOrderId}");
        await db.SaveChangesAsync(ct);
        return so.SalesOrderId;
    }

    public async Task<IReadOnlyList<QuotationListItem>> ListAsync(string? status, CancellationToken ct,
        DateOnly? dateFrom = null, DateOnly? dateTo = null, long? customerId = null, long? productId = null)
    {
        Auth();
        var qy = db.Quotations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<QuotationStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        // E1 — date-range/customer/product filters.
        if (dateFrom is { } df) qy = qy.Where(x => x.DocDate >= df);
        if (dateTo   is { } dt) qy = qy.Where(x => x.DocDate <= dt);
        if (customerId is { } cid) qy = qy.Where(x => x.CustomerId == cid);
        if (productId is { } pid) qy = qy.Where(x => x.Lines.Any(l => l.ProductId == pid));
        return await qy.OrderByDescending(x => x.QuotationId)
            .Select(x => new QuotationListItem(
                x.QuotationId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.ValidUntilDate, x.CustomerName, x.TotalAmount, x.ConvertedToSoId,
                x.CreatedViaApiKeyName, x.BusinessUnitId))
            .ToListAsync(ct);
    }

    public async Task<QuotationDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await db.Quotations.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct);
        return q is null ? null : new QuotationDetail(
            q.QuotationId, q.DocNo, q.Status.ToString(), q.DocDate, q.ValidUntilDate,
            q.CustomerId, q.CustomerName, q.BusinessUnitId, q.CurrencyCode,
            q.SubtotalAmount, q.VatAmount, q.TotalAmount, q.ShowWhtNote,
            q.ConvertedToSoId, q.Notes,
            q.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount,
                l.DiscountPercent, l.TaxCode, l.TaxCodeId)).ToList(),
            q.CreatedViaApiKeyName);
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
