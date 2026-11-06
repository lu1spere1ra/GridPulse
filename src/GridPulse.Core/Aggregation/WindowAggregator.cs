using System.Runtime.InteropServices;
using GridPulse.Core.Readings;

namespace GridPulse.Core.Aggregation;

public enum AddOutcome
{
    /// <summary>The reading was added to the meter's open window.</summary>
    Aggregated,

    /// <summary>The reading's window was already closed; it is stored but not aggregated.</summary>
    Late,
}

/// <summary>
/// Tumbling-window aggregation per meter, on event time.
/// </summary>
/// <remarks>
/// <para>
/// Windows are aligned to UTC epoch boundaries, the same alignment as TimescaleDB's
/// <c>time_bucket</c> for intervals that divide a day, so the live (stream) results and the
/// stored (database) results describe the same buckets.
/// </para>
/// <para>
/// A window closes in one of two ways:
/// <list type="number">
/// <item>A reading for a later window arrives for the same meter. Readings are already
/// guaranteed to be in order per meter (see <see cref="MeterSequenceGuard"/>), so nothing
/// else can land in the old window.</item>
/// <item>The clock passes <c>windowEnd + allowedLateness</c> with no newer reading
/// (the meter went quiet). Readings arriving after that are reported as <see cref="AddOutcome.Late"/>.</item>
/// </list>
/// </para>
/// <para>Not thread-safe by design: one instance is owned by the single pipeline processor.</para>
/// </remarks>
public sealed class WindowAggregator
{
    private readonly Dictionary<int, MeterWindow> _windows = [];
    private readonly long _sizeTicks;
    private readonly TimeSpan _allowedLateness;

    public WindowAggregator(TimeSpan size, TimeSpan allowedLateness)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(size, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(allowedLateness, TimeSpan.Zero);
        if (TimeSpan.FromDays(1).Ticks % size.Ticks != 0)
        {
            throw new ArgumentException("Window size must divide a day evenly.", nameof(size));
        }

        Size = size;
        _sizeTicks = size.Ticks;
        _allowedLateness = allowedLateness;
    }

    public TimeSpan Size { get; }

    /// <summary>Number of meters with an open window.</summary>
    public int OpenWindows => _windows.Count(w => w.Value.IsOpen);

    /// <summary>
    /// Adds a reading. If the reading starts a newer window, the meter's previous window is
    /// closed and written to <paramref name="closed"/>.
    /// </summary>
    public AddOutcome Add(in Reading reading, ICollection<WindowResult> closed)
    {
        var start = AlignStart(reading.Timestamp.UtcTicks);
        ref var window = ref CollectionsMarshal.GetValueRefOrAddDefault(_windows, reading.MeterId, out _);

        if (start < window.ClosedUntilTicks)
        {
            return AddOutcome.Late;
        }

        if (window.IsOpen && start != window.StartTicks)
        {
            closed.Add(window.ToResult(reading.MeterId, _sizeTicks));
            window.Close(_sizeTicks);
        }

        if (!window.IsOpen)
        {
            window.Open(start, reading);
        }
        else
        {
            window.Accumulate(reading);
        }

        return AddOutcome.Aggregated;
    }

    /// <summary>Closes every window whose end plus the allowed lateness is at or before <paramref name="now"/>.</summary>
    public void CloseExpired(DateTimeOffset now, ICollection<WindowResult> closed)
    {
        var cutoff = now.UtcTicks - _allowedLateness.Ticks;
        foreach (var meterId in _windows.Keys)
        {
            ref var window = ref CollectionsMarshal.GetValueRefOrNullRef(_windows, meterId);
            if (window.IsOpen && window.StartTicks + _sizeTicks <= cutoff)
            {
                closed.Add(window.ToResult(meterId, _sizeTicks));
                window.Close(_sizeTicks);
            }
        }
    }

    /// <summary>Closes every open window regardless of time (used on shutdown).</summary>
    public void CloseAll(ICollection<WindowResult> closed)
    {
        foreach (var meterId in _windows.Keys)
        {
            ref var window = ref CollectionsMarshal.GetValueRefOrNullRef(_windows, meterId);
            if (window.IsOpen)
            {
                closed.Add(window.ToResult(meterId, _sizeTicks));
                window.Close(_sizeTicks);
            }
        }
    }

    private long AlignStart(long utcTicks)
    {
        var sinceEpoch = utcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        return utcTicks - (((sinceEpoch % _sizeTicks) + _sizeTicks) % _sizeTicks);
    }

    /// <summary>Mutable per-meter state, stored inline in the dictionary (no allocation per meter).</summary>
    private struct MeterWindow
    {
        public bool IsOpen;
        public long StartTicks;
        public long ClosedUntilTicks;
        public int Count;
        public double SumPowerW;
        public float MinPowerW;
        public float MaxPowerW;
        public long FirstEnergyWh;
        public long LastEnergyWh;

        public void Open(long startTicks, in Reading reading)
        {
            IsOpen = true;
            StartTicks = startTicks;
            Count = 1;
            SumPowerW = reading.PowerW;
            MinPowerW = reading.PowerW;
            MaxPowerW = reading.PowerW;
            FirstEnergyWh = reading.EnergyWh;
            LastEnergyWh = reading.EnergyWh;
        }

        public void Accumulate(in Reading reading)
        {
            Count++;
            SumPowerW += reading.PowerW;
            MinPowerW = MathF.Min(MinPowerW, reading.PowerW);
            MaxPowerW = MathF.Max(MaxPowerW, reading.PowerW);
            LastEnergyWh = reading.EnergyWh;
        }

        public void Close(long sizeTicks)
        {
            IsOpen = false;
            ClosedUntilTicks = StartTicks + sizeTicks;
        }

        public readonly WindowResult ToResult(int meterId, long sizeTicks) => new(
            meterId,
            new DateTimeOffset(StartTicks, TimeSpan.Zero),
            TimeSpan.FromTicks(sizeTicks),
            Count,
            SumPowerW,
            MinPowerW,
            MaxPowerW,
            LastEnergyWh - FirstEnergyWh);
    }
}
