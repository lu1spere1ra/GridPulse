using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using GridPulse.Ingestion.Pipeline;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GridPulse.Ingestion.Mqtt;

/// <summary>
/// Publishes live data on <c>gridpulse/live/*</c> for the API's SignalR hub (ADR-0006).
/// QoS 0: a lost snapshot is replaced by the next one a second later.
/// </summary>
public sealed class MqttLivePublisher : ILivePublisher, IAsyncDisposable
{
    private readonly ResilientMqttClient _client;
    private readonly Lazy<Task> _connect;

    public MqttLivePublisher(IOptions<MqttOptions> options, ILogger<MqttLivePublisher> logger)
    {
        _client = new ResilientMqttClient(options.Value, "ingestion-live", logger);
        _connect = new Lazy<Task>(() => _client.ConnectAsync(CancellationToken.None));
    }

    public Task PublishFleetAsync(FleetSnapshot snapshot, CancellationToken cancellationToken) =>
        PublishAsync(MqttTopics.LiveFleet, snapshot, GridPulseJsonContext.Default.FleetSnapshot, MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);

    public Task PublishLatestAsync(MeterLatestBatch batch, CancellationToken cancellationToken) =>
        PublishAsync(MqttTopics.LiveMeters, batch, GridPulseJsonContext.Default.MeterLatestBatch, MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);

    public Task PublishWindowsAsync(IReadOnlyList<WindowClosed> windows, CancellationToken cancellationToken) =>
        PublishAsync(MqttTopics.LiveWindows, windows, GridPulseJsonContext.Default.IReadOnlyListWindowClosed, MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);

    /// <summary>Alerts are rare and matter, so they use QoS 1.</summary>
    public Task PublishAlertAsync(AlertNotification alert, CancellationToken cancellationToken) =>
        PublishAsync(MqttTopics.LiveAlerts, alert, GridPulseJsonContext.Default.AlertNotification, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task PublishAsync<T>(
        string topic, T payload, JsonTypeInfo<T> typeInfo, MqttQualityOfServiceLevel qos, CancellationToken cancellationToken)
    {
        await _connect.Value;
        if (!_client.Client.IsConnected)
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo))
            .WithQualityOfServiceLevel(qos)
            .Build();
        await _client.Client.PublishAsync(message, cancellationToken);
    }
}
