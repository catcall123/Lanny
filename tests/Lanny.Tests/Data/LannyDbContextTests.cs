using Lanny.Data;
using Lanny.Models;
using Lanny.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lanny.Tests.Data;

public class LannyDbContextTests
{
    [Fact]
    public async Task EnsureCreatedAndUpdatedAsync_WhenExistingDeviceTableLacksSnmpColumns_AddsThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE Devices (
                    MacAddress TEXT NOT NULL PRIMARY KEY,
                    IpAddress TEXT NULL,
                    Hostname TEXT NULL,
                    Vendor TEXT NULL,
                    DiscoveryMethod TEXT NULL,
                    OpenPorts TEXT NOT NULL,
                    FirstSeen TEXT NOT NULL,
                    LastSeen TEXT NOT NULL,
                    IsOnline INTEGER NOT NULL
                );";

            await command.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<LannyDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

        await LannyDbSchemaUpdater.EnsureCreatedAndUpdatedAsync(db);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info('Devices');";

        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columnNames.Add(reader.GetString(1));
        }

        Assert.Contains("SystemName", columnNames);
        Assert.Contains("SystemDescription", columnNames);
        Assert.Contains("SystemObjectId", columnNames);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTrackedOpenPortsListMutates_PersistsUpdatedPorts()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();

        var device = new Device
        {
            MacAddress = "01:23:45:67:89:AB",
            IpAddress = "192.168.1.40",
            DiscoveryMethod = "ARP",
            OpenPorts = [80],
            FirstSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
            IsOnline = true,
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        device.OpenPorts.Add(443);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.Devices.SingleAsync(d => d.MacAddress == device.MacAddress);

        Assert.Equal(new[] { 80, 443 }, reloaded.OpenPorts);
    }
}