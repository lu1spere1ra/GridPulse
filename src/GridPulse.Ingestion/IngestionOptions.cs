using System.ComponentModel.DataAnnotations;

namespace GridPulse.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Capacity of the channel between the MQTT handler and the processor.</summary>
    [Range(1, 10_000_000)]
    public int InputCapacity { get; set; } = 50_000;

    /// <summary>Capacity of the channel between the processor and the batch writer.</summary>
    [Range(1, 10_000_000)]
    public int WriterCapacity { get; set; } = 50_000;

    /// <summary>A batch is flushed when it reaches this many rows...</summary>
    [Range(1, 1_000_000)]
    public int BatchSize { get; set; } = 5_000;

    /// <summary>...or when its first row has waited this long, whichever comes first.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a window stays open after its end, waiting for a quiet meter.</summary>
    public TimeSpan AllowedLateness { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Period of the processor tick: closes expired windows and publishes live data.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>A meter with no reading for this long is counted as offline in the fleet snapshot.</summary>
    public TimeSpan OfflineAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the known-meters list is reloaded from the database.</summary>
    public TimeSpan MeterRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
