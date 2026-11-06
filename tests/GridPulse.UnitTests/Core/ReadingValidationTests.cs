using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;

namespace GridPulse.UnitTests.Core;

public sealed class ReadingValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly ReadingValidator _validator = new(new ReadingValidationOptions());

    private static ReadingMessage Valid() => new("MTR-000001", Now, 1_500, 10_000, 231);

    [Fact]
    public void Accepts_a_plausible_reading() =>
        _validator.Validate(Valid(), Now).ShouldBe(RejectReason.None);

    [Theory]
    [InlineData(-1)]
    [InlineData(50_001)]
    [InlineData(double.NaN)]
    public void Rejects_power_out_of_range(double powerW) =>
        _validator.Validate(Valid() with { PowerW = powerW }, Now).ShouldBe(RejectReason.PowerOutOfRange);

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void Rejects_voltage_out_of_range(double voltageV) =>
        _validator.Validate(Valid() with { VoltageV = voltageV }, Now).ShouldBe(RejectReason.VoltageOutOfRange);

    [Fact]
    public void Tolerates_small_clock_skew_but_rejects_future_timestamps()
    {
        _validator.Validate(Valid() with { Ts = Now.AddSeconds(30) }, Now).ShouldBe(RejectReason.None);
        _validator.Validate(Valid() with { Ts = Now.AddSeconds(31) }, Now).ShouldBe(RejectReason.TimestampInFuture);
    }

    [Fact]
    public void Rejects_readings_older_than_max_age() =>
        _validator.Validate(Valid() with { Ts = Now.AddHours(-25) }, Now).ShouldBe(RejectReason.TimestampTooOld);

    [Fact]
    public void Sequence_guard_drops_duplicates_and_out_of_order_readings()
    {
        var guard = new MeterSequenceGuard();
        var first = new Reading(1, Now, 1, 100, 230);

        guard.Check(first).ShouldBe(RejectReason.None);
        guard.Check(first).ShouldBe(RejectReason.DuplicateOrOutOfOrder);
        guard.Check(first with { Timestamp = Now.AddSeconds(-1) }).ShouldBe(RejectReason.DuplicateOrOutOfOrder);
        guard.Check(first with { Timestamp = Now.AddSeconds(1), EnergyWh = 101 }).ShouldBe(RejectReason.None);
    }

    [Fact]
    public void Sequence_guard_rejects_energy_register_regression()
    {
        var guard = new MeterSequenceGuard();
        guard.Check(new Reading(1, Now, 1, 100, 230));

        guard.Check(new Reading(1, Now.AddSeconds(1), 1, 99, 230)).ShouldBe(RejectReason.RegisterRegression);
        guard.Check(new Reading(2, Now.AddSeconds(1), 1, 0, 230)).ShouldBe(RejectReason.None);
    }
}
