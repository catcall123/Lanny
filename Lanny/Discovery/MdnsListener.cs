using Lanny.Models;
using Makaretu.Dns;

namespace Lanny.Discovery;

/// <summary>Listens for mDNS/DNS-SD service announcements on the local network.</summary>
public class MdnsListener : IDiscoveryService
{
    private readonly ILogger<MdnsListener> _logger;

    public string Name => "mDNS";

    public MdnsListener(ILogger<MdnsListener> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var devices = new List<Device>();

        try
        {
            using var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetResult());

            sd.ServiceInstanceDiscovered += (_, args) =>
            {
                var name = args.ServiceInstanceName.ToString();
                _logger.LogDebug("mDNS discovered service: {Name}", name);

                // Extract hostname from the instance name
                var hostname = args.ServiceInstanceName.Labels.FirstOrDefault();

                // Look for A/AAAA records in Additional
                string? ip = null;
                foreach (var record in args.Message.AdditionalRecords.OfType<AddressRecord>())
                {
                    ip = record.Address.ToString();
                    break;
                }

                if (hostname is not null)
                {
                    lock (devices)
                    {
                        devices.Add(new Device
                        {
                            MacAddress = string.Empty, // mDNS doesn't provide MAC
                            IpAddress = ip,
                            Hostname = hostname,
                            DiscoveryMethod = Name,
                            LastSeen = DateTimeOffset.UtcNow,
                        });
                    }
                }
            };

            mdns.Start();

            // Query common service types
            sd.QueryServiceInstances("_http._tcp");
            sd.QueryServiceInstances("_https._tcp");
            sd.QueryServiceInstances("_ipp._tcp");       // printers
            sd.QueryServiceInstances("_printer._tcp");
            sd.QueryServiceInstances("_airplay._tcp");
            sd.QueryServiceInstances("_raop._tcp");       // AirPlay audio
            sd.QueryServiceInstances("_googlecast._tcp");
            sd.QueryServiceInstances("_smb._tcp");
            sd.QueryServiceInstances("_ssh._tcp");
            sd.QueryServiceInstances("_hap._tcp");        // HomeKit

            // Listen for a few seconds
            await Task.WhenAny(tcs.Task, Task.Delay(5000, ct));
            mdns.Stop();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mDNS discovery failed");
        }

        _logger.LogInformation("mDNS discovered {Count} services", devices.Count);
        return devices;
    }
}
