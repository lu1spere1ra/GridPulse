using GridPulse.Core.Contracts;

namespace GridPulse.UnitTests.Core;

public sealed class ResolutionSelectorTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, Resolution.Raw)]
    [InlineData(15, Resolution.Raw)]
    [InlineData(16, Resolution.OneMinute)]
    [InlineData(1_000, Resolution.OneMinute)]
    [InlineData(1_001, Resolution.FifteenMinutes)]
    [InlineData(15_000, Resolution.FifteenMinutes)]
    [InlineData(15_001, Resolution.OneHour)]
    public void Picks_the_coarsest_resolution_that_keeps_about_a_thousand_points(int minutes, Resolution expected) =>
        ResolutionSelector.Choose(From, From.AddMinutes(minutes)).ShouldBe(expected);

    [Fact]
    public void Never_returns_raw_when_raw_is_not_allowed() =>
        ResolutionSelector.Choose(From, From.AddMinutes(5), allowRaw: false).ShouldBe(Resolution.OneMinute);
}
