using System.Collections.Concurrent;
using Lanny.Models;

namespace Lanny.Discovery;

internal sealed class PassiveObservationCache
{
    private readonly ConcurrentDictionary<string, Device> _observations = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(string key, Device observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(observation);

        _observations[key] = observation;
    }

    public IReadOnlyList<Device> GetSnapshot(TimeSpan retention, DateTimeOffset now)
    {
        var cutoff = now - retention;
        foreach (var (key, observation) in _observations)
        {
            if (observation.LastSeen < cutoff)
                _observations.TryRemove(key, out _);
        }

        return _observations.Values.Select(Clone).ToList();
    }

    private static Device Clone(Device device)
    {
        return new Device
        {
            MacAddress = device.MacAddress,
            IpAddress = device.IpAddress,
            Hostname = device.Hostname,
            Vendor = device.Vendor,
            SystemName = device.SystemName,
            SystemDescription = device.SystemDescription,
            SystemObjectId = device.SystemObjectId,
            SystemUptime = device.SystemUptime,
            InterfaceCount = device.InterfaceCount,
            HttpTitle = device.HttpTitle,
            HttpHeaders = device.HttpHeaders is null
                ? null
                : new Dictionary<string, string>(device.HttpHeaders, StringComparer.OrdinalIgnoreCase),
            TlsCertificateSubject = device.TlsCertificateSubject,
            TlsSubjectAlternativeNames = device.TlsSubjectAlternativeNames is null
                ? null
                : [.. device.TlsSubjectAlternativeNames],
            SshBanner = device.SshBanner,
            DiscoveryMethod = device.DiscoveryMethod,
            OpenPorts = [.. device.OpenPorts],
            FirstSeen = device.FirstSeen,
            LastSeen = device.LastSeen,
            IsOnline = device.IsOnline,
        };
    }
}
