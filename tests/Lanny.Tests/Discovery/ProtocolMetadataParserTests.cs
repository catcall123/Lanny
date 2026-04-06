using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class ProtocolMetadataParserTests
{
    [Fact]
    public void ParseHttpResponse_ExtractsTitleAndHeaders()
    {
        var metadata = HttpFingerprintParser.Parse(
            new Dictionary<string, string>
            {
                ["server"] = "nginx/1.25.5",
                ["x-powered-by"] = "Express",
            },
            "<html><head><title>Router Admin</title></head><body></body></html>");

        Assert.Equal("Router Admin", metadata.Title);
        Assert.Equal("nginx/1.25.5", metadata.Headers["server"]);
        Assert.Equal("Express", metadata.Headers["x-powered-by"]);
    }

    [Fact]
    public void ParseCertificate_ExtractsSubjectAndSubjectAlternativeNames()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=router.local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("router.local");
        sanBuilder.AddDnsName("router");
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var metadata = TlsCertificateMetadataParser.Parse(certificate);

        Assert.Equal("CN=router.local", metadata.Subject);
        Assert.Equal(["router.local", "router"], metadata.SubjectAlternativeNames);
    }

    [Fact]
    public void ParseSshBanner_TrimsWhitespaceAndLineTerminators()
    {
        var banner = SshBannerParser.Parse("SSH-2.0-OpenSSH_9.6\r\n");

        Assert.Equal("SSH-2.0-OpenSSH_9.6", banner);
    }
}