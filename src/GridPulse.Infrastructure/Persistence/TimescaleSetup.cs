using Microsoft.Extensions.Logging;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

public enum TimescaleEdition
{
    /// <summary>Timescale License (community) edition: continuous aggregates, compression, policies.</summary>
    Community,

    /// <summary>Apache-2 edition (e.g. Azure Database for PostgreSQL): hypertables and time_bucket only.</summary>
    Apache,
}

/// <summary>
/// Creates the rollup relations <c>readings_1m</c>, <c>readings_15m</c> and <c>readings_1h</c>
/// according to the TimescaleDB edition of the server (ADR-0001).
/// </summary>
/// <remarks>
/// Both editions expose the same relation names and columns, so query code does not care which one
/// it runs on. Every statement is idempotent; this runs after EF Core migrations on every deploy.
/// It lives outside the EF migration because the edition is only known at run time, and
/// continuous aggregates cannot be created inside the migration's transaction.
/// </remarks>
public static class TimescaleSetup
{
    public static async Task<TimescaleEdition> DetectEditionAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT current_setting('timescaledb.license', true)");
        var license = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(license, "apache", StringComparison.OrdinalIgnoreCase) ? TimescaleEdition.Apache : TimescaleEdition.Community;
    }

    public static async Task<TimescaleEdition> ApplyAsync(NpgsqlDataSource dataSource, ILogger logger, CancellationToken cancellationToken = default)
    {
        var edition = await DetectEditionAsync(dataSource, cancellationToken);
        logger.LogInformation("TimescaleDB edition: {Edition}", edition);

        var statements = edition == TimescaleEdition.Community ? CommunityStatements : ApacheStatements;
        foreach (var sql in statements)
        {
            // Each statement runs on its own (autocommit): CREATE MATERIALIZED VIEW ... WITH
            // (timescaledb.continuous) refuses to run inside a transaction block.
            await using var command = dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return edition;
    }

    // sum + count (not avg) are stored so that coarser buckets can be computed exactly from finer ones.
    // first/last of the energy register give the energy consumed inside the bucket: last - first.
    private const string OneMinuteSelect = """
        SELECT time_bucket(INTERVAL '1 minute', time) AS bucket,
               meter_id,
               count(*)                  AS sample_count,
               sum(power_w)::float8      AS sum_power_w,
               min(power_w)              AS min_power_w,
               max(power_w)              AS max_power_w,
               first(energy_wh, time)    AS first_energy_wh,
               last(energy_wh, time)     AS last_energy_wh
        FROM readings
        GROUP BY bucket, meter_id
        """;

    private static string RollupSelect(string interval, string source) => $"""
        SELECT time_bucket(INTERVAL '{interval}', bucket) AS bucket,
               meter_id,
               sum(sample_count)::bigint         AS sample_count,
               sum(sum_power_w)::float8          AS sum_power_w,
               min(min_power_w)                  AS min_power_w,
               max(max_power_w)                  AS max_power_w,
               first(first_energy_wh, bucket)    AS first_energy_wh,
               last(last_energy_wh, bucket)      AS last_energy_wh
        FROM {source}
        GROUP BY time_bucket(INTERVAL '{interval}', bucket), meter_id
        """;

    private static readonly string[] CommunityStatements =
    [
        // Hierarchical continuous aggregates: 1m from raw, 15m from 1m, 1h from 15m.
        // materialized_only = false: queries also see the newest, not-yet-materialised buckets.
        $"""
        CREATE MATERIALIZED VIEW IF NOT EXISTS readings_1m
        WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
        {OneMinuteSelect}
        WITH NO DATA
        """,
        $"""
        CREATE MATERIALIZED VIEW IF NOT EXISTS readings_15m
        WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
        {RollupSelect("15 minutes", "readings_1m")}
        WITH NO DATA
        """,
        $"""
        CREATE MATERIALIZED VIEW IF NOT EXISTS readings_1h
        WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
        {RollupSelect("1 hour", "readings_15m")}
        WITH NO DATA
        """,
        "SELECT add_continuous_aggregate_policy('readings_1m', start_offset => INTERVAL '2 hours', end_offset => INTERVAL '1 minute', schedule_interval => INTERVAL '1 minute', if_not_exists => true)",
        "SELECT add_continuous_aggregate_policy('readings_15m', start_offset => INTERVAL '1 day', end_offset => INTERVAL '15 minutes', schedule_interval => INTERVAL '5 minutes', if_not_exists => true)",
        "SELECT add_continuous_aggregate_policy('readings_1h', start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes', if_not_exists => true)",

        // Columnar compression of raw chunks older than a day: segment by meter so per-meter scans stay cheap.
        """
        DO $$
        BEGIN
            IF NOT (SELECT compression_enabled FROM timescaledb_information.hypertables WHERE hypertable_name = 'readings') THEN
                ALTER TABLE readings SET (timescaledb.compress, timescaledb.compress_segmentby = 'meter_id', timescaledb.compress_orderby = 'time DESC');
            END IF;
        END $$
        """,
        "SELECT add_compression_policy('readings', INTERVAL '1 day', if_not_exists => true)",

        // Raw data is kept for 7 days; the rollups keep the history.
        "SELECT add_retention_policy('readings', INTERVAL '7 days', if_not_exists => true)",
    ];

    // Apache edition: same names and columns as plain views, computed at query time.
    private static readonly string[] ApacheStatements =
    [
        $"CREATE OR REPLACE VIEW readings_1m AS {OneMinuteSelect}",
        $"CREATE OR REPLACE VIEW readings_15m AS {RollupSelect("15 minutes", "readings_1m")}",
        $"CREATE OR REPLACE VIEW readings_1h AS {RollupSelect("1 hour", "readings_15m")}",
    ];
}
