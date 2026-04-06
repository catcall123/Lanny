using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Lanny.Models;

namespace Lanny.Discovery;

public class TlsCertificateProbe : IServiceFingerprintProbe
{
    private readonly ILogger<TlsCertificateProbe> _logger;

    public TlsCertificateProbe(ILogger<TlsCertificateProbe> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(device.IpAddress, 443, cancellationToken);

            await using var stream = tcpClient.GetStream();
            using var sslStream = new SslStream(stream, false, static (_, _, _, _) => true);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = device.IpAddress,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, cancellationToken);

            if (sslStream.RemoteCertificate is null)
                return null;

            using var certificate = new X509Certificate2(sslStream.RemoteCertificate);
            var metadata = TlsCertificateMetadataParser.Parse(certificate);
            if (string.IsNullOrWhiteSpace(metadata.Subject) && metadata.SubjectAlternativeNames.Count == 0)
                return null;

            return new Device
            {
                IpAddress = device.IpAddress,
                TlsCertificateSubject = metadata.Subject,
                TlsSubjectAlternativeNames = metadata.SubjectAlternativeNames,
                DiscoveryMethod = "TLS",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "TLS certificate probe failed for {IpAddress}", device.IpAddress);
            return null;
        }
    }
}