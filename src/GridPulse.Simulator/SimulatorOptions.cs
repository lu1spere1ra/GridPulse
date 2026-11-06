using System.ComponentModel.DataAnnotations;

namespace GridPulse.Simulator;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    [Range(1, 5_000)]
    public int MeterCount { get; set; } = 500;

    /// <summary>How often each meter publishes a reading.</summary>
    public TimeSpan PublishInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Each interval is split into this many ticks and each tick publishes one slice of the fleet,
    /// so the broker sees a steady stream instead of one burst per interval.
    /// </summary>
    [Range(1, 100)]
    public int SlicesPerInterval { get; set; } = 10;

    /// <summary>Same seed, same fleet and same readings: runs are reproducible.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Probability per reading that a meter starts a 1–3 minute consumption spike.</summary>
    [Range(0, 1)]
    public double SpikeProbability { get; set; } = 0.00005;

    /// <summary>Probability per reading that a meter goes silent for 6–10 minutes.</summary>
    [Range(0, 1)]
    public double OfflineProbability { get; set; } = 0.00002;

    /// <summary>
    /// MQTT connections to publish over. One connection tops out at ~12,000 QoS 1 messages/s on the
    /// reference machine (PUBACK round trips), so load tests above that need more. Each meter
    /// always uses the same connection, which keeps its readings in order.
    /// </summary>
    [Range(1, 64)]
    public int Connections { get; set; } = 1;

    /// <summary>Base URL of the API, used to register the simulated meters at startup.</summary>
    public Uri? ApiBaseUrl { get; set; }
}
