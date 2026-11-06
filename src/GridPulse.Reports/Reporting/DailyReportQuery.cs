using Dapper;
using Npgsql;

namespace GridPulse.Reports.Reporting;

/// <summary>Reads one UTC day from the hourly rollup (and the day's alerts) with a single query.</summary>
public sealed class DailyReportQuery(NpgsqlDataSource dataSource)
{
    private const string Sql = """
        WITH day AS (
            SELECT meter_id,
                   max(last_energy_wh) - min(first_energy_wh)       AS energy_wh,
                   sum(sum_power_w) / NULLIF(sum(sample_count), 0)   AS average_w,
                   max(max_power_w)::numeric::float8                AS peak_w,
                   sum(sample_count)::bigint                        AS samples
            FROM readings_1h
            WHERE bucket >= @from AND bucket < @to
            GROUP BY meter_id
        ),
        alerts_per_meter AS (
            SELECT meter_id, count(*)::int AS alerts
            FROM alerts
            WHERE raised_at >= @from AND raised_at < @to
            GROUP BY meter_id
        )
        SELECT m.id AS MeterId, m.serial AS Serial, m.name AS Name,
               coalesce(d.energy_wh, 0) / 1000.0::float8 AS EnergyKwh,
               coalesce(d.average_w, 0)                   AS AveragePowerW,
               coalesce(d.peak_w, 0)                      AS PeakPowerW,
               coalesce(d.samples, 0)                     AS Samples,
               coalesce(a.alerts, 0)                      AS Alerts
        FROM meters m
        LEFT JOIN day d ON d.meter_id = m.id
        LEFT JOIN alerts_per_meter a ON a.meter_id = m.id
        WHERE d.meter_id IS NOT NULL OR a.meter_id IS NOT NULL
        ORDER BY m.serial
        """;

    public async Task<IReadOnlyList<MeterDay>> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<MeterDay>(new CommandDefinition(Sql, new { from, to = from.AddDays(1) }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
