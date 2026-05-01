using System.Text;
using MQTTnet;

namespace Lanny.Messaging;

public sealed class MqttNetMessagePublisher : IMqttMessagePublisher
{
    private const string BrokerHost = "192.168.2.42";
    private const int BrokerPort = 1883;
    private const string UserName = "mqttin";
    private const string Password = "zxFM5o1UhcV2LPKR7Ca6";

    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(payload);

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId(Guid.NewGuid().ToString())
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithCredentials(UserName, Password)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V310)
            .WithCleanSession()
            .Build();

        await client.ConnectAsync(options, cancellationToken);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await client.PublishAsync(message, cancellationToken);
        await client.DisconnectAsync(cancellationToken: cancellationToken);
    }
}
