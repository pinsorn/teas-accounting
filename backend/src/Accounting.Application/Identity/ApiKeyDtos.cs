using Accounting.Domain.Entities.Identity;
using FluentValidation;

namespace Accounting.Application.Identity;

public sealed record CreateApiKeyRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt = null,
    int? DefaultBusinessUnitId = null,
    // M1 (MCP) — key profile; default integration keeps existing callers unchanged.
    string Kind = ApiKeyKinds.Integration);

/// <summary>List/detail projection — never exposes KeyHash or plaintext.</summary>
public sealed record ApiKeyListItem(
    long ApiKeyId, string Name, string KeyPrefix,
    IReadOnlyList<string> Scopes,
    int? DefaultBusinessUnitId, string? DefaultBusinessUnitCode,
    DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, bool IsActive,
    string Kind);

/// <summary>Returned ONCE on create/rotate — the plaintext is never stored or re-shown.</summary>
public sealed record ApiKeyCreatedResult(
    long ApiKeyId, string Name, string KeyPrefix, string Plaintext);

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyListItem>> ListAsync(CancellationToken ct);
    Task<ApiKeyCreatedResult> CreateAsync(CreateApiKeyRequest req, CancellationToken ct);
    Task RevokeAsync(long apiKeyId, CancellationToken ct);
    Task<ApiKeyCreatedResult> RotateAsync(long apiKeyId, CancellationToken ct);
}

public sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Scopes).NotNull();
        RuleForEach(x => x.Scopes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Kind).Must(ApiKeyKinds.IsValid)
            .WithMessage("Kind must be 'integration' or 'mcp'.");
    }
}
