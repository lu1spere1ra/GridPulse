using System.Text.Json;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GridPulse.Simulator;

/// <summary>
/// Publishes readings over a small pool of MQTT connections (QoS 1). Real meters each hold their own
/// connection; pooling is a simulation shortcut (ADR-0003). A meter is pinned to one connection so
/// its readings reach the broker, and therefore ingestion, in order.
/// </summary>
public sealed class MqttReadingPublisher(
    IOptions<MqttOptions> mqttOptions,
    IOptions<SimulatorOptions> simulatorOptions,
    ILogger<MqttReadingPublisher> logger) : IReadingPublisher, IAsyncDisposable
{
    private readonly ResilientMqttClient[] _clients = Enumerable.Range(0, simulatorOptions.Value.Connections)
        .Select(i => new ResilientMqttClient(mqttOptions.Value, $"simulator-{i}", logger))
        .ToArray();

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_clients.Select(c => c.ConnectAsync(cancellationToken)));

    public async Task PublishAsync(ReadingMessage reading, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(MqttTopics.Readings(reading.MeterId))
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(reading, GridPulseJsonContext.Default.ReadingMessage))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await ClientFor(reading.MeterId).Client.PublishAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    // string.GetHashCode is randomised per process but stable within it, which is all that ordering needs.
    private ResilientMqttClient ClientFor(string serial) =>
        _clients[(int)((uint)serial.GetHashCode() % (uint)_clients.Length)];
}
