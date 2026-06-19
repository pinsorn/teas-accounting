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
                k.CreatedAt, k.LastUsedAt, k.ExpiresAt, k.RevokedAt, k.IsActive, k.Kind,
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
            r.CreatedAt, r.LastUsedAt, r.ExpiresAt, r.RevokedAt, r.IsActive, r.Kind)).ToList();
    }

    public async Task<ApiKeyCreatedResult> CreateAsync(CreateApiKeyRequest req, CancellationToken ct)
    {
        EnsureAuth();
        var kind = string.IsNullOrWhiteSpace(req.Kind) ? ApiKeyKinds.Integration : req.Kind.Trim();
        if (!ApiKeyKinds.IsValid(kind))
            throw new DomainException("api_key.invalid_kind",
                "Kind must be 'integration' or 'mcp'.");
        // M1 (MCP) compliance belt — an mcp key (AI agent) MUST NOT hold any
        // post scope; it can draft (.create) but a human posts. Reject at the
        // only grant site (CreateAsync) so the key structurally cannot post.
        EnforceMcpNoPostGuard(kind, req.Scopes);
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
            Kind = kind,
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
        // M13 — company-explicit: the key is minted for _tenant.CompanyId, so its
        // DefaultBusinessUnitId must belong to that company. The EF tenant filter
        // alone is bypassed for super admins and accepted a foreign company's BU,
        // minting a key that then failed bu.invalid on every request.
        var ok = await _db.BusinessUnits
            .AnyAsync(b => b.BusinessUnitId == buId
                           && b.CompanyId == _tenant.CompanyId && b.IsActive, ct);
        if (!ok)
            throw new DomainException("api_key.invalid_business_unit",
                $"Business Unit {buId} not found or inactive for this company.");
    }

    /// <summary>M1 (MCP) — reject ANY state-advancing (post-like) scope for a kind=mcp key:
    /// <c>.post/.approve/.issue/.send/.void/.cancel/.reject</c>. An mcp key may hold only
    /// <c>.read/.create/.manage</c> — it drafts; a human advances the document. The guard lives
    /// on the kind, not the endpoint, so M2M (integration) keys keep full action scopes. Throws
    /// <c>api_key.mcp_cannot_post</c>. (All catalog mcp scopes today end .read/.create/.manage.)</summary>
    private static readonly string[] McpForbiddenSuffixes =
        [".post", ".approve", ".issue", ".send", ".void", ".cancel", ".reject"];

    private static void EnforceMcpNoPostGuard(string kind, IReadOnlyList<string> scopes)
    {
        if (kind != ApiKeyKinds.Mcp || scopes is null) return;
        var offending = scopes.FirstOrDefault(s =>
            s is not null && McpForbiddenSuffixes.Any(suffix =>
                s.Trim().EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
        if (offending is not null)
            throw new DomainException("api_key.mcp_cannot_post",
                $"An mcp key may not be granted a state-advancing scope ('{offending}'). " +
                "MCP keys draft (.read/.create/.manage); a human posts/approves/issues/sends/voids.");
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
                key.Name, key.KeyPrefix, key.Kind, scopes = ParseScopes(key.ScopesJson),
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
