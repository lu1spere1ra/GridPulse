using GridPulse.Simulator;

namespace GridPulse.UnitTests.Simulator;

public sealed class SimulatedMeterTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_seed_produces_identical_readings()
    {
        var a = new SimulatedMeter(7, seed: 42);
        var b = new SimulatedMeter(7, seed: 42);

        for (var s = 0; s < 100; s++)
        {
            var now = Midnight.AddSeconds(s);
            a.Next(now, 0.01, 0.01).ShouldBe(b.Next(now, 0.01, 0.01));
        }
    }

    [Fact]
    public void Seeds_are_stable_across_processes()
    {
        // A constant, not a comparison of two instances: this catches process-randomised hashing.
        SimulatedMeter.DeriveSeed(42, 7).ShouldBe(42_000_133);
    }

    [Fact]
    public void Energy_register_never_decreases()
    {
        var meter = new SimulatedMeter(1, seed: 1);
        long previous = 0;

        for (var s = 0; s < 3_600; s++)
        {
            var reading = meter.Next(Midnight.AddSeconds(s), spikeProbability: 0, offlineProbability: 0)!;
            reading.EnergyWh.ShouldBeGreaterThanOrEqualTo(previous);
            previous = reading.EnergyWh;
        }
    }

    [Fact]
    public void A_restarted_simulator_continues_the_register_instead_of_rewinding_it()
    {
        var running = new SimulatedMeter(5, seed: 42);
        long last = 0;
        for (var s = 0; s < 3_600; s++)
        {
            last = running.Next(Midnight.AddHours(19).AddSeconds(s), spikeProbability: 0.01, offlineProbability: 0)!.EnergyWh;
        }

        // Same meter, new process, one second later.
        var restarted = new SimulatedMeter(5, seed: 42);
        restarted.Next(Midnight.AddHours(20).AddSeconds(1), 0, 0)!.EnergyWh.ShouldBeGreaterThanOrEqualTo(last);
    }

    [Fact]
    public void Evening_consumption_is_higher_than_night_consumption()
    {
        static double AveragePower(int hour)
        {
            var meter = new SimulatedMeter(3, seed: 9);
            return Enumerable.Range(0, 600)
                .Select(s => meter.Next(Midnight.AddHours(hour).AddSeconds(s), 0, 0)!.PowerW)
                .Average();
        }

        AveragePower(19).ShouldBeGreaterThan(AveragePower(3) * 2);
    }

    [Fact]
    public void A_meter_that_goes_offline_stays_silent_for_at_least_six_minutes()
    {
        var meter = new SimulatedMeter(1, seed: 1);

        meter.Next(Midnight, spikeProbability: 0, offlineProbability: 1).ShouldBeNull();

        meter.Next(Midnight.AddMinutes(5.9), 0, 0).ShouldBeNull();
        meter.Next(Midnight.AddMinutes(10.1), 0, 0).ShouldNotBeNull();
    }

    [Fact]
    public void A_spike_pushes_power_well_above_the_normal_range()
    {
        var meter = new SimulatedMeter(1, seed: 1);

        var reading = meter.Next(Midnight.AddHours(3), spikeProbability: 1, offlineProbability: 0)!;

        reading.PowerW.ShouldBeGreaterThan(9_000);
    }

    [Fact]
    public void Daily_profile_stays_within_zero_and_one()
    {
        for (var hour = 0.0; hour < 24; hour += 0.25)
        {
            LoadProfile.DailyFactor(hour).ShouldBeInRange(0, 1);
        }
    }
}
