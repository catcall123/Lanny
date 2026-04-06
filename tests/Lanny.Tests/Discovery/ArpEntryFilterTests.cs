using Lanny.Discovery;
using Lanny.Models;

namespace Lanny.Tests.Discovery;

public class ArpEntryFilterTests
{
    [Theory]
    [InlineData("192.168.2.42", "00-11-22-33-44-55", true)]
    [InlineData("224.0.0.251", "01-00-5E-00-00-FB", false)]
    [InlineData("255.255.255.255", "FF-FF-FF-FF-FF-FF", false)]
    [InlineData("192.168.2.10", "01-00-5E-00-00-FB", false)]
    public void IsRelevantNeighbor_ReturnsExpectedClassification(string ipAddress, string macAddress, bool expected)
    {
        var result = ArpEntryFilter.IsRelevantNeighbor(ipAddress, macAddress);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsNoiseOnlyDevice_WhenArpOnlyMulticastEntry_ReturnsTrue()
    {
        var result = ArpEntryFilter.IsNoiseOnlyDevice(new Device
        {
            MacAddress = "01-00-5E-00-00-FB",
            IpAddress = "224.0.0.251",
            DiscoveryMethod = "ARP",
        });

        Assert.True(result);
    }

    [Fact]
    public void IsNoiseOnlyDevice_WhenDeviceHasAdditionalDiscoveryEvidence_ReturnsFalse()
    {
        var result = ArpEntryFilter.IsNoiseOnlyDevice(new Device
        {
            MacAddress = "01-00-5E-00-00-FB",
            IpAddress = "224.0.0.251",
            DiscoveryMethod = "ARP,mDNS",
        });

        Assert.False(result);
    }
}