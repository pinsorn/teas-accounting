namespace Accounting.Application.Abstractions;

public sealed record XmlValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Sprint 13c — validates signed e-Tax XML against the ETDA มกค.14-2563 XSD set
/// before send. Tier 1: graceful skip when no schema is loaded (returns Valid).
/// Tier 2/3: <c>ETax:Validation:RequireSchemaPass=true</c> makes a failure abort.
/// </summary>
public interface IETaxXmlValidator
{
    Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct);
}
