using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.ETax;

public sealed class ETaxSigningOptions
{
    /// <summary>Path to the company's PKCS#12 (.pfx). Prod = CA-issued (Thailand NRCA/TUC chain).</summary>
    public string PfxPath { get; init; } = "";
    public string PfxPassword { get; init; } = "";
}

/// <summary>
/// XAdES-BES enveloped signature per docs/etax-xades-spec.md (§1 algorithms + §5 checklist
/// are NON-NEGOTIABLE). Pure signing core lives in <see cref="XadesBesSigner"/> so it can be
/// unit-tested with a self-signed dev cert; this wrapper just loads the configured PFX.
///
/// Inert by default: the auto-send pipeline (TaxInvoiceService) only signs/emails when
/// <c>ETaxBehaviorOptions.Enabled</c> is true — it is false unless explicitly opted in.
/// </summary>
public sealed class ETaxSigner : IETaxSigner
{
    private readonly ETaxSigningOptions _opts;
    private readonly QualifyingPropertiesBuilder _qp;
    private readonly IClock _clock;

    public ETaxSigner(IOptions<ETaxSigningOptions> opts, QualifyingPropertiesBuilder qp, IClock clock)
    {
        _opts = opts.Value; _qp = qp; _clock = clock;
    }

    public Task<ETaxSignedDocument> SignAsync(string xml, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.PfxPath) || !File.Exists(_opts.PfxPath))
            throw new DomainException("etax.pfx_missing",
                $"Signing certificate not found at '{_opts.PfxPath}'. e-Tax is inert until a cert is provisioned.");

        using var cert = X509CertificateLoader.LoadPkcs12FromFile(
            _opts.PfxPath, _opts.PfxPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

        var chain = BuildChain(cert);
        var signer = new XadesBesSigner(_qp);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var signingTime = _clock.UtcNow.ToOffset(TimeSpan.FromHours(7)); // Asia/Bangkok
        signer.Sign(doc, cert, chain, signingTime, rootIdFallback: $"TEAS-{Guid.NewGuid():N}");

        // BOM-free UTF-8 (spec §5 checklist).
        var signedXml = doc.OuterXml;
        var bytes = new UTF8Encoding(false).GetBytes(signedXml);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return Task.FromResult(new ETaxSignedDocument(signedXml, sha, bytes));
    }

    private static IReadOnlyList<X509Certificate2> BuildChain(X509Certificate2 leaf)
    {
        using var chain = new X509Chain
        {
            ChainPolicy = { RevocationMode = X509RevocationMode.NoCheck, VerificationFlags = X509VerificationFlags.AllFlags },
        };
        chain.Build(leaf);
        // Skip the leaf (index 0); include intermediates + root for KeyInfo / SigningCertificate.
        return chain.ChainElements.Count > 1
            ? chain.ChainElements.Skip(1).Select(e => e.Certificate).ToArray()
            : [];
    }
}

/// <summary>
/// Pure XAdES-BES signing logic (no config / IO). <see cref="ETaxSigner"/> wires the cert in;
/// tests inject a self-signed <see cref="X509Certificate2"/> directly.
/// </summary>
public sealed class XadesBesSigner
{
    private readonly QualifyingPropertiesBuilder _qp;
    public XadesBesSigner(QualifyingPropertiesBuilder qp) => _qp = qp;

    /// <summary>Signs <paramref name="doc"/> in place (enveloped). Returns the same document.</summary>
    public XmlDocument Sign(
        XmlDocument doc,
        X509Certificate2 signingCert,
        IReadOnlyList<X509Certificate2> chain,
        DateTimeOffset signingTime,
        string rootIdFallback)
    {
        var root = doc.DocumentElement
            ?? throw new DomainException("etax.no_root", "Document has no root element.");

        var rootId = root.HasAttribute("Id") ? root.GetAttribute("Id") : rootIdFallback;
        if (!root.HasAttribute("Id")) root.SetAttribute("Id", rootId);

        var signatureId   = $"Signature-{rootId}";
        var signedPropsId = $"SignedProperties-{rootId}";
        var dataRefId     = $"Reference-{rootId}";

        var rsa = signingCert.GetRSAPrivateKey()
            ?? throw new DomainException("etax.cert_no_rsa", "PFX has no usable RSA private key.");

        var signedXml = new XadesSignedXml(doc) { SigningKey = rsa };
        signedXml.Signature.Id = signatureId;
        signedXml.SignedInfo!.CanonicalizationMethod = XadesNs.AlgC14N;
        signedXml.SignedInfo.SignatureMethod = XadesNs.AlgRsaSha512;

        // Reference 1 — the invoice itself (enveloped + C14N inclusive), SHA-512.
        var dataRef = new Reference
        {
            Uri = "#" + rootId,
            Id = dataRefId,
            DigestMethod = XadesNs.AlgSha512,
        };
        dataRef.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        dataRef.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(dataRef);

        // XAdES QualifyingProperties as a signed ds:Object.
        var qp = _qp.Build(doc, signatureId, signedPropsId, signingCert, chain, signingTime, dataRefId);
        var dataObject = new DataObject();
        dataObject.Data = qp.SelectNodes(".")!;
        signedXml.AddObject(dataObject);

        // Reference 2 — the XAdES SignedProperties (SHA-512), counter-signed.
        // Exclusive C14N is REQUIRED here (etax-xades-spec.md §1 errata + §3.4): the
        // SignedProperties is canonicalized as a standalone fragment at sign time but as
        // an in-tree node at verify time; Exclusive C14N drops ancestor-scope namespaces
        // so both produce identical bytes → round-trip CheckSignature succeeds + xades4j parity.
        var spRef = new Reference
        {
            Uri = "#" + signedPropsId,
            Type = XadesNs.SignedPropsType,
            DigestMethod = XadesNs.AlgSha512,
        };
        spRef.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(spRef);

        // KeyInfo — full chain (leaf + intermediates + root).
        var keyInfo = new KeyInfo();
        var x509 = new KeyInfoX509Data();
        x509.AddCertificate(signingCert);
        foreach (var c in chain) x509.AddCertificate(c);
        keyInfo.AddClause(x509);
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var sigEl = signedXml.GetXml();
        root.AppendChild(doc.ImportNode(sigEl, deep: true));
        return doc;
    }
}

/// <summary>
/// <see cref="SignedXml"/> cannot resolve a same-signature <c>Reference URI="#SignedProperties…"</c>
/// because the XAdES element lives inside the in-flight <c>ds:Object</c>, not the document.
/// Overriding <see cref="GetIdElement"/> to also search the signature's data objects is the
/// canonical XAdES-with-SignedXml workaround.
/// </summary>
internal sealed class XadesSignedXml : SignedXml
{
    public XadesSignedXml(XmlDocument doc) : base(doc) { }

    public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
    {
        var found = base.GetIdElement(document, idValue);
        if (found is not null) return found;

        foreach (DataObject obj in Signature.ObjectList)
        {
            foreach (XmlNode node in obj.Data)
            {
                if (node is not XmlElement el) continue;
                if (el.GetAttribute("Id") == idValue) return el;
                var nested = el.SelectSingleNode($".//*[@Id='{idValue}']") as XmlElement;
                if (nested is not null) return nested;
            }
        }
        return null;
    }
}
