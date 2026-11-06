using System.Text.Json;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GridPulse.Api.Live;

/// <summary>
/// Bridges ingestion's live topics (<c>gridpulse/live/#</c>) to SignalR clients (ADR-0006).
/// </summary>
/// <remarks>
/// Every API replica holds its own non-shared subscription, so every replica receives every live
/// message and can serve its own connected clients. That is why scaling the API out needs sticky
/// sessions for SignalR, but no Redis/Azure SignalR backplane.
/// </remarks>
public sealed class LiveTelemetryBridge(
    IOptions<MqttOptions> options,
    IHubContext<TelemetryHub, ITelemetryClient> hub,
    MeterSubscriptions subscriptions,
    LiveState state,
    ILogger<LiveTelemetryBridge> logger) : BackgroundService
{
    private ResilientMqttClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client = new ResilientMqttClient(options.Value, "api-live", logger);
        _client.Client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.OnConnectedAsync = ct => _client.Client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder().WithTopicFilter(MqttTopics.LiveSubscription, MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            ct);
        await _client.ConnectAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    internal async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            await DispatchAsync(e.ApplicationMessage.Topic, e.ApplicationMessage.Payload);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Ignoring malformed live message on {Topic}: {Message}", e.ApplicationMessage.Topic, ex.Message);
        }
    }

    internal async Task DispatchAsync(string topic, System.Buffers.ReadOnlySequence<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        switch (topic)
        {
            case MqttTopics.LiveFleet:
                var fleet = JsonSerializer.Deserialize(ref reader, GridPulseJsonContext.Default.FleetSnapshot);
                if (fleet is not null)
                {
                    state.Fleet = fleet;
                    await hub.Clients.All.FleetUpdated(fleet);
                }

                break;

            case MqttTopics.LiveMeters:
                var batch = JsonSerializer.Deserialize(ref reader, GridPulseJsonContext.Default.MeterLatestBatch);
                foreach (var latest in batch?.Meters ?? [])
                {
                    if (subscriptions.IsWatched(latest.MeterId))
                    {
                        await hub.Clients.Group(TelemetryHub.MeterGroup(latest.MeterId)).MeterUpdated(latest);
                    }
                }

                break;

            case MqttTopics.LiveWindows:
                var windows = JsonSerializer.Deserialize(ref reader, GridPulseJsonContext.Default.IReadOnlyListWindowClosed);
                foreach (var window in windows ?? [])
                {
                    if (subscriptions.IsWatched(window.MeterId))
                    {
                        await hub.Clients.Group(TelemetryHub.MeterGroup(window.MeterId)).WindowClosed(window);
                    }
                }

                break;

            case MqttTopics.LiveAlerts:
                var alert = JsonSerializer.Deserialize(ref reader, GridPulseJsonContext.Default.AlertNotification);
                if (alert is not null)
                {
                    await hub.Clients.All.AlertChanged(alert);
                }

                break;
        }
    }
}
