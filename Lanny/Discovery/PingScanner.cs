using System.Net;
using System.Net.NetworkInformation;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

/// <summary>ICMP ping sweep across the configured subnet.</summary>
public class PingScanner : IDiscoveryService
{
    private readonly IHostNameResolver _hostNameResolver;
    private readonly ILogger<PingScanner> _logger;
    private readonly ScanSettings _settings;

    public string Name => "Ping";

    public PingScanner(
        IHostNameResolver hostNameResolver,
        ILogger<PingScanner> logger,
        IOptions<ScanSettings> settings)
    {
        _hostNameResolver = hostNameResolver ?? throw new ArgumentNullException(nameof(hostNameResolver));
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var (network, prefixLen) = ParseCidr(_settings.Subnet);
        var ips = GenerateAddresses(network, prefixLen);
        var devices = new List<Device>();
        var timeout = 1000; // ms

        // Ping in parallel batches
        var tasks = ips.Select(async ip =>
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeout);
                if (reply.Status == IPStatus.Success)
                {
                    var hostname = await _hostNameResolver.ResolveAsync(ip, ct);

                    return new Device
                    {
                        MacAddress = string.Empty, // Will be filled by correlating with ARP
                        IpAddress = ip.ToString(),
                        Hostname = hostname,
                        DiscoveryMethod = Name,
                        LastSeen = DateTimeOffset.UtcNow,
                    };
                }
            }
            catch { /* timeout or host unreachable */ }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        foreach (var d in results)
        {
            if (d is not null) devices.Add(d);
        }

        _logger.LogInformation("Ping sweep found {Count} responsive hosts", devices.Count);
        return devices;
    }

    private static (IPAddress network, int prefixLen) ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        return (IPAddress.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static List<IPAddress> GenerateAddresses(IPAddress network, int prefixLen)
    {
        var bytes = network.GetAddressBytes();
        var networkInt = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        var hostBits = 32 - prefixLen;
        var hostCount = (1u << hostBits) - 2; // exclude network and broadcast

        var addresses = new List<IPAddress>((int)hostCount);
        for (uint i = 1; i <= hostCount; i++)
        {
            var ip = networkInt + i;
            addresses.Add(new IPAddress(new[]
            {
                (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip
            }));
        }
        return addresses;
    }
}
