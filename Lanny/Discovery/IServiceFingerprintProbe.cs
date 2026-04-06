using Lanny.Models;

namespace Lanny.Discovery;

public interface IServiceFingerprintProbe
{
    Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken);
}