using System.Net;

namespace Lanny.Discovery;

public interface IReverseDnsLookup
{
    Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken);
}
