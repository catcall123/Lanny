using Lanny.Models;

namespace Lanny.Discovery;

/// <summary>Common interface for all discovery methods.</summary>
public interface IDiscoveryService
{
    string Name { get; }
    Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct);
}
