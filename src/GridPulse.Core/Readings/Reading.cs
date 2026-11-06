namespace GridPulse.Core.Readings;

/// <summary>
/// A validated reading inside the ingestion pipeline. The meter serial has already been
/// resolved to its database id, so the hot path works with integers only.
/// </summary>
public readonly record struct Reading(
    int MeterId,
    DateTimeOffset Timestamp,
    float PowerW,
    long EnergyWh,
    float VoltageV);
