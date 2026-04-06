using System.Net.Sockets;
using System.Text;
using Lanny.Models;

namespace Lanny.Discovery;

public class SshBannerProbe : IServiceFingerprintProbe
{
    private readonly ILogger<SshBannerProbe> _logger;

    public SshBannerProbe(ILogger<SshBannerProbe> logger)
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
            await tcpClient.ConnectAsync(device.IpAddress, 22, cancellationToken);

            await using var stream = tcpClient.GetStream();
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return null;

            var banner = SshBannerParser.Parse(Encoding.ASCII.GetString(buffer, 0, read));
            if (string.IsNullOrWhiteSpace(banner))
                return null;

            return new Device
            {
                IpAddress = device.IpAddress,
                SshBanner = banner,
                DiscoveryMethod = "SSH",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "SSH banner probe failed for {IpAddress}", device.IpAddress);
            return null;
        }
    }
}