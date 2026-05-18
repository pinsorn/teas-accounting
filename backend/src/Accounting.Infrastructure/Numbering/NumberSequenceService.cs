using System.Data;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounting.Infrastructure.Numbering;

/// <summary>
/// Allocates document numbers via a single atomic UPSERT on <c>sys.number_sequences</c>
/// (<c>INSERT … ON CONFLICT … DO UPDATE … RETURNING</c>). The unique index
/// <c>ux_number_sequences_period</c> guarantees per-(company,branch,prefix,sub,year,month)
/// serialization under concurrency — the conflicting writer blocks on the row lock until the
/// first transaction commits, so no two callers ever read the same value.
/// Numbers are only allocated at POST (CLAUDE.md §17.5). Runs on the caller's ambient
/// transaction when present (number + document commit atomically).
/// </summary>
public sealed class NumberSequenceService : INumberSequenceService
{
    private readonly AccountingDbContext _db;

    public NumberSequenceService(AccountingDbContext db) => _db = db;

    public async Task<DocumentNumber> NextAsync(
        int companyId,
        int branchId,
        string prefixCode,
        string? subPrefix,
        DateOnly docDate,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefixCode))
            throw new DomainException("seq.prefix_required", "PrefixCode is required.");

        var year  = docDate.Year;
        var month = docDate.Month;
        var sub   = subPrefix ?? "";

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = """
            INSERT INTO sys.number_sequences
                (company_id, branch_id, prefix_code, sub_prefix,
                 period_year, period_month, current_value, last_issued_at)
            VALUES (@p0, @p1, @p2, @p3, @p4, @p5, 1, NOW())
            ON CONFLICT (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month)
            DO UPDATE SET current_value  = sys.number_sequences.current_value + 1,
                          last_issued_at = NOW()
            RETURNING current_value
            """;

        AddParam(cmd, "@p0", companyId);
        AddParam(cmd, "@p1", branchId);
        AddParam(cmd, "@p2", prefixCode);
        AddParam(cmd, "@p3", sub);
        AddParam(cmd, "@p4", year);
        AddParam(cmd, "@p5", month);

        var scalar = await cmd.ExecuteScalarAsync(ct)
            ?? throw new DomainException("seq.alloc_failed",
                $"Number sequence upsert returned no value for {prefixCode}.");
        var next = Convert.ToInt32(scalar);

        return DocumentNumber.Build(
            year, month, prefixCode, string.IsNullOrEmpty(sub) ? null : sub, next);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
