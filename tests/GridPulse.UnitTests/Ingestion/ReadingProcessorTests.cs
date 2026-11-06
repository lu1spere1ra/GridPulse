using GridPulse.Core.Readings;
using GridPulse.Ingestion;
using GridPulse.Ingestion.Pipeline;
using GridPulse.UnitTests.Fakes;

namespace GridPulse.UnitTests.Ingestion;

public sealed class ReadingProcessorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly ReadingProcessor _sut = new(
        new IngestionOptions { AllowedLateness = TimeSpan.FromSeconds(5), OfflineAfter = TimeSpan.FromMinutes(5) },
        new InMemoryMeterRegistry(3),
        new IngestionMetrics(new TestMeterFactory()));

    private static Reading R(int meterId, double seconds, float powerW = 100, long energyWh = 0) =>
        new(meterId, T0.AddSeconds(seconds), powerW, energyWh, 230);

    [Fact]
    public void Duplicates_are_not_persisted()
    {
        _sut.Process(R(1, 0)).ShouldBeTrue();
        _sut.Process(R(1, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Tick_reports_closed_one_and_fifteen_minute_windows()
    {
        for (var s = 0; s < 60; s++)
        {
            _sut.Process(R(1, s, powerW: 200, energyWh: s));
        }

        var update = _sut.Tick(T0.AddMinutes(15).AddSeconds(5));

        update.Windows.Select(w => (w.WindowMinutes, w.Count, w.AvgPowerW, w.EnergyWh))
            .ShouldBe([(1, 60, 200d, 59L), (15, 60, 200d, 59L)], ignoreOrder: true);
    }

    [Fact]
    public void Fleet_snapshot_counts_online_and_offline_meters()
    {
        _sut.Process(R(1, 0, powerW: 1_000));
        _sut.Process(R(2, 0, powerW: 500));

        var update = _sut.Tick(T0.AddSeconds(1));

        update.Fleet.OnlineMeters.ShouldBe(2);
        update.Fleet.OfflineMeters.ShouldBe(1);
        update.Fleet.TotalPowerW.ShouldBe(1_500);
    }

    [Fact]
    public void A_meter_silent_for_longer_than_offline_after_is_no_longer_online()
    {
        _sut.Process(R(1, 0));

        _sut.Tick(T0.AddMinutes(5)).Fleet.OnlineMeters.ShouldBe(1);
        _sut.Tick(T0.AddMinutes(5).AddSeconds(1)).Fleet.OnlineMeters.ShouldBe(0);
    }

    [Fact]
    public void Latest_batch_only_contains_meters_that_changed_since_the_last_tick()
    {
        _sut.Process(R(1, 0));
        _sut.Process(R(2, 0));
        _sut.Tick(T0.AddSeconds(1)).Latest.Meters.Count.ShouldBe(2);

        _sut.Process(R(2, 1));
        _sut.Tick(T0.AddSeconds(2)).Latest.Meters.ShouldHaveSingleItem().MeterId.ShouldBe(2);
    }

    [Fact]
    public void Late_readings_are_still_persisted()
    {
        _sut.Process(R(1, 10));
        _sut.Tick(T0.AddMinutes(2));

        // Newer than the last reading (passes ordering) but its 1-minute window already closed.
        _sut.Process(R(1, 30)).ShouldBeTrue();
    }
}
