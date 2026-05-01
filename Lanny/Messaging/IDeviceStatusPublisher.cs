using Lanny.Models;

namespace Lanny.Messaging;

public interface IDeviceStatusPublisher
{
    Task PublishAsync(IReadOnlyCollection<Device> devices, CancellationToken cancellationToken = default);
}
