namespace GridPulse.Core.Readings;

public sealed class ReadingValidationOptions
{
    /// <summary>Upper bound for a single meter (a residential connection is far below this).</summary>
    public double MaxPowerW { get; set; } = 50_000;

    public double MinVoltageV { get; set; } = 180;

    public double MaxVoltageV { get; set; } = 270;

    /// <summary>Tolerated clock skew between a meter and the server.</summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Readings older than this are rejected instead of being stored.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
}
