using Dapper;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;

namespace GridPulse.IntegrationTests.Database;

public sealed class CopyReadingBatchWriterTests(CommunityDatabase community, ApacheDatabase apache) : DatabaseTestBase(community, apache)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Writes_a_batch_and_ignores_redelivered_duplicates(TimescaleEdition edition)
    {
        var meters = NewMeterIds(3);
        var batch = meters.SelectMany(m => Enumerable.Range(0, 100).Select(s => new Reading(m, T0.AddSeconds(s), 1_000 + s, s, 230))).ToList();
        var writer = new CopyReadingBatchWriter(Db(edition).DataSource);

        (await writer.WriteAndCountAsync(batch, Ct)).ShouldBe(300);

        // QoS 1 redelivery after a reconnect: the same readings plus one new reading.
        var redelivery = batch.Take(50).Append(new Reading(meters[0], T0.AddSeconds(100), 1, 100, 230)).ToList();
        (await writer.WriteAndCountAsync(redelivery, Ct)).ShouldBe(1);

        await using var connection = await Db(edition).DataSource.OpenConnectionAsync(Ct);
        var stored = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM readings WHERE meter_id = ANY(@meters)", new { meters });
        stored.ShouldBe(301);
    }

    [Fact]
    public async Task Round_trips_values_exactly()
    {
        var meter = NewMeterIds(1)[0];
        var reading = new Reading(meter, T0.AddMilliseconds(123), 1234.5f, 9_876_543_210, 231.2f);

        await new CopyReadingBatchWriter(Db(TimescaleEdition.Community).DataSource).WriteAsync([reading], Ct);

        await using var connection = await Db(TimescaleEdition.Community).DataSource.OpenConnectionAsync(Ct);
        var row = await connection.QuerySingleAsync<(DateTime Time, float PowerW, long EnergyWh, float VoltageV)>(
            "SELECT time, power_w, energy_wh, voltage_v FROM readings WHERE meter_id = @meter", new { meter });
        new DateTimeOffset(row.Time, TimeSpan.Zero).ShouldBe(reading.Timestamp);
        (row.PowerW, row.EnergyWh, row.VoltageV).ShouldBe((1234.5f, 9_876_543_210L, 231.2f));
    }
}
