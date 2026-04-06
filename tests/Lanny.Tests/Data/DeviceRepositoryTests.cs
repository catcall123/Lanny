using Lanny.Data;
using Lanny.Models;
using Lanny.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lanny.Tests.Data;

public class DeviceRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_NewDevice_SetsFirstSeenOnlineAndPersists()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var lastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);

        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            Hostname = "nas",
            Vendor = "Synology",
            DiscoveryMethod = "ARP",
            OpenPorts = [80, 443],
            LastSeen = lastSeen,
        };

        var stored = await repository.UpsertAsync(device);

        Assert.True(stored.IsOnline);
        Assert.Equal(lastSeen, stored.FirstSeen);
        Assert.Equal(new[] { 80, 443 }, stored.OpenPorts);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        var persisted = await db.Devices.SingleAsync(d => d.MacAddress == device.MacAddress);

        Assert.True(persisted.IsOnline);
        Assert.Equal(lastSeen, persisted.FirstSeen);
        Assert.Equal(new[] { 80, 443 }, persisted.OpenPorts);
    }

    [Fact]
    public async Task UpsertAsync_ExistingDevice_MergesFieldsAndAvoidsDuplicateDiscoveryMethods()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            Vendor = "HP",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen,
        });

        await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Hostname = "printer.local",
            DiscoveryMethod = "Ping",
            LastSeen = firstSeen.AddMinutes(1),
        });

        var merged = await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            DiscoveryMethod = "Ping",
            LastSeen = firstSeen.AddMinutes(2),
        });

        Assert.Equal("192.168.1.10", merged.IpAddress);
        Assert.Equal("printer.local", merged.Hostname);
        Assert.Equal("HP", merged.Vendor);
        Assert.Equal("ARP,Ping", merged.DiscoveryMethod);
        Assert.Equal(firstSeen, merged.FirstSeen);
        Assert.Equal(firstSeen.AddMinutes(2), merged.LastSeen);
    }

    [Fact]
    public async Task LoadFromDatabaseAsync_LoadsPersistedDevicesIntoCache()
    {
        await using var host = await SqliteTestHost.CreateAsync();

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
            db.Devices.Add(new Device
            {
                MacAddress = "10:20:30:40:50:60",
                IpAddress = "192.168.1.20",
                Hostname = "seeded-host",
                DiscoveryMethod = "ARP",
                FirstSeen = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                LastSeen = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero),
                IsOnline = true,
            });
            await db.SaveChangesAsync();
        }

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        await repository.LoadFromDatabaseAsync();

        var loaded = repository.Get("10:20:30:40:50:60");
        Assert.NotNull(loaded);
        Assert.Equal("seeded-host", loaded.Hostname);
        Assert.Equal("192.168.1.20", loaded.IpAddress);
    }

    [Fact]
    public async Task MarkOffline_ExistingDevice_UpdatesCacheAndDatabaseWhenPersisted()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();

        await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:AA:AA:AA:AA:AA",
            IpAddress = "192.168.1.30",
            DiscoveryMethod = "ARP",
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
        });

        repository.MarkOffline("AA:AA:AA:AA:AA:AA");
        await repository.PersistAllAsync();

        Assert.False(repository.Get("AA:AA:AA:AA:AA:AA")!.IsOnline);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        var persisted = await db.Devices.SingleAsync(d => d.MacAddress == "AA:AA:AA:AA:AA:AA");
        Assert.False(persisted.IsOnline);
    }
}