using System.Runtime.InteropServices;

namespace GridPulse.Core.Readings;

/// <summary>
/// Per-meter ordering checks. A meter's readings must have strictly increasing timestamps
/// and a non-decreasing energy register. This drops MQTT QoS 1 redeliveries (same timestamp)
/// before they reach the aggregates, and catches broken or replaced meters.
/// </summary>
/// <remarks>Not thread-safe by design: owned by the single pipeline processor.</remarks>
public sealed class MeterSequenceGuard
{
    private readonly Dictionary<int, (long Ticks, long EnergyWh)> _last = [];

    public RejectReason Check(in Reading reading)
    {
        ref var last = ref CollectionsMarshal.GetValueRefOrAddDefault(_last, reading.MeterId, out var exists);
        if (exists)
        {
            if (reading.Timestamp.UtcTicks <= last.Ticks)
            {
                return RejectReason.DuplicateOrOutOfOrder;
            }

            if (reading.EnergyWh < last.EnergyWh)
            {
                return RejectReason.RegisterRegression;
            }
        }

        last = (reading.Timestamp.UtcTicks, reading.EnergyWh);
        return RejectReason.None;
    }
}
