using Lanny.Messaging;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lanny.Tests.Messaging;

public class MqttDeviceStatusPublisherTests
{
    [Fact]
    public async Task PublishAsync_WhenDeviceHasQualifiedHostname_PublishesOnlineStatus()
    {
        var broker = new RecordingMqttMessagePublisher();
        var publisher = new MqttDeviceStatusPublisher(broker, NullLogger<MqttDeviceStatusPublisher>.Instance);

        await publisher.PublishAsync([
            new Device
            {
                MacAddress = "AA:BB:CC:DD:EE:FF",
                Hostname = "S24-von-Gisela",
                IsOnline = true,
            },
        ]);

        var message = Assert.Single(broker.Messages);
        Assert.Equal("network_device_update.S24-von-Gisela", message.Topic);
        Assert.Equal("true", message.Payload);
    }

    [Fact]
    public async Task PublishAsync_WhenDeviceHasGenericHostname_DoesNotPublish()
    {
        var broker = new RecordingMqttMessagePublisher();
        var publisher = new MqttDeviceStatusPublisher(broker, NullLogger<MqttDeviceStatusPublisher>.Instance);

        await publisher.PublishAsync([
            new Device
            {
                MacAddress = "AA:BB:CC:DD:EE:FF",
                Hostname = "localhost",
                IsOnline = false,
            },
        ]);

        Assert.Empty(broker.Messages);
    }

    [Fact]
    public async Task PublishAsync_WhenBrokerPublishFails_ContinuesPublishingRemainingDevices()
    {
        var broker = new RecordingMqttMessagePublisher { FailFirstPublish = true };
        var publisher = new MqttDeviceStatusPublisher(broker, NullLogger<MqttDeviceStatusPublisher>.Instance);

        await publisher.PublishAsync([
            new Device
            {
                MacAddress = "AA:BB:CC:DD:EE:01",
                Hostname = "first",
                IsOnline = true,
            },
            new Device
            {
                MacAddress = "AA:BB:CC:DD:EE:02",
                Hostname = "second",
                IsOnline = false,
            },
        ]);

        var message = Assert.Single(broker.Messages);
        Assert.Equal("network_device_update.second", message.Topic);
        Assert.Equal("false", message.Payload);
    }

    private sealed class RecordingMqttMessagePublisher : IMqttMessagePublisher
    {
        private bool _publishFailed;

        public bool FailFirstPublish { get; init; }
        public List<MqttMessage> Messages { get; } = [];

        public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
        {
            if (FailFirstPublish && !_publishFailed)
            {
                _publishFailed = true;
                throw new InvalidOperationException("broker unavailable");
            }

            Messages.Add(new MqttMessage(topic, payload));
            return Task.CompletedTask;
        }
    }

    private sealed record MqttMessage(string Topic, string Payload);
}
