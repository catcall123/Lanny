using Lanny.Data;
using Lanny.Models;
using Lanny.Tests.Support;
using Microsoft.Data.Sqlite;
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
    public async Task UpsertAsync_ExistingDevice_MergesCompositeDiscoveryMethodsWithoutDuplicatingExistingTags()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen,
        });

        var merged = await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Hostname = "printer.local",
            DiscoveryMethod = "ARP,mDNS",
            LastSeen = firstSeen.AddMinutes(1),
        });

        Assert.Equal("ARP,mDNS", merged.DiscoveryMethod);
    }

    [Fact]
    public async Task UpsertAsync_WhenNewerMacUsesExistingHostname_DeletesOlderHostnamePairing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "C6:9B:76:67:10:0B",
            IpAddress = "192.168.2.136",
            Hostname = "S24-von-Gisela",
            DiscoveryMethod = "DHCP",
            LastSeen = firstSeen,
        });

        var current = await repository.UpsertAsync(new Device
        {
            MacAddress = "3A:F6:E9:E1:17:2A",
            IpAddress = "192.168.2.47",
            Hostname = "S24-von-Gisela",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen.AddHours(1),
        });

        Assert.Equal("3A:F6:E9:E1:17:2A", current.MacAddress);
        Assert.Null(repository.Get("C6:9B:76:67:10:0B"));
        Assert.NotNull(repository.Get("3A:F6:E9:E1:17:2A"));

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        Assert.False(await db.Devices.AnyAsync(d => d.MacAddress == "C6:9B:76:67:10:0B"));
        Assert.True(await db.Devices.AnyAsync(d => d.MacAddress == "3A:F6:E9:E1:17:2A"));
    }

    [Fact]
    public async Task UpsertAsync_WhenOlderMacUsesGenericHostname_DoesNotDeleteHostnamePairing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "00:00:00:00:00:01",
            IpAddress = "192.168.2.10",
            Hostname = "localhost",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen,
        });

        await repository.UpsertAsync(new Device
        {
            MacAddress = "00:00:00:00:00:02",
            IpAddress = "192.168.2.11",
            Hostname = "localhost",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen.AddHours(1),
        });

        Assert.NotNull(repository.Get("00:00:00:00:00:01"));
        Assert.NotNull(repository.Get("00:00:00:00:00:02"));
    }

    [Fact]
    public async Task UpsertAsync_WhenExistingHostnamePairingIsNewer_DoesNotDeleteIt()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "00:00:00:00:00:03",
            IpAddress = "192.168.2.12",
            Hostname = "shared-name",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen.AddHours(1),
        });

        await repository.UpsertAsync(new Device
        {
            MacAddress = "00:00:00:00:00:04",
            IpAddress = "192.168.2.13",
            Hostname = "shared-name",
            DiscoveryMethod = "DHCP",
            LastSeen = firstSeen,
        });

        Assert.NotNull(repository.Get("00:00:00:00:00:03"));
        Assert.NotNull(repository.Get("00:00:00:00:00:04"));
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
    public async Task LoadFromDatabaseAsync_NormalizesPersistedDuplicateDiscoveryMethods()
    {
        await using var host = await SqliteTestHost.CreateAsync();

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
            db.Devices.Add(new Device
            {
                MacAddress = "10:20:30:40:50:61",
                IpAddress = "192.168.1.21",
                Hostname = "named-device",
                DiscoveryMethod = "ARP,ARP,mDNS",
                FirstSeen = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                LastSeen = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero),
                IsOnline = true,
            });
            await db.SaveChangesAsync();
        }

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        await repository.LoadFromDatabaseAsync();

        var loaded = repository.Get("10:20:30:40:50:61");
        Assert.NotNull(loaded);
        Assert.Equal("ARP,mDNS", loaded.DiscoveryMethod);
    }

    [Fact]
    public async Task LoadFromDatabaseAsync_RemovesPersistedArpNoiseEntries()
    {
        await using var host = await SqliteTestHost.CreateAsync();

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
            db.Devices.AddRange(
                new Device
                {
                    MacAddress = "01-00-5E-00-00-FB",
                    IpAddress = "224.0.0.251",
                    DiscoveryMethod = "ARP",
                    FirstSeen = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                    LastSeen = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero),
                    IsOnline = true,
                },
                new Device
                {
                    MacAddress = "10:20:30:40:50:62",
                    IpAddress = "192.168.1.22",
                    DiscoveryMethod = "ARP",
                    FirstSeen = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                    LastSeen = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero),
                    IsOnline = true,
                });
            await db.SaveChangesAsync();
        }

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        await repository.LoadFromDatabaseAsync();

        Assert.Null(repository.Get("01-00-5E-00-00-FB"));
        Assert.NotNull(repository.Get("10:20:30:40:50:62"));

        await using var verificationScope = host.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<LannyDbContext>();
        Assert.False(await verificationDb.Devices.AnyAsync(device => device.MacAddress == "01-00-5E-00-00-FB"));
        Assert.True(await verificationDb.Devices.AnyAsync(device => device.MacAddress == "10:20:30:40:50:62"));
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

    [Fact]
    public async Task TryMergeObservationByIpAsync_ExistingDevice_RefreshesIpOnlyPassiveObservation()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var firstSeen = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.2.96",
            Hostname = "receiver",
            DiscoveryMethod = "ARP",
            LastSeen = firstSeen,
        });

        repository.MarkOffline("AA:BB:CC:DD:EE:FF");

        var merged = await repository.TryMergeObservationByIpAsync(new Device
        {
            IpAddress = "192.168.2.96",
            Vendor = "Linux/6.1 UPnP/1.0 Example/1.0",
            DiscoveryMethod = "SSDP",
            LastSeen = firstSeen.AddMinutes(2),
        });

        Assert.True(merged);
        var stored = repository.Get("AA:BB:CC:DD:EE:FF");
        Assert.NotNull(stored);
        Assert.True(stored.IsOnline);
        Assert.Equal(firstSeen.AddMinutes(2), stored.LastSeen);
        Assert.Equal("ARP,SSDP", stored.DiscoveryMethod);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        var persisted = await db.Devices.SingleAsync(d => d.MacAddress == "AA:BB:CC:DD:EE:FF");
        Assert.True(persisted.IsOnline);
        Assert.Equal("ARP,SSDP", persisted.DiscoveryMethod);
    }

    [Fact]
    public async Task PruneOfflineDevicesAsync_RemovesExpiredOfflineDevicesFromCacheAndDatabase()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var now = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
            db.Devices.AddRange(
                new Device
                {
                    MacAddress = "00:00:00:00:00:01",
                    IpAddress = "192.168.1.10",
                    DiscoveryMethod = "ARP",
                    FirstSeen = now.AddDays(-10),
                    LastSeen = now.AddDays(-7),
                    IsOnline = false,
                },
                new Device
                {
                    MacAddress = "00:00:00:00:00:02",
                    IpAddress = "192.168.1.11",
                    DiscoveryMethod = "ARP",
                    FirstSeen = now.AddDays(-2),
                    LastSeen = now.AddHours(-4),
                    IsOnline = false,
                },
                new Device
                {
                    MacAddress = "00:00:00:00:00:03",
                    IpAddress = "192.168.1.12",
                    DiscoveryMethod = "ARP",
                    FirstSeen = now.AddDays(-1),
                    LastSeen = now.AddMinutes(-1),
                    IsOnline = true,
                });
            await db.SaveChangesAsync();
        }

        await repository.LoadFromDatabaseAsync();

        var removed = await repository.PruneOfflineDevicesAsync(now.AddDays(-1), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Null(repository.Get("00:00:00:00:00:01"));
        Assert.NotNull(repository.Get("00:00:00:00:00:02"));
        Assert.NotNull(repository.Get("00:00:00:00:00:03"));

        await using var verificationScope = host.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<LannyDbContext>();
        Assert.False(await verificationDb.Devices.AnyAsync(device => device.MacAddress == "00:00:00:00:00:01"));
    }

    [Fact]
    public async Task UpsertAsync_WhenDatabaseIsTemporarilyLocked_RetriesUntilSaveSucceeds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"lanny-tests-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=1";

        try
        {
            await using (var host = await SqliteTestHost.CreateFileBackedAsync(connectionString))
            {
                var repository = host.Services.GetRequiredService<DeviceRepository>();

                await using var lockConnection = new SqliteConnection(connectionString);
                await lockConnection.OpenAsync();

                await using (var beginExclusiveLock = lockConnection.CreateCommand())
                {
                    beginExclusiveLock.CommandText = "BEGIN EXCLUSIVE;";
                    await beginExclusiveLock.ExecuteNonQueryAsync();
                }

                var upsertTask = repository.UpsertAsync(new Device
                {
                    MacAddress = "AA:BB:CC:DD:EE:99",
                    IpAddress = "192.168.1.99",
                    DiscoveryMethod = "ARP",
                    LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
                });

                await Task.Delay(200);

                await using (var releaseLock = lockConnection.CreateCommand())
                {
                    releaseLock.CommandText = "COMMIT;";
                    await releaseLock.ExecuteNonQueryAsync();
                }

                var stored = await upsertTask;

                Assert.Equal("AA:BB:CC:DD:EE:99", stored.MacAddress);

                await using var scope = host.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
                Assert.True(await db.Devices.AnyAsync(device => device.MacAddress == "AA:BB:CC:DD:EE:99"));
            }
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete($"{databasePath}-shm");
            TryDelete($"{databasePath}-wal");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
