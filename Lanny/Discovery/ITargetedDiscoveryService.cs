using Lanny.Models;

namespace Lanny.Discovery;

public interface ITargetedDiscoveryService
{
    Task<IReadOnlyList<Device>> ScanAsync(IReadOnlyList<Device> devices, CancellationToken cancellationToken);
}