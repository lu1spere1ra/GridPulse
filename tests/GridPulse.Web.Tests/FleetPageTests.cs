using GridPulse.Core.Contracts;
using GridPulse.Web.Components.Pages;

namespace GridPulse.Web.Tests;

public sealed class FleetPageTests : GridPulseTestContext
{
    public FleetPageTests()
    {
        Api.Respond("/api/v1/analytics/fleet", new FleetSummary(Now.AddMinutes(-15), Now, Resolution.OneMinute, 12.5, 400_000,
                [new FleetPoint(Now.AddMinutes(-2), 390_000, 6_000, 500), new FleetPoint(Now.AddMinutes(-1), 400_000, 6_500, 500)]))
            .Respond("/api/v1/alerts", new List<AlertDto>())
            .Respond("/api/v1/analytics/top-consumers", new List<TopConsumer> { new(10, "MTR-000010", null, 4.2, 9_100) });
    }

    [Fact]
    public void Backfills_the_chart_from_the_api_and_lists_top_consumers()
    {
        var page = Render<Fleet>();

        page.WaitForAssertion(() => page.Find("polyline").GetAttribute("points")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2));
        page.Markup.ShouldContain("MTR-000010");
    }

    [Fact]
    public void Live_snapshots_update_the_headline_value_and_the_chart()
    {
        var page = Render<Fleet>();
        page.WaitForAssertion(() => page.FindAll("polyline").Count.ShouldBe(1));

        Feed.Publish(new FleetSnapshot(Now.AddSeconds(1), 512_340, 500, 0, 499));

        page.WaitForAssertion(() =>
        {
            page.Find(".gp-hero-value").TextContent.ShouldBe("512.34 kW");
            page.Find("polyline").GetAttribute("points")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(3);
        });
    }

    [Fact]
    public void A_snapshot_published_while_history_loads_does_not_break_time_order()
    {
        Api.OnRequest = path =>
        {
            if (path.StartsWith("/api/v1/analytics/fleet", StringComparison.Ordinal))
            {
                Feed.Publish(new FleetSnapshot(Now, 999_000, 500, 0, 499));
            }
        };

        var page = Render<Fleet>();
        Feed.Publish(new FleetSnapshot(Now.AddSeconds(1), 512_000, 500, 0, 499));

        page.WaitForAssertion(() =>
        {
            var xs = page.Find("polyline").GetAttribute("points")!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => double.Parse(p.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            xs.Count.ShouldBe(3, "two history points and the snapshot that arrived after loading");
            xs.ShouldBeInOrder(SortDirection.Ascending);
        });
    }
}
