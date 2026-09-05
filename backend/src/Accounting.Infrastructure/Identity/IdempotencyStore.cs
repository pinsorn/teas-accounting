using System.Data;
using System.Data.Common;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Claim-first idempotency store (fix-idempotency-claim-first.md §3.2/§3.7-H4). The key row is
/// INSERTed BEFORE the endpoint executes (the claim, <c>response_status IS NULL</c>) so the
/// UNIQUE (company,api_key,key) index — not a post-hoc save — arbitrates concurrency. All SQL
/// runs as a raw <see cref="NpgsqlCommand"/> on the SAME scoped connection
/// (<c>_db.Database.GetDbConnection()</c>) that <c>TenantMiddleware</c> pinned
/// <c>app.company_id</c> on, never a new connection and never <c>SaveChangesAsync</c> — the
/// change tracker is untouched and the RLS pin applies (H1). No <c>catch</c> of ANY persistence
/// exception anywhere in this class (I5) — unexpected errors propagate.
/// <c>idempotency_key_id</c> IS the claim token: a stale takeover DELETEs the dead row and
/// re-INSERTs rather than reusing it in place, so a stale owner's <see cref="CompleteAsync"/>/
/// <see cref="ReleaseAsync"/> can never name the new owner's row (I9; see the rejected in-place
/// UPDATE alternative in the spec §3.6).
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AccountingDbContext _db;
    public IdempotencyStore(AccountingDbContext db) => _db = db;

    public async Task<ClaimResult> ClaimAsync(int companyId, long apiKeyId, string key, string requestHash,
        DateTimeOffset now, TimeSpan staleAfter, CancellationToken ct)
    {
        now = now.ToUniversalTime();                 // guarantee Offset 0 for TimestampTz params
        var staleBefore = now - staleAfter;
        var expiresAt = now.AddHours(24);

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(ct);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using (var insertCmd = conn.CreateCommand())
            {
                insertCmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
                insertCmd.CommandText = """
                    INSERT INTO sys.idempotency_keys
                        (company_id, api_key_id, "key", request_hash, response_status,
                         response_body, response_headers, created_at, expires_at)
                    VALUES (@company_id, @api_key_id, @key, @request_hash, NULL, NULL, NULL, @created_at, @expires_at)
                    ON CONFLICT (company_id, api_key_id, "key") DO NOTHING
                    RETURNING idempotency_key_id
                    """;
                AddParam(insertCmd, "company_id", NpgsqlDbType.Integer, companyId);
                AddParam(insertCmd, "api_key_id", NpgsqlDbType.Bigint, apiKeyId);
                AddParam(insertCmd, "key", NpgsqlDbType.Text, key);
                AddParam(insertCmd, "request_hash", NpgsqlDbType.Text, requestHash);
                AddParam(insertCmd, "created_at", NpgsqlDbType.TimestampTz, now);
                AddParam(insertCmd, "expires_at", NpgsqlDbType.TimestampTz, expiresAt);

                var claimedId = await insertCmd.ExecuteScalarAsync(ct);
                if (claimedId is not null)
                    return new ClaimResult(ClaimOutcome.Claimed, (long)claimedId, null);
            }

            bool found; long existingId; string existingHash; int? existingStatus;
            string? existingBody; string? existingHeaders; bool isDead;
            await using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
                selectCmd.CommandText = """
                    SELECT idempotency_key_id, request_hash, response_status, response_body, response_headers,
                           (expires_at <= @now OR (response_status IS NULL AND created_at < @stale_before)) AS is_dead
                    FROM sys.idempotency_keys
                    WHERE company_id = @company_id AND api_key_id = @api_key_id AND "key" = @key
                    """;
                AddParam(selectCmd, "company_id", NpgsqlDbType.Integer, companyId);
                AddParam(selectCmd, "api_key_id", NpgsqlDbType.Bigint, apiKeyId);
                AddParam(selectCmd, "key", NpgsqlDbType.Text, key);
                AddParam(selectCmd, "now", NpgsqlDbType.TimestampTz, now);
                AddParam(selectCmd, "stale_before", NpgsqlDbType.TimestampTz, staleBefore);

                await using var reader = await selectCmd.ExecuteReaderAsync(ct);
                found = await reader.ReadAsync(ct);
                if (found)
                {
                    existingId = reader.GetInt64(0);
                    existingHash = reader.GetString(1);
                    existingStatus = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    existingBody = reader.IsDBNull(3) ? null : reader.GetString(3);
                    existingHeaders = reader.IsDBNull(4) ? null : reader.GetString(4);
                    isDead = reader.GetBoolean(5);
                }
                else
                {
                    existingId = 0; existingHash = ""; existingStatus = null;
                    existingBody = null; existingHeaders = null; isDead = false;
                }
            }

            if (!found)
                continue;   // owner Released between step 1 and step 2 — retry the INSERT

            if (isDead)
            {
                await using var deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
                deleteCmd.CommandText = """
                    DELETE FROM sys.idempotency_keys
                    WHERE idempotency_key_id = @id
                      AND (expires_at <= @now OR (response_status IS NULL AND created_at < @stale_before))
                    """;
                AddParam(deleteCmd, "id", NpgsqlDbType.Bigint, existingId);
                AddParam(deleteCmd, "now", NpgsqlDbType.TimestampTz, now);
                AddParam(deleteCmd, "stale_before", NpgsqlDbType.TimestampTz, staleBefore);
                await deleteCmd.ExecuteNonQueryAsync(ct);
                continue;   // either 0 (a contender already removed/refreshed it) or 1 — next iteration re-attempts the INSERT
            }

            // Checked AFTER the dead check (an expired record must not 409 a new body) and
            // BEFORE in-progress (a mismatched body must never wait on a stranger's claim).
            if (existingHash != requestHash)
                return new ClaimResult(ClaimOutcome.Mismatch, null, null);

            if (existingStatus is null)
                return new ClaimResult(ClaimOutcome.InProgress, null, null);

            return new ClaimResult(ClaimOutcome.Completed, null,
                new IdempotencyRecord(existingHash, existingStatus.Value, existingBody, existingHeaders));
        }

        return new ClaimResult(ClaimOutcome.InProgress, null, null);   // 3 iterations of pure contention
    }

    public async Task<int> CompleteAsync(long claimId, int status, string? body, string? headersJson, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = """
            UPDATE sys.idempotency_keys
            SET response_status = @status, response_body = @body, response_headers = @headers
            WHERE idempotency_key_id = @id AND response_status IS NULL
            """;
        AddParam(cmd, "status", NpgsqlDbType.Integer, status);
        AddParam(cmd, "body", NpgsqlDbType.Text, (object?)body ?? DBNull.Value);
        AddParam(cmd, "headers", NpgsqlDbType.Jsonb, (object?)headersJson ?? DBNull.Value);
        AddParam(cmd, "id", NpgsqlDbType.Bigint, claimId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReleaseAsync(long claimId, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = """
            DELETE FROM sys.idempotency_keys
            WHERE idempotency_key_id = @id AND response_status IS NULL
            """;
        AddParam(cmd, "id", NpgsqlDbType.Bigint, claimId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct) =>
        _db.IdempotencyKeys.Where(k => k.ExpiresAt < now).ExecuteDeleteAsync(ct);

    private static void AddParam(DbCommand cmd, string name, NpgsqlDbType type, object? value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}
