using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// AR/AP sub-ledger suite (specs/subledgers.md) — AR Aging, Customer Statement, Vendor
/// Ledger, all sharing one reconciliation model. <c>JournalLine</c> carries no
/// customer/vendor tag, so the party-level subledger is sourced entirely from document
/// tables (TaxInvoice/Receipt/TaxAdjustmentNote for AR; VendorInvoice/PaymentVoucherApplication
/// for AP) — never from GL. Control accounts (1130 AR / 2110 AP) are resolved from
/// <see cref="GlAccountsOptions"/> (never hardcoded) exactly as <c>GlPostingService</c> does,
/// except a missing account code resolves to a 0 control balance (no postings are possible to
/// an account that doesn't exist in the tenant's CoA) rather than throwing.
/// Multi-tenant: explicit <c>CompanyId == tenant.CompanyId</c> predicate (CLAUDE.md §4.7) on
/// every query, in addition to the EF global query filter.
/// </summary>
public sealed class SubledgerReportService(
    AccountingDbContext db, ITenantContext tenant, IOptions<GlAccountsOptions> accounts)
    : ISubledgerReportService
{
    /// <summary>One AR or AP movement against a party (customer/vendor), from a single
    /// source document. <c>Rank</c> is the fixed DocType tie-break for deterministic
    /// ordering when two movements share a DocDate.</summary>
    private sealed record PartyMovement(
        long PartyId, DateOnly DocDate, string DocType, int Rank, long SourceId,
        string DocNo, string? Description, decimal Debit, decimal Credit);

    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    // ── Control-account resolution + balance (mirrors GlPostingService.ResolveAccountIdAsync,
    //    except a missing code → null/0 balance instead of throwing — spec §"Control accounts") ──

    private async Task<long?> ResolveControlAccountIdAsync(string code, CancellationToken ct) =>
        await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.CompanyId == tenant.CompanyId && a.AccountCode == code)
            .Select(a => (long?)a.AccountId)
            .FirstOrDefaultAsync(ct);

    private async Task<decimal> ControlAccountBalanceAsync(
        long? accountId, DateOnly asOf, bool arSign, CancellationToken ct)
    {
        if (accountId is null) return 0m;
        // Server-side aggregation (single SUM, no line materialization) — AR: 1130 DR-normal;
        // AP: 2110 CR-normal. SumAsync on an empty decimal sequence returns 0m.
        var query =
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            where j.CompanyId == tenant.CompanyId && j.Status == DocumentStatus.Posted
                  && j.DocDate <= asOf && l.AccountId == accountId.Value
            select arSign ? l.DebitAmount - l.CreditAmount : l.CreditAmount - l.DebitAmount;
        return await query.SumAsync(ct);
    }

    // ── Document-derived AR/AP movements (enumerated per GlPostingService — see spec
    //    §"What posts to the control accounts"). partyId==null → ALL parties (reconciliation). ──

    private async Task<List<PartyMovement>> ArMovementsAsync(long? customerId, CancellationToken ct)
    {
        var tiRows = await db.TaxInvoices.AsNoTracking()
            .Where(t => t.CompanyId == tenant.CompanyId && t.Status == DocumentStatus.Posted
                     && (customerId == null || t.CustomerId == customerId))
            .Select(t => new PartyMovement(
                t.CustomerId, t.DocDate, "TaxInvoice", 0, t.TaxInvoiceId, t.DocNo ?? "", null,
                t.TotalAmount, 0m))
            .ToListAsync(ct);

        // One row per Receipt (not per application) — Credit = the portion of the receipt
        // that actually clears AR (sum of TI-linked applications only; DO/BillingNote
        // applications recognize revenue at receipt and never touch 1130 — GlPostingService
        // .PostReceiptAsync). AppliedAmount already includes any WHT withheld on that
        // application (cash + WHT = sum(applied), Sprint 8.6), so it ties to the AR credit
        // GlPostingService actually posts — the same value TaxInvoice.AmountPaid accumulates.
        var receiptRaw = await db.Receipts.AsNoTracking()
            .Where(r => r.CompanyId == tenant.CompanyId && r.Status == DocumentStatus.Posted
                     && (customerId == null || r.CustomerId == customerId))
            .Select(r => new
            {
                r.CustomerId, r.ReceiptId, r.DocDate, r.DocNo,
                Applied = r.Applications.Where(a => a.TaxInvoiceId != null).Sum(a => a.AppliedAmount),
            })
            .ToListAsync(ct);
        var rcRows = receiptRaw.Where(x => x.Applied > 0m)
            .Select(x => new PartyMovement(
                x.CustomerId, x.DocDate, "Receipt", 1, x.ReceiptId, x.DocNo ?? "", null, 0m, x.Applied));

        // CN reverses AR (Credit); DN increases AR (Debit) — sign per GlPostingService
        // .PostTaxAdjustmentNoteAsync.
        var noteRaw = await db.TaxAdjustmentNotes.AsNoTracking()
            .Where(n => n.CompanyId == tenant.CompanyId && n.Status == DocumentStatus.Posted
                     && (customerId == null || n.CustomerId == customerId))
            .Select(n => new { n.CustomerId, n.NoteId, n.DocDate, n.DocNo, n.NoteType, n.TotalAmount, n.Reason })
            .ToListAsync(ct);
        var noteRows = noteRaw.Select(n => n.NoteType == TaxAdjustmentNoteType.Credit
            ? new PartyMovement(n.CustomerId, n.DocDate, "CreditNote", 2, n.NoteId, n.DocNo ?? "", n.Reason, 0m, n.TotalAmount)
            : new PartyMovement(n.CustomerId, n.DocDate, "DebitNote", 2, n.NoteId, n.DocNo ?? "", n.Reason, n.TotalAmount, 0m));

        return tiRows.Concat(rcRows).Concat(noteRows).ToList();
    }

    private async Task<List<PartyMovement>> ApMovementsAsync(long? vendorId, CancellationToken ct)
    {
        var viRows = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.CompanyId == tenant.CompanyId && v.Status == DocumentStatus.Posted
                     && (vendorId == null || v.VendorId == vendorId))
            .Select(v => new PartyMovement(
                v.VendorId, v.DocDate, "VendorInvoice", 0, v.VendorInvoiceId, v.DocNo ?? "", null,
                0m, v.TotalAmount))
            .ToListAsync(ct);

        // PaymentVoucherApplication is the authoritative AP-settle source (populated 1:1 with
        // PaymentVoucher.VendorInvoiceId at PV POST — PaymentVoucherService.PostAsync — with
        // AppliedAmount == the exact amount GlPostingService debits against 2110). Party is
        // resolved through the linked VendorInvoice (not pv.VendorId) so it always matches the
        // invoice actually being settled.
        var pvRows = await db.PaymentVoucherApplications.AsNoTracking()
            .Join(db.PaymentVouchers.AsNoTracking(), a => a.PaymentVoucherId, pv => pv.PaymentVoucherId,
                (a, pv) => new { a, pv })
            .Join(db.VendorInvoices.AsNoTracking(), x => x.a.VendorInvoiceId, vi => vi.VendorInvoiceId,
                (x, vi) => new { x.a, x.pv, vi.VendorId })
            .Where(x => x.pv.CompanyId == tenant.CompanyId && x.pv.Status == DocumentStatus.Posted
                     && (vendorId == null || x.VendorId == vendorId))
            .Select(x => new PartyMovement(
                x.VendorId, x.pv.DocDate, "PaymentVoucher", 1, x.pv.PaymentVoucherId, x.pv.DocNo ?? "", null,
                x.a.AppliedAmount, 0m))
            .ToListAsync(ct);

        return viRows.Concat(pvRows).ToList();
    }

    // ── Shared reconciliation (spec §"Design — reconciliation model") ──────────────────────

    private async Task<SubledgerReconciliation> ArReconciliationAsync(DateOnly asOf, CancellationToken ct)
    {
        var code = accounts.Value.ArAccount;
        var accountId = await ResolveControlAccountIdAsync(code, ct);
        var controlBalance = await ControlAccountBalanceAsync(accountId, asOf, arSign: true, ct);
        var movements = await ArMovementsAsync(null, ct);
        var subLedgerTotal = movements.Where(m => m.DocDate <= asOf).Sum(m => m.Debit - m.Credit);
        var diff = controlBalance - subLedgerTotal;
        return new SubledgerReconciliation(code, controlBalance, subLedgerTotal, diff, diff == 0m);
    }

    // WP3 — public (implements ISubledgerReportService) so ApAgingService can reuse it for the
    // AP-aging tie-out banner, mirroring the one AR-aging already shows (chief01.md MED finding).
    public async Task<SubledgerReconciliation> ApReconciliationAsync(DateOnly asOf, CancellationToken ct)
    {
        var code = accounts.Value.ApAccount;
        var accountId = await ResolveControlAccountIdAsync(code, ct);
        var controlBalance = await ControlAccountBalanceAsync(accountId, asOf, arSign: false, ct);
        var movements = await ApMovementsAsync(null, ct);
        var subLedgerTotal = movements.Where(m => m.DocDate <= asOf).Sum(m => m.Credit - m.Debit);
        var diff = controlBalance - subLedgerTotal;
        return new SubledgerReconciliation(code, controlBalance, subLedgerTotal, diff, diff == 0m);
    }

    // ── AR Aging (mirrors ApAgingService — snapshot basis, locked decision #2) ─────────────

    public async Task<ArAgingReport> ArAgingAsync(DateOnly asOf, long? customerId, CancellationToken ct)
    {
        EnsureAuth();

        var q = db.TaxInvoices.AsNoTracking()
            .Where(t => t.CompanyId == tenant.CompanyId && t.Status == DocumentStatus.Posted
                     && t.PaymentStatus != "PAID");
        if (customerId is { } cid) q = q.Where(t => t.CustomerId == cid);

        var tiRaw = await q.Select(t => new
        {
            t.CustomerId, t.CustomerName, t.CustomerTaxId, t.DocDate,
            Amount = t.TotalAmount - t.AmountPaid,
        }).ToListAsync(ct);

        // F-1 (2026-07-19 usage drive) — Credit/Debit Notes net a customer's AR balance but were
        // never pulled into this report (only TaxInvoices were queried above): a CN posted
        // against an already-fully-PAID TI leaves that TI excluded by PaymentStatus=="PAID"
        // while the CN itself still reduces AR, so a net-CREDIT customer vanished from the table
        // entirely and the visible table total disagreed with the control-account banner (which
        // ties out via the movement-based ArReconciliationAsync below, unaffected by this gap).
        // Bucketed by the note's OWN DocDate (same aging rule as TI); CN negative / DN positive,
        // mirroring ArMovementsAsync's sign convention (Credit reduces AR, Debit increases it).
        var noteQ = db.TaxAdjustmentNotes.AsNoTracking()
            .Where(n => n.CompanyId == tenant.CompanyId && n.Status == DocumentStatus.Posted);
        if (customerId is { } cid2) noteQ = noteQ.Where(n => n.CustomerId == cid2);

        var noteRaw = await noteQ.Select(n => new
        {
            n.CustomerId, n.CustomerName, n.CustomerTaxId, n.DocDate,
            Amount = n.NoteType == TaxAdjustmentNoteType.Credit ? -n.TotalAmount : n.TotalAmount,
        }).ToListAsync(ct);

        var customerCodes = await db.Customers.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new { c.CustomerId, c.CustomerCode })
            .ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);

        // Grouped by CustomerId alone (not name/taxId — a note's snapshot could in principle
        // differ from a TI's if the customer master changed between the two doc dates, which
        // would otherwise split one customer into two rows); display name/taxId from either
        // source, whichever is present.
        var rows = tiRaw.Concat(noteRaw)
            .GroupBy(x => x.CustomerId)
            .Select(g =>
            {
                var first = g.First();
                decimal cur = 0m, b3160 = 0m, b6190 = 0m, over90 = 0m;
                foreach (var x in g)
                {
                    var age = asOf.DayNumber - x.DocDate.DayNumber;
                    if (age <= 30) cur += x.Amount;              // includes future-dated (age < 0)
                    else if (age <= 60) b3160 += x.Amount;
                    else if (age <= 90) b6190 += x.Amount;
                    else over90 += x.Amount;
                }
                return new ArAgingRow(
                    g.Key, customerCodes.GetValueOrDefault(g.Key, ""), first.CustomerName,
                    first.CustomerTaxId, cur, b3160, b6190, over90, cur + b3160 + b6190 + over90);
            })
            // F-1 — include net-CREDIT customers too (negative Total), not just net-debit ones;
            // only a customer with a genuinely zero net balance across every bucket is dropped.
            .Where(r => r.Total != 0m)
            .OrderByDescending(r => r.Total)
            .ToList();

        var totals = new ArAgingRow(0, "", "TOTAL", null,
            rows.Sum(r => r.Current), rows.Sum(r => r.Bucket31To60), rows.Sum(r => r.Bucket61To90),
            rows.Sum(r => r.BucketOver90), rows.Sum(r => r.Total));

        var reconciliation = await ArReconciliationAsync(asOf, ct);
        return new ArAgingReport(asOf, tenant.CompanyId, rows, totals, reconciliation);
    }

    // ── Customer Statement ──────────────────────────────────────────────────────────────────

    public async Task<CustomerStatement> CustomerStatementAsync(
        long customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        EnsureAuth();

        var customer = await db.Customers.AsNoTracking()
                .Where(c => c.CompanyId == tenant.CompanyId && c.CustomerId == customerId)
                .Select(c => new { c.CustomerId, c.CustomerCode, c.NameTh })
                .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("customer.not_found",
                $"Customer {customerId} not found (or belongs to another company).");

        var movements = await ArMovementsAsync(customerId, ct);
        var opening = movements.Where(m => m.DocDate < fromDate).Sum(m => m.Debit - m.Credit);

        var inRange = movements.Where(m => m.DocDate >= fromDate && m.DocDate <= toDate)
            .OrderBy(m => m.DocDate).ThenBy(m => m.Rank).ThenBy(m => m.SourceId).ToList();

        var lines = new List<CustomerStatementLine>();
        var running = opening;
        decimal totalDebit = 0m, totalCredit = 0m;
        foreach (var m in inRange)
        {
            totalDebit += m.Debit; totalCredit += m.Credit;
            running += m.Debit - m.Credit;
            lines.Add(new CustomerStatementLine(m.DocDate, m.DocType, m.DocNo, m.Description, m.Debit, m.Credit, running));
        }
        var closing = opening + totalDebit - totalCredit;

        var reconciliation = await ArReconciliationAsync(toDate, ct);
        return new CustomerStatement(
            customer.CustomerId, customer.CustomerCode, customer.NameTh,
            fromDate, toDate, opening, lines, totalDebit, totalCredit, closing, reconciliation);
    }

    // ── Vendor Ledger (AP analog — payable-positive, Credit−Debit orientation) ─────────────

    public async Task<VendorLedger> VendorLedgerAsync(
        long vendorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        EnsureAuth();

        var vendor = await db.Vendors.AsNoTracking()
                .Where(v => v.CompanyId == tenant.CompanyId && v.VendorId == vendorId)
                .Select(v => new { v.VendorId, v.VendorCode, v.NameTh })
                .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("vendor.not_found",
                $"Vendor {vendorId} not found (or belongs to another company).");

        var movements = await ApMovementsAsync(vendorId, ct);
        var opening = movements.Where(m => m.DocDate < fromDate).Sum(m => m.Credit - m.Debit);

        var inRange = movements.Where(m => m.DocDate >= fromDate && m.DocDate <= toDate)
            .OrderBy(m => m.DocDate).ThenBy(m => m.Rank).ThenBy(m => m.SourceId).ToList();

        var lines = new List<VendorLedgerLine>();
        var running = opening;
        decimal totalDebit = 0m, totalCredit = 0m;
        foreach (var m in inRange)
        {
            totalDebit += m.Debit; totalCredit += m.Credit;
            running += m.Credit - m.Debit;
            lines.Add(new VendorLedgerLine(m.DocDate, m.DocType, m.DocNo, m.Description, m.Debit, m.Credit, running));
        }
        var closing = opening + totalCredit - totalDebit;

        var reconciliation = await ApReconciliationAsync(toDate, ct);
        return new VendorLedger(
            vendor.VendorId, vendor.VendorCode, vendor.NameTh,
            fromDate, toDate, opening, lines, totalDebit, totalCredit, closing, reconciliation);
    }
}
