using System.Runtime.InteropServices;
using GridPulse.Core.Aggregation;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;

namespace GridPulse.Ingestion.Pipeline;

/// <summary>Everything the processor produced on one tick, handed to the live publisher.</summary>
public sealed record LiveUpdate(
    FleetSnapshot Fleet,
    MeterLatestBatch Latest,
    IReadOnlyList<WindowClosed> Windows);

/// <summary>
/// The stateful middle stage of the pipeline: ordering checks, window aggregation and live state.
/// </summary>
/// <remarks>
/// Deliberately synchronous and single-threaded: the pipeline calls it from exactly one task,
/// so every dictionary below is owned by one thread and needs no locks. That keeps the logic
/// testable without channels or timers.
/// </remarks>
public sealed class ReadingProcessor
{
    private readonly IngestionOptions _options;
    private readonly IMeterRegistry _registry;
    private readonly IngestionMetrics _metrics;
    private readonly MeterSequenceGuard _sequence = new();
    private readonly WindowAggregator _oneMinute;
    private readonly WindowAggregator _fifteenMinutes;
    private readonly Dictionary<int, LatestState> _latest = [];
    private readonly List<WindowResult> _closed = [];
    private readonly IReadOnlyList<IWindowListener> _listeners;
    private long _readingsSinceTick;
    private DateTimeOffset? _lastTick;

    public ReadingProcessor(
        IngestionOptions options,
        IMeterRegistry registry,
        IngestionMetrics metrics,
        IEnumerable<IWindowListener>? listeners = null)
    {
        _options = options;
        _registry = registry;
        _metrics = metrics;
        _listeners = listeners?.ToArray() ?? [];
        _oneMinute = new WindowAggregator(TimeSpan.FromMinutes(1), options.AllowedLateness);
        _fifteenMinutes = new WindowAggregator(TimeSpan.FromMinutes(15), options.AllowedLateness);
    }

    /// <summary>Processes one reading. Returns <c>true</c> when the reading must be persisted.</summary>
    public bool Process(in Reading reading)
    {
        var rejection = _sequence.Check(reading);
        if (rejection != RejectReason.None)
        {
            _metrics.Rejected(rejection);
            return false;
        }

        _readingsSinceTick++;
        var outcome = _oneMinute.Add(reading, _closed);
        _fifteenMinutes.Add(reading, _closed);
        if (outcome == AddOutcome.Late)
        {
            // Persisted (the database is the source of truth) but excluded from live aggregates.
            _metrics.Late();
        }

        ref var latest = ref CollectionsMarshal.GetValueRefOrAddDefault(_latest, reading.MeterId, out _);
        latest = new LatestState(reading.Timestamp, reading.PowerW, Changed: true);

        foreach (var listener in _listeners)
        {
            listener.OnReading(reading);
        }

        return true;
    }

    /// <summary>
    /// Closes expired windows and snapshots live state. Called once per <see cref="IngestionOptions.TickInterval"/>.
    /// </summary>
    public LiveUpdate Tick(DateTimeOffset now)
    {
        _oneMinute.CloseExpired(now, _closed);
        _fifteenMinutes.CloseExpired(now, _closed);
        var windows = DrainClosedWindows();

        foreach (var listener in _listeners)
        {
            listener.OnTick(now);
        }

        var elapsed = _lastTick is { } last ? (now - last).TotalSeconds : _options.TickInterval.TotalSeconds;
        var readingsPerSecond = elapsed > 0 ? _readingsSinceTick / elapsed : 0;
        _readingsSinceTick = 0;
        _lastTick = now;

        var onlineCutoff = now - _options.OfflineAfter;
        var online = 0;
        var totalPowerW = 0d;
        var changed = new List<MeterLatest>();
        foreach (var meterId in _latest.Keys)
        {
            ref var state = ref CollectionsMarshal.GetValueRefOrNullRef(_latest, meterId);
            if (state.Timestamp >= onlineCutoff)
            {
                online++;
                totalPowerW += state.PowerW;
            }

            if (state.Changed)
            {
                changed.Add(new MeterLatest(meterId, _registry.GetSerial(meterId) ?? string.Empty, state.Timestamp, state.PowerW));
                state = state with { Changed = false };
            }
        }

        var offline = Math.Max(0, _registry.Count - online);
        return new LiveUpdate(
            new FleetSnapshot(now, Math.Round(totalPowerW, 1), online, offline, Math.Round(readingsPerSecond, 1)),
            new MeterLatestBatch(now, changed),
            windows);
    }

    /// <summary>Closes all open windows (shutdown) so their results are not lost.</summary>
    public IReadOnlyList<WindowClosed> Flush()
    {
        _oneMinute.CloseAll(_closed);
        _fifteenMinutes.CloseAll(_closed);
        return DrainClosedWindows();
    }

    private List<WindowClosed> DrainClosedWindows()
    {
        var windows = new List<WindowClosed>(_closed.Count);
        foreach (var window in _closed)
        {
            foreach (var listener in _listeners)
            {
                listener.OnWindowClosed(window);
            }

            windows.Add(new WindowClosed(
                window.MeterId,
                window.Start,
                (int)window.Size.TotalMinutes,
                window.Count,
                Math.Round(window.AvgPowerW, 1),
                window.MaxPowerW,
                window.EnergyWh));
        }

        _metrics.WindowsClosed(_closed.Count);
        _closed.Clear();
        return windows;
    }

    private readonly record struct LatestState(DateTimeOffset Timestamp, float PowerW, bool Changed);
}

/// <summary>Hook for components that react to the stream (the alert engine). Called on the processor thread.</summary>
public interface IWindowListener
{
    void OnReading(in Reading reading);

    void OnWindowClosed(in WindowResult window);

    void OnTick(DateTimeOffset now);
}
