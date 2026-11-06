namespace GridPulse.Simulator;

/// <summary>The simulated meters, pre-split into the slices published on each tick.</summary>
public sealed class MeterFleet
{
    private readonly SimulatedMeter[][] _slices;

    public MeterFleet(int meterCount, int sliceCount, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(meterCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sliceCount, 1);

        Meters = Enumerable.Range(1, meterCount).Select(i => new SimulatedMeter(i, seed)).ToArray();
        _slices = Enumerable.Range(0, sliceCount)
            .Select(slice => Meters.Where((_, index) => index % sliceCount == slice).ToArray())
            .ToArray();
    }

    public IReadOnlyList<SimulatedMeter> Meters { get; }

    public int SliceCount => _slices.Length;

    public IReadOnlyList<SimulatedMeter> Slice(long tick) => _slices[tick % _slices.Length];
}
