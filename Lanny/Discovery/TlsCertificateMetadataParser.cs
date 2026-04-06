using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Lanny.Discovery;

public static class TlsCertificateMetadataParser
{
    private static readonly Asn1Tag DnsNameTag = new(TagClass.ContextSpecific, 2);

    public static TlsCertificateMetadata Parse(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new TlsCertificateMetadata(
            certificate.Subject,
            ParseSubjectAlternativeNames(certificate));
    }

    private static List<string> ParseSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions["2.5.29.17"];
        if (extension is null)
            return [];

        var sans = new List<string>();
        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        while (sequence.HasData)
        {
            var tag = sequence.PeekTag();
            if (tag.HasSameClassAndValue(DnsNameTag))
            {
                sans.Add(sequence.ReadCharacterString(UniversalTagNumber.IA5String, DnsNameTag));
            }
            else
            {
                sequence.ReadEncodedValue();
            }
        }

        return sans;
    }
}