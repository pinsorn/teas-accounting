using System.Xml;
using System.Xml.Schema;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.ETax;

public sealed class ETaxValidationOptions
{
    /// <summary>Dir holding the ETDA มกค.14-2563 XSD set. Relative → ContentRoot.</summary>
    public string XsdSchemaDir { get; init; } = "etax-schemas/";

    /// <summary>Tier 1: false (graceful skip if empty). Tier 2/3: true (mandatory).</summary>
    public bool RequireSchemaPass { get; init; } = false;
}

/// <summary>
/// Sprint 13c — XSD validation against a locally-provisioned schema set. When
/// the dir is absent or holds no <c>*.xsd</c> (Tier 1 dev — real ETDA schemas
/// are an ops/Tier-2 prerequisite, not committed), <see cref="ValidateAsync"/>
/// returns <c>IsValid=true</c> so the dev pipeline is not blocked. The
/// pipeline's <c>RequireSchemaPass</c> gate decides whether a failure aborts.
/// </summary>
public sealed class LocalXsdValidator : IETaxXmlValidator
{
    private readonly XmlSchemaSet _schemas = new();

    public LocalXsdValidator(IOptions<ETaxValidationOptions> opts)
    {
        var dir = opts.Value.XsdSchemaDir;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;
        foreach (var xsd in Directory.EnumerateFiles(dir, "*.xsd"))
            _schemas.Add(null, xsd);
        if (_schemas.Count > 0)
            _schemas.Compile();
    }

    public Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct)
    {
        if (_schemas.Count == 0)
            return Task.FromResult(new XmlValidationResult(true, []));   // Tier 1 graceful skip

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message}");

        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, settings);
        try
        {
            while (reader.Read()) { /* drain to fire validation events */ }
        }
        catch (XmlException ex)
        {
            errors.Add($"Error: {ex.Message}");   // malformed XML — not schema-valid
        }

        return Task.FromResult(new XmlValidationResult(errors.Count == 0, errors));
    }
}
