using System.Net;

namespace Lanny.Discovery;

public interface INetBiosNameService
{
    Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken);
}
