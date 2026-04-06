using Lanny.Models;

namespace Lanny.Discovery;

public interface ISnmpMetadataProvider
{
    Task<Device?> TryGetObservationAsync(Device device, CancellationToken cancellationToken);
}