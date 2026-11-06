namespace GridPulse.Core.Aggregation;

/// <summary>
/// Aggregate of one meter over one tumbling window <c>[Start, Start + Size)</c>.
/// <see cref="EnergyWh"/> is <c>last(register) - first(register)</c> inside the window,
/// which is exactly what the database continuous aggregates compute.
/// </summary>
public readonly record struct WindowResult(
    int MeterId,
    DateTimeOffset Start,
    TimeSpan Size,
    int Count,
    double SumPowerW,
    float MinPowerW,
    float MaxPowerW,
    long EnergyWh)
{
    public double AvgPowerW => Count == 0 ? 0 : SumPowerW / Count;

    public DateTimeOffset End => Start + Size;
}
