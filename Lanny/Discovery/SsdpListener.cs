using System.Net;
using System.Net.Sockets;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public sealed class SsdpListener : BackgroundService, IDiscoveryService
{
    private static readonly IPAddress SsdpMulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int SsdpPort = 1900;

    private readonly PassiveObservationCache _observations = new();
    private readonly ILogger<SsdpListener> _logger;
    private readonly ScanSettings _settings;
    private UdpClient? _udp;

    public SsdpListener(ILogger<SsdpListener> logger, IOptions<ScanSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "SSDP";

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var devices = _observations.GetSnapshot(GetObservationRetention(), DateTimeOffset.UtcNow);
        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableSsdpListener)
        {
            _logger.LogInformation("SSDP listener disabled by configuration");
            return;
        }

        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, SsdpPort));
            _udp.JoinMulticastGroup(SsdpMulticastAddress);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("SSDP listener could not bind UDP/{Port}: {Message}", SsdpPort, ex.Message);
            _udp?.Dispose();
            _udp = null;
            return;
        }

        _logger.LogInformation("SSDP listener bound to UDP/{Port}", SsdpPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(stoppingToken);
                if (SsdpMessageParser.TryParse(result.Buffer, result.RemoteEndPoint, DateTimeOffset.UtcNow, out var device) &&
                    device is not null &&
                    !string.IsNullOrWhiteSpace(device.IpAddress))
                {
                    _observations.Upsert(device.IpAddress, device);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSDP receive error: {Message}", ex.Message);
            }
        }
    }

    public override void Dispose()
    {
        _udp?.Dispose();
        _udp = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private TimeSpan GetObservationRetention()
    {
        var retentionMinutes = Math.Max(1, _settings.PassiveObservationRetentionMinutes);
        return TimeSpan.FromMinutes(retentionMinutes);
    }
}
