using Lanny.Discovery;
using Lanny.Models;

namespace Lanny.Tests.Discovery;

public class DeviceObservationBatchFilterTests
{
    [Fact]
    public void RemoveSupersededHostnamePairings_WhenOlderDifferentMacSharesHostname_RemovesOlderObservation()
    {
        var firstSeen = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero);

        var filtered = DeviceObservationBatchFilter.RemoveSupersededHostnamePairings([
            new Device
            {
                MacAddress = "DC:A6:32:A5:A2:FD",
                IpAddress = "192.168.2.11",
                Hostname = "SPACE",
                DiscoveryMethod = "DHCP",
                LastSeen = firstSeen,
            },
            new Device
            {
                MacAddress = "D8:3A:DD:E2:2E:DA",
                IpAddress = "192.168.2.30",
                Hostname = "SPACE",
                DiscoveryMethod = "ARP",
                LastSeen = firstSeen.AddMinutes(1),
            },
        ]);

        var device = Assert.Single(filtered);
        Assert.Equal("D8:3A:DD:E2:2E:DA", device.MacAddress);
    }

    [Fact]
    public void RemoveSupersededHostnamePairings_WhenSameMacSharesHostname_KeepsBothObservations()
    {
        var firstSeen = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero);

        var filtered = DeviceObservationBatchFilter.RemoveSupersededHostnamePairings([
            new Device
            {
                MacAddress = "D8:3A:DD:E2:2E:DA",
                IpAddress = "192.168.2.30",
                Hostname = "SPACE",
                DiscoveryMethod = "DHCP",
                LastSeen = firstSeen,
            },
            new Device
            {
                MacAddress = "D8:3A:DD:E2:2E:DA",
                IpAddress = "192.168.2.30",
                Hostname = "SPACE",
                DiscoveryMethod = "ARP",
                LastSeen = firstSeen.AddMinutes(1),
            },
        ]);

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void RemoveSupersededHostnamePairings_WhenHostnameIsGeneric_KeepsDifferentMacObservations()
    {
        var firstSeen = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero);

        var filtered = DeviceObservationBatchFilter.RemoveSupersededHostnamePairings([
            new Device
            {
                MacAddress = "00:00:00:00:00:01",
                Hostname = "localhost",
                LastSeen = firstSeen,
            },
            new Device
            {
                MacAddress = "00:00:00:00:00:02",
                Hostname = "localhost",
                LastSeen = firstSeen.AddMinutes(1),
            },
        ]);

        Assert.Equal(2, filtered.Count);
    }
}
