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

    // L2-4 (PLAN-fix-findings-r2.md §U4, findings-r2/findings-leg2.md) — mirrors the varchar
    // caps in BankReconciliationConfiguration.cs's StatementLineConfiguration exactly (Channel/
    // TxnType/RawRef 100, Description 500). Checked at parse time so a bank-supplied field that
    // exceeds the DB column never reaches SaveChangesAsync as a raw DbUpdateException.
    private const int MaxDescriptionLength = 500;
    private const int MaxShortFieldLength = 100;

    /// <summary>Throws a typed, no-PII (D10 convention: line numbers only, never raw text)
    /// DomainException for the first line whose Description/Channel/TxnType/RawRef would
    /// overflow its DB column — caught cleanly by the caller's existing
    /// <c>catch (DomainException) { throw; }</c>.</summary>
    private static void ValidateLineFieldLengths(ParsedStatement parsed)
    {
        foreach (var line in parsed.Lines)
        {
            if (line.Description.Length > MaxDescriptionLength)
                throw new DomainException("bank.import_line_too_long",
                    $"Line {line.LineNo}: description exceeds {MaxDescriptionLength} characters.");
            if (line.Channel.Length > MaxShortFieldLength)
                throw new DomainException("bank.import_line_too_long",
                    $"Line {line.LineNo}: channel exceeds {MaxShortFieldLength} characters.");
            if (line.TxnType.Length > MaxShortFieldLength)
                throw new DomainException("bank.import_line_too_long",
                    $"Line {line.LineNo}: transaction type exceeds {MaxShortFieldLength} characters.");
            if (line.RawRef is { Length: > MaxShortFieldLength })
                throw new DomainException("bank.import_line_too_long",
                    $"Line {line.LineNo}: reference exceeds {MaxShortFieldLength} characters.");
        }
    }

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
            // L2-4 — same "fails loud before anything persists" placement; catches an oversized
            // bank-supplied field BEFORE it would otherwise crash SaveChangesAsync with a raw,
            // untyped DbUpdateException.
            ValidateLineFieldLengths(parsed);
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
        // L2-4 residual defense — ValidateLineFieldLengths above catches the KNOWN oversized-
        // field case, but any OTHER unexpected persistence failure (e.g. an oversized
        // SourceFileName, or a future column this method doesn't know about) must still map to
        // a typed error, never a raw DbUpdateException/Postgres text leak. Still inside the
        // `await using var txn` scope above, so the rethrow still triggers rollback (atomicity
        // unaffected — same "nothing persists on failure" guarantee as every other exit here).
        try
        {
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
        }
        catch (DbUpdateException ex)
        {
            log.LogError(ex, "Statement persistence failed for bank account {BankAccountId}, file '{FileName}'.",
                bankAccountId, fileName);
            throw new DomainException("bank.import_failed",
                "The statement could not be saved — check the file's field lengths and try again.");
        }

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

    public async Task DeleteImportAsync(long importId, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var import = await db.StatementImports
                .FirstOrDefaultAsync(x => x.StatementImportId == importId && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank.import_not_found", $"Statement import {importId} not found.");

        await using var txn = await db.Database.BeginTransactionAsync(ct);

        // L2-3 remediation (PLAN-fix-findings-r2.md §U3.2) — refuse if any line has progressed
        // past Unmatched/Ignored (a confirmed match, or a JE-backed adjustment — MatchStatus.
        // Posted per D8/BankEnums.cs); those real Receipt/PV/JE links must be unmatched or
        // reversed through their own flows first. Otherwise the import is a pure parse artifact
        // — hard-delete is safe, the source CSV can always be re-imported (B2.5).
        // Fast-path pre-check only — a friendly error with zero rows touched when the common case
        // (no matched lines) doesn't apply. Codex review 2026-08-20 F4 — this check does NOT by
        // itself close the race: under read-committed, a concurrent ConfirmMatchAsync can commit
        // a match between this AnyAsync and the delete below, and this check alone would not see
        // it. The CONDITIONAL DELETE + count comparison below is the actual correctness
        // mechanism (see its own comment).
        var hasBlockingLines = await db.StatementLines.AsNoTracking().AnyAsync(x =>
            x.StatementImportId == importId && x.CompanyId == tenant.CompanyId &&
            (x.MatchStatus == MatchStatus.Matched || x.MatchStatus == MatchStatus.Posted), ct);
        if (hasBlockingLines)
            throw new DomainException("bank.import_has_matched_lines",
                "Cannot delete an import that has matched or posted lines — unmatch or reverse them first.");

        // Codex review 2026-08-20 F3 — the raw uploaded file must not outlive its parent import:
        // listing/download both already filter on DeletedAt == null (AttachmentService.ListAsync/
        // OpenForDownloadAsync/ResolveParentAsync), so soft-deleting it here is enough to stop it
        // being served. Runs on the SAME scoped AccountingDbContext `db` as this transaction, so
        // it participates in `txn` automatically — no separate commit needed. `callerHasDeletePerm:
        // true` — the caller already cleared Permissions.Bank.StatementImport to reach this method
        // at all (StatementImportEndpoints' DELETE route), which is the real authority governing
        // this cascade; SoftDeleteAsync's own "delete perm OR uploader" check is for the standalone
        // attachment-delete route, not relevant to this authorized parent-delete cascade.
        if (import.AttachmentId is long attachmentId)
            await attachments.SoftDeleteAsync(attachmentId, callerHasDeletePerm: true, ct);

        // Codex review 2026-08-20 F4 — the correctness mechanism for the delete-vs-match race.
        // Read-committed does not lock rows read by the AnyAsync check above, so a concurrent
        // match confirmation could still land between that check and a plain unconditional
        // delete. Constraining the DELETE's own WHERE to the allowed statuses closes this:
        // Postgres evaluates a DELETE's WHERE against each row's latest COMMITTED state at
        // execution time (blocking on any row a concurrent still-open transaction holds a lock
        // on, then re-checking after it resolves) — so a line that was concurrently matched is
        // simply excluded from the delete, never silently discarded. Comparing the deleted count
        // against the import's current total line count catches exactly that case and rolls the
        // whole transaction back instead of leaving a partially-deleted import.
        var totalLineCount = await db.StatementLines.AsNoTracking().CountAsync(x =>
            x.StatementImportId == importId && x.CompanyId == tenant.CompanyId, ct);

        var deletedCount = await db.StatementLines
            .Where(x => x.StatementImportId == importId && x.CompanyId == tenant.CompanyId &&
                (x.MatchStatus == MatchStatus.Unmatched || x.MatchStatus == MatchStatus.Ignored))
            .ExecuteDeleteAsync(ct);

        if (deletedCount < totalLineCount)
            throw new DomainException("bank.import_has_matched_lines",
                "Cannot delete an import that has matched or posted lines — unmatch or reverse them first.");

        db.StatementImports.Remove(import);
        await db.SaveChangesAsync(ct);
        await txn.CommitAsync(ct);
    }
}
