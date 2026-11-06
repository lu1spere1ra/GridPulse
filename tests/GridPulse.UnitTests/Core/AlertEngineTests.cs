using GridPulse.Core.Aggregation;
using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GridPulse.UnitTests.Core;

public sealed class AlertEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly AlertEngine _engine = new();

    private static readonly AlertRule FleetThreshold = new() { Id = 1, Name = "High consumption", Type = AlertRuleType.Threshold, ThresholdW = 7_000 };
    private static readonly AlertRule FleetOffline = new() { Id = 2, Name = "Offline", Type = AlertRuleType.Offline, OfflineMinutes = 5 };

    private void Configure(params AlertRule[] rules)
    {
        _engine.UpdateRules(rules);
        _engine.OnTick(_time.GetUtcNow());
    }

    private static WindowResult Window(int meterId, double avgW, double maxW = 0, int minutes = 1) =>
        new(meterId, T0, TimeSpan.FromMinutes(minutes), 60, avgW * 60, (float)avgW, (float)Math.Max(avgW, maxW), 10);

    [Fact]
    public void Threshold_fires_once_when_the_window_average_exceeds_the_limit()
    {
        Configure(FleetThreshold);

        _engine.OnWindowClosed(Window(1, 7_500));
        _engine.OnWindowClosed(Window(1, 8_000));

        var transition = _engine.DrainTransitions().ShouldHaveSingleItem();
        transition.State.ShouldBe(AlertState.Firing);
        transition.MeterId.ShouldBe(1);
        transition.Message.ShouldContain("7.50 kW");
    }

    [Fact]
    public void Threshold_resolves_only_below_the_hysteresis_band()
    {
        Configure(FleetThreshold);
        _engine.OnWindowClosed(Window(1, 7_500));
        _engine.DrainTransitions();

        _engine.OnWindowClosed(Window(1, 6_500)); // below the limit but within 90 %: still firing
        _engine.DrainTransitions().ShouldBeEmpty();

        _engine.OnWindowClosed(Window(1, 6_000));
        _engine.DrainTransitions().ShouldHaveSingleItem().State.ShouldBe(AlertState.Resolved);
    }

    [Fact]
    public void Maximum_rules_compare_the_peak_not_the_average()
    {
        Configure(new AlertRule { Id = 3, Name = "Peak", Type = AlertRuleType.Threshold, ThresholdW = 9_000, Aggregate = ThresholdAggregate.Maximum });

        _engine.OnWindowClosed(Window(1, avgW: 2_000, maxW: 9_500));

        _engine.DrainTransitions().ShouldHaveSingleItem().State.ShouldBe(AlertState.Firing);
    }

    [Fact]
    public void Fifteen_minute_windows_do_not_trigger_threshold_rules()
    {
        Configure(FleetThreshold);

        _engine.OnWindowClosed(Window(1, 9_000, minutes: 15));

        _engine.DrainTransitions().ShouldBeEmpty();
    }

    [Fact]
    public void A_meter_specific_rule_overrides_the_fleet_rule()
    {
        Configure(FleetThreshold, new AlertRule { Id = 4, Name = "Bakery", Type = AlertRuleType.Threshold, MeterId = 1, ThresholdW = 20_000 });

        _engine.OnWindowClosed(Window(1, 10_000));
        _engine.OnWindowClosed(Window(2, 10_000));

        _engine.DrainTransitions().ShouldHaveSingleItem().MeterId.ShouldBe(2);
    }

    [Fact]
    public void Offline_fires_after_the_limit_and_resolves_when_the_meter_reports_again()
    {
        Configure(FleetOffline);
        _engine.OnReading(1, _time.GetUtcNow());

        _time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        _engine.OnTick(_time.GetUtcNow());
        _engine.DrainTransitions().ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(2));
        _engine.OnTick(_time.GetUtcNow());
        _engine.DrainTransitions().ShouldHaveSingleItem().State.ShouldBe(AlertState.Firing);

        _time.Advance(TimeSpan.FromMinutes(10));
        _engine.OnTick(_time.GetUtcNow());
        _engine.DrainTransitions().ShouldBeEmpty("an offline alert fires once, not on every tick");

        _engine.OnReading(1, _time.GetUtcNow());
        _engine.DrainTransitions().ShouldHaveSingleItem().State.ShouldBe(AlertState.Resolved);
    }

    [Fact]
    public void Registered_meters_that_never_report_go_offline_after_a_grace_period()
    {
        _engine.UpdateMeters([7]);
        Configure(FleetOffline);

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        _engine.OnTick(_time.GetUtcNow());

        _engine.DrainTransitions().ShouldHaveSingleItem().MeterId.ShouldBe(7);
    }

    [Fact]
    public void Disabling_a_rule_resolves_its_open_alerts()
    {
        Configure(FleetThreshold);
        _engine.OnWindowClosed(Window(1, 9_000));
        _engine.DrainTransitions();

        Configure(FleetOffline);

        var resolved = _engine.DrainTransitions().ShouldHaveSingleItem();
        (resolved.RuleId, resolved.State).ShouldBe((1, AlertState.Resolved));
    }

    [Fact]
    public void Alerts_restored_after_a_restart_can_still_resolve()
    {
        _engine.RestoreOpen([(FleetThreshold.Id, 1)]);
        Configure(FleetThreshold);

        _engine.OnWindowClosed(Window(1, 1_000));

        _engine.DrainTransitions().ShouldHaveSingleItem().State.ShouldBe(AlertState.Resolved);
    }
}
