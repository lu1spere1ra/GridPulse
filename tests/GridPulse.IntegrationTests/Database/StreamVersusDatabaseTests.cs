using Dapper;
using GridPulse.Core.Aggregation;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;

namespace GridPulse.IntegrationTests.Database;

/// <summary>
/// The live path (in-memory <see cref="WindowAggregator"/>) and the stored path (TimescaleDB
/// rollups) must describe the same buckets with the same numbers (ADR-0005). If either side
/// changes its alignment or its energy semantics, this test fails.
/// </summary>
public sealed class StreamVersusDatabaseTests(CommunityDatabase community, ApacheDatabase apache) : DatabaseTestBase(community, apache)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 9, 52, 17, TimeSpan.Zero);

    [Theory]
    [InlineData(TimescaleEdition.Community, 1, "readings_1m")]
    [InlineData(TimescaleEdition.Community, 15, "readings_15m")]
    [InlineData(TimescaleEdition.Apache, 1, "readings_1m")]
    [InlineData(TimescaleEdition.Apache, 15, "readings_15m")]
    public async Task In_memory_windows_match_database_rollups(TimescaleEdition edition, int minutes, string relation)
    {
        var meters = NewMeterIds(4);
        var readings = GenerateReadings(meters);
        await new CopyReadingBatchWriter(Db(edition).DataSource).WriteAsync(readings, Ct);

        var aggregator = new WindowAggregator(TimeSpan.FromMinutes(minutes), TimeSpan.Zero);
        var expected = new List<WindowResult>();
        foreach (var reading in readings)
        {
            aggregator.Add(reading, expected);
        }

        aggregator.CloseAll(expected);

        await using var connection = await Db(edition).DataSource.OpenConnectionAsync(Ct);
        var actual = (await connection.QueryAsync<RollupRow>(
            $"""
            SELECT meter_id AS MeterId, bucket AS Bucket, sample_count AS SampleCount, sum_power_w AS SumPowerW,
                   min_power_w AS MinPowerW, max_power_w AS MaxPowerW, last_energy_wh - first_energy_wh AS EnergyWh
            FROM {relation}
            WHERE meter_id = ANY(@meters)
            ORDER BY meter_id, bucket
            """,
            new { meters })).ToList();

        actual.Count.ShouldBe(expected.Count);
        foreach (var (stream, stored) in expected.OrderBy(w => w.MeterId).ThenBy(w => w.Start).Zip(actual))
        {
            stored.MeterId.ShouldBe(stream.MeterId);
            new DateTimeOffset(stored.Bucket, TimeSpan.Zero).ShouldBe(stream.Start);
            stored.SampleCount.ShouldBe(stream.Count);
            stored.SumPowerW.ShouldBe(stream.SumPowerW, tolerance: 0.01);
            stored.MinPowerW.ShouldBe(stream.MinPowerW);
            stored.MaxPowerW.ShouldBe(stream.MaxPowerW);
            stored.EnergyWh.ShouldBe(stream.EnergyWh);
        }
    }

    /// <summary>~40 minutes of readings with irregular gaps, so buckets have different sizes.</summary>
    private static List<Reading> GenerateReadings(int[] meters)
    {
        var random = new Random(7);
        var readings = new List<Reading>();
        foreach (var meter in meters)
        {
            var ts = T0;
            long energy = 1_000_000;
            while (ts < T0.AddMinutes(40))
            {
                var power = (float)Math.Round(200 + (random.NextDouble() * 3_000), 1);
                readings.Add(new Reading(meter, ts, power, energy, 230));
                energy += random.Next(0, 5);
                ts = ts.AddSeconds(random.Next(1, 20));
            }
        }

        // Interleave meters by time, as they would arrive.
        return readings.OrderBy(r => r.Timestamp).ToList();
    }

    private sealed record RollupRow(int MeterId, DateTime Bucket, long SampleCount, double SumPowerW, float MinPowerW, float MaxPowerW, long EnergyWh);
}
