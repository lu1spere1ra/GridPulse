using System.Collections.Concurrent;

namespace GridPulse.Api.Live;

/// <summary>
/// Which meters at least one connected dashboard is watching. Lets the bridge forward only those
/// meters' updates instead of pushing every meter's value through SignalR every second.
/// </summary>
public sealed class MeterSubscriptions
{
    private readonly ConcurrentDictionary<int, int> _watchers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _byConnection = new();

    public bool IsWatched(int meterId) => _watchers.TryGetValue(meterId, out var count) && count > 0;

    public bool Add(string connectionId, int meterId)
    {
        var meters = _byConnection.GetOrAdd(connectionId, _ => new ConcurrentDictionary<int, byte>());
        if (!meters.TryAdd(meterId, 0))
        {
            return false;
        }

        _watchers.AddOrUpdate(meterId, 1, (_, count) => count + 1);
        return true;
    }

    public bool Remove(string connectionId, int meterId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var meters) || !meters.TryRemove(meterId, out _))
        {
            return false;
        }

        Decrement(meterId);
        return true;
    }

    public void RemoveConnection(string connectionId)
    {
        if (_byConnection.TryRemove(connectionId, out var meters))
        {
            foreach (var meterId in meters.Keys)
            {
                Decrement(meterId);
            }
        }
    }

    private void Decrement(int meterId)
    {
        var remaining = _watchers.AddOrUpdate(meterId, 0, (_, count) => Math.Max(0, count - 1));
        if (remaining == 0)
        {
            _watchers.TryRemove(new KeyValuePair<int, int>(meterId, 0));
        }
    }
}
