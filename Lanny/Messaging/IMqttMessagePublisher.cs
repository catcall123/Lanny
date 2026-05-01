namespace Lanny.Messaging;

public interface IMqttMessagePublisher
{
    Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default);
}
