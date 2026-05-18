namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Namespace + algorithm URI constants for the ETDA XAdES-BES profile.
/// Values are NON-NEGOTIABLE — they mirror docs/etax-xades-spec.md §1 and the official
/// ETDA Java sample. RD's validator is strict; do not "modernise" these to SHA-256.
/// </summary>
public static class XadesNs
{
    public const string DSig            = "http://www.w3.org/2000/09/xmldsig#";
    public const string Xades132        = "http://uri.etsi.org/01903/v1.3.2#";
    public const string SignedPropsType = "http://uri.etsi.org/01903#SignedProperties";

    public const string AlgC14N      = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    public const string AlgEnveloped = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
    public const string AlgSha512    = "http://www.w3.org/2001/04/xmlenc#sha512";
    public const string AlgRsaSha512 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512";
}
