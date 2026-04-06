using System.Net;
using Lanny.Discovery;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests.Discovery;

public class SnmpMetadataProviderTests
{
    [Fact]
    public async Task TryGetObservationAsync_WhenSnmpIsDisabled_ReturnsNullWithoutCallingClient()
    {
        var client = new RecordingSnmpClient();
        var provider = new SnmpMetadataProvider(
            client,
            Options.Create(new SnmpSettings
            {
                Enabled = false,
                Community = "public",
            }),
            NullLogger<SnmpMetadataProvider>.Instance);

        var observation = await provider.TryGetObservationAsync(
            new Device { IpAddress = "192.168.1.10" },
            CancellationToken.None);

        Assert.Null(observation);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task TryGetObservationAsync_WhenSystemDataIsReturned_MapsSnmpMetadataToADeviceObservation()
    {
        var client = new RecordingSnmpClient
        {
            Response = new SnmpSystemData("core-switch", "Cisco IOS XE", "1.3.6.1.4.1.9.1.1208", 123456, 24),
        };
        var provider = new SnmpMetadataProvider(
            client,
            Options.Create(new SnmpSettings
            {
                Enabled = true,
                Community = "public",
                TimeoutMilliseconds = 500,
            }),
            NullLogger<SnmpMetadataProvider>.Instance);

        var observation = await provider.TryGetObservationAsync(
            new Device { IpAddress = "192.168.1.10" },
            CancellationToken.None);

        Assert.NotNull(observation);
        Assert.Equal("192.168.1.10", observation.IpAddress);
        Assert.Equal("core-switch", observation.Hostname);
        Assert.Equal("core-switch", observation.SystemName);
        Assert.Equal("Cisco IOS XE", observation.SystemDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1208", observation.SystemObjectId);
        Assert.Equal(123456, observation.SystemUptime);
        Assert.Equal(24, observation.InterfaceCount);
        Assert.Equal("SNMP", observation.DiscoveryMethod);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(IPAddress.Parse("192.168.1.10"), client.RequestedIpAddress);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task TryGetObservationAsync_WhenClientFails_ReturnsNull(Type exceptionType)
    {
        var client = new RecordingSnmpClient
        {
            ExceptionToThrow = (Exception)Activator.CreateInstance(exceptionType, "boom")!,
        };
        var provider = new SnmpMetadataProvider(
            client,
            Options.Create(new SnmpSettings
            {
                Enabled = true,
                Community = "public",
            }),
            NullLogger<SnmpMetadataProvider>.Instance);

        var observation = await provider.TryGetObservationAsync(
            new Device { IpAddress = "192.168.1.25" },
            CancellationToken.None);

        Assert.Null(observation);
    }

    private sealed class RecordingSnmpClient : ISnmpClient
    {
        public int CallCount { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public IPAddress? RequestedIpAddress { get; private set; }

        public SnmpSystemData? Response { get; init; }

        public Task<SnmpSystemData?> GetSystemDataAsync(IPAddress ipAddress, SnmpSettings settings, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedIpAddress = ipAddress;

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(Response);
        }
    }
}