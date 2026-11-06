using GridPulse.Core.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace GridPulse.Api.Live;

/// <summary>
/// Real-time feed for dashboards. Fleet snapshots and alerts go to everyone; per-meter updates
/// go only to the SignalR group of that meter.
/// </summary>
public sealed class TelemetryHub(MeterSubscriptions subscriptions, LiveState state) : Hub<ITelemetryClient>
{
    public static string MeterGroup(int meterId) => $"meter:{meterId}";

    public async Task SubscribeMeter(int meterId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, MeterGroup(meterId));
        subscriptions.Add(Context.ConnectionId, meterId);
    }

    public async Task UnsubscribeMeter(int meterId)
    {
        subscriptions.Remove(Context.ConnectionId, meterId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MeterGroup(meterId));
    }

    public FleetSnapshot? GetFleetSnapshot() => state.Fleet;

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
