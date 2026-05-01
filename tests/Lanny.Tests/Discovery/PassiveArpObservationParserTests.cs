using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class PassiveArpObservationParserTests
{
    [Fact]
    public void TryParseTcpdumpLine_RequestLine_ReturnsSenderObservation()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        const string line = "12:00:00.000000 aa:bb:cc:dd:ee:ff > ff:ff:ff:ff:ff:ff, ethertype ARP (0x0806), length 42: Request who-has 192.168.2.1 tell 192.168.2.104, length 28";

        var parsed = PassiveArpObservationParser.TryParseTcpdumpLine(line, capturedAt, out var device);

        Assert.True(parsed);
        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device.MacAddress);
        Assert.Equal("192.168.2.104", device.IpAddress);
        Assert.Equal("PassiveARP", device.DiscoveryMethod);
        Assert.Equal(capturedAt, device.LastSeen);
    }

    [Fact]
    public void TryParseTcpdumpLine_ReplyLine_ReturnsAdvertisedObservation()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        const string line = "12:00:00.000000 3c:52:a1:a6:a3:b4 > aa:bb:cc:dd:ee:ff, ethertype ARP (0x0806), length 42: Reply 192.168.2.1 is-at 3c:52:a1:a6:a3:b4, length 28";

        var parsed = PassiveArpObservationParser.TryParseTcpdumpLine(line, capturedAt, out var device);

        Assert.True(parsed);
        Assert.NotNull(device);
        Assert.Equal("3C:52:A1:A6:A3:B4", device.MacAddress);
        Assert.Equal("192.168.2.1", device.IpAddress);
    }

    [Fact]
    public void TryParseTcpdumpLine_MulticastMac_ReturnsFalse()
    {
        var parsed = PassiveArpObservationParser.TryParseTcpdumpLine(
            "12:00:00.000000 01:00:5e:00:00:fb > ff:ff:ff:ff:ff:ff, ethertype ARP (0x0806), length 42: Reply 224.0.0.251 is-at 01:00:5e:00:00:fb, length 28",
            DateTimeOffset.UtcNow,
            out var device);

        Assert.False(parsed);
        Assert.Null(device);
    }
}
