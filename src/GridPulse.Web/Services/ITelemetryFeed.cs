using GridPulse.Core.Contracts;

namespace GridPulse.Web.Services;

public enum FeedStatus
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
}

/// <summary>
/// Live data for one user's session (one Blazor circuit). Components depend on this
/// abstraction, so bUnit tests can drive them with a fake feed.
/// </summary>
public interface ITelemetryFeed
{
    FeedStatus Status { get; }

    FleetSnapshot? LastFleet { get; }

    event Action<FleetSnapshot>? FleetUpdated;

    event Action<MeterLatest>? MeterUpdated;

    event Action<AlertNotification>? AlertChanged;

    event Action<FeedStatus>? StatusChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task SubscribeMeterAsync(int meterId);

    Task UnsubscribeMeterAsync(int meterId);
}
