# ETDA XAdES Profile & QualifyingProperties — Implementation Spec

> **For:** TEAS backend e-Tax signing service  
> **Standard:** มกค.14-2563 (ETDA Recommendation) + ประกาศกรมสรรพากร ฉบับที่ 53/2560  
> **Reference impl:** [ETDA/etax-xades](https://github.com/ETDA/etax-xades) (Java, uses `xades4j`)  
> **Our impl:** .NET 10 + `System.Security.Cryptography.Xml.SignedXml` + BouncyCastle for cert chain  

---

## 1. Profile & Algorithm Choices (mandatory)

These are extracted from the official ETDA Java sample (`XadesBesSigner.java`). **Match exactly** — RD's validator is strict.

| Property | Value | XML Algorithm URI |
|---|---|---|
| **Profile** | **XAdES-BES** (Basic Electronic Signature) | — |
| **Signature algorithm** | **RSA-SHA512** | `http://www.w3.org/2001/04/xmldsig-more#rsa-sha512` |
| **Digest algorithm — `DataObjects` references** | **SHA-512** | `http://www.w3.org/2001/04/xmlenc#sha512` |
| **Digest algorithm — `QualifyingProperties` references** | **SHA-512** | `http://www.w3.org/2001/04/xmlenc#sha512` |
| **Outer `SignedInfo` `CanonicalizationMethod`** | **Inclusive C14N** (no comments) | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` |
| **Data Reference Transform (after Enveloped)** | **Inclusive C14N** | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` |
| **SignedProperties Reference Transform** ⚠ | **Exclusive C14N** — REQUIRED for self-verify + xades4j parity | `http://www.w3.org/2001/10/xml-exc-c14n#` |
| **Enveloped transform** | for the data Reference only | `http://www.w3.org/2000/09/xmldsig#enveloped-signature` |
| **XAdES namespace** | XAdES v1.3.2 | `http://uri.etsi.org/01903/v1.3.2#` |
| **XML DSig namespace** | — | `http://www.w3.org/2000/09/xmldsig#` |
| **Number of signatures** | **2** per document — one by issuing software (TEAS), one by signer (organization CA) | — |

> **⚠ Errata (corrected 2026-05-16):** Earlier revisions of this spec said "use inclusive C14N
> everywhere". That was a misreading of the Java sample. The ETDA reference impl uses `xades4j`,
> whose `XadesBesSigningProfile` default for the **SignedProperties Reference** is **Exclusive C14N**
> (per ETSI TS 101 903 §6.3.1 + xades4j defaults). Outer `SignedInfo` Canonicalization stays
> inclusive. Without this distinction, .NET `SignedXml.CheckSignature` round-trip cannot succeed
> on the same document we just signed (ancestor-scope namespace context differs between sign-time
> as a `DataObject` and verify-time as an in-tree node).

**Signature type:** **Enveloped** — `<ds:Signature>` lives inside the root document being signed.

**Reference URI rules:**
- If root element has `Id="..."` attribute → `URI="#that-id"`
- Otherwise → `URI=""` (signs the document root) — and the signed element MUST be the root, not a child.

---

## 2. Required XML Structure

```xml
<TaxInvoice xmlns="urn:etda:uncefact:data:standard:CrossIndustryInvoice:2"
            Id="TEAS-TI-05-2026-0001">
  <!-- … invoice content … -->

  <ds:Signature xmlns:ds="http://www.w3.org/2000/09/xmldsig#" Id="Signature1">
    <ds:SignedInfo>
      <ds:CanonicalizationMethod Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315"/>
      <ds:SignatureMethod Algorithm="http://www.w3.org/2001/04/xmldsig-more#rsa-sha512"/>

      <!-- Reference 1: the invoice itself (enveloped) -->
      <ds:Reference URI="#TEAS-TI-05-2026-0001">
        <ds:Transforms>
          <ds:Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature"/>
          <ds:Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315"/>
        </ds:Transforms>
        <ds:DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha512"/>
        <ds:DigestValue>BASE64...</ds:DigestValue>
      </ds:Reference>

      <!-- Reference 2: the XAdES SignedProperties (counter-signed via reference) -->
      <!-- Transform = Exclusive C14N is mandatory — see §1 errata -->
      <ds:Reference Type="http://uri.etsi.org/01903#SignedProperties"
                    URI="#SignedProperties1">
        <ds:Transforms>
          <ds:Transform Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#"/>
        </ds:Transforms>
        <ds:DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha512"/>
        <ds:DigestValue>BASE64...</ds:DigestValue>
      </ds:Reference>
    </ds:SignedInfo>

    <ds:SignatureValue>BASE64-RSA-SHA512...</ds:SignatureValue>

    <ds:KeyInfo>
      <ds:X509Data>
        <ds:X509Certificate>BASE64-DER...</ds:X509Certificate>
        <!-- Include the full chain (signer + intermediates + root) -->
      </ds:X509Data>
    </ds:KeyInfo>

    <ds:Object>
      <xades:QualifyingProperties
          xmlns:xades="http://uri.etsi.org/01903/v1.3.2#"
          Target="#Signature1">
        <xades:SignedProperties Id="SignedProperties1">
          <xades:SignedSignatureProperties>
            <xades:SigningTime>2026-05-16T14:30:00.000+07:00</xades:SigningTime>
            <xades:SigningCertificate>
              <xades:Cert>
                <xades:CertDigest>
                  <ds:DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha512"/>
                  <ds:DigestValue>BASE64-SHA512-of-cert-DER...</ds:DigestValue>
                </xades:CertDigest>
                <xades:IssuerSerial>
                  <ds:X509IssuerName>CN=Thai Digital ID CA G3, O=TDID, C=TH</ds:X509IssuerName>
                  <ds:X509SerialNumber>1234567890</ds:X509SerialNumber>
                </xades:IssuerSerial>
              </xades:Cert>
              <!-- Include each cert in the chain as a separate <xades:Cert> -->
            </xades:SigningCertificate>
          </xades:SignedSignatureProperties>

          <!-- Optional but recommended for traceability -->
          <xades:SignedDataObjectProperties>
            <xades:DataObjectFormat ObjectReference="#Reference1">
              <xades:MimeType>text/xml</xades:MimeType>
            </xades:DataObjectFormat>
          </xades:SignedDataObjectProperties>
        </xades:SignedProperties>
      </xades:QualifyingProperties>
    </ds:Object>
  </ds:Signature>
</TaxInvoice>
```

**Critical: namespace prefix consistency.** ETDA's validator rejects signatures with prefix mismatches between `<ds:Signature>` and the canonicalized digest input. Lock prefixes to `ds:` and `xades:` everywhere.

---

## 3. .NET 10 Implementation Pattern

`System.Security.Cryptography.Xml.SignedXml` does **NOT** natively support XAdES `QualifyingProperties` — we have to:
1. Build XML DSig with `SignedXml`
2. Manually inject the `<xades:QualifyingProperties>` block as a `DataObject`
3. Add a second `Reference` pointing at `SignedProperties1`
4. Let `SignedXml.ComputeSignature()` calculate both digests + signature value

### 3.1 Project layout

```
src/Accounting.Infrastructure/ETax/
  ├─ EtaxSigningOptions.cs       ← bound to "ETax:Signing" config
  ├─ IXadesBesSigner.cs          ← interface for DI
  ├─ XadesBesSigner.cs           ← the implementation below
  ├─ XadesNs.cs                  ← namespace + URI constants
  └─ QualifyingPropertiesBuilder.cs  ← builds the XAdES XML fragment
```

### 3.2 Namespace constants

```csharp
namespace Accounting.Infrastructure.ETax;

public static class XadesNs
{
    public const string DSig            = "http://www.w3.org/2000/09/xmldsig#";
    public const string Xades132        = "http://uri.etsi.org/01903/v1.3.2#";
    public const string SignedPropsType = "http://uri.etsi.org/01903#SignedProperties";

    public const string AlgC14N         = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    public const string AlgEnveloped    = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
    public const string AlgSha512       = "http://www.w3.org/2001/04/xmlenc#sha512";
    public const string AlgRsaSha512    = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512";
}
```

### 3.3 QualifyingProperties builder

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Accounting.Infrastructure.ETax;

public sealed class QualifyingPropertiesBuilder
{
    public XmlElement Build(
        XmlDocument owner,
        string signatureId,
        string signedPropsId,
        X509Certificate2 signingCert,
        IEnumerable<X509Certificate2> chain,
        DateTimeOffset signingTime,
        string dataObjectReferenceId)
    {
        var qp = owner.CreateElement("xades", "QualifyingProperties", XadesNs.Xades132);
        qp.SetAttribute("Target", "#" + signatureId);

        var sp = owner.CreateElement("xades", "SignedProperties", XadesNs.Xades132);
        sp.SetAttribute("Id", signedPropsId);

        // ── SignedSignatureProperties ─────────────────────────────────
        var ssp = owner.CreateElement("xades", "SignedSignatureProperties", XadesNs.Xades132);

        // SigningTime — ISO-8601 with timezone offset (Asia/Bangkok)
        var st = owner.CreateElement("xades", "SigningTime", XadesNs.Xades132);
        st.InnerText = signingTime.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        ssp.AppendChild(st);

        // SigningCertificate — one <Cert> per cert in the chain
        var sc = owner.CreateElement("xades", "SigningCertificate", XadesNs.Xades132);
        foreach (var cert in chain.Prepend(signingCert).Distinct())
            sc.AppendChild(BuildCertElement(owner, cert));
        ssp.AppendChild(sc);

        sp.AppendChild(ssp);

        // ── SignedDataObjectProperties (optional but recommended) ─────
        var sdop = owner.CreateElement("xades", "SignedDataObjectProperties", XadesNs.Xades132);
        var dof = owner.CreateElement("xades", "DataObjectFormat", XadesNs.Xades132);
        dof.SetAttribute("ObjectReference", "#" + dataObjectReferenceId);

        var mime = owner.CreateElement("xades", "MimeType", XadesNs.Xades132);
        mime.InnerText = "text/xml";
        dof.AppendChild(mime);
        sdop.AppendChild(dof);
        sp.AppendChild(sdop);

        qp.AppendChild(sp);
        return qp;
    }

    private static XmlElement BuildCertElement(XmlDocument owner, X509Certificate2 cert)
    {
        var c = owner.CreateElement("xades", "Cert", XadesNs.Xades132);

        // CertDigest
        var cd = owner.CreateElement("xades", "CertDigest", XadesNs.Xades132);

        var dm = owner.CreateElement("ds", "DigestMethod", XadesNs.DSig);
        dm.SetAttribute("Algorithm", XadesNs.AlgSha512);
        cd.AppendChild(dm);

        var dv = owner.CreateElement("ds", "DigestValue", XadesNs.DSig);
        dv.InnerText = Convert.ToBase64String(SHA512.HashData(cert.RawData));
        cd.AppendChild(dv);

        c.AppendChild(cd);

        // IssuerSerial — RFC 4514 issuer name + serial as decimal
        var iss = owner.CreateElement("xades", "IssuerSerial", XadesNs.Xades132);

        var name = owner.CreateElement("ds", "X509IssuerName", XadesNs.DSig);
        name.InnerText = cert.Issuer;
        iss.AppendChild(name);

        var serial = owner.CreateElement("ds", "X509SerialNumber", XadesNs.DSig);
        // Cert serial is hex-stored; ETDA expects DECIMAL
        serial.InnerText = HexToDecimal(cert.SerialNumber);
        iss.AppendChild(serial);

        c.AppendChild(iss);
        return c;
    }

    private static string HexToDecimal(string hex) =>
        System.Numerics.BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber).ToString();
}
```

### 3.4 The signer

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Accounting.Infrastructure.ETax;

public interface IXadesBesSigner
{
    /// <summary>Signs <paramref name="document"/> in place and returns the resulting XML.</summary>
    XmlDocument Sign(XmlDocument document, string rootIdAttributeValue);
}

public sealed class XadesBesSigner : IXadesBesSigner
{
    private readonly X509Certificate2 _signingCert;
    private readonly X509Certificate2[] _chain;
    private readonly QualifyingPropertiesBuilder _qpBuilder;
    private readonly TimeProvider _clock;

    public XadesBesSigner(
        X509Certificate2 signingCert,
        IEnumerable<X509Certificate2> chain,
        QualifyingPropertiesBuilder qpBuilder,
        TimeProvider clock)
    {
        _signingCert = signingCert;
        _chain       = chain.ToArray();
        _qpBuilder   = qpBuilder;
        _clock       = clock;
    }

    public XmlDocument Sign(XmlDocument document, string rootIdAttributeValue)
    {
        // Make sure the root has Id — XAdES references it by URI fragment.
        var root = document.DocumentElement
            ?? throw new ArgumentException("Document has no root element.");

        if (!root.HasAttribute("Id"))
            root.SetAttribute("Id", rootIdAttributeValue);

        var signatureId     = $"Signature-{rootIdAttributeValue}";
        var signedPropsId   = $"SignedProperties-{rootIdAttributeValue}";
        var dataObjectRefId = $"Reference-{rootIdAttributeValue}";

        var signedXml = new SignedXml(document)
        {
            SigningKey = _signingCert.GetRSAPrivateKey(),
            Signature = { Id = signatureId },
            SignedInfo =
            {
                CanonicalizationMethod = XadesNs.AlgC14N,
                SignatureMethod        = XadesNs.AlgRsaSha512,
            },
        };

        // ── Reference 1: enveloped data ────────────────────────────────
        var dataRef = new Reference
        {
            Uri          = "#" + rootIdAttributeValue,
            Id           = dataObjectRefId,
            DigestMethod = XadesNs.AlgSha512,
        };
        dataRef.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        dataRef.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(dataRef);

        // ── XAdES QualifyingProperties as a DataObject ─────────────────
        var qp = _qpBuilder.Build(
            document, signatureId, signedPropsId,
            _signingCert, _chain,
            _clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)),  // Asia/Bangkok
            dataObjectRefId);

        var dataObject = new DataObject { Data = qp.SelectNodes(".") };
        signedXml.AddObject(dataObject);

        // ── Reference 2: SignedProperties (XAdES-specific) ─────────────
        // ⚠ Must use Exclusive C14N transform — see §1 errata. Without this,
        //   .NET round-trip CheckSignature fails because the SignedProperties
        //   element is canonicalized as a standalone fragment at sign time but
        //   as an in-tree node at verify time (ancestor namespaces differ).
        var spRef = new Reference
        {
            Uri          = "#" + signedPropsId,
            Type         = XadesNs.SignedPropsType,
            DigestMethod = XadesNs.AlgSha512,
        };
        spRef.AddTransform(new XmlDsigExcC14NTransform());   // Exclusive C14N — REQUIRED
        signedXml.AddReference(spRef);

        // ── KeyInfo: include the full cert chain ───────────────────────
        var keyInfo = new KeyInfo();
        var x509 = new KeyInfoX509Data();
        foreach (var c in new[] { _signingCert }.Concat(_chain))
            x509.AddCertificate(c);
        keyInfo.AddClause(x509);
        signedXml.KeyInfo = keyInfo;

        // Compute the signature
        signedXml.ComputeSignature();

        // Append <ds:Signature> into the document root (enveloped)
        var sigEl = signedXml.GetXml();
        root.AppendChild(document.ImportNode(sigEl, deep: true));

        return document;
    }
}
```

