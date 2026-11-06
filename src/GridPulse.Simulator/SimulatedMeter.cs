using GridPulse.Core.Contracts;

namespace GridPulse.Simulator;

/// <summary>
/// One simulated meter. Each meter owns its random generator, so its readings depend only on
/// the seed and the clock, not on the order in which meters are processed.
/// </summary>
public sealed class SimulatedMeter
{
    private const double SpikePowerW = 9_000;

    private readonly Random _random;
    private readonly double _baseLoadW;
    private readonly double _peakLoadW;
    private readonly double _profileShiftHours;
    private double _energyWh;
    private DateTimeOffset? _lastTick;
    private DateTimeOffset _spikeUntil;
    private DateTimeOffset _offlineUntil;

    public SimulatedMeter(int index, int seed)
    {
        Serial = FormatSerial(index);
        _random = new Random(HashCode.Combine(seed, index));
        _baseLoadW = 150 + (_random.NextDouble() * 350);
        _peakLoadW = 800 + (_random.NextDouble() * 2_200);
        _profileShiftHours = (_random.NextDouble() - 0.5) * 1.5;
        _energyWh = 1_000_000 + (_random.NextDouble() * 20_000_000);
    }

    public string Serial { get; }

    public bool IsOffline(DateTimeOffset now) => now < _offlineUntil;

    public static string FormatSerial(int index) => $"MTR-{index:D6}";

    /// <summary>
    /// Advances the meter to <paramref name="now"/>. Returns <c>null</c> while the meter is
    /// offline (communication loss): the register keeps counting, it just does not report.
    /// </summary>
    public ReadingMessage? Next(DateTimeOffset now, double spikeProbability, double offlineProbability)
    {
        var elapsed = _lastTick is { } last ? now - last : TimeSpan.Zero;
        _lastTick = now;

        if (now >= _offlineUntil && _random.NextDouble() < offlineProbability)
        {
            _offlineUntil = now + TimeSpan.FromMinutes(6 + (_random.NextDouble() * 4));
        }

        if (now >= _spikeUntil && _random.NextDouble() < spikeProbability)
        {
            _spikeUntil = now + TimeSpan.FromMinutes(1 + (_random.NextDouble() * 2));
        }

        var powerW = CurrentPower(now);
        _energyWh += powerW * elapsed.TotalHours;

        if (now < _offlineUntil)
        {
            return null;
        }

        var voltageV = 230 + ((_random.NextDouble() - 0.5) * 8);
        return new ReadingMessage(Serial, now, Math.Round(powerW, 1), (long)_energyWh, Math.Round(voltageV, 1));
    }

    private double CurrentPower(DateTimeOffset now)
    {
        var hour = (now.UtcDateTime.TimeOfDay.TotalHours + _profileShiftHours + 24) % 24;
        var noise = 1 + ((_random.NextDouble() - 0.5) * 0.2);
        var powerW = (_baseLoadW + (_peakLoadW * LoadProfile.DailyFactor(hour))) * noise;
        return now < _spikeUntil ? powerW + SpikePowerW : powerW;
    }
}
