using Microsoft.Extensions.Logging;
using MQTTnet;

namespace GridPulse.Infrastructure.Messaging;

/// <summary>
/// Thin wrapper over an MQTTnet client that connects with exponential backoff and
/// reconnects (and re-subscribes, via <see cref="OnConnectedAsync"/>) after the connection drops.
/// MQTTnet 5 no longer ships a "managed" client, and this is all the services need.
/// </summary>
public sealed class ResilientMqttClient : IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly MqttClientOptions _options;
    private readonly string _endpoint;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _disposed = new();

    /// <param name="receiveMaximum">
    /// MQTT 5 Receive Maximum: how many QoS 1 messages the broker may have in flight to us
    /// before it waits for our PUBACKs. Consumers set it so that a slow handler slows the broker
    /// down instead of filling MQTTnet's internal (unbounded) receive queue. 0 keeps the default.
    /// </param>
    public ResilientMqttClient(MqttOptions options, string role, ILogger logger, ushort receiveMaximum = 0)
    {
        _logger = logger;
        _endpoint = $"{options.Host}:{options.Port}";
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId($"{options.ClientIdPrefix}-{role}-{Guid.NewGuid():N}")
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithCleanStart()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15));
        if (receiveMaximum > 0)
        {
            builder = builder.WithReceiveMaximum(receiveMaximum);
        }

        _options = builder.Build();

        Client = new MqttClientFactory().CreateMqttClient();
        Client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public IMqttClient Client { get; }

    /// <summary>Invoked after every successful (re)connect, e.g. to subscribe.</summary>
    public Func<CancellationToken, Task>? OnConnectedAsync { get; set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Client.ConnectAsync(_options, cancellationToken);
                _logger.LogInformation("Connected to MQTT broker {Endpoint}", _endpoint);
                if (OnConnectedAsync is not null)
                {
                    await OnConnectedAsync(cancellationToken);
                }

                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("MQTT connect failed ({Message}); retrying in {Delay}", ex.Message, delay);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();
        try
        {
            if (Client.IsConnected)
            {
                await Client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build());
            }
        }
        catch (Exception ex)
        {
            // The connection may already be gone (broker restart, shutdown race); nothing to clean up.
            _logger.LogDebug("Ignoring MQTT disconnect failure: {Message}", ex.Message);
        }

        // _disposed is cancelled but deliberately not disposed: MQTTnet can still raise
        // DisconnectedAsync after this point, and the handler reads the token.
        Client.Dispose();
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        // Failed connection attempts also raise this event; ConnectAsync's loop handles those.
        if (!e.ClientWasConnected || _disposed.IsCancellationRequested)
        {
            return;
        }

        _logger.LogWarning("MQTT connection lost ({Reason}); reconnecting", e.Reason);
        try
        {
            await ConnectAsync(_disposed.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
