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
        var devices = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        var queriedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetResult());

            void QueryServiceInstances(string serviceName)
            {
                if (string.IsNullOrWhiteSpace(serviceName))
                    return;

                lock (queriedServices)
                {
                    if (!queriedServices.Add(serviceName))
                        return;
                }

                sd.QueryServiceInstances(serviceName);
            }

            sd.ServiceDiscovered += (_, serviceName) =>
            {
                QueryServiceInstances(serviceName.ToString());
            };

            sd.ServiceInstanceDiscovered += (_, args) =>
            {
                var serviceInstanceName = args.ServiceInstanceName.ToString();
                _logger.LogDebug("mDNS discovered service: {Name}", serviceInstanceName);

                var records = args.Message.Answers.Concat(args.Message.AdditionalRecords).ToArray();
                var serviceType = GetServiceType(args.ServiceInstanceName);
                var hostName = records
                    .OfType<SRVRecord>()
                    .Select(record => record.Target?.ToString())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var textRecords = records
                    .OfType<TXTRecord>()
                    .SelectMany(record => record.Strings);
                var addresses = records
                    .OfType<AddressRecord>()
                    .Select(record => record.Address);

                var device = MdnsDeviceDecoder.Decode(
                    serviceInstanceName,
                    serviceType,
                    hostName,
                    textRecords,
                    addresses,
                    DateTimeOffset.UtcNow);

                if (device is not null)
                {
                    var key = device.IpAddress ?? device.Hostname ?? serviceInstanceName;
                    lock (devices)
                    {
                        if (devices.TryGetValue(key, out var existing))
                        {
                            DeviceMetadataEnricher.MergeObservation(existing, device);
                        }
                        else
                        {
                            devices[key] = device;
                        }
                    }
                }
            };

            mdns.Start();

            sd.QueryAllServices();

            // Query common service types
            QueryServiceInstances("_http._tcp");
            QueryServiceInstances("_https._tcp");
            QueryServiceInstances("_ipp._tcp");
            QueryServiceInstances("_ipps._tcp");
            QueryServiceInstances("_printer._tcp");
            QueryServiceInstances("_scanner._tcp");
            QueryServiceInstances("_airplay._tcp");
            QueryServiceInstances("_raop._tcp");
            QueryServiceInstances("_googlecast._tcp");
            QueryServiceInstances("_smb._tcp");
            QueryServiceInstances("_ssh._tcp");
            QueryServiceInstances("_hap._tcp");
            QueryServiceInstances("_device-info._tcp");
            QueryServiceInstances("_workstation._tcp");

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
        return devices.Values.ToList();
    }

    private static string? GetServiceType(DomainName serviceInstanceName)
    {
        var labels = serviceInstanceName.Labels.ToArray();
        for (var i = 0; i < labels.Length - 1; i++)
        {
            if (labels[i].StartsWith('_') && labels[i + 1].StartsWith('_'))
                return $"{labels[i]}.{labels[i + 1]}";
        }

        return null;
    }
}
