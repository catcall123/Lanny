using System.Net;

namespace Lanny.Discovery;

public interface IHostNameResolver
{
    Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken);
}
