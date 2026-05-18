using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Accounting.Infrastructure.ETax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.ETax;

/// <summary>
/// XAdES-BES signer tests per docs/etax-xades-spec.md §6. Uses an in-memory self-signed
/// RSA cert (no real CA / no RD submission) — proves the signature is well-formed and
/// self-verifies, and that the XAdES §1/§5 invariants hold.
/// </summary>
public sealed class XadesBesSignerTests
{
    private const string SampleXml =
        """<TaxInvoice xmlns="urn:etda:uncefact:data:standard:CrossIndustryInvoice:2"><DocNo>05-2026-TI-0001</DocNo><Total>1070.00</Total></TaxInvoice>""";

    private static X509Certificate2 NewSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=TEAS Dev Signing, O=TEAS, C=TH", rsa, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
    }

    private static XmlDocument Sign(X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(SampleXml);
        new XadesBesSigner(new QualifyingPropertiesBuilder())
            .Sign(doc, cert, chain: [], DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)),
                  rootIdFallback: "TEAS-TEST-0001");
        return doc;
    }

    // Round-trip now works: etax-xades-spec.md §1 errata corrected — SignedProperties
    // Reference uses Exclusive C14N (xades4j parity), so sign-time fragment and
    // verify-time in-tree canonical forms match.
    [Fact]
    public void Signed_document_self_verifies()
    {
        using var cert = NewSelfSigned();
        var doc = Sign(cert);

        var sig = (XmlElement)doc.GetElementsByTagName("Signature", XadesNs.DSig)[0]!;
        var sx = new SignedXml(doc);
        sx.LoadXml(sig);

        sx.CheckSignature(cert, verifySignatureOnly: true).Should().BeTrue();
    }

    [Fact]
    public void Tampered_content_fails_verification()
    {
        using var cert = NewSelfSigned();
        var doc = Sign(cert);

        // Flip the invoice total AFTER signing.
        doc.GetElementsByTagName("Total")[0]!.InnerText = "9999.99";

        var sig = (XmlElement)doc.GetElementsByTagName("Signature", XadesNs.DSig)[0]!;
        var sx = new SignedXml(doc);
        sx.LoadXml(sig);

        sx.CheckSignature(cert, verifySignatureOnly: true).Should().BeFalse();
    }

    [Fact]
    public void Different_certificate_fails_verification()
    {
        using var signer = NewSelfSigned();
        using var attacker = NewSelfSigned();
        var doc = Sign(signer);

        var sig = (XmlElement)doc.GetElementsByTagName("Signature", XadesNs.DSig)[0]!;
        var sx = new SignedXml(doc);
        sx.LoadXml(sig);

        sx.CheckSignature(attacker, verifySignatureOnly: true).Should().BeFalse();
    }

    [Fact]
    public void Survives_string_roundtrip_and_reparse()
    {
        using var cert = NewSelfSigned();
        var doc = Sign(cert);

        // Mirror the production path: serialize to a BOM-free UTF-8 byte stream,
        // round-trip through string, re-parse, then re-verify. Catches BOM/encoding
        // regressions that corrupt the canonicalized digest.
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(doc.OuterXml);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.Should().BeFalse("e-Tax XML must be BOM-free (spec §5)");

        var xml = new System.Text.UTF8Encoding(false).GetString(bytes);
        var reloaded = new XmlDocument { PreserveWhitespace = true };
        reloaded.LoadXml(xml);

        var sig = (XmlElement)reloaded.GetElementsByTagName("Signature", XadesNs.DSig)[0]!;
        var sx = new SignedXml(reloaded);
        sx.LoadXml(sig);

        sx.CheckSignature(cert, verifySignatureOnly: true).Should().BeTrue();
    }

    [Fact]
    public void Emits_mandatory_xades_profile_per_spec()
    {
        using var cert = NewSelfSigned();
        var doc = Sign(cert);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", XadesNs.DSig);
        ns.AddNamespace("xades", XadesNs.Xades132);

        // §1 algorithms — non-negotiable.
        doc.SelectSingleNode("//ds:SignatureMethod", ns)!.Attributes!["Algorithm"]!.Value
            .Should().Be(XadesNs.AlgRsaSha512);
        doc.SelectSingleNode("//ds:CanonicalizationMethod", ns)!.Attributes!["Algorithm"]!.Value
            .Should().Be(XadesNs.AlgC14N);
        foreach (XmlNode dm in doc.SelectNodes("//ds:DigestMethod", ns)!)
            dm.Attributes!["Algorithm"]!.Value.Should().Be(XadesNs.AlgSha512);

        // Two references: data + SignedProperties (typed).
        doc.SelectNodes("//ds:SignedInfo/ds:Reference", ns)!.Count.Should().Be(2);
        doc.SelectSingleNode("//ds:Reference[@Type]", ns)!.Attributes!["Type"]!.Value
            .Should().Be(XadesNs.SignedPropsType);

        // XAdES SignedProperties content.
        doc.SelectSingleNode("//xades:SignedProperties", ns).Should().NotBeNull();
        doc.SelectSingleNode("//xades:SigningTime", ns)!.InnerText
            .Should().MatchRegex(@"\+07:00$");
        var serial = doc.SelectSingleNode("//ds:X509SerialNumber", ns)!.InnerText;
        serial.Should().MatchRegex(@"^\d+$"); // decimal, not hex
    }
}
