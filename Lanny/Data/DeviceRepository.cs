using System.Collections.Concurrent;
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
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task LoadFromDatabaseAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        var devices = await db.Devices.AsNoTracking().ToListAsync();
        foreach (var d in devices)
            _cache[d.MacAddress] = d;
        _logger.LogInformation("Loaded {Count} devices from database", devices.Count);
    }

    public IReadOnlyCollection<Device> GetAll() => _cache.Values.ToList().AsReadOnly();

    public Device? Get(string mac) => _cache.GetValueOrDefault(mac);

    public async Task<Device> UpsertAsync(Device device)
    {
        var existing = _cache.GetValueOrDefault(device.MacAddress);
        if (existing is not null)
        {
            existing.IpAddress = device.IpAddress ?? existing.IpAddress;
            existing.Hostname = device.Hostname ?? existing.Hostname;
            existing.Vendor = device.Vendor ?? existing.Vendor;
            existing.LastSeen = device.LastSeen;
            existing.IsOnline = true;
            if (!string.IsNullOrEmpty(device.DiscoveryMethod) &&
                existing.DiscoveryMethod?.Contains(device.DiscoveryMethod, StringComparison.OrdinalIgnoreCase) != true)
            {
                existing.DiscoveryMethod = string.IsNullOrEmpty(existing.DiscoveryMethod)
                    ? device.DiscoveryMethod
                    : $"{existing.DiscoveryMethod},{device.DiscoveryMethod}";
            }
        }
        else
        {
            device.FirstSeen = device.LastSeen;
            device.IsOnline = true;
            _cache[device.MacAddress] = device;
            existing = device;
        }

        await PersistAsync(existing);
        return existing;
    }

    public void MarkOffline(string mac)
    {
        if (_cache.TryGetValue(mac, out var device))
            device.IsOnline = false;
    }

    public async Task PersistAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

        foreach (var device in _cache.Values)
        {
            var tracked = await db.Devices.FindAsync(device.MacAddress);
            if (tracked is null)
                db.Devices.Add(device);
            else
                db.Entry(tracked).CurrentValues.SetValues(device);
        }

        await db.SaveChangesAsync();
    }

    private async Task PersistAsync(Device device)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

        var tracked = await db.Devices.FindAsync(device.MacAddress);
        if (tracked is null)
            db.Devices.Add(device);
        else
            db.Entry(tracked).CurrentValues.SetValues(device);

        await db.SaveChangesAsync();
    }
}
