using System.Net;
using Lanny.Discovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lanny.Tests.Discovery;

public class HostNameResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenReverseDnsExists_ReturnsReverseDnsName()
    {
        var resolver = new HostNameResolver(
            new StubReverseDnsLookup("nas.local"),
            new StubNetBiosNameService("WORKSTATION"),
            NullLogger<HostNameResolver>.Instance);

        var hostname = await resolver.ResolveAsync(IPAddress.Parse("192.168.1.10"), CancellationToken.None);

        Assert.Equal("nas.local", hostname);
    }

    [Fact]
    public async Task ResolveAsync_WhenReverseDnsMissing_FallsBackToNetBios()
    {
        var resolver = new HostNameResolver(
            new StubReverseDnsLookup(null),
            new StubNetBiosNameService("WORKSTATION"),
            NullLogger<HostNameResolver>.Instance);

        var hostname = await resolver.ResolveAsync(IPAddress.Parse("192.168.1.11"), CancellationToken.None);

        Assert.Equal("WORKSTATION", hostname);
    }

    private sealed class StubReverseDnsLookup : IReverseDnsLookup
    {
        private readonly string? _hostname;

        public StubReverseDnsLookup(string? hostname)
        {
            _hostname = hostname;
        }

        public Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ipAddress);
            return Task.FromResult(_hostname);
        }
    }

    private sealed class StubNetBiosNameService : INetBiosNameService
    {
        private readonly string? _hostname;

        public StubNetBiosNameService(string? hostname)
        {
            _hostname = hostname;
        }

        public Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ipAddress);
            return Task.FromResult(_hostname);
        }
    }
}
