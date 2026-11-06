using GridPulse.Core.Contracts;
using GridPulse.Web.Components.Shared;
using GridPulse.Web.Services;

namespace GridPulse.Web.Tests;

public sealed class FleetComponentsTests : GridPulseTestContext
{
    private static AlertDto Alert(long id, AlertState state, DateTimeOffset? acknowledged = null) =>
        new(id, 1, "High consumption", AlertRuleType.Threshold, 10, "MTR-000010", state, Now.AddMinutes(-3), null, acknowledged, "Average 9.1 kW above 7 kW");

    [Fact]
    public void Status_strip_shows_the_snapshot_and_highlights_alerts()
    {
        var strip = Render<FleetStatusStrip>(p => p
            .Add(c => c.Snapshot, new FleetSnapshot(Now, 500_000, 498, 2, 497.6))
            .Add(c => c.ActiveAlerts, 3));

        strip.FindAll("dd").Select(d => d.TextContent).ShouldBe(["498", "2", "498", "3"]);
        strip.Find(".gp-alert dd").TextContent.ShouldBe("3");
    }

    [Fact]
    public void Alert_list_shows_an_actionable_empty_state()
    {
        var list = Render<AlertList>(p => p.Add(c => c.Alerts, []).Add(c => c.EmptyText, "No active alerts."));

        list.Markup.ShouldContain("No active alerts.");
    }

    [Fact]
    public void Acknowledge_invokes_the_callback_with_the_alert_id()
    {
        long? acknowledged = null;
        var list = Render<AlertList>(p => p
            .Add(c => c.Alerts, [Alert(42, AlertState.Firing)])
            .Add(c => c.OnAcknowledge, (long id) => acknowledged = id));

        list.Find("li").ClassList.ShouldContain("is-firing");
        list.Find("button").Click();

        acknowledged.ShouldBe(42);
    }

    [Fact]
    public void Acknowledged_alerts_have_no_acknowledge_button()
    {
        var list = Render<AlertList>(p => p
            .Add(c => c.Alerts, [Alert(1, AlertState.Firing, Now)])
            .Add(c => c.OnAcknowledge, (long _) => { }));

        list.FindAll("button").ShouldBeEmpty();
        list.Markup.ShouldContain("Acknowledged");
    }

    [Fact]
    public void Connection_banner_appears_only_while_the_feed_is_down()
    {
        var banner = Render<ConnectionBanner>();
        banner.Markup.Trim().ShouldBeEmpty();

        Feed.SetStatus(FeedStatus.Reconnecting);

        banner.WaitForAssertion(() => banner.Markup.ShouldContain("Live data is paused"));
    }
}
