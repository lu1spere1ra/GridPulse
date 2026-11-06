using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using GridPulse.Core.Alerts;

namespace GridPulse.Core.Contracts;

// DTOs of the HTTP API, shared by the API and the Blazor client.

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record MeterDto(int Id, string Serial, string? Name, string? Zone, DateTimeOffset CreatedAt);

public sealed record CreateMeterRequest(
    [property: Required, RegularExpression("^[A-Z0-9-]{3,32}$")] string Serial,
    [property: MaxLength(128)] string? Name,
    [property: MaxLength(64)] string? Zone);

public sealed record UpdateMeterRequest(
    [property: MaxLength(128)] string? Name,
    [property: MaxLength(64)] string? Zone);

public sealed record RegisterMetersResponse(int Requested, int Created);

[JsonConverter(typeof(JsonStringEnumConverter<Resolution>))]
public enum Resolution
{
    Auto,
    Raw,
    OneMinute,
    FifteenMinutes,
    OneHour,
}

/// <summary>One point of a power series. Raw points have Min = Avg = Max and no energy value.</summary>
public sealed record SeriesPoint(DateTimeOffset Ts, double AvgPowerW, double MinPowerW, double MaxPowerW, long? EnergyWh, long Samples);

public sealed record ReadingSeries(int MeterId, Resolution Resolution, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<SeriesPoint> Points);

public sealed record FleetPoint(DateTimeOffset Ts, double TotalPowerW, long EnergyWh, int ReportingMeters);

public sealed record FleetSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    Resolution Resolution,
    double TotalEnergyKwh,
    double PeakPowerW,
    IReadOnlyList<FleetPoint> Series);

public sealed record TopConsumer(int MeterId, string Serial, string? Name, double EnergyKwh, double PeakPowerW);

public sealed record AlertRuleDto(
    int Id,
    string Name,
    AlertRuleType Type,
    int? MeterId,
    double? ThresholdW,
    ThresholdAggregate Aggregate,
    int? OfflineMinutes,
    bool Enabled);

public sealed record AlertRuleRequest(
    [property: Required, MaxLength(128)] string Name,
    AlertRuleType Type,
    int? MeterId,
    [property: Range(1, 1_000_000)] double? ThresholdW,
    ThresholdAggregate Aggregate,
    [property: Range(1, 1_440)] int? OfflineMinutes,
    bool Enabled = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == AlertRuleType.Threshold && ThresholdW is null)
        {
            yield return new ValidationResult("Threshold rules need a threshold.", [nameof(ThresholdW)]);
        }

        if (Type == AlertRuleType.Offline && OfflineMinutes is null)
        {
            yield return new ValidationResult("Offline rules need a number of minutes.", [nameof(OfflineMinutes)]);
        }
    }
}

public sealed record AlertDto(
    long Id,
    int RuleId,
    string RuleName,
    AlertRuleType RuleType,
    int MeterId,
    string MeterSerial,
    AlertState State,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? AcknowledgedAt,
    string Message);
