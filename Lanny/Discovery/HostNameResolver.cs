using System.Net;
using System.Net.Sockets;

namespace Lanny.Discovery;

public sealed class HostNameResolver : IHostNameResolver
{
    private readonly IReverseDnsLookup _reverseDnsLookup;
    private readonly INetBiosNameService _netBiosNameService;
    private readonly ILogger<HostNameResolver> _logger;

    public HostNameResolver(
        IReverseDnsLookup reverseDnsLookup,
        INetBiosNameService netBiosNameService,
        ILogger<HostNameResolver> logger)
    {
        _reverseDnsLookup = reverseDnsLookup ?? throw new ArgumentNullException(nameof(reverseDnsLookup));
        _netBiosNameService = netBiosNameService ?? throw new ArgumentNullException(nameof(netBiosNameService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        var reverseDnsName = NormalizeHostName(await _reverseDnsLookup.ResolveAsync(ipAddress, cancellationToken));
        if (!string.IsNullOrEmpty(reverseDnsName))
            return reverseDnsName;

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var netBiosName = NormalizeHostName(await _netBiosNameService.ResolveAsync(ipAddress, cancellationToken));
        if (!string.IsNullOrEmpty(netBiosName))
            _logger.LogDebug("Resolved {IpAddress} via NetBIOS as {HostName}", ipAddress, netBiosName);

        return netBiosName;
    }

    private static string? NormalizeHostName(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return null;

        var normalized = hostname.Trim().TrimEnd('.');
        if (IPAddress.TryParse(normalized, out _))
            return null;

        if (normalized.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase))
            return null;

        return normalized;
    }
}
