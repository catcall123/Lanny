using System.Net;
using System.Reflection;
using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class PingScannerTests
{
    [Fact]
    public void ParseCidr_ValidSubnet_ReturnsNetworkAndPrefixLength()
    {
        var (network, prefixLength) = InvokeParseCidr("192.168.50.0/24");

        Assert.Equal(IPAddress.Parse("192.168.50.0"), network);
        Assert.Equal(24, prefixLength);
    }

    [Fact]
    public void GenerateAddresses_ThirtyBitSubnet_ExcludesNetworkAndBroadcastAddresses()
    {
        var addresses = InvokeGenerateAddresses(IPAddress.Parse("192.168.50.0"), 30);

        Assert.Equal(new[] { "192.168.50.1", "192.168.50.2" }, addresses.Select(ip => ip.ToString()));
    }

    [Fact]
    public void GenerateAddresses_TwentyFourBitSubnet_ReturnsAllUsableHosts()
    {
        var addresses = InvokeGenerateAddresses(IPAddress.Parse("10.0.0.0"), 24);

        Assert.Equal(254, addresses.Count);
        Assert.Equal("10.0.0.1", addresses.First().ToString());
        Assert.Equal("10.0.0.254", addresses.Last().ToString());
    }

    private static (IPAddress network, int prefixLength) InvokeParseCidr(string cidr)
    {
        var method = typeof(PingScanner).GetMethod("ParseCidr", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return ((IPAddress network, int prefixLength))method.Invoke(null, [cidr])!;
    }

    private static List<IPAddress> InvokeGenerateAddresses(IPAddress network, int prefixLength)
    {
        var method = typeof(PingScanner).GetMethod("GenerateAddresses", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (List<IPAddress>)method.Invoke(null, [network, prefixLength])!;
    }
}