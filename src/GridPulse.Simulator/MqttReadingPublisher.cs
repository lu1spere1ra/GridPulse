using System.Text.Json;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GridPulse.Simulator;

/// <summary>
/// Publishes every meter's readings over one shared MQTT connection (QoS 1).
/// Real meters each hold their own connection; sharing one is a simulation shortcut (ADR-0003).
/// </summary>
public sealed class MqttReadingPublisher(IOptions<MqttOptions> options, ILogger<MqttReadingPublisher> logger)
    : IReadingPublisher, IAsyncDisposable
{
    private readonly ResilientMqttClient _client = new(options.Value, "simulator", logger);

    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);

    public async Task PublishAsync(ReadingMessage reading, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(MqttTopics.Readings(reading.MeterId))
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(reading, GridPulseJsonContext.Default.ReadingMessage))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.Client.PublishAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
