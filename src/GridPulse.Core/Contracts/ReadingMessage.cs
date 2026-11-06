namespace GridPulse.Core.Contracts;

/// <summary>
/// Telemetry payload a meter publishes on <c>gridpulse/meters/{meterId}/readings</c>.
/// </summary>
/// <param name="MeterId">Meter serial number, e.g. <c>MTR-000123</c>.</param>
/// <param name="Ts">Measurement time (event time), UTC.</param>
/// <param name="PowerW">Instantaneous active power in watts.</param>
/// <param name="EnergyWh">Cumulative energy register in watt-hours (monotonic).</param>
/// <param name="VoltageV">RMS voltage in volts.</param>
public sealed record ReadingMessage(
    string MeterId,
    DateTimeOffset Ts,
    double PowerW,
    long EnergyWh,
    double VoltageV);
