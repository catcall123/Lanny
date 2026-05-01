using Lanny.Discovery;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests.Discovery;

public class ArpScanInterfaceResolverTests
{
    [Fact]
    public void Resolve_WhenInterfaceConfigured_ReturnsConfiguredInterface()
    {
        var resolver = new ArpScanInterfaceResolver(
            Options.Create(new ScanSettings { ArpScanInterface = "ens18" }),
            NullLogger<ArpScanInterfaceResolver>.Instance);

        var interfaceName = resolver.Resolve("default via 192.168.2.1 dev eth0 proto static");

        Assert.Equal("ens18", interfaceName);
    }

    [Fact]
    public void Resolve_WhenDefaultRouteHasDevice_ReturnsDefaultRouteDevice()
    {
        var resolver = new ArpScanInterfaceResolver(
            Options.Create(new ScanSettings { ArpScanInterface = "auto" }),
            NullLogger<ArpScanInterfaceResolver>.Instance);

        var interfaceName = resolver.Resolve("""
            default via 192.168.2.1 dev ens18 proto static
            default via 1.2.3.1 dev fake0 proto static metric 550
            192.168.2.0/24 dev ens18 proto kernel scope link src 192.168.2.43
            """);

        Assert.Equal("ens18", interfaceName);
    }

    [Fact]
    public void Resolve_WhenDefaultRouteOutputHasNoDevice_FallsBackToEth0()
    {
        var resolver = new ArpScanInterfaceResolver(
            Options.Create(new ScanSettings { ArpScanInterface = "auto" }),
            NullLogger<ArpScanInterfaceResolver>.Instance);

        var interfaceName = resolver.Resolve("192.168.2.0/24 dev ens18 proto kernel scope link");

        Assert.Equal("eth0", interfaceName);
    }
}
