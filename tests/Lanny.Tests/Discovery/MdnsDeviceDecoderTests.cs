using System.Net;
using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class MdnsDeviceDecoderTests
{
    [Fact]
    public void Decode_WhenFriendlyNameAndManufacturerExist_UsesThem()
    {
        var device = MdnsDeviceDecoder.Decode(
            "Living Room TV._googlecast._tcp.local",
            "_googlecast._tcp",
            "Chromecast-ABCD.local",
            ["fn=Living Room TV", "manufacturer=Google"],
            [IPAddress.Parse("192.168.1.25")],
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(device);
        Assert.Equal("Living Room TV", device.Hostname);
        Assert.Equal("Google", device.Vendor);
        Assert.Equal("192.168.1.25", device.IpAddress);
        Assert.Equal("mDNS", device.DiscoveryMethod);
    }

    [Fact]
    public void Decode_WhenPrinterTypeHintExists_InfersVendor()
    {
        var device = MdnsDeviceDecoder.Decode(
            "Office Printer._ipps._tcp.local",
            "_ipps._tcp",
            null,
            ["ty=Brother HL-L2350DW series"],
            [IPAddress.Parse("192.168.1.40")],
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(device);
        Assert.Equal("Office Printer", device.Hostname);
        Assert.Equal("Brother", device.Vendor);
    }

    [Fact]
    public void Decode_WhenInstanceNameContainsDnsSdEscapes_DecodesFriendlyHostname()
    {
        var device = MdnsDeviceDecoder.Decode(
            "Hue\\032Bridge\\032-\\032016484._hap._tcp.local",
            "_hap._tcp",
            null,
            [],
            [IPAddress.Parse("192.168.2.125")],
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(device);
        Assert.Equal("Hue Bridge - 016484", device.Hostname);
    }

    [Fact]
    public void Decode_WhenFriendlyNamePropertyContainsDnsSdEscapes_DecodesPropertyValue()
    {
        var device = MdnsDeviceDecoder.Decode(
            "Living\\032Room\\032TV._googlecast._tcp.local",
            "_googlecast._tcp",
            null,
            ["fn=Living\\032Room\\032TV"],
            [IPAddress.Parse("192.168.1.25")],
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(device);
        Assert.Equal("Living Room TV", device.Hostname);
        Assert.Equal("Google", device.Vendor);
    }
}
