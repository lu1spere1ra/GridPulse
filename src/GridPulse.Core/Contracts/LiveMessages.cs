namespace GridPulse.Core.Contracts;

/// <summary>Fleet-wide numbers, published once per second by ingestion.</summary>
public sealed record FleetSnapshot(
    DateTimeOffset Ts,
    double TotalPowerW,
    int OnlineMeters,
    int OfflineMeters,
    double ReadingsPerSecond);

/// <summary>Latest known value of one meter.</summary>
public sealed record MeterLatest(
    int MeterId,
    string Serial,
    DateTimeOffset Ts,
    double PowerW);

/// <summary>Batch of latest values, published once per second by ingestion.</summary>
public sealed record MeterLatestBatch(DateTimeOffset Ts, IReadOnlyList<MeterLatest> Meters);

/// <summary>A closed aggregation window for one meter.</summary>
public sealed record WindowClosed(
    int MeterId,
    DateTimeOffset Start,
    int WindowMinutes,
    int Count,
    double AvgPowerW,
    double MaxPowerW,
    long EnergyWh);

public enum AlertState
{
    Firing,
    Resolved,
}

public enum AlertRuleType
{
    Threshold,
    Offline,
}

/// <summary>Alert transition pushed to dashboards (raised or resolved).</summary>
public sealed record AlertNotification(
    long AlertId,
    int RuleId,
    string RuleName,
    AlertRuleType RuleType,
    int MeterId,
    string MeterSerial,
    AlertState State,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    string Message);
