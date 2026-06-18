using System.Collections.Concurrent;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 14 — resolves <c>X-Api-Key</c> to a tenant principal. Lookup by the
/// deterministic <c>KeyPrefix</c> then bcrypt-verify the full secret (prefix
/// alone is not sufficient — verify is authoritative). <c>LastUsedAt</c> is
/// updated at most every 5 min/key (rate-limited; zero amortized write cost,
/// no per-request latency — spec §P1 perf, Tier-1 synchronous acceptable).
/// </summary>
public sealed class ApiKeyResolver : IApiKeyResolver
{
    private static readonly ConcurrentDictionary<long, long> LastTouchUtcTicks = new();
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);

    private readonly AccountingDbContext _db;
    private readonly ILogger<ApiKeyResolver> _logger;

    public ApiKeyResolver(AccountingDbContext db, ILogger<ApiKeyResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiKeyAuthResult> AuthenticateAsync(string presentedKey, CancellationToken ct)
    {
        var prefix = ApiKeyGenerator.PrefixOf(presentedKey);
        if (prefix is null)
            return ApiKeyAuthResult.Fail("auth.invalid_api_key");

        // Auth runs before tenant context — bypass the company query filter; the
        // key itself carries the company.
        var key = await _db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix, ct);
        if (key is null || !BCrypt.Net.BCrypt.Verify(presentedKey, key.KeyHash))
            return ApiKeyAuthResult.Fail("auth.invalid_api_key");

        if (key.RevokedAt is not null || !key.IsActive)
            return ApiKeyAuthResult.Fail("auth.revoked_api_key");
        if (key.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            return ApiKeyAuthResult.Fail("auth.expired_api_key");

        await TouchLastUsedAsync(key.ApiKeyId, ct);

        // M13 — the principal must carry a real branch (numbering + JE rows are
        // branch-keyed); the external surface acts as the company's head office.
        var hqBranchId = await _db.Branches.IgnoreQueryFilters()
            .Where(b => b.CompanyId == key.CompanyId)
            .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.BranchId)
            .Select(b => b.BranchId)
            .FirstOrDefaultAsync(ct);

        return ApiKeyAuthResult.Ok(new ResolvedApiKey(
            key.ApiKeyId, key.CompanyId, key.Name,
            ScopesCsv: ScopesToCsv(key.ScopesJson),
            DefaultBusinessUnitId: key.DefaultBusinessUnitId,
            HeadOfficeBranchId: hqBranchId));
    }

    private async Task TouchLastUsedAsync(long apiKeyId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var last = LastTouchUtcTicks.GetValueOrDefault(apiKeyId);
        if (last != 0 && now.UtcTicks - last < TouchEvery.Ticks) return;
        LastTouchUtcTicks[apiKeyId] = now.UtcTicks;
        try
        {
            await _db.ApiKeys.IgnoreQueryFilters()
                .Where(k => k.ApiKeyId == apiKeyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — caller requested it.
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort telemetry write: never fail auth because a timestamp update failed.
            _logger.LogWarning(ex, "ApiKeyResolver: failed to update LastUsedAt for key {ApiKeyId}", apiKeyId);
        }
    }

    /// <summary>ScopesJson is a JSON string array — flatten to CSV for the claim.</summary>
    private static string ScopesToCsv(string scopesJson)
    {
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(scopesJson);
            return arr is null ? "" : string.Join(",", arr);
        }
        catch { return ""; }
    }
}
