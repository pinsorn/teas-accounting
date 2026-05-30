using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Ledger;

/// <summary>
/// Auto-posts a balanced JournalEntry from a posted fiscal document.
/// Lookups resolve account codes from <see cref="GlAccountsOptions"/> into ChartOfAccount IDs
/// once per call and cache for the lifetime of the request. Throws <see cref="DomainException"/>
/// if any configured account code is missing from the company's CoA — surfaces seed gaps early.
/// </summary>
public sealed class GlPostingService : IGlPostingService
{
    private const string JvPrefix = "JV";

    private readonly AccountingDbContext     _db;
    private readonly ITenantContext          _tenant;
    private readonly IClock                  _clock;
    private readonly INumberSequenceService  _numbers;
    private readonly GlAccountsOptions       _accounts;

    public GlPostingService(
        AccountingDbContext db,
        ITenantContext tenant,
        IClock clock,
        INumberSequenceService numbers,
        IOptions<GlAccountsOptions> accounts)
    {
        _db = db; _tenant = tenant; _clock = clock; _numbers = numbers;
        _accounts = accounts.Value;
    }

    public async Task<long> PostTaxInvoiceAsync(long taxInvoiceId, CancellationToken ct)
    {
        var ti = await _db.TaxInvoices.Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.TaxInvoiceId == taxInvoiceId, ct)
            ?? throw new DomainException("gl.ti_missing", $"Tax Invoice {taxInvoiceId} not found for GL posting.");

        var ar    = await ResolveAccountIdAsync(ti.CompanyId, _accounts.ArAccount, ct);
        var sales = await ResolveAccountIdAsync(ti.CompanyId, _accounts.SalesAccount, ct);
        var ovat  = await ResolveAccountIdAsync(ti.CompanyId, _accounts.OutputVatAccount, ct);

        var net = ti.SubtotalAmount;
        var vat = ti.TaxAmount;
        var gross = ti.TotalAmount;

        var lines = new List<JournalLine>
        {
            new() { LineNo = 1, AccountId = ar,    DebitAmount = gross, CreditAmount = 0m, Description = $"AR {ti.DocNo}" },
            new() { LineNo = 2, AccountId = sales, DebitAmount = 0m,    CreditAmount = net,   Description = $"Sales {ti.DocNo}" },
        };
        if (vat > 0m)
            lines.Add(new JournalLine { LineNo = 3, AccountId = ovat, DebitAmount = 0m, CreditAmount = vat, Description = $"Output VAT {ti.DocNo}" });

