namespace GridPulse.Core.Contracts;

/// <summary>Methods the SignalR hub (<c>/hubs/telemetry</c>) invokes on connected dashboards.</summary>
public interface ITelemetryClient
{
    /// <summary>Fleet-wide snapshot, once per second, to every client.</summary>
    Task FleetUpdated(FleetSnapshot snapshot);

    /// <summary>Latest reading of a meter, only to clients that subscribed to that meter.</summary>
    Task MeterUpdated(MeterLatest latest);

    /// <summary>A closed 1- or 15-minute window, only to clients that subscribed to that meter.</summary>
    Task WindowClosed(WindowClosed window);

    /// <summary>An alert was raised or resolved, to every client.</summary>
    Task AlertChanged(AlertNotification alert);
}

/// <summary>Hub method names callable by clients.</summary>
public static class TelemetryHubMethods
{
    public const string Path = "/hubs/telemetry";
    public const string SubscribeMeter = "SubscribeMeter";
    public const string UnsubscribeMeter = "UnsubscribeMeter";
    public const string GetFleetSnapshot = "GetFleetSnapshot";
}
