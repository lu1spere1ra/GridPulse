namespace GridPulse.Ingestion.Alerts;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host; leave empty to disable email.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 1025;

    public string From { get; set; } = "gridpulse@localhost";

    public string To { get; set; } = "operators@localhost";

    /// <summary>Minimum time between two emails for the same (rule, meter).</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Global cap, so a mass event (e.g. the whole fleet going offline) cannot flood the inbox.</summary>
    public int MaxPerMinute { get; set; } = 10;
}
