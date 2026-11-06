using System.Text.Json.Serialization;
using GridPulse.Core.Contracts;

namespace GridPulse.Core.Alerts;

[JsonConverter(typeof(JsonStringEnumConverter<ThresholdAggregate>))]
public enum ThresholdAggregate
{
    /// <summary>Average power over the closed 1-minute window.</summary>
    Average,

    /// <summary>Peak power within the closed 1-minute window.</summary>
    Maximum,
}

/// <summary>
/// A configurable alert rule. <see cref="MeterId"/> = <c>null</c> applies the rule to every meter;
/// a rule for a specific meter takes precedence over a fleet-wide rule of the same type.
/// </summary>
public sealed class AlertRule
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public AlertRuleType Type { get; set; }

    public int? MeterId { get; set; }

    /// <summary>Threshold rules: the power limit in watts.</summary>
    public double? ThresholdW { get; set; }

    /// <summary>Threshold rules: which window statistic is compared with the limit.</summary>
    public ThresholdAggregate Aggregate { get; set; } = ThresholdAggregate.Average;

    /// <summary>Offline rules: minutes without a reading before the meter is considered offline.</summary>
    public int? OfflineMinutes { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
