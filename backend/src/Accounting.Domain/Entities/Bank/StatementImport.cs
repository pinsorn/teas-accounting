using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md D1/B2) — one row per uploaded statement
/// file. Raw file bytes live in the Attachment infra (<see cref="AttachmentId"/>), stored
/// EXACTLY as uploaded — a K-Plus PDF stays password-protected at rest (D11).
/// </summary>
public class StatementImport : ITenantOwned
{
    public long StatementImportId { get; set; }
    public int CompanyId { get; set; }

    public int BankAccountId { get; set; }
    public required string AdapterCode { get; set; }
    public required string SourceFileName { get; set; }
    public long? AttachmentId { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public int LineCount { get; set; }
    public decimal? WithdrawalTotal { get; set; }
    public decimal? DepositTotal { get; set; }

    public ImportStatus Status { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public long ImportedBy { get; set; }
}
