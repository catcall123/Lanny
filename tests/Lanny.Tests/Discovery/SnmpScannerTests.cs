using Lanny.Discovery;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests.Discovery;

public class SnmpScannerTests
{
    [Fact]
    public async Task ScanAsync_WhenSnmpInspectionIsDisabled_ReturnsNoObservations()
    {
        var provider = new RecordingSnmpMetadataProvider();
        var scanner = new SnmpScanner(
            provider,
            NullLogger<SnmpScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableSnmpInspection = false,
            }));

        var observations = await scanner.ScanAsync(
            [new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10" }],
            CancellationToken.None);

        Assert.Empty(observations);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ScanAsync_WhenManagedDeviceResponds_ReturnsSnmpObservation()
    {
        var provider = new RecordingSnmpMetadataProvider
        {
            Observation = new Device
            {
                IpAddress = "192.168.1.10",
                Hostname = "core-switch",
                SystemName = "core-switch",
                SystemDescription = "Cisco IOS XE",
                SystemObjectId = "1.3.6.1.4.1.9.1.1208",
                SystemUptime = 123456,
                InterfaceCount = 24,
                DiscoveryMethod = "SNMP",
            },
        };
        var scanner = new SnmpScanner(
            provider,
            NullLogger<SnmpScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableSnmpInspection = true,
            }));

        var observations = await scanner.ScanAsync(
            [new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10" }],
            CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("192.168.1.10", observation.IpAddress);
        Assert.Equal("core-switch", observation.SystemName);
        Assert.Equal("Cisco IOS XE", observation.SystemDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1208", observation.SystemObjectId);
        Assert.Equal(123456, observation.SystemUptime);
        Assert.Equal(24, observation.InterfaceCount);
        Assert.Equal("SNMP", observation.DiscoveryMethod);
    }

    private sealed class RecordingSnmpMetadataProvider : ISnmpMetadataProvider
    {
        public int CallCount { get; private set; }

        public Device? Observation { get; init; }

        public Task<Device?> TryGetObservationAsync(Device device, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Observation?.IpAddress == device.IpAddress ? Observation : null);
        }
    }
}