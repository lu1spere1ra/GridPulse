namespace GridPulse.Reports.Reporting;

/// <summary>One meter's day.</summary>
public sealed record MeterDay(
    int MeterId,
    string Serial,
    string? Name,
    double EnergyKwh,
    double AveragePowerW,
    double PeakPowerW,
    long Samples,
    int Alerts);

/// <summary>Daily consumption report for the whole fleet (one UTC day).</summary>
public sealed record DailyReport(DateOnly Date, DateTimeOffset GeneratedAt, IReadOnlyList<MeterDay> Meters)
{
    public double TotalEnergyKwh => Meters.Sum(m => m.EnergyKwh);

    public double PeakMeterPowerW => Meters.Count == 0 ? 0 : Meters.Max(m => m.PeakPowerW);

    public int TotalAlerts => Meters.Sum(m => m.Alerts);

    public int ReportingMeters => Meters.Count(m => m.Samples > 0);

    /// <summary>Blob path for this report, e.g. <c>daily/2026/11/05/gridpulse-daily-2026-11-05.csv</c>.</summary>
    public string BlobName(string extension) =>
        $"daily/{Date:yyyy}/{Date:MM}/{Date:dd}/gridpulse-daily-{Date:yyyy-MM-dd}.{extension}";
}
