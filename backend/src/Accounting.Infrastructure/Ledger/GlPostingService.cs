using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Expense;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
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

    /// <summary>R1/C6 (WP-1) — non-VAT revenue+AR accrual at Invoice issue. Mirrors
    /// <see cref="PostTaxInvoiceAsync"/> exactly. A non-VAT company's Invoice is its ONLY
    /// sales document (ม.86/4 blocks the Tax Invoice), so it is the revenue-recognition
    /// point. A VAT company's BillingNote groups already-accrued Tax Invoices and must
    /// NEVER post here — that would double-count AR and revenue.</summary>
    public async Task<long> PostBillingNoteAsync(long billingNoteId, CancellationToken ct)
    {
        var bn = await _db.BillingNotes.FirstOrDefaultAsync(b => b.BillingNoteId == billingNoteId, ct)
            ?? throw new DomainException("gl.bn_missing", $"Invoice {billingNoteId} not found for GL posting.");

        if (bn.VatAmount != 0m)
            throw new DomainException("gl.bn_vat_unexpected",
                $"Invoice {bn.DocNo} carries VAT {bn.VatAmount}; a non-VAT invoice must not.");

        var ar    = await ResolveAccountIdAsync(bn.CompanyId, _accounts.ArAccount, ct);
        var sales = await ResolveAccountIdAsync(bn.CompanyId, _accounts.SalesAccount, ct);

        var lines = new List<JournalLine>
        {
            new() { LineNo = 1, AccountId = ar,    DebitAmount = bn.TotalAmount, CreditAmount = 0m,
                    Description = $"AR {bn.DocNo}" },
            new() { LineNo = 2, AccountId = sales, DebitAmount = 0m, CreditAmount = bn.TotalAmount,
                    Description = $"Sales {bn.DocNo}" },
        };

        return await BuildAndPostAsync(
            bn.CompanyId, bn.BranchId, bn.DocDate, $"IV {bn.DocNo}", bn.DocNo, lines, ct,
            businessUnitId: bn.BusinessUnitId);
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
            ? await _db.TaxInvoices.AsNoTracking() // ponytail: read-only BU lookup
                .Where(t => tiIds.Contains(t.TaxInvoiceId))
                .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.BusinessUnitId, ct)
            : new Dictionary<long, int?>();
        // R1/C6 (WP-1) — pre-load every applied BillingNote once (mirrors tiBu above).
        // JournalEntryId != null means the Invoice already accrued AR+revenue at Issue
        // (PostBillingNoteAsync); this receipt then only CLEARS that AR (Cr AR, not Cr
        // Sales — crediting Sales again would double-count revenue). JournalEntryId ==
        // null is a pre-fix Invoice that never accrued: the receipt is still its
        // revenue-recognition point (transition safety — self-heals once WP-2 backfills it).
        var bnIds = rc.Applications.Where(a => a.BillingNoteId.HasValue)
            .Select(a => a.BillingNoteId!.Value).ToList();
        var bnInfo = bnIds.Count > 0
            ? await _db.BillingNotes.AsNoTracking()
                .Where(b => bnIds.Contains(b.BillingNoteId))
                .ToDictionaryAsync(b => b.BillingNoteId, b => (b.JournalEntryId, b.BusinessUnitId), ct)
            : new Dictionary<long, (long? JournalEntryId, int? BusinessUnitId)>();
        // Revenue recognized at RECEIPT (Cr Sales, cash basis, ม.86 — non-VAT issues no TI)
        // applies only to a DO-applied/standalone receipt or a not-yet-accrued Invoice —
        // never to an accrued Invoice, which recognized revenue at Issue instead.
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
            else if (a.BillingNoteId is { } bid
                     && bnInfo.TryGetValue(bid, out var bnI) && bnI.JournalEntryId is not null)
                // R1/C6 — accrued Invoice: the receipt only clears the AR that
                // PostBillingNoteAsync already posted at Issue.
                lines.Add(new JournalLine
                {
                    LineNo = ln++, AccountId = ar, DebitAmount = 0m,
                    CreditAmount = a.AppliedAmount,
                    Description = $"AR settle {rc.DocNo}",
                    BusinessUnitId = bnI.BusinessUnitId,
                });
            else // DO (cont.68) application, or a pre-fix Invoice (BillingNote.JournalEntryId
                 // == null, never accrued) — non-VAT, recognize revenue now (Cr Sales 4000,
                 // cash basis; no prior AR to settle).
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

            // Sprint 8.7 — self-withhold gross-up (Scenario A/B), VI-settlement path.
            // Mirrors the standalone else-branch below: a VI-linked PV for a foreign
            // no-Thai-VAT-D vendor still auto-locks self-withhold (CreateDraftAsync's
            // autoSelfWithhold), so the WHT-payable credit (line ~211) and the Cash/Bank
            // credit (= TotalPaid, NOT netted of WHT under self-withhold) fire same as
            // standalone — without this debit the JE was short by exactly WhtAmount
            // (gl.unbalanced, army B-rc F1).
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

    /// <summary>
    /// Cycle C (specs/expense-claims.md §3, Option A) — self-contained cash disbursement for an
    /// employee expense claim. NEW additive method mirroring <see cref="PostPaymentVoucherAsync"/>'s
    /// structure; calls the SAME private <see cref="BuildAndPostAsync"/> balance-check/JV-number/
    /// MarkPosted machinery. Touches zero PaymentVoucher/Vendor/WhtCertificate code.
    /// WHT: NONE — reimbursing an employee's out-of-pocket spend already had any WHT handled by
    /// the employee at the original purchase; no <c>WhtPayableAccount</c> line, no 50-ทวิ cert.
    /// </summary>
    public async Task<long> PostExpenseClaimAsync(long expenseClaimId, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.ExpenseClaimId == expenseClaimId, ct)
            ?? throw new DomainException("gl.ec_missing", $"Expense Claim {expenseClaimId} not found for GL posting.");

        var inputVat = await ResolveAccountIdAsync(claim.CompanyId, _accounts.InputVatAccount, ct);

        // Credit side: the selected bank account's OWN gl_cash_account_id for TRANSFER (an
        // employee claim may be paid from any configured bank account, unlike PV's single
        // company-wide GlAccountsOptions.BankAccount default), else the company cash account.
        long creditAccountId;
        if (string.Equals(claim.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
        {
            creditAccountId = await ResolveAccountIdAsync(claim.CompanyId, _accounts.CashAccount, ct);
        }
        else
        {
            var bankAccountId = claim.BankAccountId
                ?? throw new DomainException("gl.ec_bank_account_missing",
                    "TRANSFER payment requires a BankAccountId.");
            creditAccountId = await _db.BankAccounts.AsNoTracking()
                    .Where(b => b.BankAccountId == bankAccountId && b.CompanyId == claim.CompanyId)
                    .Select(b => (long?)b.GlCashAccountId)
                    .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("gl.ec_bank_account_not_found",
                    $"Bank account {bankAccountId} not found.");
        }

        var lines = new List<JournalLine>();
        var lineNo = 1;
        var recoverableVatTotal = 0m;
        foreach (var l in claim.Lines.OrderBy(l => l.LineNo))
        {
            // Non-recoverable VAT is folded into the expense debit (ภาษีซื้อต้องห้าม), exactly
            // like PostPaymentVoucherAsync's IsRecoverableVat split above.
            var expenseDebit = l.IsRecoverableVat ? l.Amount : l.Amount + l.VatAmount;
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = l.ExpenseAccountId,
                DebitAmount = expenseDebit, CreditAmount = 0m,
                Description = l.Description,
            });
            if (l.IsRecoverableVat && l.VatAmount > 0m)
                recoverableVatTotal += l.VatAmount;
        }
        if (recoverableVatTotal > 0m)
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = inputVat,
                DebitAmount = recoverableVatTotal, CreditAmount = 0m,
                Description = $"Input VAT {claim.DocNo}",
            });

        // WHT: NONE. No WhtPayableAccount line is ever added here — an expense-claim
        // reimbursement is not a withholding event under Thai law.
        lines.Add(new JournalLine
        {
            LineNo = lineNo, AccountId = creditAccountId,
            DebitAmount = 0m, CreditAmount = claim.TotalAmount,
            Description = $"Cash/Bank {claim.DocNo}",
        });

        // No claim.DocDate column (specs/expense-claims.md §1 DDL) — the JE posts at
        // server-today (FOOTGUN 5), same as every other poster's post-date.
        var postDate = _clock.TodayInBangkok();
        return await BuildAndPostAsync(
            claim.CompanyId, claim.BranchId, postDate, $"EX {claim.DocNo}", claim.DocNo, lines, ct,
            businessUnitId: claim.BusinessUnitId);
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

    public async Task<long> PostPayrollRunAsync(long payrollRunId, CancellationToken ct)
    {
        var run = await _db.PayrollRuns
                .FirstOrDefaultAsync(r => r.PayrollRunId == payrollRunId, ct)
            ?? throw new DomainException("gl.payroll_missing", $"Payroll run {payrollRunId} not found for GL posting.");

        var salaryExp = await ResolveAccountIdAsync(run.CompanyId, _accounts.SalaryExpenseAccount, ct);
        var erSsoExp  = await ResolveAccountIdAsync(run.CompanyId, _accounts.EmployerSsoExpenseAccount, ct);
        var pitPay    = await ResolveAccountIdAsync(run.CompanyId, _accounts.PitPayableAccount, ct);
        var ssoPay    = await ResolveAccountIdAsync(run.CompanyId, _accounts.SsoPayableAccount, ct);
        var netPay    = await ResolveAccountIdAsync(run.CompanyId, _accounts.NetWagesPayableAccount, ct);
        var otherPay  = await ResolveAccountIdAsync(run.CompanyId, _accounts.OtherDeductionsPayableAccount, ct);

        var gross = run.TotalGrossTaxable + run.TotalGrossNonTaxable;

        var lines = new List<JournalLine>
        {
            new() { LineNo = 1, AccountId = salaryExp, DebitAmount = gross,                CreditAmount = 0m, Description = $"Salaries {run.DocNo}" },
        };
        var ln = 2;
        if (run.TotalSsoEmployer > 0m)
            lines.Add(new JournalLine { LineNo = ln++, AccountId = erSsoExp, DebitAmount = run.TotalSsoEmployer, CreditAmount = 0m, Description = $"Employer SSO {run.DocNo}" });
        if (run.TotalPit > 0m)
            lines.Add(new JournalLine { LineNo = ln++, AccountId = pitPay, DebitAmount = 0m, CreditAmount = run.TotalPit, Description = $"PIT payable (ภ.ง.ด.1) {run.DocNo}" });
        if (run.TotalSsoEmployee + run.TotalSsoEmployer > 0m)
            lines.Add(new JournalLine { LineNo = ln++, AccountId = ssoPay, DebitAmount = 0m, CreditAmount = run.TotalSsoEmployee + run.TotalSsoEmployer, Description = $"SSO payable {run.DocNo}" });
        lines.Add(new JournalLine { LineNo = ln++, AccountId = netPay, DebitAmount = 0m, CreditAmount = run.TotalNet, Description = $"Net wages payable {run.DocNo}" });
        if (run.TotalOtherDeductions > 0m)
            lines.Add(new JournalLine { LineNo = ln, AccountId = otherPay, DebitAmount = 0m, CreditAmount = run.TotalOtherDeductions, Description = $"Other deductions payable {run.DocNo}" });

        return await BuildAndPostAsync(
            run.CompanyId, run.BranchId, run.PayDate, $"PR {run.DocNo}", run.DocNo, lines, ct);
    }

    /// <summary>Year-end closing (specs/year-end-closing.md D3/B4). Mirrors
    /// <see cref="BuildAndPostAsync"/>'s balance-validate + JV-number + MarkPosted machinery,
    /// but takes ALREADY-RESOLVED AccountIds (no ResolveAccountIdAsync) and deliberately does
    /// NOT call EnsureOpenAsync — posting into an already-closed fiscal year is the point.</summary>
    public async Task<long> PostClosingEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description,
        bool isClosingEntry, long? reversalOfId,
        IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines,
        CancellationToken ct)
    {
        var journalLines = lines.Select((l, i) => new JournalLine
        {
            LineNo = i + 1, AccountId = l.AccountId,
            DebitAmount = l.Debit, CreditAmount = l.Credit,
        }).ToList();

        var totalD = journalLines.Sum(l => l.DebitAmount);
        var totalC = journalLines.Sum(l => l.CreditAmount);
        if (totalD != totalC || totalD == 0m)
            throw new DomainException("gl.unbalanced",
                $"GL post unbalanced: D={totalD} C={totalC} for {description}.");

        var now = _clock.UtcNow;

        var je = new JournalEntry
        {
            CompanyId      = companyId,
            BranchId       = branchId,
            PrefixCode     = JvPrefix,
            DocDate        = docDate,
            PostingDate    = docDate,
            Description    = description,
            TotalDebit     = totalD,
            TotalCredit    = totalC,
            Lines          = journalLines,
            IsClosingEntry = isClosingEntry,
            ReversalOfId   = reversalOfId,
        };
        _db.JournalEntries.Add(je);

        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            _db,
            c => _numbers.NextAsync(companyId, JvPrefix, subPrefix: null, docDate, c),
            (v, first) => { if (first) je.MarkPosted(v.Value, _tenant.UserId ?? 0, now); else je.DocNo = v.Value; },
            ct);
        return je.JournalId;
    }

    /// <summary>Bank reconciliation (specs/bank-reconciliation.md D7) — thin public wrapper over
    /// the same private <see cref="BuildAndPostAsync"/> balance-check + JV-number + MarkPosted
    /// machinery every other poster uses, taking ALREADY-RESOLVED AccountIds. Never touches
    /// <see cref="PostClosingEntryAsync"/> or any existing poster.</summary>
    public async Task<long> PostManualEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description, string? reference,
        IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines, CancellationToken ct)
    {
        var journalLines = lines.Select((l, i) => new JournalLine
        {
            LineNo = i + 1, AccountId = l.AccountId, DebitAmount = l.Debit, CreditAmount = l.Credit,
        }).ToList();

        return await BuildAndPostAsync(companyId, branchId, docDate, description, reference, journalLines, ct);
    }

    /// <summary>Manual JV overload — same private BuildAndPostAsync engine as every other poster
    /// (balance check, JV numbering, MarkPosted). NOT a second posting path: the only difference
    /// from the 3-tuple overload above is that the caller may set a per-line description and BU.</summary>
    public async Task<long> PostManualEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description, string? reference,
        IReadOnlyList<ManualJvLine> lines, CancellationToken ct)
    {
        var journalLines = lines.Select((l, i) => new JournalLine
        {
            LineNo = i + 1, AccountId = l.AccountId,
            DebitAmount = l.Debit, CreditAmount = l.Credit,
            Description = l.Description, BusinessUnitId = l.BusinessUnitId,
        }).ToList();
        return await BuildAndPostAsync(companyId, branchId, docDate, description, reference, journalLines, ct);
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

        var now = _clock.UtcNow;

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

        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        await NumberedDocumentWriter.AllocateAndSaveAsync(
            _db,
            c => _numbers.NextAsync(companyId, JvPrefix, subPrefix: null, docDate, c),
            (v, first) => { if (first) je.MarkPosted(v.Value, _tenant.UserId ?? 0, now); else je.DocNo = v.Value; },
            ct);
        return je.JournalId;
    }

    private async Task<long> ResolveAccountIdAsync(int companyId, string code, CancellationToken ct)
    {
        var account = await _db.ChartOfAccounts
            .AsNoTracking() // ponytail: read-only lookup — no change tracking needed
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct);
        if (account is null)
            throw new DomainException("gl.account_missing",
                $"Configured GL account '{code}' is missing from chart_of_accounts for company {companyId}. " +
                "Seed the CoA or update GlAccounts in appsettings.");
        return account.AccountId;
    }
}
