using Dapper;
using GridPulse.Core.Contracts;
using Npgsql;

namespace GridPulse.Infrastructure.Queries;

/// <summary>
/// Analytical reads with Dapper over the hypertable and its rollups (ADR-0004): hand-written SQL
/// that uses time_bucket output directly, with no change tracking and no LINQ translation surprises.
/// <c>real</c> columns are cast through <c>numeric</c> so 752.3 is returned as 752.3, not 752.2999877929688.
/// </summary>
/// <remarks>
/// Energy over a range is computed from the cumulative register (<c>max(last) - min(first)</c>), which is
/// exact. Summing per-bucket deltas would miss the energy consumed between the last reading of one
/// bucket and the first reading of the next.
/// </remarks>
public sealed class TelemetryQueries(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<SeriesPoint>> GetMeterSeriesAsync(
        int meterId, DateTimeOffset from, DateTimeOffset to, Resolution resolution, CancellationToken cancellationToken)
    {
        var sql = resolution == Resolution.Raw
            ? """
              SELECT time AS ts, power_w::numeric::float8 AS avg, power_w::numeric::float8 AS min, power_w::numeric::float8 AS max, NULL::bigint AS energy, 1::bigint AS samples
              FROM readings
              WHERE meter_id = @meterId AND time >= @from AND time < @to
              ORDER BY time
              """
            : $"""
              SELECT bucket AS ts, sum_power_w / sample_count AS avg, min_power_w::numeric::float8 AS min, max_power_w::numeric::float8 AS max,
                     last_energy_wh - first_energy_wh AS energy, sample_count AS samples
              FROM {Relation(resolution)}
              WHERE meter_id = @meterId AND bucket >= @from AND bucket < @to
              ORDER BY bucket
              """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<SeriesRow>(new CommandDefinition(sql, new { meterId, from, to }, cancellationToken: cancellationToken));
        return rows.Select(r => new SeriesPoint(Utc(r.Ts), Math.Round(r.Avg, 1), r.Min, r.Max, r.Energy, r.Samples)).ToList();
    }

    public async Task<FleetSummary> GetFleetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, Resolution resolution, CancellationToken cancellationToken)
    {
        var relation = Relation(resolution);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var series = (await connection.QueryAsync<FleetRow>(new CommandDefinition(
            $"""
            SELECT bucket AS ts, sum(sum_power_w / sample_count) AS total_power, sum(last_energy_wh - first_energy_wh)::bigint AS energy,
                   count(*)::int AS meters
            FROM {relation}
            WHERE bucket >= @from AND bucket < @to
            GROUP BY bucket
            ORDER BY bucket
            """,
            new { from, to },
            cancellationToken: cancellationToken))).ToList();

        var totalEnergyWh = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"""
            SELECT sum(energy)::bigint FROM (
                SELECT max(last_energy_wh) - min(first_energy_wh) AS energy
                FROM {relation}
                WHERE bucket >= @from AND bucket < @to
                GROUP BY meter_id) per_meter
            """,
            new { from, to },
            cancellationToken: cancellationToken)) ?? 0;

        return new FleetSummary(
            from,
            to,
            resolution,
            Math.Round(totalEnergyWh / 1_000d, 3),
            series.Count == 0 ? 0 : Math.Round(series.Max(p => p.Total_Power), 1),
            series.Select(p => new FleetPoint(Utc(p.Ts), Math.Round(p.Total_Power, 1), p.Energy, p.Meters)).ToList());
    }

    public async Task<IReadOnlyList<TopConsumer>> GetTopConsumersAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var relation = Relation(ResolutionSelector.Choose(from, to, allowRaw: false));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TopConsumer>(new CommandDefinition(
            $"""
            SELECT r.meter_id AS MeterId, m.serial AS Serial, m.name AS Name,
                   round(((max(r.last_energy_wh) - min(r.first_energy_wh)) / 1000.0)::numeric, 3)::float8 AS EnergyKwh,
                   max(r.max_power_w)::numeric::float8 AS PeakPowerW
            FROM {relation} r
            JOIN meters m ON m.id = r.meter_id
            WHERE r.bucket >= @from AND r.bucket < @to
            GROUP BY r.meter_id, m.serial, m.name
            ORDER BY EnergyKwh DESC
            LIMIT @limit
            """,
            new { from, to, limit },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static string Relation(Resolution resolution) => resolution switch
    {
        Resolution.OneMinute => "readings_1m",
        Resolution.FifteenMinutes => "readings_15m",
        Resolution.OneHour => "readings_1h",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Rollup resolution expected."),
    };

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record SeriesRow(DateTime Ts, double Avg, double Min, double Max, long? Energy, long Samples);

    private sealed record FleetRow(DateTime Ts, double Total_Power, long Energy, int Meters);
}
