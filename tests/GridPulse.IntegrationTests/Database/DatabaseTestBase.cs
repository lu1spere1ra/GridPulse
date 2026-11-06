using GridPulse.Infrastructure.Persistence;

namespace GridPulse.IntegrationTests.Database;

/// <summary>Gives each test its own meter ids so tests can share one database and run in parallel.</summary>
public abstract class DatabaseTestBase(CommunityDatabase community, ApacheDatabase apache)
{
    private static int _nextMeterId = 100_000;

    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    protected TimescaleDatabase Db(TimescaleEdition edition) => edition == TimescaleEdition.Community ? community : apache;

    protected static int[] NewMeterIds(int count)
    {
        var last = Interlocked.Add(ref _nextMeterId, count);
        return Enumerable.Range(last - count + 1, count).ToArray();
    }
}
