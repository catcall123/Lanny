using Lanny.Discovery;
using Lanny.Models;

namespace Lanny.Tests.Discovery;

public class DeviceMetadataEnricherTests
{
    [Fact]
    public void MergeRelatedObservations_FillsMissingHostnameVendorAndDiscoveryMethod()
    {
        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            DiscoveryMethod = "ARP",
            LastSeen = DateTimeOffset.UtcNow,
        };

        DeviceMetadataEnricher.MergeRelatedObservations(device,
        [
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.10",
                Hostname = "workstation.local",
                DiscoveryMethod = "Ping",
            },
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.10",
                Vendor = "Google",
                DiscoveryMethod = "mDNS",
            },
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.99",
                Hostname = "ignored-host",
                Vendor = "Ignored Vendor",
                DiscoveryMethod = "mDNS",
            },
        ]);

        Assert.Equal("workstation.local", device.Hostname);
        Assert.Equal("Google", device.Vendor);
        Assert.Equal("ARP,Ping,mDNS", device.DiscoveryMethod);
    }
}
