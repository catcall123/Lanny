using System.Collections.Concurrent;
using Lanny.Discovery;
using Lanny.Models;
using Microsoft.EntityFrameworkCore;

namespace Lanny.Data;

public class DeviceRepository
{
    private readonly ConcurrentDictionary<string, Device> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceRepository> _logger;

    public DeviceRepository(IServiceScopeFactory scopeFactory, ILogger<DeviceRepository> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LoadFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var devices = await SqliteBusyRetryPolicy.ExecuteAsync(
            async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
                var devices = await db.Devices.ToListAsync(token);
                var noisyDevices = devices.Where(ArpEntryFilter.IsNoiseOnlyDevice).ToList();
                if (noisyDevices.Count > 0)
                {
                    db.Devices.RemoveRange(noisyDevices);
                    await db.SaveChangesAsync(token);
                    devices = devices.Except(noisyDevices).ToList();
                }

                return devices;
            },
            _logger,
            "load devices from the database",
            cancellationToken);

        _cache.Clear();
        foreach (var d in devices)
        {
            d.DiscoveryMethod = DiscoveryMethodSet.Normalize(d.DiscoveryMethod);
            _cache[d.MacAddress] = d;
        }

        _logger.LogInformation("Loaded {Count} devices from database", devices.Count);
    }

    public IReadOnlyCollection<Device> GetAll() => _cache.Values.ToList().AsReadOnly();

    public Device? Get(string mac)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mac);
        return _cache.GetValueOrDefault(mac);
    }

    public async Task<Device> UpsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.MacAddress);

        var existing = _cache.GetValueOrDefault(device.MacAddress);
        if (existing is not null)
        {
            existing.DiscoveryMethod = DiscoveryMethodSet.Normalize(existing.DiscoveryMethod);
            existing.IpAddress = device.IpAddress ?? existing.IpAddress;
            existing.Hostname = device.Hostname ?? existing.Hostname;
            existing.Vendor = device.Vendor ?? existing.Vendor;
            existing.SystemName = device.SystemName ?? existing.SystemName;
            existing.SystemDescription = device.SystemDescription ?? existing.SystemDescription;
            existing.SystemObjectId = device.SystemObjectId ?? existing.SystemObjectId;
            existing.SystemUptime = device.SystemUptime ?? existing.SystemUptime;
            existing.InterfaceCount = device.InterfaceCount ?? existing.InterfaceCount;
            existing.HttpTitle = device.HttpTitle ?? existing.HttpTitle;
            existing.TlsCertificateSubject = device.TlsCertificateSubject ?? existing.TlsCertificateSubject;
            existing.SshBanner = device.SshBanner ?? existing.SshBanner;
            if (device.HttpHeaders is not null)
            {
                existing.HttpHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in device.HttpHeaders)
                {
                    existing.HttpHeaders.TryAdd(key, value);
                }
            }

            if (device.TlsSubjectAlternativeNames is not null)
            {
                existing.TlsSubjectAlternativeNames ??= [];
                foreach (var subjectAlternativeName in device.TlsSubjectAlternativeNames)
                {
                    if (!existing.TlsSubjectAlternativeNames.Contains(subjectAlternativeName, StringComparer.OrdinalIgnoreCase))
                        existing.TlsSubjectAlternativeNames.Add(subjectAlternativeName);
                }
            }

            existing.LastSeen = device.LastSeen;
            existing.IsOnline = true;
            existing.DiscoveryMethod = DiscoveryMethodSet.Merge(existing.DiscoveryMethod, device.DiscoveryMethod);
        }
        else
        {
            device.DiscoveryMethod = DiscoveryMethodSet.Normalize(device.DiscoveryMethod);
            device.FirstSeen = device.LastSeen;
            device.IsOnline = true;
            _cache[device.MacAddress] = device;
            existing = device;
        }

        await PersistAsync(existing, cancellationToken);
        return existing;
    }

    public void MarkOffline(string mac)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mac);

        if (_cache.TryGetValue(mac, out var device))
            device.IsOnline = false;
    }

    public async Task<int> PruneOfflineDevicesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var staleKeys = _cache
            .Where(entry => !entry.Value.IsOnline && entry.Value.LastSeen < cutoff)
            .Select(entry => entry.Key)
            .ToList();

        if (staleKeys.Count == 0)
            return 0;

        foreach (var staleKey in staleKeys)
            _cache.TryRemove(staleKey, out _);

        await SqliteBusyRetryPolicy.ExecuteAsync(
            async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
                var staleDevices = await db.Devices
                    .Where(device => staleKeys.Contains(device.MacAddress))
                    .ToListAsync(token);

                db.Devices.RemoveRange(staleDevices);
                await db.SaveChangesAsync(token);
            },
            _logger,
            "prune stale offline devices",
            cancellationToken);

        _logger.LogInformation("Pruned {Count} stale offline devices from the repository", staleKeys.Count);
        return staleKeys.Count;
    }

    public async Task PersistAllAsync(CancellationToken cancellationToken = default)
    {
        await SqliteBusyRetryPolicy.ExecuteAsync(
            async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

                foreach (var device in _cache.Values)
                {
                    token.ThrowIfCancellationRequested();

                    var tracked = await db.Devices.FindAsync([device.MacAddress], token);
                    if (tracked is null)
                        db.Devices.Add(device);
                    else
                        db.Entry(tracked).CurrentValues.SetValues(device);
                }

                await db.SaveChangesAsync(token);
            },
            _logger,
            "persist all devices",
            cancellationToken);
    }

    private async Task PersistAsync(Device device, CancellationToken cancellationToken)
    {
        await SqliteBusyRetryPolicy.ExecuteAsync(
            async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

                var tracked = await db.Devices.FindAsync([device.MacAddress], token);
                if (tracked is null)
                    db.Devices.Add(device);
                else
                    db.Entry(tracked).CurrentValues.SetValues(device);

                await db.SaveChangesAsync(token);
            },
            _logger,
            $"persist device {device.MacAddress}",
            cancellationToken);
    }
}
