using GridPulse.Core.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace GridPulse.Web.Services;

/// <summary>
/// SignalR client to the API's telemetry hub, one per circuit (scoped). With Interactive Server
/// rendering this connection runs server-side, so the browser never talks to the API directly:
/// no CORS and no API credentials in the browser (ADR-0007).
/// </summary>
public sealed class SignalRTelemetryFeed : ITelemetryFeed, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRTelemetryFeed> _logger;
    private readonly HashSet<int> _meters = [];
    private Task? _start;

    public SignalRTelemetryFeed(IConfiguration configuration, ILogger<SignalRTelemetryFeed> logger)
    {
        _logger = logger;
        var hubUrl = new Uri(new Uri(configuration["Api:BaseUrl"] ?? "http://localhost:5301/"), TelemetryHubMethods.Path);
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _connection.On<FleetSnapshot>(nameof(ITelemetryClient.FleetUpdated), snapshot =>
        {
            LastFleet = snapshot;
            FleetUpdated?.Invoke(snapshot);
        });
        _connection.On<MeterLatest>(nameof(ITelemetryClient.MeterUpdated), latest => MeterUpdated?.Invoke(latest));
        _connection.On<AlertNotification>(nameof(ITelemetryClient.AlertChanged), alert => AlertChanged?.Invoke(alert));

        _connection.Reconnecting += _ => SetStatus(FeedStatus.Reconnecting);
        _connection.Closed += _ => SetStatus(FeedStatus.Disconnected);
        _connection.Reconnected += async _ =>
        {
            // Group membership does not survive a new connection: subscribe again.
            foreach (var meterId in _meters.ToArray())
            {
                await _connection.InvokeAsync(TelemetryHubMethods.SubscribeMeter, meterId);
            }

            await SetStatus(FeedStatus.Connected);
        };
    }

    public FeedStatus Status { get; private set; } = FeedStatus.Connecting;

    public FleetSnapshot? LastFleet { get; private set; }

    public event Action<FleetSnapshot>? FleetUpdated;

    public event Action<MeterLatest>? MeterUpdated;

    public event Action<AlertNotification>? AlertChanged;

    public event Action<FeedStatus>? StatusChanged;

    /// <summary>Idempotent: several components on a page can call it.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default) => _start ??= ConnectAsync(cancellationToken);

    public async Task SubscribeMeterAsync(int meterId)
    {
        await StartAsync();
        if (_meters.Add(meterId) && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(TelemetryHubMethods.SubscribeMeter, meterId);
        }
    }

    public async Task UnsubscribeMeterAsync(int meterId)
    {
        if (_meters.Remove(meterId) && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(TelemetryHubMethods.UnsubscribeMeter, meterId);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _connection.StartAsync(cancellationToken);
                LastFleet = await _connection.InvokeAsync<FleetSnapshot?>(TelemetryHubMethods.GetFleetSnapshot, cancellationToken);
                await SetStatus(FeedStatus.Connected);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning("Telemetry hub unavailable ({Message}); retrying", ex.Message);
                await SetStatus(FeedStatus.Reconnecting);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    private Task SetStatus(FeedStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
        return Task.CompletedTask;
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5))));
    }
}
