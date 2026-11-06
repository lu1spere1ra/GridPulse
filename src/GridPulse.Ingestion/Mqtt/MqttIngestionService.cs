using System.Text.Json;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using GridPulse.Ingestion.Pipeline;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GridPulse.Ingestion.Mqtt;

/// <summary>
/// Subscribes to meter readings and feeds the pipeline. Registered after <see cref="ReadingPipeline"/>,
/// so on shutdown it stops first (hosted services stop in reverse order) and no reading arrives
/// after the pipeline starts draining.
/// </summary>
public sealed class MqttIngestionService(
    IOptions<MqttOptions> options,
    ReadingPipeline pipeline,
    ILogger<MqttIngestionService> logger) : BackgroundService
{
    private ResilientMqttClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client = new ResilientMqttClient(options.Value, "ingestion", logger, receiveMaximum: 1_000);
        _client.Client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.OnConnectedAsync = SubscribeAsync;
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

    private Task SubscribeAsync(CancellationToken cancellationToken) =>
        _client!.Client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(MqttTopics.ReadingsSubscription, MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            cancellationToken);

    /// <summary>
    /// Awaiting <see cref="ReadingPipeline.SubmitAsync"/> here is the backpressure point. MQTTnet
    /// sends the PUBACK only when this handler returns (i.e. once the reading is enqueued), and
    /// with Receive Maximum = 1,000 the broker stops sending after 1,000 unacknowledged messages.
    /// So while the pipeline is full, messages wait in the broker's queue, not in our memory.
    /// </summary>
    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        ReadingMessage? message;
        try
        {
            var reader = new Utf8JsonReader(e.ApplicationMessage.Payload);
            message = JsonSerializer.Deserialize(ref reader, GridPulseJsonContext.Default.ReadingMessage);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message?.MeterId is null)
        {
            pipeline.RejectMalformed();
            return;
        }

        try
        {
            await pipeline.SubmitAsync(message, CancellationToken.None);
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            // Shutting down; the broker will redeliver unacknowledged QoS 1 messages next session.
        }
    }
}
