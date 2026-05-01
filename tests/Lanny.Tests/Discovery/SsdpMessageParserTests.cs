using Lanny.Discovery;
using System.Net;
using System.Text;

namespace Lanny.Tests.Discovery;

public class SsdpMessageParserTests
{
    [Fact]
    public void TryParse_NotifyMessage_ReturnsIpObservationWithHeaders()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var message = string.Join("\r\n",
            "NOTIFY * HTTP/1.1",
            "HOST: 239.255.255.250:1900",
            "NT: urn:schemas-upnp-org:device:MediaRenderer:1",
            "NTS: ssdp:alive",
            "USN: uuid:device-1::urn:schemas-upnp-org:device:MediaRenderer:1",
            "SERVER: Linux/6.1 UPnP/1.0 Example/1.0",
            "LOCATION: http://192.168.2.96:8080/description.xml",
            "",
            "");

        var parsed = SsdpMessageParser.TryParse(
            Encoding.ASCII.GetBytes(message),
            new IPEndPoint(IPAddress.Parse("192.168.2.96"), 1900),
            capturedAt,
            out var device);

        Assert.True(parsed);
        Assert.NotNull(device);
        Assert.Equal(string.Empty, device.MacAddress);
        Assert.Equal("192.168.2.96", device.IpAddress);
        Assert.Equal("Linux/6.1 UPnP/1.0 Example/1.0", device.Vendor);
        Assert.Equal("SSDP", device.DiscoveryMethod);
        Assert.Equal(capturedAt, device.LastSeen);
        Assert.Equal("urn:schemas-upnp-org:device:MediaRenderer:1", device.HttpHeaders!["NT"]);
    }

    [Fact]
    public void TryParse_ByebyeMessage_ReturnsFalse()
    {
        var message = string.Join("\r\n",
            "NOTIFY * HTTP/1.1",
            "NTS: ssdp:byebye",
            "USN: uuid:device-1",
            "",
            "");

        var parsed = SsdpMessageParser.TryParse(
            Encoding.ASCII.GetBytes(message),
            new IPEndPoint(IPAddress.Parse("192.168.2.96"), 1900),
            DateTimeOffset.UtcNow,
            out var device);

        Assert.False(parsed);
        Assert.Null(device);
    }
}
