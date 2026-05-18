using System.Numerics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Builds the <c>xades:QualifyingProperties</c> / <c>SignedProperties</c> fragment that the
/// XAdES-BES profile requires (docs/etax-xades-spec.md §3.3). The fragment is added to the
/// signature as a signed <c>ds:Object</c> + a second <c>ds:Reference</c>.
/// </summary>
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

        var ssp = owner.CreateElement("xades", "SignedSignatureProperties", XadesNs.Xades132);

        // SigningTime — ISO-8601 with timezone offset (Asia/Bangkok), milliseconds present.
        var st = owner.CreateElement("xades", "SigningTime", XadesNs.Xades132);
        st.InnerText = signingTime.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        ssp.AppendChild(st);

        // SigningCertificate — one <Cert> per cert in the chain (leaf first).
        var sc = owner.CreateElement("xades", "SigningCertificate", XadesNs.Xades132);
        foreach (var cert in new[] { signingCert }.Concat(chain)
                     .DistinctBy(c => c.Thumbprint))
            sc.AppendChild(BuildCertElement(owner, cert));
        ssp.AppendChild(sc);

        sp.AppendChild(ssp);

        // SignedDataObjectProperties — optional but recommended for traceability.
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

        var cd = owner.CreateElement("xades", "CertDigest", XadesNs.Xades132);
        var dm = owner.CreateElement("ds", "DigestMethod", XadesNs.DSig);
        dm.SetAttribute("Algorithm", XadesNs.AlgSha512);
        cd.AppendChild(dm);
        var dv = owner.CreateElement("ds", "DigestValue", XadesNs.DSig);
        // SHA-512 of the raw DER bytes (spec §5 checklist).
        dv.InnerText = Convert.ToBase64String(SHA512.HashData(cert.RawData));
        cd.AppendChild(dv);
        c.AppendChild(cd);

        var iss = owner.CreateElement("xades", "IssuerSerial", XadesNs.Xades132);
        var name = owner.CreateElement("ds", "X509IssuerName", XadesNs.DSig);
        name.InnerText = cert.Issuer;
        iss.AppendChild(name);
        var serial = owner.CreateElement("ds", "X509SerialNumber", XadesNs.DSig);
        // Cert serial is hex; ETDA expects DECIMAL (spec §5 checklist). Leading 0 keeps it positive.
        serial.InnerText = BigInteger
            .Parse("0" + cert.SerialNumber, NumberStyles.HexNumber)
            .ToString(CultureInfo.InvariantCulture);
        iss.AppendChild(serial);
        c.AppendChild(iss);

        return c;
    }
}
