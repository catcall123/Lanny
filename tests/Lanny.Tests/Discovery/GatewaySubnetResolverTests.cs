using System.Net;
using System.Net.NetworkInformation;
using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class GatewaySubnetResolverTests
{
    [Fact]
    public void ResolveSubnet_WhenConfiguredSubnetIsExplicit_ReturnsConfiguredValue()
    {
        var resolver = new GatewaySubnetResolver();

        var subnet = resolver.ResolveSubnet("192.168.50.0/24");

        Assert.Equal("192.168.50.0/24", subnet);
    }

    [Fact]
    public void TryResolveSubnet_WhenActiveGatewayCandidateExists_ReturnsGatewaySubnet()
    {
        var subnet = GatewaySubnetResolver.TryResolveSubnet([
            new GatewayInterfaceCandidate(
                IPAddress.Parse("169.254.10.2"),
                IPAddress.Parse("169.254.10.1"),
                16,
                NetworkInterfaceType.Loopback,
                OperationalStatus.Up),
            new GatewayInterfaceCandidate(
                IPAddress.Parse("192.168.2.42"),
                IPAddress.Parse("192.168.2.1"),
                24,
                NetworkInterfaceType.Ethernet,
                OperationalStatus.Up),
        ]);

        Assert.Equal("192.168.2.0/24", subnet);
    }

    [Fact]
    public void TryResolveSubnet_WhenCandidatesLackUsableDefaultGateway_ReturnsNull()
    {
        var subnet = GatewaySubnetResolver.TryResolveSubnet([
            new GatewayInterfaceCandidate(
                IPAddress.Parse("192.168.2.42"),
                IPAddress.Parse("10.0.0.1"),
                24,
                NetworkInterfaceType.Ethernet,
                OperationalStatus.Up),
            new GatewayInterfaceCandidate(
                IPAddress.Parse("192.168.2.99"),
                IPAddress.Parse("192.168.2.1"),
                24,
                NetworkInterfaceType.Tunnel,
                OperationalStatus.Up),
        ]);

        Assert.Null(subnet);
    }
}