using Lanny.Data;
using Lanny.Models;
using Lanny.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lanny.Tests.Data;

public class LannyDbContextTests
{
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