using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using GridPulse.Core.Meters;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using GridPulse.Reports.Reporting;

namespace GridPulse.IntegrationTests.Database;

public sealed class DailyReportQueryTests(CommunityDatabase community, ApacheDatabase apache) : DatabaseTestBase(community, apache)
{
    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Reports_energy_peak_and_alerts_of_one_utc_day(TimescaleEdition edition)
    {
        var db = Db(edition);
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        Meter meter;
        await using (var context = db.CreateDbContext())
        {
            meter = new Meter { Serial = $"D-{Guid.NewGuid().ToString("N")[..10]}", Name = "Report test" };
            context.Meters.Add(meter);
            await context.SaveChangesAsync(Ct);
            var rule = context.AlertRules.First();
            context.Alerts.Add(new Alert { RuleId = rule.Id, MeterId = meter.Id, State = AlertState.Resolved, RaisedAt = start.AddHours(13), Message = "test" });
            await context.SaveChangesAsync(Ct);
        }

        // One reading per minute for the whole day; the register climbs 2 Wh per minute.
        // The readings of the next day must not be counted.
        var readings = Enumerable.Range(0, (24 * 60) + 30)
            .Select(i => new Reading(meter.Id, start.AddMinutes(i), i == 600 ? 9_000 : 120, 1_000_000 + (2L * i), 230))
            .ToList();
        await new CopyReadingBatchWriter(db.DataSource).WriteAsync(readings, Ct);

        var rows = await new DailyReportQuery(db.DataSource).GetAsync(day, Ct);

        var row = rows.Single(r => r.MeterId == meter.Id);
        row.Samples.ShouldBe(24 * 60);
        row.EnergyKwh.ShouldBe(((24 * 60) - 1) * 2 / 1000.0, tolerance: 1e-9);
        row.PeakPowerW.ShouldBe(9_000);
        row.Alerts.ShouldBe(1);
        row.Name.ShouldBe("Report test");
    }
}
