using Lanny.Models;

namespace Lanny.Messaging;

public sealed class MqttDeviceStatusPublisher : IDeviceStatusPublisher
{
    private const string TopicPrefix = "network_device_update.";

    private readonly IMqttMessagePublisher _mqttMessagePublisher;
    private readonly ILogger<MqttDeviceStatusPublisher> _logger;

    public MqttDeviceStatusPublisher(
        IMqttMessagePublisher mqttMessagePublisher,
        ILogger<MqttDeviceStatusPublisher> logger)
    {
        _mqttMessagePublisher = mqttMessagePublisher ?? throw new ArgumentNullException(nameof(mqttMessagePublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(IReadOnlyCollection<Device> devices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hostName = NormalizeTopicHostName(device.Hostname);
            if (hostName is null)
                continue;

            try
            {
                await _mqttMessagePublisher.PublishAsync(
                    $"{TopicPrefix}{hostName}",
                    device.IsOnline ? "true" : "false",
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish MQTT device status for {HostName}",
                    hostName);
            }
        }
    }

    private static string? NormalizeTopicHostName(string? hostName)
    {
        if (!HostNameQualification.IsQualified(hostName))
            return null;

        var normalized = hostName!.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
