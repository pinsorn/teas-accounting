using System.Text.Json;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Audit;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 14 — ApiKey lifecycle (admin via BFF/JWT, not the external surface).
/// Plaintext is returned ONCE on create/rotate and NEVER stored or logged;
/// only the bcrypt hash + lookup prefix persist. Every action writes a
/// minimal, secret-free <c>audit.activity_log</c> row (no general
/// IActivityLogger exists yet — direct minimal write, flagged for Phase 2).
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public ApiKeyService(AccountingDbContext db, ITenantContext tenant, IClock clock)
    {
        _db = db; _tenant = tenant; _clock = clock;
    }

    private void EnsureAuth()
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<IReadOnlyList<ApiKeyListItem>> ListAsync(CancellationToken ct)
    {
        EnsureAuth();
        var rows = await _db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.ApiKeyId)
            .Select(k => new
            {
                k.ApiKeyId, k.Name, k.KeyPrefix, k.ScopesJson, k.DefaultBusinessUnitId,
                k.CreatedAt, k.LastUsedAt, k.ExpiresAt, k.RevokedAt, k.IsActive,
            })
            .ToListAsync(ct);

        var buIds = rows.Where(r => r.DefaultBusinessUnitId is not null)
            .Select(r => r.DefaultBusinessUnitId!.Value).Distinct().ToList();
        var buCodes = await _db.BusinessUnits.AsNoTracking()
            .Where(b => buIds.Contains(b.BusinessUnitId))
            .ToDictionaryAsync(b => b.BusinessUnitId, b => b.Code, ct);

        return rows.Select(r => new ApiKeyListItem(
            r.ApiKeyId, r.Name, r.KeyPrefix, ParseScopes(r.ScopesJson),
            r.DefaultBusinessUnitId,
            r.DefaultBusinessUnitId is { } b && buCodes.TryGetValue(b, out var c) ? c : null,
            r.CreatedAt, r.LastUsedAt, r.ExpiresAt, r.RevokedAt, r.IsActive)).ToList();
    }

    public async Task<ApiKeyCreatedResult> CreateAsync(CreateApiKeyRequest req, CancellationToken ct)
    {
        EnsureAuth();
        await ValidateBuAsync(req.DefaultBusinessUnitId, ct);

        var minted = ApiKeyGenerator.New();
        var now = _clock.UtcNow;
        var key = new ApiKey
        {
            CompanyId = _tenant.CompanyId,
            Name = req.Name.Trim(),
            KeyHash = minted.KeyHash,
            KeyPrefix = minted.KeyPrefix,
            ScopesJson = JsonSerializer.Serialize(req.Scopes),
            CreatedBy = _tenant.UserId ?? 0,
            CreatedAt = now,
            ExpiresAt = req.ExpiresAt,
            DefaultBusinessUnitId = req.DefaultBusinessUnitId,
            IsActive = true,
        };
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync(ct);
        await AuditAsync("api_key.create", key, now, ct);

        return new ApiKeyCreatedResult(key.ApiKeyId, key.Name, key.KeyPrefix, minted.Plaintext);
    }

    public async Task RevokeAsync(long apiKeyId, CancellationToken ct)
    {
        EnsureAuth();
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.ApiKeyId == apiKeyId, ct)
            ?? throw new DomainException("api_key.not_found", $"API key {apiKeyId} not found.");
        var now = _clock.UtcNow;
        key.RevokedAt = now;
        key.RevokedBy = _tenant.UserId;
        key.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await AuditAsync("api_key.revoke", key, now, ct);
    }

    public async Task<ApiKeyCreatedResult> RotateAsync(long apiKeyId, CancellationToken ct)
    {
        EnsureAuth();
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.ApiKeyId == apiKeyId, ct)
            ?? throw new DomainException("api_key.not_found", $"API key {apiKeyId} not found.");

        var minted = ApiKeyGenerator.New();
        var now = _clock.UtcNow;
        key.KeyHash = minted.KeyHash;       // old secret invalid immediately
        key.KeyPrefix = minted.KeyPrefix;
        key.LastUsedAt = null;
        key.RevokedAt = null;
        key.RevokedBy = null;
        key.IsActive = true;
        await _db.SaveChangesAsync(ct);
        await AuditAsync("api_key.rotate", key, now, ct);

        return new ApiKeyCreatedResult(key.ApiKeyId, key.Name, key.KeyPrefix, minted.Plaintext);
    }

    private async Task ValidateBuAsync(int? buId, CancellationToken ct)
    {
        if (buId is null) return;
        var ok = await _db.BusinessUnits
            .AnyAsync(b => b.BusinessUnitId == buId && b.IsActive, ct);
        if (!ok)
            throw new DomainException("api_key.invalid_business_unit",
                $"Business Unit {buId} not found or inactive for this company.");
    }

    private async Task AuditAsync(string type, ApiKey key, DateTimeOffset at, CancellationToken ct)
    {
        // Secret-free: name/prefix/scopes/BU only — never KeyHash or plaintext.
        _db.Set<ActivityLog>().Add(new ActivityLog
        {
            CompanyId = _tenant.CompanyId,
            UserId = _tenant.UserId,
            ActivityAt = at,
            ActivityType = type,
            Module = "sys",
            EntityType = "ApiKey",
            EntityId = key.ApiKeyId,
            EntityDocNo = key.KeyPrefix,
            AfterValueJson = JsonSerializer.Serialize(new
            {
                key.Name, key.KeyPrefix, scopes = ParseScopes(key.ScopesJson),
                key.DefaultBusinessUnitId, key.ExpiresAt, key.IsActive, key.RevokedAt,
            }),
        });
        await _db.SaveChangesAsync(ct);
    }

    private static List<string> ParseScopes(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