        return await BuildAndPostAsync(
            ti.CompanyId, ti.BranchId, ti.DocDate, $"TI {ti.DocNo}", ti.DocNo, lines, ct,
            businessUnitId: ti.BusinessUnitId);
    }

    public async Task<long> PostReceiptAsync(long receiptId, CancellationToken ct)
    {
        var rc = await _db.Receipts.Include(r => r.Applications)
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId, ct)
            ?? throw new DomainException("gl.rc_missing", $"Receipt {receiptId} not found for GL posting.");

        var ar         = await ResolveAccountIdAsync(rc.CompanyId, _accounts.ArAccount, ct);
        var debitCode  = rc.PaymentMethod == PaymentMethod.Cash ? _accounts.CashAccount : _accounts.BankAccount;
        var debitAcct  = await ResolveAccountIdAsync(rc.CompanyId, debitCode, ct);

        // Sprint 8.6 — AR-side WHT: customer withheld → bank receives only
        // cash_received; the withheld portion is an asset (1180 WHT-Receivable,
        // ภ.ง.ด.50 credit). cash_received + wht = sum(applied) keeps the JV balanced.
        var hasWht   = rc.WhtAmount > 0m;
        var cashDr   = hasWht ? rc.CashReceived : rc.Amount;

        // BU of each applied TI — the AR-clearing credit line inherits the TI's BU
        // so cross-BU receipts split AR by stream; cash is fungible (BU = NULL).
        var tiIds = rc.Applications.Where(a => a.TaxInvoiceId.HasValue)
            .Select(a => a.TaxInvoiceId!.Value).ToList();
        var tiBu = tiIds.Count > 0
            ? await _db.TaxInvoices.Where(t => tiIds.Contains(t.TaxInvoiceId))
                .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.BusinessUnitId, ct)
            : new Dictionary<long, int?>();
        // Non-VAT receipts (DO-applied / standalone) have no prior AR — they recognize
        // revenue at receipt (Cr Sales), cash basis (ม.86 — non-VAT issues no TI).
        var salesAcct = await ResolveAccountIdAsync(rc.CompanyId, _accounts.SalesAccount, ct);

        var lines = new List<JournalLine>
        {
            new() { LineNo = 1, AccountId = debitAcct, DebitAmount = cashDr,
                    CreditAmount = 0m, Description = $"Receipt {rc.DocNo}" }, // cash — BU NULL
        };
        var ln = 2;
        if (hasWht)
        {
            var whtRecv = await ResolveAccountIdAsync(rc.CompanyId, _accounts.WhtReceivableAccount, ct);
            lines.Add(new JournalLine
            {
                LineNo = ln++, AccountId = whtRecv, DebitAmount = rc.WhtAmount,
                CreditAmount = 0m, Description = $"WHT receivable {rc.DocNo}",
                BusinessUnitId = rc.BusinessUnitId,   // header BU; NULL if cross-BU
            });
        }
        foreach (var a in rc.Applications)
        {
            if (a.TaxInvoiceId is { } tid)
                lines.Add(new JournalLine
                {
                    LineNo = ln++, AccountId = ar, DebitAmount = 0m,
                    CreditAmount = a.AppliedAmount,
                    Description = $"AR settle {rc.DocNo}",
                    BusinessUnitId = tiBu.GetValueOrDefault(tid),
                });
            else // DO (cont.68) or Invoice/BillingNote (cont.69) application — non-VAT,
                 // recognize revenue now (Cr Sales 4000, cash basis; no prior AR to settle).
                lines.Add(new JournalLine
                {
                    LineNo = ln++, AccountId = salesAcct, DebitAmount = 0m,
                    CreditAmount = a.AppliedAmount,
                    Description = $"Sales (non-VAT receipt) {rc.DocNo}",
                    BusinessUnitId = rc.BusinessUnitId,
                });
        }
        // Standalone non-VAT receipt — no applications; recognize the full amount as revenue.
        if (rc.Applications.Count == 0)
            lines.Add(new JournalLine
            {
                LineNo = ln++, AccountId = salesAcct, DebitAmount = 0m,
                CreditAmount = rc.Amount,
                Description = $"Sales (non-VAT receipt) {rc.DocNo}",
                BusinessUnitId = rc.BusinessUnitId,
            });

        // businessUnitId: null — lines carry their own BU; cash line stays NULL.
        return await BuildAndPostAsync(
            rc.CompanyId, rc.BranchId, rc.DocDate, $"RC {rc.DocNo}", rc.DocNo, lines, ct);
    }

    public async Task<long> PostPaymentVoucherAsync(long paymentVoucherId, CancellationToken ct)
    {
        var pv = await _db.PaymentVouchers.Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.PaymentVoucherId == paymentVoucherId, ct)
            ?? throw new DomainException("gl.pv_missing", $"PV {paymentVoucherId} not found for GL posting.");

        var inputVat  = await ResolveAccountIdAsync(pv.CompanyId, _accounts.InputVatAccount, ct);
        var whtPay    = await ResolveAccountIdAsync(pv.CompanyId, _accounts.WhtPayableAccount, ct);
        var debitCode = pv.PaymentMethod == PaymentMethod.Cash ? _accounts.CashAccount : _accounts.BankAccount;
        var bankCash  = await ResolveAccountIdAsync(pv.CompanyId, debitCode, ct);

        var lines = new List<JournalLine>();
        var lineNo = 1;
        if (pv.VendorInvoiceId is not null)
        {
            // Settling a Vendor Invoice: expense + input-VAT already hit the books at VI
            // POST. The PV just clears the payable — Dr AP for the accrued gross.
            var ap = await ResolveAccountIdAsync(pv.CompanyId, _accounts.ApAccount, ct);
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = ap,
                DebitAmount = pv.SubtotalAmount + pv.VatAmount, CreditAmount = 0m,
                Description = $"AP settle VI via {pv.DocNo}",
            });
        }
        else
        {
            // Standalone PV (no VI): expense hits the books here (recoverable VAT split).
            foreach (var l in pv.Lines)
            {
                var expenseGross = l.IsRecoverableVat ? l.Amount : l.Amount + l.VatAmount;
                lines.Add(new JournalLine
                {
                    LineNo = lineNo++, AccountId = l.ExpenseAccountId,
                    DebitAmount = expenseGross, CreditAmount = 0m,
                    Description = l.Description ?? $"Expense line {l.LineNo}",
                });
                if (l.IsRecoverableVat && l.VatAmount > 0m)
                {
                    lines.Add(new JournalLine
                    {
                        LineNo = lineNo++, AccountId = inputVat,
                        DebitAmount = l.VatAmount, CreditAmount = 0m,
                        Description = $"Input VAT {pv.DocNo}",
                    });
                }
            }

            // Sprint 8.7 — self-withhold gross-up (Scenario A/B): the WHT is OUR
            // cost (we pay vendor full + remit WHT to RD), so it's an extra
            // expense debit. Cash (Cr bank = TotalPaid = subtotal+vat) + WHT
            // payable already balance it. Posted to the first line's account.
            if (pv.SelfWithholdMode && pv.WhtAmount > 0m)
            {
                lines.Add(new JournalLine
                {
                    LineNo = lineNo++, AccountId = pv.Lines.First().ExpenseAccountId,
                    DebitAmount = pv.WhtAmount, CreditAmount = 0m,
                    Description = $"Self-withhold gross-up {pv.DocNo}",
                });
            }
        }
        if (pv.WhtAmount > 0m)
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = whtPay,
                DebitAmount = 0m, CreditAmount = pv.WhtAmount,
                Description = $"WHT payable {pv.DocNo}",
            });
        lines.Add(new JournalLine
        {
            LineNo = lineNo, AccountId = bankCash,
            DebitAmount = 0m, CreditAmount = pv.TotalPaid,
            Description = $"Cash/Bank {pv.DocNo}",
        });

        return await BuildAndPostAsync(
            pv.CompanyId, pv.BranchId, pv.DocDate, $"PV {pv.DocNo}", pv.DocNo, lines, ct,
            businessUnitId: pv.BusinessUnitId);   // cont.79 — stamp BU on every PV journal line
    }

    public async Task<long> PostVendorInvoiceAsync(long vendorInvoiceId, CancellationToken ct)
    {
        var vi = await _db.VendorInvoices.Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.VendorInvoiceId == vendorInvoiceId, ct)
            ?? throw new DomainException("gl.vi_missing", $"Vendor Invoice {vendorInvoiceId} not found for GL posting.");

        var inputVat = await ResolveAccountIdAsync(vi.CompanyId, _accounts.InputVatAccount, ct);
        var ap       = await ResolveAccountIdAsync(vi.CompanyId, _accounts.ApAccount, ct);

        var lines = new List<JournalLine>();
        var lineNo = 1;
        decimal apTotal = 0m;
        // is_recoverable_vat is the SNAPSHOT taken at draft (never re-resolved) — ม.82/5.
        foreach (var l in vi.Lines.OrderBy(l => l.LineNo))
        {
            // Sprint 8.7 — receipt-only / non-VAT / foreign-no-VAT-D vendor:
            // VAT can't be claimed, lump it into expense (ม.82/5 pattern).
            var recoverable = vi.HasInputVat && l.IsRecoverableVat;
            var expenseDebit = recoverable ? l.Amount : l.Amount + l.VatAmount;
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = l.ExpenseAccountId,
                DebitAmount = expenseDebit, CreditAmount = 0m,
                Description = l.Description,
            });
            if (recoverable && l.VatAmount > 0m)
                lines.Add(new JournalLine
                {
                    LineNo = lineNo++, AccountId = inputVat,
                    DebitAmount = l.VatAmount, CreditAmount = 0m,
                    Description = $"Input VAT {vi.DocNo}",
                });
            apTotal += l.Amount + l.VatAmount;
        }
        lines.Add(new JournalLine
        {
            LineNo = lineNo, AccountId = ap,
            DebitAmount = 0m, CreditAmount = apTotal,
            Description = $"AP {vi.DocNo} ({vi.VendorName})",
        });

        return await BuildAndPostAsync(
            vi.CompanyId, vi.BranchId, vi.DocDate, $"VI {vi.DocNo}", vi.DocNo, lines, ct,
            businessUnitId: vi.BusinessUnitId);   // cont.79 — stamp BU on every VI journal line
    }

    public async Task<long> PostTaxAdjustmentNoteAsync(long noteId, CancellationToken ct)
    {
        var note = await _db.TaxAdjustmentNotes.FirstOrDefaultAsync(n => n.NoteId == noteId, ct)
            ?? throw new DomainException("gl.note_missing", $"Note {noteId} not found for GL posting.");

        var ar    = await ResolveAccountIdAsync(note.CompanyId, _accounts.ArAccount, ct);
        var sret  = await ResolveAccountIdAsync(note.CompanyId, _accounts.SalesReturnAccount, ct);
        var ovat  = await ResolveAccountIdAsync(note.CompanyId, _accounts.OutputVatAccount, ct);

        // CN: reverse AR; DN: increase AR. Sign per NoteType.
        var sign  = note.NoteType == TaxAdjustmentNoteType.Credit ? -1m : +1m;
        var net   = sign * note.SubtotalAmount;
        var vat   = sign * note.TaxAmount;
        var gross = sign * note.TotalAmount;

        // Use absolute amounts split into debit/credit columns per direction.
        var lines = new List<JournalLine>();
        if (note.NoteType == TaxAdjustmentNoteType.Credit)
        {
            lines.Add(new JournalLine { LineNo = 1, AccountId = sret, DebitAmount = note.SubtotalAmount, CreditAmount = 0m, Description = $"Sales return {note.DocNo}" });
            if (note.TaxAmount > 0m)
                lines.Add(new JournalLine { LineNo = 2, AccountId = ovat, DebitAmount = note.TaxAmount, CreditAmount = 0m, Description = $"Output VAT reverse {note.DocNo}" });
            lines.Add(new JournalLine { LineNo = lines.Count + 1, AccountId = ar, DebitAmount = 0m, CreditAmount = note.TotalAmount, Description = $"AR reverse {note.DocNo}" });
        }
        else // DN — increases customer's bill
        {
            lines.Add(new JournalLine { LineNo = 1, AccountId = ar, DebitAmount = note.TotalAmount, CreditAmount = 0m, Description = $"AR add {note.DocNo}" });
            lines.Add(new JournalLine { LineNo = 2, AccountId = sret, DebitAmount = 0m, CreditAmount = note.SubtotalAmount, Description = $"Adjust {note.DocNo}" });
            if (note.TaxAmount > 0m)
                lines.Add(new JournalLine { LineNo = 3, AccountId = ovat, DebitAmount = 0m, CreditAmount = note.TaxAmount, Description = $"Output VAT {note.DocNo}" });
        }

        return await BuildAndPostAsync(
            note.CompanyId, note.BranchId, note.DocDate,
            $"{note.PrefixCode} {note.DocNo}", note.DocNo, lines, ct,
            businessUnitId: note.BusinessUnitId);
    }

    private async Task<long> BuildAndPostAsync(
        int companyId, int branchId, DateOnly docDate,
        string description, string? reference, List<JournalLine> lines, CancellationToken ct,
        int? businessUnitId = null)
    {
        // Sprint 8 — snapshot the source document's BU onto every line that didn't
        // already set one (Receipt cross-BU sets per-line BU itself, cash = null).
        foreach (var l in lines)
            l.BusinessUnitId ??= businessUnitId;

        var totalD = lines.Sum(l => l.DebitAmount);
        var totalC = lines.Sum(l => l.CreditAmount);
        if (totalD != totalC || totalD == 0m)
            throw new DomainException("gl.unbalanced",
                $"GL post unbalanced: D={totalD} C={totalC} for {description}.");

        var docNo = await _numbers.NextAsync(companyId, branchId, JvPrefix, subPrefix: null, docDate, ct);
        var now   = _clock.UtcNow;

        var je = new JournalEntry
        {
            CompanyId   = companyId,
            BranchId    = branchId,
            PrefixCode  = JvPrefix,
            DocDate     = docDate,
            PostingDate = docDate,
            Description = description,
            Reference   = reference,
            TotalDebit  = totalD,
            TotalCredit = totalC,
            Lines       = lines,
        };
        _db.JournalEntries.Add(je);
        je.MarkPosted(docNo.Value, _tenant.UserId ?? 0, now);

        await _db.SaveChangesAsync(ct);
        return je.JournalId;
    }

    private async Task<long> ResolveAccountIdAsync(int companyId, string code, CancellationToken ct)
    {
        var account = await _db.ChartOfAccounts
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct);
        if (account is null)
            throw new DomainException("gl.account_missing",
                $"Configured GL account '{code}' is missing from chart_of_accounts for company {companyId}. " +
                "Seed the CoA or update GlAccounts in appsettings.");
        return account.AccountId;
    }
}
