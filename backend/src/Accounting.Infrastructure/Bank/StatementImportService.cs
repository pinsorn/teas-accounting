using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Bank;

/// <summary>Bank reconciliation (specs/bank-reconciliation.md B2.5).</summary>
public sealed class StatementImportService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    IEnumerable<IBankStatementAdapter> adapters, IAttachmentService attachments,
    IOptions<FileStorageOptions> fileStorageOptions, ILogger<StatementImportService> log)
    : IStatementImportService
{
    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    public async Task<StatementImportResult> ImportAsync(
        int bankAccountId, string fileName, string mimeType, long sizeBytes, Stream content,
        string? password, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var bankAccount = await db.BankAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.BankAccountId == bankAccountId && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank_account.not_found", $"Bank account {bankAccountId} not found.");

        // Reject cheap first (Fable cross-review, 2026-07-09) — the SAME size/mime limits
        // AttachmentService.UploadAsync enforces, checked BEFORE the expensive adapter.Parse
        // (previously this validation only ran downstream, inside UploadAsync, AFTER a full
        // parse of an oversized/disallowed file).
        var maxBytes = (long)fileStorageOptions.Value.MaxFileSizeMb * 1024 * 1024;
        if (sizeBytes > maxBytes)
            throw new DomainException("attachment.too_large",
                $"File exceeds the {fileStorageOptions.Value.MaxFileSizeMb} MB limit.");
        if (!fileStorageOptions.Value.AllowedMimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            throw new DomainException("attachment.bad_mime", $"MIME type '{mimeType}' is not allowed.");

        var adapter = adapters.FirstOrDefault(a => a.CanHandle(fileName, mimeType))
            ?? throw new DomainException("bank.no_adapter", $"No adapter can handle '{fileName}' ({mimeType}).");

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await content.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        // WP-C (B-br F1) — a real-world statement can hit a parsing edge case the adapter's own
        // DomainException checks don't anticipate (proven: an unhandled FormatException from the
        // real K-Plus PDF, root-caused and fixed in KPlusPdfLineAssembler — this is defense-in-
        // depth for the NEXT such gap, in this or the CSV adapter). Any exception that isn't
        // already a clean DomainException is mapped to one here so the caller always gets a 422,
        // never a raw 500; nothing has been persisted yet at this point (mirrors the D10 comment
        // below), and the real exception is logged server-side (never leaked to the client — same
        // "hardcoded generic message" policy as KPlusPdfTextExtractor's password-failure path).
        ParsedStatement parsed;
        try
        {
            parsed = adapter.Parse(new MemoryStream(bytes), password);
            // D10 — fails LOUD; nothing below has run yet, so a throw here persists nothing.
            BankStatementIntegrity.Validate(parsed);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Statement parse failed for bank account {BankAccountId}, file '{FileName}'.",
                bankAccountId, fileName);
            throw new DomainException("bank.statement_parse_failed",
                "Could not parse the statement file — it may be corrupted or in an unexpected format.");
        }

        // Codex review finding #3 (2026-07-10) — reject a statement parsed from the WRONG bank
        // account BEFORE any attachment/db write (mirrors the D10 placement above). Compared
        // digits-only: the statement's raw account no. and the selected account's stored one are
        // formatted differently ("999-9-99999-9" vs "9999999999") but must be the SAME account.
        if (DigitsOnly(parsed.AccountNoRaw) != DigitsOnly(bankAccount.AccountNo))
            throw new DomainException("bank.statement_account_mismatch",
                $"The statement's account number does not match the selected bank account " +
                $"'{bankAccount.AccountNo}'.");

        // Idempotency (B2.5) — warn, never block, on an overlapping prior import.
        var overlap = await db.StatementImports.AsNoTracking().AnyAsync(x =>
            x.BankAccountId == bankAccountId && x.CompanyId == tenant.CompanyId &&
            x.PeriodStart <= parsed.PeriodEnd && x.PeriodEnd >= parsed.PeriodStart, ct);

        await using var txn = await db.Database.BeginTransactionAsync(ct);

        var import = new StatementImport
        {
            CompanyId = tenant.CompanyId,
            BankAccountId = bankAccountId,
            AdapterCode = adapter.AdapterCode,
            SourceFileName = fileName,
            PeriodStart = parsed.PeriodStart,
            PeriodEnd = parsed.PeriodEnd,
            OpeningBalance = parsed.OpeningBalance,
            ClosingBalance = parsed.ClosingBalance,
            LineCount = parsed.Lines.Count,
            WithdrawalTotal = parsed.WithdrawalTotal,
            DepositTotal = parsed.DepositTotal,
            Status = ImportStatus.Parsed,
            ImportedAt = clock.UtcNow,
            ImportedBy = tenant.UserId ?? 0,
        };
        db.StatementImports.Add(import);
        await db.SaveChangesAsync(ct);   // generates StatementImportId

        // D11 — raw bytes stored EXACTLY as uploaded via the shared Attachment infra.
        var uploaded = await attachments.UploadAsync(
            "BANK_STATEMENT", import.StatementImportId, "BANK_STATEMENT", null,
            fileName, mimeType, bytes.LongLength, new MemoryStream(bytes), ct);
        import.AttachmentId = uploaded.AttachmentId;

        foreach (var line in parsed.Lines)
        {
            db.StatementLines.Add(new StatementLine
            {
                CompanyId = tenant.CompanyId,
                StatementImportId = import.StatementImportId,
                BankAccountId = bankAccountId,
                LineNo = line.LineNo,
                TxnDate = line.TxnDate,
                TxnTime = line.TxnTime,
                ValueDate = line.ValueDate,
                Direction = line.Direction,
                Amount = line.Amount,
                RunningBalance = line.RunningBalance,
                Channel = line.Channel,
                TxnType = line.TxnType,
                Description = line.Description,
                RawRef = line.RawRef,
                MatchStatus = MatchStatus.Unmatched,
            });
        }

        await db.SaveChangesAsync(ct);
        await txn.CommitAsync(ct);

        return new StatementImportResult(import.StatementImportId, parsed.Lines.Count, overlap);
    }

    public async Task<IReadOnlyList<StatementImportListItem>> ListAsync(int bankAccountId, CancellationToken ct) =>
        await db.StatementImports.AsNoTracking()
            .Where(x => x.BankAccountId == bankAccountId && x.CompanyId == tenant.CompanyId)
            .OrderByDescending(x => x.ImportedAt)
            .Select(x => new StatementImportListItem(
                x.StatementImportId, x.BankAccountId, x.AdapterCode, x.SourceFileName,
                x.PeriodStart, x.PeriodEnd, x.OpeningBalance, x.ClosingBalance,
                x.LineCount, x.Status, x.ImportedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StatementImportLineItem>> GetLinesAsync(long importId, CancellationToken ct)
    {
        var exists = await db.StatementImports.AsNoTracking()
            .AnyAsync(x => x.StatementImportId == importId && x.CompanyId == tenant.CompanyId, ct);
        if (!exists)
            throw new DomainException("bank.import_not_found", $"Statement import {importId} not found.");

        return await db.StatementLines.AsNoTracking()
            .Where(x => x.StatementImportId == importId && x.CompanyId == tenant.CompanyId)
            .OrderBy(x => x.LineNo)
            .Select(x => new StatementImportLineItem(
                x.StatementLineId, x.LineNo, x.TxnDate, x.TxnTime, x.Direction, x.Amount,
                x.RunningBalance, x.Channel, x.TxnType, x.Description, x.RawRef, x.MatchStatus))
            .ToListAsync(ct);
    }
}
