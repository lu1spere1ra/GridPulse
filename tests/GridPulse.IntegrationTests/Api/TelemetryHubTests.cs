using System.Text.Json;
using System.Threading.Channels;
using GridPulse.Core.Contracts;
using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;

namespace GridPulse.IntegrationTests.Api;

/// <summary>MQTT live topic → API bridge → SignalR client, through a real broker.</summary>
public sealed class TelemetryHubTests(CommunityDatabase database, MosquittoBroker broker) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiFactory _factory = new(database, broker);
    private readonly IMqttClient _publisher = new MqttClientFactory().CreateMqttClient();
    private HubConnection _hub = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _ = _factory.Server; // start the host (and its MQTT bridge)
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, TelemetryHubMethods.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await _hub.StartAsync(Ct);
        await _publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port).Build(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();
        _publisher.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>The bridge subscribes asynchronously after start-up, so publish until something arrives.</summary>
    private async Task<T> PublishUntilReceivedAsync<T>(string topic, object payload, ChannelReader<T> received)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await _publisher.PublishBinaryAsync(topic, JsonSerializer.SerializeToUtf8Bytes(payload, Json), cancellationToken: timeout.Token);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            wait.CancelAfter(TimeSpan.FromMilliseconds(500));
            try
            {
                return await received.ReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
            {
                // Not subscribed yet; try again.
            }
        }
    }

    [Fact]
    public async Task Fleet_snapshots_are_broadcast_to_every_client()
    {
        var received = Channel.CreateUnbounded<FleetSnapshot>();
        _hub.On<FleetSnapshot>(nameof(ITelemetryClient.FleetUpdated), s => received.Writer.TryWrite(s));

        var snapshot = await PublishUntilReceivedAsync(MqttTopics.LiveFleet, new FleetSnapshot(DateTimeOffset.UtcNow, 12_345, 10, 2, 9.5), received.Reader);

        snapshot.TotalPowerW.ShouldBe(12_345);
        (await _hub.InvokeAsync<FleetSnapshot?>(TelemetryHubMethods.GetFleetSnapshot, Ct))!.OnlineMeters.ShouldBe(10);
    }

    [Fact]
    public async Task Meter_updates_reach_only_clients_watching_that_meter()
    {
        var received = Channel.CreateUnbounded<MeterLatest>();
        _hub.On<MeterLatest>(nameof(ITelemetryClient.MeterUpdated), m => received.Writer.TryWrite(m));
        await _hub.InvokeAsync(TelemetryHubMethods.SubscribeMeter, 7, Ct);

        var batch = new MeterLatestBatch(DateTimeOffset.UtcNow,
        [
            new MeterLatest(7, "MTR-000007", DateTimeOffset.UtcNow, 700),
            new MeterLatest(8, "MTR-000008", DateTimeOffset.UtcNow, 800),
        ]);
        var first = await PublishUntilReceivedAsync(MqttTopics.LiveMeters, batch, received.Reader);

        first.MeterId.ShouldBe(7);
        await Task.Delay(300, Ct);
        while (received.Reader.TryRead(out var extra))
        {
            extra.MeterId.ShouldBe(7, "meter 8 was not subscribed");
        }
    }
}
