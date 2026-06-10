namespace Accounting.Application.Abstractions;

/// <summary>A successfully-resolved external API key (no human user).</summary>
/// <remarks>
/// <see cref="HeadOfficeBranchId"/>: keys are company-scoped, but doc numbering
/// (<c>sys.number_sequences</c>) and JournalEntry rows are branch-keyed. Without a
/// branch claim the principal resolved BranchId=0, allocating from a fresh
/// (branch-0) sequence whose numbers collide with the head office's on the
/// company-wide <c>ix_journal_entries_company_id_doc_no</c> (M13). The external
/// surface acts as the company's head office.
/// </remarks>
public sealed record ResolvedApiKey(
    long ApiKeyId,
    int  CompanyId,
    string Name,
    string ScopesCsv,                 // comma-joined permission strings
    int? DefaultBusinessUnitId,
    int  HeadOfficeBranchId);

/// <summary>
/// Sprint 14 — resolve a presented <c>X-Api-Key</c> to a tenant principal.
/// <see cref="FailCode"/> is the stable error code for the 401 envelope when
/// <see cref="Key"/> is null (never leak which check failed beyond the code).
/// </summary>
public sealed record ApiKeyAuthResult(ResolvedApiKey? Key, string? FailCode)
{
    public static ApiKeyAuthResult Ok(ResolvedApiKey k) => new(k, null);
    public static ApiKeyAuthResult Fail(string code)    => new(null, code);
}

public interface IApiKeyResolver
{
    /// <summary>Verify the plaintext key (bcrypt) + active/expiry/revoked state.</summary>
    Task<ApiKeyAuthResult> AuthenticateAsync(string presentedKey, CancellationToken ct);
}
