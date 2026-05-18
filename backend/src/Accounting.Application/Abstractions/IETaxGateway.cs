namespace Accounting.Application.Abstractions;

public sealed record ETaxSignedDocument(string Xml, string Sha256, byte[] Bytes);

public sealed record ETaxDeliveryResult(
    bool Delivered,
    DateTimeOffset DeliveredAt,
    string MessageId,
    string? Error,
    // Sprint 13c — what was ACTUALLY sent (after the Tier-2 redirect guard),
    // so the submission-audit row can record the forensic trail.
    string To = "",
    string Cc = "",
    bool Redirected = false);

/// <summary>Builds the canonical e-Tax XML for a Tax Invoice (per RD spec).</summary>
public interface IETaxXmlBuilder
{
    /// <summary>Build raw XML (unsigned). The signer applies XAdES-BES afterwards.</summary>
    string BuildTaxInvoiceXml(long taxInvoiceId, CancellationToken ct);
}

/// <summary>Applies XAdES-BES enveloped signature using the company PFX certificate.</summary>
public interface IETaxSigner
{
    Task<ETaxSignedDocument> SignAsync(string xml, CancellationToken ct);
}

/// <summary>
/// Delivers a signed e-Tax XML by email — customer To + <c>csemail@rd.go.th</c> CC
/// (same message, simultaneously, per RD e-Tax-by-Email protocol).
/// </summary>
public interface IETaxEmailSender
{
    Task<ETaxDeliveryResult> SendAsync(
        string toEmail,
        string subject,
        ETaxSignedDocument xml,
        byte[]? pdfA3,
        CancellationToken ct);
}
