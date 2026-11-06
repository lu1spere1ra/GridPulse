using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using GridPulse.Core.Meters;
using GridPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.IntegrationTests.Database;

public sealed class AlertStoreTests(CommunityDatabase community, ApacheDatabase apache) : DatabaseTestBase(community, apache)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed class Factory(TimescaleDatabase db) : IDbContextFactory<GridPulseDbContext>
    {
        public GridPulseDbContext CreateDbContext() => db.CreateDbContext();
    }

    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Raises_once_and_resolves_the_open_alert(TimescaleEdition edition)
    {
        var db = Db(edition);
        int meterId;
        await using (var context = db.CreateDbContext())
        {
            var meter = new Meter { Serial = $"A-{Guid.NewGuid().ToString("N")[..10]}" };
            context.Meters.Add(meter);
            await context.SaveChangesAsync(Ct);
            meterId = meter.Id;
        }

        var store = new AlertStore(new Factory(db));
        var firing = new AlertTransition(1, "High consumption", AlertRuleType.Threshold, meterId, AlertState.Firing, T0, "Above 7 kW.");

        var raised = await store.ApplyAsync(firing, Ct);
        raised.ShouldNotBeNull().Alert.State.ShouldBe(AlertState.Firing);

        (await store.ApplyAsync(firing with { At = T0.AddMinutes(1) }, Ct)).ShouldBeNull("a second firing for the same rule and meter is a no-op");

        var resolved = await store.ApplyAsync(firing with { State = AlertState.Resolved, At = T0.AddMinutes(5), Message = "Back to 2 kW." }, Ct);
        resolved.ShouldNotBeNull().Alert.Id.ShouldBe(raised.Value.Alert.Id);
        resolved.Value.Alert.ResolvedAt.ShouldBe(T0.AddMinutes(5));

        // After resolving, the same rule can fire again for the same meter.
        (await store.ApplyAsync(firing with { At = T0.AddMinutes(10) }, Ct)).ShouldNotBeNull();
    }
}
