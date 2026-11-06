using GridPulse.Web.Components.Shared;

namespace GridPulse.Web.Tests;

public sealed class LineChartTests : GridPulseTestContext
{
    private static List<ChartPoint> Points(int count) =>
        Enumerable.Range(0, count).Select(i => new ChartPoint(Now.AddSeconds(i), 1_000 + i)).ToList();

    [Fact]
    public void Shows_the_empty_text_until_there_are_two_points()
    {
        var chart = Render<LineChart>(p => p.Add(c => c.Points, Points(1)).Add(c => c.EmptyText, "Waiting for live readings"));

        chart.Find(".gp-chart-empty").TextContent.ShouldBe("Waiting for live readings");
        chart.FindAll("polyline").ShouldBeEmpty();
    }

    [Fact]
    public void Draws_one_vertex_per_point()
    {
        var chart = Render<LineChart>(p => p.Add(c => c.Points, Points(60)));

        var vertices = chart.Find("polyline").GetAttribute("points")!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        vertices.Length.ShouldBe(60);
        chart.Instance.RenderedPoints.ShouldBe(60);
    }

    [Fact]
    public void Draws_the_threshold_when_given()
    {
        var chart = Render<LineChart>(p => p.Add(c => c.Points, Points(10)).Add(c => c.Threshold, 7_000));

        chart.FindAll("line.gp-threshold").Count.ShouldBe(1);
    }

    [Fact]
    public void Labels_the_axis_in_readable_units()
    {
        var chart = Render<LineChart>(p => p.Add(c => c.Points, Points(10)));

        chart.FindAll(".gp-axis-y span").Select(s => s.TextContent).ShouldContain("2.00 kW");
    }
}
