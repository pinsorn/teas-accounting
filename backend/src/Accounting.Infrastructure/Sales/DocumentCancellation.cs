using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

/// <summary>cancel-reissue spec 3.3 - mechanics shared by the TI / Receipt / Invoice cancel paths.</summary>
internal static class DocumentCancellation
{
    /// <summary>glDate(d) = d when its month is open, else today; today must itself be open (period.closed).</summary>
    public static async Task<DateOnly> ResolveGlDateAsync(
        IPeriodCloseService period, IClock clock, DateOnly docDate, CancellationToken ct)
    {
        var gl = await period.IsOpenAsync(docDate.Year, docDate.Month, ct) ? docDate : clock.TodayInBangkok();
        await period.EnsureOpenAsync(gl, ct);
        return gl;
    }

    /// <summary>The unreversed posting JE of a document: the stored id when present, else lookup by
    /// Reference = DocNo and Description = prefix + DocNo (legacy docs stored no id).</summary>
    public static async Task<JournalEntry> ResolveOriginalJournalAsync(
        AccountingDbContext db, int companyId, long? storedId, string? docNo, string descPrefix, CancellationToken ct)
    {
        JournalEntry? je;
        if (storedId is { } id)
            je = await db.JournalEntries.AsNoTracking().FirstOrDefaultAsync(
                j => j.JournalId == id && j.CompanyId == companyId, ct);
        else
        {
            var found = await db.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == companyId && j.Reference == docNo
                         && j.Description == descPrefix + docNo
                         && j.ReversalOfId == null && j.Status == DocumentStatus.Posted && !j.IsClosingEntry)
                .ToListAsync(ct);
            if (found.Count > 1)
                throw new DomainException("cancel.journal_ambiguous",
                    $"More than one posting journal exists for {docNo}; cannot cancel automatically.");
            je = found.FirstOrDefault();
        }
        if (je is null)
            throw new DomainException("cancel.journal_not_found",
                $"No posting journal found for {docNo}; use a credit/debit note instead.");
        if (await db.JournalEntries.AnyAsync(j => j.ReversalOfId == je.JournalId, ct))
            throw new DomainException("cancel.already_reversed", $"Journal {je.DocNo} is already reversed.");
        return je;
    }

    // Row locks (lock order 3.3.5: the doc, then TIs ascending, then BNs ascending). Id-only predicate: RLS hides
    // other tenants. Ascending ORDER BY is what makes concurrent multi-row locks deadlock-free.
    public static Task LockTaxInvoicesAsync(AccountingDbContext db, IEnumerable<long> ids, CancellationToken ct)
    {
        var a = ids.Distinct().Order().ToArray();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT tax_invoice_id FROM sales.tax_invoices WHERE tax_invoice_id = ANY({a}) ORDER BY tax_invoice_id FOR UPDATE", ct);
    }

    public static Task LockReceiptsAsync(AccountingDbContext db, IEnumerable<long> ids, CancellationToken ct)
    {
        var a = ids.Distinct().Order().ToArray();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT receipt_id FROM sales.receipts WHERE receipt_id = ANY({a}) ORDER BY receipt_id FOR UPDATE", ct);
    }

    public static Task LockBillingNotesAsync(AccountingDbContext db, IEnumerable<long> ids, CancellationToken ct)
    {
        var a = ids.Distinct().Order().ToArray();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT billing_note_id FROM sales.billing_notes WHERE billing_note_id = ANY({a}) ORDER BY billing_note_id FOR UPDATE", ct);
    }

    public static string Note(string code, string reason) => $"[{code}] {reason}";
}
