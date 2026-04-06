using System.Net;

namespace Lanny.Discovery;

public sealed class ReverseDnsLookup : IReverseDnsLookup
{
    public async Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await Dns.GetHostEntryAsync(ipAddress);
            return entry.HostName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