### 3.5 DI registration

```csharp
// Accounting.Infrastructure/DependencyInjection.cs
services.AddSingleton<QualifyingPropertiesBuilder>();
services.AddScoped<IXadesBesSigner>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<EtaxSigningOptions>>().Value;
    var cert = new X509Certificate2(opts.PfxPath, opts.PfxPassword,
                                    X509KeyStorageFlags.MachineKeySet
                                  | X509KeyStorageFlags.PersistKeySet
                                  | X509KeyStorageFlags.Exportable);

    // Build chain — for production, use X509Chain.Build() and validate against CA roots
    var chain = LoadCertChain(opts.PfxPath, opts.PfxPassword);

    return new XadesBesSigner(
        cert, chain,
        sp.GetRequiredService<QualifyingPropertiesBuilder>(),
        sp.GetRequiredService<TimeProvider>());
});
```

---

## 4. Two-Signature Pattern (ETDA requires)

Per RD/ETDA spec, **2 signatures** are needed: one from the issuing software, one from the signer (the organization). The pattern:

1. Sign the invoice with the **software cert** (TEAS-issued — could be from your CA registration).
2. Re-sign the now-signed XML with the **organization cert** (the company's CA Class 2). This is added as a second `<ds:Signature>` sibling within the root.

For Phase 1 (SME, single-tenant TEAS install), the **same cert can be used twice** — but each `<ds:Signature>` must have a distinct `Id` and reference its own `SignedProperties`.

---

## 5. Validation Checklist Before Submitting

- [ ] Both `<ds:Reference>` elements present (data + SignedProperties)
- [ ] `SignedProperties1` referenced via `#SignedProperties-XXX`, Type matches XAdES URI
- [ ] `SigningTime` ISO-8601 with `+07:00` offset, milliseconds present
- [ ] `CertDigest` is SHA-512 of the **raw DER** bytes
- [ ] `X509SerialNumber` is **decimal**, not hex
- [ ] Cert chain in `KeyInfo` includes root + intermediates (not just leaf)
- [ ] XML canonicalized with C14N inclusive (no comments)
- [ ] Namespace prefixes locked to `ds:` and `xades:`
- [ ] BOM stripped from XML output
- [ ] Self-verify with `signedXml.CheckSignature(_signingCert, verifySignatureOnly: true)` before sending

---

## 6. Test Vectors

For unit/integration tests in `Accounting.Infrastructure.Tests`:

1. **Self-signed dev cert** generated via `dotnet user-jwts` or PowerShell `New-SelfSignedCertificate`.
2. Sign a fixture XML → re-load → call `signedXml.CheckSignature(cert, true)` → assert `true`.
3. **Negative test:** tamper one byte in the invoice content after signing → `CheckSignature` must return `false`.
4. **Negative test:** swap the certificate → `CheckSignature` must return `false`.
5. Use [XML Signature Online Verifier](https://tools.chilkat.io/xmlDsigVerify.cshtml) or `xmlsec1` CLI for cross-validation.

For RD UAT, ETDA provides a sandbox at the official portal — submit a signed test invoice and ensure they parse `<xades:SigningCertificate>` and `<xades:SigningTime>` without error.

---

## 7. Common Pitfalls (Java sample → .NET porting)

| Java/xades4j | .NET 10 / SignedXml | Note |
|---|---|---|
| `DefaultAlgorithmsProviderEx.getDigestAlgorithmForDataObjsReferences()` | Set `Reference.DigestMethod` explicitly | xades4j defaults to SHA-1; we override to SHA-512 |
| `PKCS11KeyStoreKeyingDataProvider` | `RSACng` or `RSAOpenSsl` via custom `RSA` impl from HSM | Phase 1 = PFX; Phase 2 = HSM (Azure Key Vault) |
| `FirstCertificateSelector` | Manual — pick `X509Certificate2` from `X509Store` | If multiple certs in PFX, choose by friendly name or thumbprint |
| `EnvelopedSignatureTransform` | `XmlDsigEnvelopedSignatureTransform` | Same algorithm URI |
| `xades4j.production.XadesBesSigner` | Hand-roll using `SignedXml + DataObject + Reference` | See §3.4 above |
| `getSignatureAlgorithm(...)` returns `RSA_SHA512` | `signedXml.SignedInfo.SignatureMethod = AlgRsaSha512` | Must match key type |

---

## 8. Sources

- [ETDA/etax-xades — official Java sample](https://github.com/ETDA/etax-xades)
- [XadesBesSigner.java — direct source](https://github.com/ETDA/etax-xades/blob/master/src/main/java/XadesBesSigner.java)
- [ETSI TS 101 903 v1.4.1 — XAdES standard](https://www.etsi.org/deliver/etsi_ts/101900_101999/101903/01.04.01_60/ts_101903v010401p.pdf)
- [W3C XML Signature Syntax and Processing (Second Edition)](https://www.w3.org/TR/xmldsig-core/)
- [ETDA TEDA schemas (XSD + Schematron)](https://schemas.teda.th/teda/teda-objects/common/e-tax-invoice-receipt)
- [.NET `System.Security.Cryptography.Xml.SignedXml` docs](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.xml.signedxml)
- See also: `docs/accounting-system-plan.md` §13 (e-Tax Invoice & e-Receipt Module) and §13.1.1 (Digital Certificate Requirements)

---

**End of spec.** Claude Code should treat §1 (algorithm choices) and §5 (validation checklist) as non-negotiable — every other section is illustrative pattern.
