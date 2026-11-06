using GridPulse.Core.Aggregation;
using GridPulse.Core.Readings;

namespace GridPulse.UnitTests.Core;

public sealed class WindowAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly List<WindowResult> _closed = [];

    private static Reading At(TimeSpan offset, float powerW, long energyWh = 0, int meterId = 1) =>
        new(meterId, T0 + offset, powerW, energyWh, 230);

    [Fact]
    public void Aggregates_readings_of_the_same_window()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

        sut.Add(At(TimeSpan.FromSeconds(0), 100, 1_000), _closed);
        sut.Add(At(TimeSpan.FromSeconds(20), 300, 1_002), _closed);
        sut.Add(At(TimeSpan.FromSeconds(59.999), 200, 1_005), _closed);
        sut.CloseAll(_closed);

        var window = _closed.ShouldHaveSingleItem();
        window.Start.ShouldBe(T0);
        window.Count.ShouldBe(3);
        window.AvgPowerW.ShouldBe(200);
        window.MinPowerW.ShouldBe(100);
        window.MaxPowerW.ShouldBe(300);
        window.EnergyWh.ShouldBe(5);
    }

    [Fact]
    public void A_reading_in_the_next_window_closes_the_previous_one_immediately()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

        sut.Add(At(TimeSpan.FromSeconds(30), 100), _closed);
        _closed.ShouldBeEmpty();

        sut.Add(At(TimeSpan.FromSeconds(60), 200), _closed);

        var window = _closed.ShouldHaveSingleItem();
        window.Start.ShouldBe(T0);
        window.Count.ShouldBe(1);
    }

    [Fact]
    public void Window_boundary_is_inclusive_at_start_and_exclusive_at_end()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.Zero);

        sut.Add(At(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1), 1), _closed);
        sut.Add(At(TimeSpan.FromMinutes(1), 2), _closed);
        sut.CloseAll(_closed);

        _closed.Select(w => (w.Start, w.Count)).ShouldBe(
        [
            (T0, 1),
            (T0.AddMinutes(1), 1),
        ]);
    }

    [Fact]
    public void Windows_are_aligned_to_utc_boundaries_like_time_bucket()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(15), TimeSpan.Zero);

        sut.Add(At(TimeSpan.FromMinutes(22), 1), _closed);
        sut.CloseAll(_closed);

        _closed.ShouldHaveSingleItem().Start.ShouldBe(T0.AddMinutes(15));
    }

    [Fact]
    public void Quiet_meter_window_closes_only_after_allowed_lateness()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        sut.Add(At(TimeSpan.FromSeconds(10), 100), _closed);

        sut.CloseExpired(T0.AddMinutes(1).AddSeconds(4.9), _closed);
        _closed.ShouldBeEmpty();

        sut.CloseExpired(T0.AddMinutes(1).AddSeconds(5), _closed);
        _closed.ShouldHaveSingleItem().Start.ShouldBe(T0);
    }

    [Fact]
    public void Reading_for_an_already_closed_window_is_late_and_not_aggregated()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        sut.Add(At(TimeSpan.FromSeconds(10), 100), _closed);
        sut.CloseExpired(T0.AddMinutes(2), _closed);
        _closed.Clear();

        var outcome = sut.Add(At(TimeSpan.FromSeconds(50), 999), _closed);

        outcome.ShouldBe(AddOutcome.Late);
        sut.CloseAll(_closed);
        _closed.ShouldBeEmpty();
    }

    [Fact]
    public void Meters_are_aggregated_independently()
    {
        var sut = new WindowAggregator(TimeSpan.FromMinutes(1), TimeSpan.Zero);

        sut.Add(At(TimeSpan.FromSeconds(1), 100, meterId: 1), _closed);
        sut.Add(At(TimeSpan.FromSeconds(2), 500, meterId: 2), _closed);
        sut.Add(At(TimeSpan.FromSeconds(3), 300, meterId: 1), _closed);
        sut.CloseAll(_closed);

        _closed.OrderBy(w => w.MeterId).Select(w => (w.MeterId, w.AvgPowerW)).ShouldBe([(1, 200d), (2, 500d)]);
    }

    [Fact]
    public void Rejects_window_sizes_that_do_not_divide_a_day()
    {
        Should.Throw<ArgumentException>(() => new WindowAggregator(TimeSpan.FromMinutes(7), TimeSpan.Zero));
    }
}
