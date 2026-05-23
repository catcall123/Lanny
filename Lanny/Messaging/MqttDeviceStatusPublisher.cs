using System.Text;
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
        var sanitized = SanitizeTopicSegment(normalized);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    // Strips characters that are illegal in an MQTT publish topic segment: the
    // wildcards '#'/'+', the level separator '/', and control characters. A
    // hostname is attacker/peripheral-controlled, so leaving these in throws
    // MqttProtocolViolationException on every scan cycle (see issue #15).
    private static string SanitizeTopicSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '#' or '+' or '/' || char.IsControl(c))
                continue;

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }
}
